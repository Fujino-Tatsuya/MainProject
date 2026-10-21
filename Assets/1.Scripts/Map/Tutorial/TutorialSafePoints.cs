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
///  · 구간 이탈 — Alive·접지 상태로 튜토리얼 구간(존1~보스 진입) 사각형 + 여유 밖에 <see cref="outOfAreaSeconds"/> 이상.
///  · 끼임 — Alive 인 몸 중심 탐침이 벽(Wall)·닫힌 배리어 속에 <see cref="stuckSeconds"/> 이상(개인 뒤 배리어는 피어마다 달라 제외).
///  구간 이탈·끼임 이동 = <see cref="PlayerFallRecovery.RelocateServer"/>(오너 권한 이동 — 오너에게 순간이동 지시, 자동 부활과 같은 경로).
///
/// 어느 지점인가: 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'(<see cref="TutorialSafePointRules.AreaOrder"/>)부터 거꾸로,
/// 지점이 있고 설 수 있는(바닥·점유·<see cref="PlayerSafePointTracker.ReturnPointFilter"/> — 닫힌 배리어·뒤 배리어·패드 규칙) 첫 구간.
/// 지점 자체가 막혔으면 그 구간에서 지점에 가장 가까운 안전 격자점(<see cref="TutorialDeathRules.TryFindAreaSpreadPoints"/>).
/// 아무것도 없으면 기존 동작(낙사 = 안전지점 큐, 이탈·끼임 = 이동 없음 + 경고).
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

    [Header("구간 이탈")]
    [SerializeField] private bool recoverOutOfArea = true;
    [Tooltip("튜토리얼 구간 밖에 접지한 채 이만큼(실제 시간 초) 이어지면 되돌린다.")]
    [SerializeField, Min(0f)] private float outOfAreaSeconds = TutorialSafePointRules.DefaultOutOfAreaSeconds;
    [Tooltip("구간 사각형을 바깥으로 넓혀 보는 여유(m) — 경계 근처 벽 두께·복제 흔들림.")]
    [SerializeField, Min(0f)] private float outOfAreaMargin = TutorialSafePointRules.DefaultOutOfAreaMargin;

    [Header("끼임")]
    [SerializeField] private bool recoverStuck = true;
    [Tooltip("몸 중심 탐침이 벽·닫힌 배리어 속에 이만큼(실제 시간 초) 이어지면 되돌린다.")]
    [SerializeField, Min(0f)] private float stuckSeconds = TutorialSafePointRules.DefaultStuckSeconds;
    [Tooltip("몸 중심 탐침 반경(m) — 플레이어 캡슐보다 훨씬 작게. 벽에 붙어 선 정도로는 안 걸리고, 몸이 벽에 깊이 박혀야 걸린다.")]
    [SerializeField, Min(0.02f)] private float stuckProbeRadius = 0.15f;
    [Tooltip("탐침 캡슐 아래·위 끝 높이(발 기준 m).")]
    [SerializeField, Min(0f)] private float stuckProbeBottom = 0.5f;
    [SerializeField, Min(0f)] private float stuckProbeTop = 1.3f;

    [Header("판정")]
    [SerializeField, Range(0.05f, 1f)] private float pollInterval = 0.2f;
    [Tooltip("지점 아래 바닥을 찾는 범위(지점 Y ± m).")]
    [SerializeField, Min(0.1f)] private float floorSearchHeight = 1.5f;
    [SerializeField, Min(0.05f)] private float occupancyRadius = 0.4f;
    [SerializeField, Range(0f, 30f)] private float maxFlatAngle = 10f;

    private sealed class Watch
    {
        public readonly TutorialRecoveryTimer OutOfArea = new TutorialRecoveryTimer();
        public readonly TutorialRecoveryTimer Stuck = new TutorialRecoveryTimer();
    }

    private Func<ulong, Vector3, Vector3?> _fallOverride;
    private TutorialAreaTracker _areas;
    private float _pollTimer;
    private int _groundMask, _obstacleMask, _stuckMask;
    private readonly Dictionary<ulong, Watch> _watch = new Dictionary<ulong, Watch>();
    private readonly List<ulong> _gone = new List<ulong>();
    private readonly Collider[] _hits = new Collider[8];
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
        _obstacleMask = _groundMask | LayerMask.GetMask("Wall", "Barrier");
        _stuckMask = LayerMask.GetMask("Wall", "Barrier");
    }

    private void OnDisable() => Unhook();

    private void Update()
    {
        if (!TryGetServerDirector(out TutorialDirector d))
        {
            Unhook();
            return;
        }

        Hook();
        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer > 0f) return;
        _pollTimer = pollInterval;
        Poll(d);
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

        _areas = new TutorialAreaTracker();
        if (!TutorialAreaLayout.TryBuild(TutorialStageMarker.Active.transform, _areas, out string error))
        {
            Edit.LogWarning($"[Tutorial] 안전 지점: 구간 계산 실패 — {error}. 구간 이탈 판정을 하지 않는다.", this);
            _areas = null;
        }

        if (PlayerSafePointTracker.ReturnPointOverride != null)
            Edit.LogWarning("[Tutorial] 추락 복귀 지점 훅이 이미 걸려 있다 — 튜토리얼 안전 지점으로 덮는다.", this);
        _fallOverride = ResolveFallPoint;
        PlayerSafePointTracker.ReturnPointOverride = _fallOverride;
        _pollTimer = 0f;

        int count = 0;
        foreach (Entry e in points) if (e.point != null) count++;
        Edit.Log($"[Tutorial] 안전 지점 켜짐 — 지점 {count}개 · 낙사 {(useOnFall ? "켬" : "끔")} · " +
                 $"구간 이탈 {(recoverOutOfArea ? $"{outOfAreaSeconds:0.#}초" : "끔")} · 끼임 {(recoverStuck ? $"{stuckSeconds:0.#}초" : "끔")}.", this);
    }

    private void Unhook()
    {
        if (_fallOverride == null) return;
        if (PlayerSafePointTracker.ReturnPointOverride == _fallOverride)
            PlayerSafePointTracker.ReturnPointOverride = null;
        _fallOverride = null;
        _areas = null;
        _watch.Clear();
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
        if (!TryResolve(d, clientId, out Vector3 point, out TutorialArea area)) return null;

        Edit.Log($"[Tutorial] client {clientId} 낙사 → {area} 안전 지점 {point}.", this);
        return point;
    }

    // ─── 구간 이탈 · 끼임 ─────────────────────────────────────────────────

    private void Poll(TutorialDirector d)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (!TutorialSafePointRules.ShouldWatch(d.IsTutorialActive, d.Step, BossMoveBusy()))
        {
            _watch.Clear();
            return;
        }

        double now = Time.unscaledTimeAsDouble;   // 실제 시간 — 슬로우모션 무관
        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            NetworkObject po = client.PlayerObject;
            if (po == null) continue;

            var life = po.GetComponent<PlayerLifeCycleController>();
            bool alive = life == null || life.State == PlayerLifeState.Alive;
            var grounding = po.GetComponent<PlayerGroundingSensor>();
            Vector3 pos = po.transform.position;

            bool outside = recoverOutOfArea && alive && grounding != null && grounding.IsGrounded
                           && TutorialSafePointRules.IsOutsideAll(_areas, pos.x, pos.z, outOfAreaMargin);
            bool stuck = recoverStuck && alive && IsEmbedded(pos);

            if (!_watch.TryGetValue(client.ClientId, out Watch w))
                _watch[client.ClientId] = w = new Watch();

            if (w.OutOfArea.Tick(outside, now, outOfAreaSeconds))
                Recover(d, client.ClientId, po, TutorialRecoveryReason.OutOfArea, w);
            else if (w.Stuck.Tick(stuck, now, stuckSeconds))
                Recover(d, client.ClientId, po, TutorialRecoveryReason.Stuck, w);
        }

        _gone.Clear();
        foreach (ulong id in _watch.Keys)
            if (!nm.ConnectedClients.ContainsKey(id)) _gone.Add(id);
        foreach (ulong id in _gone) _watch.Remove(id);
    }

    // 몸 중심 탐침이 벽·닫힌 배리어 속인가. 개인 뒤 배리어(BackBarrier_*)는 이 피어(호스트) 기준이라 남의 플레이어엔 없는 벽이다 — 뺀다.
    private bool IsEmbedded(Vector3 feet)
    {
        int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * stuckProbeBottom, feet + Vector3.up * Mathf.Max(stuckProbeBottom, stuckProbeTop),
                                               stuckProbeRadius, _hits, _stuckMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = _hits[i];
            if (c != null && !IsBackBarrier(c.transform)) return true;
        }
        return false;
    }

    private static bool IsBackBarrier(Transform t)
    {
        for (int i = 0; t != null && i < 3; i++, t = t.parent)
            if (t.name.StartsWith("BackBarrier_", StringComparison.Ordinal)) return true;
        return false;
    }

    private void Recover(TutorialDirector d, ulong clientId, NetworkObject po, TutorialRecoveryReason reason, Watch w)
    {
        string why = reason == TutorialRecoveryReason.Stuck ? "끼임" : "구간 이탈";
        if (!TryResolve(d, clientId, out Vector3 point, out TutorialArea area))
        {
            Edit.LogWarning($"[Tutorial] client {clientId} {why} — 갈 안전 지점이 없다(지점 미배치·전부 막힘). 그대로 둔다.", this);
            return;
        }

        var recovery = po.GetComponent<PlayerFallRecovery>();
        if (recovery == null || !recovery.RelocateServer(point))
        {
            Edit.LogWarning($"[Tutorial] client {clientId} {why} — 순간이동 불가(PlayerFallRecovery).", this);
            return;
        }

        w.OutOfArea.Reset();
        w.Stuck.Reset();
        Edit.Log($"[Tutorial] client {clientId} {why} {po.transform.position} → {area} 안전 지점 {point}.", this);
    }

    // ─── 지점 고르기 ─────────────────────────────────────────────────────

    private bool TryResolve(TutorialDirector d, ulong clientId, out Vector3 point, out TutorialArea area)
    {
        int mask = 0;
        for (int g = 0; g < TutorialFlowModel.GateCount; g++)
            if (d.IsGateOpen((TutorialGate)g)) mask |= 1 << g;
        int passed = d.GetBackBarrierMaskServer(clientId);
        var rules = d.GetComponent<TutorialDeathRules>();

        foreach (TutorialArea a in TutorialSafePointRules.AreaOrder(d.Step, mask, passed))
        {
            if (!TryGetPoint(a, out Transform t)) continue;
            Vector3 anchor = t.position;
            if (TryStandAt(anchor, out point) && IsAllowed(clientId, point))
            {
                area = a;
                return true;
            }

            // 지점이 막혔다(소품·몬스터 시체가 아닌 고정물·배리어 규칙) — 같은 구간에서 지점에 가장 가까운 안전 격자점(전멸 리스폰·구간 스킵과 같은 탐색).
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

    // 지점 아래 평평한 바닥이 있고 서 있을 자리가 비었는가 — 자동 부활 격자점과 같은 검사.
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
