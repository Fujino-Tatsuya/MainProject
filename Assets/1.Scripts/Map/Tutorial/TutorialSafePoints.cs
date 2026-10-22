using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 스텝(구간)별 고정 안전 지점(할 일 80 — 1안). <b>4.MapScene 상주(비네트워크)</b>, 동작은 서버만.
/// 규칙은 순수 <see cref="TutorialSafePointRules"/>. 문서: Docs/tech/tutorial-director.md §12.
///
/// 지점 = 이 컴포넌트의 <see cref="points"/>(구간 → 씬 Transform). 저작 메뉴 <c>Tools/Map/Authoring/Tutorial/8. Place Safe Points</c> 가
/// 빠진 구간만 기본 위치(들어오는 배리어 안쪽)에 만든다 — 옮긴 지점은 다시 돌려도 그대로다.
///
/// 언제 되돌리나(튜토리얼 진행 중 · 단계 Finished 전 · 보스방 이동 중 아님):
///  · 낙사 — 기존 추락 복구(<see cref="PlayerFallRecovery"/>)가 복귀 지점을 물을 때(<see cref="PlayerSafePointTracker.ReturnPointOverride"/>).
///    추락 생존·추락 사망 모두. 연출(낙하 카메라·무적·입력 잠금)은 기존 그대로, 지점만 바뀐다.
///
/// 어느 지점인가: 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'(<see cref="TutorialSafePointRules.AreaOrder"/>)부터 거꾸로,
/// 지점이 있고 설 수 있는(바닥·점유·<see cref="PlayerSafePointTracker.ReturnPointFilter"/> — 닫힌 배리어·뒤 배리어·패드 규칙) 첫 구간.
/// 점유 = 정적 장애물만(<see cref="TutorialSafePointRules.BlockingLayers"/>) — 몬스터·플레이어가 근처에 있어도 지점은 그대로 쓴다.
/// 지점 자체가 막혔으면 그 구간 안에서 지점 주변 링(<see cref="TutorialSafePointRules.TryFindNearestFree"/>)의 가장 가까운 빈자리,
/// 그다음 지점에 가장 가까운 안전 격자점(<see cref="TutorialDeathRules.TryFindAreaSpreadPoints"/>).
/// 아무 구간에서도 못 찾으면 씬 시작 지점으로 보내지 않고 지금 단계 구간 지점에 그대로(겹침 허용 + 경고, <see cref="TutorialSafePointRules.TryPickLastResort"/>).
/// 지점이 하나도 배치되지 않았을 때만 기존 동작(안전지점 큐).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialSafePoints : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public TutorialArea area;
        public Transform point;
    }

    [Tooltip("구간별 고정 안전 지점. 같은 구간이 둘이면 앞 것. 지점이 없는 구간은 건너뛰고 앞 구간 지점을 쓴다.")]
    [SerializeField] private List<Entry> points = new List<Entry>();

    [Header("낙사")]
    [Tooltip("추락 생존·추락 사망 복귀를 기존 안전지점 큐(최근 밟은 자리) 대신 이 지점으로.")]
    [SerializeField] private bool useOnFall = true;

    [Header("판정")]
    [Tooltip("지점 아래 바닥을 찾는 범위(지점 Y ± m).")]
    [SerializeField, Min(0.1f)] private float floorSearchHeight = 1.5f;
    [SerializeField, Min(0.05f)] private float occupancyRadius = 0.4f;
    [SerializeField, Range(0f, 30f)] private float maxFlatAngle = 10f;

    private Func<ulong, Vector3, Vector3?> _fallOverride;
    private int _groundMask, _obstacleMask;
    private readonly List<Vector3> _spread = new List<Vector3>(1);

    public IReadOnlyList<Entry> Points => points;

    public bool TryGetPoint(TutorialArea area, out Transform point)
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
        foreach (string name in TutorialSafePointRules.BlockingLayers)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0) _obstacleMask |= 1 << layer;
        }
    }

    private void OnDisable() => Unhook();

    private void Update()
    {
        if (TryGetServerDirector(out _)) Hook();
        else Unhook();
    }

    private static bool TryGetServerDirector(out TutorialDirector d)
    {
        d = TutorialDirector.Instance;
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsServer && TutorialStageMarker.IsActive && d != null && d.IsTutorialActive;
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

    private static bool BossMoveBusy()
    {
        BossTeleportManager t = BossTeleportManager.Instance;
        return t != null && t.IsSpawned && (t.IsCountdownActive || t.IsEncounterBusy);
    }

    // ─── 낙사 ────────────────────────────────────────────────────────────

    private Vector3? ResolveFallPoint(ulong clientId, Vector3 fallPoint)
    {
        if (!useOnFall || !TryGetServerDirector(out TutorialDirector d)) return null;
        if (!TutorialSafePointRules.ShouldWatch(d.IsTutorialActive, d.Step, BossMoveBusy())) return null;

        int mask = 0;
        for (int g = 0; g < TutorialFlowModel.GateCount; g++)
            if (d.IsGateOpen((TutorialGate)g)) mask |= 1 << g;
        List<TutorialArea> order = TutorialSafePointRules.AreaOrder(d.Step, mask, d.GetBackBarrierMaskServer(clientId));

        if (TryResolve(d, clientId, order, out Vector3 point, out TutorialArea area))
        {
            Edit.Log($"[Tutorial] client {clientId} 낙사 → {area} 안전 지점 {point}.", this);
            return point;
        }

        // 튜토리얼 진행 중에는 씬 시작 지점(추락 복구의 스폰 폴백)으로 보내지 않는다 — 지금 단계 구간 지점에 그대로(겹침 허용).
        if (TutorialSafePointRules.TryPickLastResort(order, a => TryGetPoint(a, out _), out area) && TryGetPoint(area, out Transform last))
        {
            Edit.LogWarning($"[Tutorial] client {clientId} 낙사 — 설 자리를 못 찾아 {area} 안전 지점 {last.position} 에 그대로 내린다(겹침 허용).", this);
            return last.position;
        }

        Edit.LogWarning($"[Tutorial] client {clientId} 낙사 — 안전 지점이 하나도 없다(미배치). 기존 추락 복구로 넘긴다.", this);
        return null;
    }

    // ─── 지점 고르기 ─────────────────────────────────────────────────────

    private bool TryResolve(TutorialDirector d, ulong clientId, List<TutorialArea> order, out Vector3 point, out TutorialArea area)
    {
        var rules = d.GetComponent<TutorialDeathRules>();

        foreach (TutorialArea a in order)
        {
            if (!TryGetPoint(a, out Transform t)) continue;
            Vector3 anchor = t.position;
            if (TryStandAt(anchor, out point) && IsAllowed(clientId, point))
            {
                area = a;
                return true;
            }

            // 지점이 정적 장애물(소품·벽·배리어)이나 배리어 규칙에 막혔다 — 먼저 지점 주변 링에서 가장 가까운 빈자리(같은 구간 안).
            if (rules != null && rules.TryGetAreaRect(a, out TutorialRect rect))
            {
                Vector3 found = default;
                if (TutorialSafePointRules.TryFindNearestFree(anchor.x, anchor.z, rect, TutorialSafePointRules.DefaultRingRadii,
                        TutorialSafePointRules.DefaultRingDirections,
                        (x, z) => TryStandAt(new Vector3(x, anchor.y, z), out found) && IsAllowed(clientId, found), out _, out _))
                {
                    point = found;
                    area = a;
                    return true;
                }
            }

            // 그래도 없으면 같은 구간에서 지점에 가장 가까운 안전 격자점(전멸 리스폰·구간 스킵과 같은 탐색).
            if (rules != null && rules.TryFindAreaSpreadPoints(a, 1, anchor.x, anchor.z, _spread) && IsAllowed(clientId, _spread[0]))
            {
                point = _spread[0];
                area = a;
                return true;
            }
        }

        point = default;
        area = TutorialArea.None;
        return false;
    }

    private static bool IsAllowed(ulong clientId, Vector3 p) =>
        PlayerSafePointTracker.ReturnPointFilter == null || PlayerSafePointTracker.ReturnPointFilter(clientId, p);

    // 지점 아래 평평한 바닥이 있고 서 있을 자리가 비었는가(정적 장애물만) — 자동 부활 격자점과 같은 검사.
    private bool TryStandAt(Vector3 anchor, out Vector3 point)
    {
        point = anchor;
        var origin = anchor + Vector3.up * floorSearchHeight;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, floorSearchHeight * 2f, _groundMask, QueryTriggerInteraction.Ignore))
            return false;
        if (Vector3.Angle(hit.normal, Vector3.up) > maxFlatAngle) return false;

        Vector3 center = hit.point + Vector3.up * (occupancyRadius + 0.05f);
        if (Physics.CheckSphere(center, occupancyRadius, _obstacleMask, QueryTriggerInteraction.Ignore)) return false;

        point = hit.point;
        return true;
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
