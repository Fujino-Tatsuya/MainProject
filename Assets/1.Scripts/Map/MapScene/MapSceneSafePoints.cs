using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 스텝(구간)별 고정 안전 지점(할 일 80 — 1안). <b>4.MapScene 상주(비네트워크)</b>, 동작은 서버만.
/// 규칙은 순수 <see cref="MapSceneSafePointRules"/>. 문서: Docs/tech/tutorial-director.md §12.
///
/// 지점 = 이 컴포넌트의 <see cref="points"/>(구간 → 씬 Transform). 저작 메뉴 <c>Tools/Map/Authoring/MapScene/8. Place Safe Points</c> 가
/// 빠진 구간만 기본 위치(들어오는 배리어 안쪽)에 만든다 — 옮긴 지점은 다시 돌려도 그대로다.
///
/// 언제 되돌리나 — 낙사: 기존 추락 복구(<see cref="PlayerFallRecovery"/>)가 복귀 지점을 물을 때(<see cref="PlayerSafePointTracker.ReturnPointOverride"/>).
///    추락 생존·추락 사망 모두. 연출(낙하 카메라·무적·입력 잠금)은 기존 그대로, 지점만 바뀐다. 구간(<see cref="MapSceneSafePointRules.ReturnPhase"/>):
///  · 스텝 진행 중 — 지금 스텝 구간 고정 지점.
///  · 보스방 이동 카운트다운 중 — 보스 진입(BossEnter) 고정 지점(같은 규칙). 이 동안 자동 부활도 이 지점(<see cref="TryResolveFixedPoint"/>).
///  · 카운트다운 뒤 엘리베이터 연출·도착 대기 — 기존 경로(override null, 할 일 89).
///  · 보스방 도착 뒤(단계 Finished = 보스전) — 재합류 모서리 규칙(<see cref="BossRoomFallPoint"/>) + <see cref="dropHeight"/>.
///
/// 스텝 지점: 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'(<see cref="MapSceneSafePointRules.StepArea"/>)의 지점 하나.
/// 점유 = 정적 장애물만(<see cref="MapSceneSafePointRules.BlockingLayers"/>) — 몬스터·플레이어가 지점 위에 있어도 지점은 그대로 쓴다.
/// 지점이 정적 장애물에 막혔으면 그 구간 안 지점 주변 링(<see cref="MapSceneSafePointRules.TryFindNearestFree"/>, 가까운 것부터)의 빈자리.
/// 고른 자리 높이 = 지점 Y + <see cref="dropHeight"/>(기본 3m) — 떨어져 착지한다. 링도 실패하면 지점 위 그대로(겹침 허용 + 경고).
/// 그 구간에 지점이 없을 때만 기존 동작(안전지점 큐 → 스폰 폴백). 다른 구간 지점으로 대체하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneSafePoints : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public MapSceneArea area;
        public Transform point;
    }

    [Tooltip("구간별 고정 안전 지점. 같은 구간이 둘이면 앞 것. 지점이 없는 구간은 기존 추락 복구(최근 밟은 자리)를 쓴다.")]
    [SerializeField] private List<Entry> points = new List<Entry>();

    [Header("낙사")]
    [Tooltip("추락 생존·추락 사망 복귀를 기존 안전지점 큐(최근 밟은 자리) 대신 이 지점으로.")]
    [SerializeField] private bool useOnFall = true;

    [Tooltip("고른 자리(지점 또는 주변 링 빈자리) 위로 이만큼(m) 띄워 내려놓는다 — 떨어져 착지.")]
    [SerializeField, Min(0f)] private float dropHeight = MapSceneSafePointRules.DefaultDropHeight;

    [Header("판정")]
    [Tooltip("정적 장애물 점유 검사 반경(m). 적·플레이어는 보지 않는다.")]
    [SerializeField, Min(0.05f)] private float occupancyRadius = 0.4f;
    [Tooltip("링 빈자리 아래 바닥을 찾는 범위(지점 Y ± m).")]
    [SerializeField, Min(0.1f)] private float floorSearchHeight = 1.5f;
    [SerializeField, Range(0f, 30f)] private float maxFlatAngle = 10f;

    private Func<ulong, Vector3, Vector3?> _fallOverride;
    private int _groundMask, _obstacleMask;

    public IReadOnlyList<Entry> Points => points;

    public bool TryGetPoint(MapSceneArea area, out Transform point)
    {
        foreach (Entry e in points)
        {
            if (e.area != area || e.point == null) continue;
            point = e.point;
            return true;
        }
        point = null;
        return false;
    }

    // ─── 생명주기(서버) ──────────────────────────────────────────────────

    private void Awake()
    {
        _groundMask = LayerMask.GetMask("Default", "Ground", "Env");
        _obstacleMask = 0;
        foreach (string name in MapSceneSafePointRules.BlockingLayers)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0) _obstacleMask |= 1 << layer;
        }
    }

    /// <summary>씬의 활성 인스턴스(없으면 null) — 보스방 이동 카운트다운 중 자동 부활(<see cref="MapSceneDeathRules"/>)이 읽는다.</summary>
    public static MapSceneSafePoints Active { get; private set; }

    private void OnEnable() => Active = this;

    private void OnDisable()
    {
        if (Active == this) Active = null;
        Unhook();
    }

    private void Update()
    {
        if (TryGetServerDirector(out _)) Hook();
        else Unhook();
    }

    private static bool TryGetServerDirector(out MapSceneDirector d)
    {
        d = MapSceneDirector.Instance;
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsServer && MapSceneStageMarker.IsActive && d != null && d.IsTutorialActive;
    }

    private void Hook()
    {
        if (_fallOverride != null) return;

        if (PlayerSafePointTracker.ReturnPointOverride != null)
            Edit.LogWarning("[Tutorial] 추락 복귀 지점 훅이 이미 걸려 있다 — 튜토리얼 안전 지점으로 덮는다.", this);
        _fallOverride = ResolveFallPoint;
        PlayerSafePointTracker.ReturnPointOverride = _fallOverride;

        int count = 0;
        foreach (Entry e in points) if (e.point != null) count++;
        Edit.Log($"[Tutorial] 안전 지점 켜짐 — 지점 {count}개 · 낙사 {(useOnFall ? "켬" : "끔")}.", this);
    }

    private void Unhook()
    {
        if (_fallOverride == null) return;
        if (PlayerSafePointTracker.ReturnPointOverride == _fallOverride)
            PlayerSafePointTracker.ReturnPointOverride = null;
        _fallOverride = null;
    }

    // 보스방 이동 상태 — 카운트다운 = IsCountdownActive, 그 뒤 엘리베이터 연출·도착 대기 = IsEncounterBusy(카운트다운 중에도 true).
    private static MapSceneReturnPhase CurrentPhase(MapSceneDirector d)
    {
        BossTeleportManager t = BossTeleportManager.Instance;
        bool spawned = t != null && t.IsSpawned;
        return MapSceneSafePointRules.ReturnPhase(d.IsTutorialActive, d.Step,
            spawned && t.IsCountdownActive, spawned && t.IsEncounterBusy);
    }

    // ─── 낙사 ────────────────────────────────────────────────────────────

    private Vector3? ResolveFallPoint(ulong clientId, Vector3 fallPoint)
    {
        if (!useOnFall || !TryGetServerDirector(out MapSceneDirector d)) return null;

        MapSceneReturnPhase phase = CurrentPhase(d);
        if (phase == MapSceneReturnPhase.None) return null;
        if (phase == MapSceneReturnPhase.BossFight) return ResolveBossRoomCorner(clientId);

        MapSceneArea stepArea = MapSceneArea.None;
        if (phase == MapSceneReturnPhase.Step)
        {
            int mask = 0;
            for (int g = 0; g < MapSceneFlowModel.GateCount; g++)
                if (d.IsGateOpen((MapSceneGate)g)) mask |= 1 << g;
            stepArea = MapSceneSafePointRules.StepArea(d.Step, mask, d.GetBackBarrierMaskServer(clientId));
        }

        MapSceneArea wanted = MapSceneSafePointRules.FixedPointArea(phase, stepArea);
        return TryResolveFixedPoint(wanted, clientId, "낙사", out Vector3 point) ? point : null;
    }

    /// <summary>
    /// [서버] 구간 <paramref name="area"/> 고정 지점에서 내릴 자리 — 정적 장애물에 막혔으면 구간 안 주변 링 빈자리, 링도 실패하면 지점 그대로(겹침 허용),
    /// 높이 = 지점 Y + <see cref="dropHeight"/>. 그 구간에 지점이 없으면 false(호출자는 기존 경로). 보스방 이동 카운트다운 중 자동 부활도 쓴다.
    /// </summary>
    public bool TryResolveFixedPoint(MapSceneArea area, ulong clientId, string why, out Vector3 point)
    {
        point = default;
        if (!MapSceneSafePointRules.TryPickStepPoint(area, a => TryGetPoint(a, out _), out MapSceneArea picked)
            || !TryGetPoint(picked, out Transform t))
        {
            Edit.LogWarning($"[Tutorial] client {clientId} {why} — {area} 안전 지점이 없다(미배치). 기존 경로로 넘긴다.", this);
            return false;
        }

        Vector3 p = t.position;
        MapSceneRect? bounds = null;
        MapSceneDirector d = MapSceneDirector.Instance;
        var rules = d != null ? d.GetComponent<MapSceneDeathRules>() : null;
        if (rules != null && rules.TryGetAreaRect(picked, out MapSceneRect rect)) bounds = rect;

        MapSceneSafeSpotKind kind = MapSceneSafePointRules.PickSpot(p.x, p.z, IsBlocked(p), bounds,
            (x, z) => IsFreeRingSpot(new Vector3(x, p.y, z)), out float sx, out float sz);
        point = new Vector3(sx, MapSceneSafePointRules.DropY(p.y, dropHeight), sz);

        if (kind == MapSceneSafeSpotKind.PointOverlap)
            Edit.LogWarning($"[Tutorial] client {clientId} {why} — {picked} 안전 지점이 장애물에 막히고 주변 빈자리도 없어 지점 위 {point} 에 그대로 내린다(겹침 허용).", this);
        else
            Edit.Log($"[Tutorial] client {clientId} {why} → {picked} 안전 지점{(kind == MapSceneSafeSpotKind.Ring ? " 주변 빈자리" : "")} {point}.", this);
        return true;
    }

    // ─── 보스전 낙사 — 재합류 모서리 ─────────────────────────────────────

    private readonly List<Pose> _corners = new List<Pose>();
    private readonly List<Vector3> _others = new List<Vector3>();
    private readonly List<Vector3> _bosses = new List<Vector3>();

    // 판 도중 재합류(할 일 48 R3c — NetworkLoadingFlowController.SpawnRejoiner)와 같은 입력: 다른 플레이어 전원·활성 TwentyThreeBoss.
    private Vector3? ResolveBossRoomCorner(ulong clientId)
    {
        if (!BossRoomRejoin.TryCollectCorners(_corners, out bool fromPrefab))
        {
            Edit.LogWarning($"[Tutorial] client {clientId} 보스전 낙사 — 보스 룸 모서리를 못 찾아 기존 추락 복구로 넘긴다.", this);
            return null;
        }

        _others.Clear();
        foreach (NetworkClient other in NetworkManager.Singleton.ConnectedClientsList)
            if (other.ClientId != clientId && other.PlayerObject != null)
                _others.Add(other.PlayerObject.transform.position);

        _bosses.Clear();
        foreach (TwentyThreeBoss boss in FindObjectsByType<TwentyThreeBoss>(FindObjectsSortMode.None))
            if (boss.isActiveAndEnabled) _bosses.Add(boss.transform.position);

        Vector3 point = BossRoomFallPoint(_corners, _others, _bosses, dropHeight, out int corner);
        Edit.Log($"[Tutorial] client {clientId} 보스전 낙사 → 보스 룸 모서리 {BossRoomRejoin.CornerName(corner)}" +
                 $"({(fromPrefab ? "prefab" : "fallback")}) {point}.", this);
        return point;
    }

    /// <summary>
    /// 보스전 낙사 복귀 자리 — 재합류 모서리 규칙(<see cref="RejoinSpawnPicker.FarthestCorner"/> — 다른 플레이어·보스에서 가장 먼 모서리) 그대로,
    /// 높이만 모서리 Y + <paramref name="dropHeight"/>(<see cref="MapSceneSafePointRules.DropY"/>). <paramref name="corners"/> 는 비어 있으면 안 된다.
    /// </summary>
    public static Vector3 BossRoomFallPoint(IReadOnlyList<Pose> corners, IReadOnlyList<Vector3> others, IReadOnlyList<Vector3> bosses,
                                            float dropHeight, out int corner)
    {
        corner = RejoinSpawnPicker.FarthestCorner(corners, others, bosses);
        Vector3 p = corners[corner].position;
        return new Vector3(p.x, MapSceneSafePointRules.DropY(p.y, dropHeight), p.z);
    }

    // 정적 장애물(소품·벽·배리어)만 — 적·플레이어는 보지 않는다.
    private bool IsBlocked(Vector3 floor) =>
        Physics.CheckSphere(floor + Vector3.up * (occupancyRadius + 0.05f), occupancyRadius, _obstacleMask, QueryTriggerInteraction.Ignore);

    // 링 후보 — 아래 평평한 바닥이 있고 정적 장애물에 막히지 않았는가(자동 부활 격자점과 같은 검사).
    private bool IsFreeRingSpot(Vector3 anchor)
    {
        var origin = anchor + Vector3.up * floorSearchHeight;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, floorSearchHeight * 2f, _groundMask, QueryTriggerInteraction.Ignore))
            return false;
        if (Vector3.Angle(hit.normal, Vector3.up) > maxFlatAngle) return false;
        return !IsBlocked(hit.point);
    }

#if UNITY_EDITOR
    // 지점 확인용 — 초록 원 + 구간 이름.
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        foreach (Entry e in points)
        {
            if (e.point == null) continue;
            Vector3 p = e.point.position;
            Gizmos.DrawWireSphere(p + Vector3.up * 0.5f, 0.5f);
            Gizmos.DrawLine(p, p + Vector3.up * 2f);
            UnityEditor.Handles.Label(p + Vector3.up * 2.2f, $"Safe {e.area}");
        }
    }
#endif
}
