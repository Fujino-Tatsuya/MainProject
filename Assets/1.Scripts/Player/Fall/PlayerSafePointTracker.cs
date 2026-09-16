using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 생존 추락 복귀용 안전지점 추적(서버 권한). (PLAN §13)
///
/// - Grounded Idle/Move가 stableSeconds 유지되고, 정적·평평한 Ground/Env(이동 플랫폼 제외)일 때만 기록.
/// - Dash 중에는 기록하지 않는다. 최초 스폰 위치를 신뢰 가능한 fallback으로 보관.
/// - 기록된 지점은 큐(FIFO)에 보관하며, 이전 지점과 minSafePointSpacing 이상 떨어졌을 때만 새로 쌓는다
///   (거의 동일한 위치가 매 틱 반복 저장되는 것을 방지). 큐 용량을 넘으면 가장 오래된 지점부터 버린다.
/// - 복귀 지점 계산은 큐를 최신 → 과거 순으로 훑는다. 각 후보가 막혀 있으면 추락지점 반대 방향부터
///   ringRadii(1/2/3m)를 8방향으로 탐색하고, 그래도 실패하면 다음(더 오래된) 후보로 넘어간다.
///   모든 후보가 실패하면 최초 스폰 위치를 반환한다.
/// - 큐가 바뀔 때마다 Owner RPC로 좌표 배열을 보내 본인 클라이언트에서만 safePointMarkerPrefab을
///   Instantiate/Destroy한다(다른 플레이어에게는 보이지 않음). 마커 Collider는 안전지점 판정용
///   Physics 쿼리(IsFreeStanding 등)를 오염시키지 않도록 생성 직후 강제로 비활성화한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerSafePointTracker : NetworkBehaviour
{
    [SerializeField] private PlayerGroundingSensor grounding;
    [SerializeField] private PlayerStateController stateController;

    [Header("기록 조건")]
    [SerializeField, Min(0f)] private float stableSeconds = 0.5f;
    [SerializeField, Range(0f, 30f)] private float maxFlatAngle = 5f;

    [Header("히스토리 (Queue)")]
    [SerializeField, Min(1)] private int safePointQueueCapacity = 5;
    [SerializeField, Min(0f)] private float minSafePointSpacing = 1.5f;

    [Header("복귀 지점 탐색")]
    [SerializeField] private float[] ringRadii = { 1f, 2f, 3f };
    [SerializeField, Min(0f)] private float returnYTolerance = 0.5f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0.05f)] private float occupancyRadius = 0.4f;
    [SerializeField, Min(0.1f)] private float probeUpDistance = 1.5f;
    [SerializeField, Min(0.1f)] private float probeDownDistance = 3.0f;

    [Header("안전지점 마커 (Owner 전용 로컬 시각화)")]
    [SerializeField] private GameObject safePointMarkerPrefab;

    private readonly Queue<Vector3> _safePointQueue = new Queue<Vector3>();
    private readonly List<GameObject> _markerInstances = new List<GameObject>();
    private Vector3 _spawnPoint;
    private Vector3 _newestSafePoint;
    private bool _hasSafePoint;
    private double _stableSince = -1.0;

    private double Now() => NetworkClock.Instance != null ? NetworkClock.Instance.GameNow : Time.timeAsDouble;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer)
            return;

        ResolveReferences();
        _spawnPoint = transform.position;
        EnqueueSafePoint(_spawnPoint);
    }

    public override void OnNetworkDespawn()
    {
        ClearMarkers();
        base.OnNetworkDespawn();
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
            return;

        if (IsStableRecordable())
        {
            if (_stableSince < 0.0)
                _stableSince = Now();
            else if (Now() - _stableSince >= stableSeconds)
                TryEnqueueSafePoint(transform.position);
        }
        else
        {
            _stableSince = -1.0;
        }
    }

    // 직전 큐 지점과 minSafePointSpacing 이상 떨어졌을 때만 새 지점을 쌓는다.
    private void TryEnqueueSafePoint(Vector3 point)
    {
        if (_hasSafePoint && (point - _newestSafePoint).sqrMagnitude < minSafePointSpacing * minSafePointSpacing)
            return;

        EnqueueSafePoint(point);
    }

    private void EnqueueSafePoint(Vector3 point)
    {
        _safePointQueue.Enqueue(point);
        while (_safePointQueue.Count > safePointQueueCapacity)
            _safePointQueue.Dequeue();

        _newestSafePoint = point;
        _hasSafePoint = true;

        SyncSafePointMarkersRpc(_safePointQueue.ToArray());
    }

    // 본인 클라이언트에서만 마커를 다시 그린다. 다른 플레이어는 이 RPC를 받지 않는다.
    [Rpc(SendTo.Owner)]
    private void SyncSafePointMarkersRpc(Vector3[] points)
    {
        ClearMarkers();

        if (safePointMarkerPrefab == null)
            return;

        foreach (Vector3 point in points)
        {
            GameObject marker = Instantiate(safePointMarkerPrefab, point, Quaternion.identity);

            // 시각화 전용 — 리슨 서버(호스트)에서는 같은 물리 씬에 생기므로,
            // Collider가 남아 있으면 IsFreeStanding/Ring 탐색의 점유 판정을 오염시킨다.
            foreach (Collider col in marker.GetComponentsInChildren<Collider>())
                col.enabled = false;

            _markerInstances.Add(marker);
        }
    }

    private void ClearMarkers()
    {
        foreach (GameObject marker in _markerInstances)
        {
            if (marker != null)
                Destroy(marker);
        }

        _markerInstances.Clear();
    }

    private bool IsStableRecordable()
    {
        if (grounding == null || !grounding.IsGrounded || grounding.IsMovingPlatform)
            return false;

        // 정적·평평한 지면만.
        if (Vector3.Angle(grounding.GroundNormal, Vector3.up) > maxFlatAngle)
            return false;

        if (stateController != null)
        {
            PlayerActionState s = stateController.CurrentState;
            if (s != PlayerActionState.Idle && s != PlayerActionState.Move)
                return false; // Dash·Knockback·Grabbed 등에서는 기록 안 함
        }

        return true;
    }

    /// <summary>서버 전용. 복귀 지점을 산출한다.
    /// 큐를 최신 → 과거 순으로 훑어 첫 번째로 설 수 있는 지점을 반환한다(각 후보는 막히면 Ring 탐색까지 시도).
    /// 모두 실패하면 최종 fallback으로 스폰 위치를 반환한다.</summary>
    public Vector3 ResolveReturnPoint(Vector3 fallPoint)
    {
        // 오래된 → 최신 순으로 채워지므로 역순으로 훑는다.
        Vector3[] candidates = _safePointQueue.ToArray();

        // 진단 — "마지막 위치가 아니라 스폰포인트로 돌아온다"의 원인을 가른다(2026-07-30).
        // ① 기록이 아예 안 된 것(큐가 비어있음)인지 ② 기록은 됐는데 큐의 모든 후보가 IsFreeStanding에서
        // 거부돼 스폰 폴백으로 넘어간 것인지가 육안으로는 똑같이 보인다.
        // 기록 조건은 접지 + 평지 + 이동플랫폼 아님 + 상태가 Idle/Move + stableSeconds 유지다 —
        // 전투 중엔 Attack/Dash 로 계속 끊기므로 한 번도 못 기록할 수 있다.
        for (int i = candidates.Length - 1; i >= 0; i--)
        {
            Vector3 basePoint = candidates[i];
            int rank = candidates.Length - i; // 1 = 최신

            Edit.Log(
                $"[Fall/진단] 복귀 지점 후보 {rank}/{candidates.Length} — 기준점={basePoint}, " +
                $"낙하지점={fallPoint}, 기준점이 설 수 있는가={IsFreeStanding(basePoint)}", this);

            if (IsFreeStanding(basePoint))
                return basePoint;

            if (TryFindRingCandidate(basePoint, fallPoint, out Vector3 resolved))
                return resolved;
        }

        Edit.Log($"[Fall/진단] 큐의 모든 안전지점이 막혀 스폰포인트로 폴백 — {_spawnPoint}", this);
        return _spawnPoint;
    }

    // basePoint가 막혀 있을 때 추락지점 반대 방향부터 ringRadii를 8방향으로 탐색한다.
    private bool TryFindRingCandidate(Vector3 basePoint, Vector3 fallPoint, out Vector3 resolved)
    {
        Vector3 away = basePoint - fallPoint;
        away.y = 0f;
        away = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.forward;

        for (int r = 0; r < ringRadii.Length; r++)
        {
            for (int d = 0; d < 8; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f * d, 0f) * away;
                Vector3 candidate = basePoint + dir * ringRadii[r];
                if (TryResolveFlatUnoccupied(candidate, basePoint.y, out resolved))
                    return true;
            }
        }

        resolved = basePoint;
        return false;
    }

    // 지점이 비어 있고(점유 X) 발밑에 지면이 있는지.
    private bool IsFreeStanding(Vector3 point)
    {
        if (Physics.CheckSphere(point + Vector3.up * occupancyRadius, occupancyRadius, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        return Physics.Raycast(point + Vector3.up * probeUpDistance, Vector3.down, probeUpDistance + 0.2f, groundMask, QueryTriggerInteraction.Ignore);
    }

    // 후보 XZ에서 지면을 찾아 평면·점유·Y 허용오차를 검사하고 스냅 위치를 돌려준다.
    private bool TryResolveFlatUnoccupied(Vector3 candidate, float referenceY, out Vector3 resolved)
    {
        resolved = candidate;

        Vector3 origin = new Vector3(candidate.x, referenceY + probeUpDistance, candidate.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeUpDistance + probeDownDistance, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        if (Vector3.Angle(hit.normal, Vector3.up) > maxFlatAngle)
            return false;

        if (Mathf.Abs(hit.point.y - referenceY) > returnYTolerance)
            return false;

        Vector3 standPoint = hit.point;
        if (Physics.CheckSphere(standPoint + Vector3.up * occupancyRadius, occupancyRadius, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        resolved = standPoint;
        return true;
    }

    private void ResolveReferences()
    {
        if (grounding == null)
            grounding = GetComponent<PlayerGroundingSensor>();

        if (stateController == null)
            stateController = GetComponent<PlayerStateController>();
    }
}
