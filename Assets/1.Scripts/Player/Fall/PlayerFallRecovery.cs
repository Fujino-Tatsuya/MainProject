using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 생존 추락 복귀 흐름. (PLAN §13, §14 / W11·W12)
///
/// 서버가 PlayerFallController.ServerFallSurvived를 받아 복귀 지점·복귀 무적·충전 리셋을 확정하고,
/// 오너에게 로컬 연출(Float Camera → 지연 → 순간이동/응시 → Follow Camera → 입력 잠금)을 지시한다.
/// 위치 쓰기는 오너 권한이므로 순간이동은 오너가 수행한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerFallRecovery : NetworkBehaviour
{
    [SerializeField] private PlayerFallController fallController;
    [SerializeField] private PlayerSafePointTracker safePointTracker;
    [SerializeField] private PlayerLandingProtection protection;
    [SerializeField] private PlayerDashController dashController;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private Rigidbody body;

    [Header("Timing (PLAN §5)")]
    [SerializeField, Min(0f)] private float fallReturnDelay = 0.75f;
    [SerializeField, Min(0f)] private float fallReturnInputLock = 0.5f;
    [SerializeField, Min(0f)] private float fallReturnInvulnerability = 1.5f;

    private void Awake()
    {
        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            ResolveReferences();
            if (fallController != null)
                fallController.ServerFallSurvived += HandleServerFallSurvived;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (fallController != null)
            fallController.ServerFallSurvived -= HandleServerFallSurvived;
        base.OnNetworkDespawn();
    }

    // 서버: 복귀 지점·무적·충전 리셋 확정 후 오너에게 연출 지시.
    private void HandleServerFallSurvived(Vector3 fallPoint)
    {
        Vector3 returnPoint = safePointTracker != null
            ? safePointTracker.ResolveReturnPoint(fallPoint)
            : transform.position;

        // 복귀 무적/Blink는 낙하 지연 + 복귀 무적 전체를 덮는다(Stun 없음).
        if (protection != null)
            protection.BeginProtection(InvulnerabilityCause.FallRecovery, fallReturnDelay + fallReturnInvulnerability, applyStun: false);

        // 충전을 1개로 초기화하고 회복 타이머를 새로 시작. (PLAN §10)
        if (dashController != null)
            dashController.ServerResetChargeToOne();

        BeginRecoveryRpc(returnPoint, fallPoint);
    }

    [Rpc(SendTo.Owner)]
    private void BeginRecoveryRpc(Vector3 returnPoint, Vector3 fallPoint)
    {
        StopAllCoroutines();
        StartCoroutine(OwnerRecoveryRoutine(returnPoint, fallPoint));
    }

    private IEnumerator OwnerRecoveryRoutine(Vector3 returnPoint, Vector3 fallPoint)
    {
        // 1. 추락 판정 즉시 Float Camera + 입력·전투 잠금(중력·관성 유지).
        CameraTargetSwitcher.Active?.EnterFallView();
        input?.SetInputEnabled(false);

        // 2. fallReturnDelay 동안 계속 낙하.
        yield return new WaitForSeconds(fallReturnDelay);

        // 3. 안전지점으로 순간이동 + 속도 0.
        if (body != null)
        {
            body.position = returnPoint;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            transform.position = returnPoint;
        }

        // 4. 추락 지점을 바라본다.
        Vector3 look = fallPoint - returnPoint;
        look.y = 0f;
        if (look.sqrMagnitude > 0.0001f)
            movement?.RotateImmediately(look);

        // 5. 즉시 Follow Camera로 복귀.
        CameraTargetSwitcher.Active?.ReturnToPlayerView();

        // 9. 예측 충전 리셋(권한은 서버가 이미 반영).
        dashController?.OwnerResetChargeToOne();

        // 6. 입력 잠금 0.5초 유지 후 해제. (무적/Blink는 서버가 별도로 1.5초 유지)
        yield return new WaitForSeconds(fallReturnInputLock);
        input?.SetInputEnabled(true);
    }

    private void ResolveReferences()
    {
        if (fallController == null) fallController = GetComponent<PlayerFallController>();
        if (safePointTracker == null) safePointTracker = GetComponent<PlayerSafePointTracker>();
        if (protection == null) protection = GetComponent<PlayerLandingProtection>();
        if (dashController == null) dashController = GetComponent<PlayerDashController>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (input == null) input = GetComponent<PlayerInputReader>();
        if (body == null) body = GetComponent<Rigidbody>();
    }
}
