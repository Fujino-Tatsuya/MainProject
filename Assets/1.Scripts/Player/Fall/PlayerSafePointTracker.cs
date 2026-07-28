using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 생존 추락 복귀용 안전지점 추적(서버 권한). (PLAN §13)
///
/// - Grounded Idle/Move가 stableSeconds 유지되고, 정적·평평한 Ground/Env(이동 플랫폼 제외)일 때만 기록.
/// - Dash 중에는 기록하지 않는다. 최초 스폰 위치를 신뢰 가능한 fallback으로 보관.
/// - 복귀 지점이 막혔으면 추락지점 반대 방향부터 ringRadii(1/2/3m)를 8방향으로 탐색,
///   각 후보는 평면·점유·Y 허용오차를 검사하고 실패하면 스폰 위치를 반환.
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

    [Header("복귀 지점 탐색")]
    [SerializeField] private float[] ringRadii = { 1f, 2f, 3f };
    [SerializeField, Min(0f)] private float returnYTolerance = 0.5f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0.05f)] private float occupancyRadius = 0.4f;
    [SerializeField, Min(0.1f)] private float probeUpDistance = 1.5f;
    [SerializeField, Min(0.1f)] private float probeDownDistance = 3.0f;

    private Vector3 _spawnPoint;
    private Vector3 _safePoint;
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
        _safePoint = _spawnPoint;
        _hasSafePoint = true;
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
            {
                _safePoint = transform.position;
                _hasSafePoint = true;
            }
        }
        else
        {
            _stableSince = -1.0;
        }
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

    /// <summary>서버 전용. 복귀 지점을 산출한다(막히면 Ring 탐색, 최종 fallback은 스폰).</summary>
    public Vector3 ResolveReturnPoint(Vector3 fallPoint)
    {
        Vector3 basePoint = _hasSafePoint ? _safePoint : _spawnPoint;

        if (IsFreeStanding(basePoint))
            return basePoint;

        Vector3 away = basePoint - fallPoint;
        away.y = 0f;
        away = away.sqrMagnitude > 0.001f ? away.normalized : Vector3.forward;

        for (int r = 0; r < ringRadii.Length; r++)
        {
            for (int d = 0; d < 8; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f * d, 0f) * away;
                Vector3 candidate = basePoint + dir * ringRadii[r];
                if (TryResolveFlatUnoccupied(candidate, basePoint.y, out Vector3 resolved))
                    return resolved;
            }
        }

        return _spawnPoint;
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
