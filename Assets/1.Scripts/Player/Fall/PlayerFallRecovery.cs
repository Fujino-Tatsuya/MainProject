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
    [SerializeField] private PlayerGroundingSensor grounding;

    [Header("Timing (PLAN §5)")]
    [SerializeField, Min(0f)] private float fallReturnDelay = 0.75f;
    [SerializeField, Min(0f)] private float landedFollowCameraDelay = 0.5f;
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
            {
                fallController.ServerFallSurvived += HandleServerFallSurvived;
                fallController.ServerFallDeath += HandleServerFallDeath;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (fallController != null)
        {
            fallController.ServerFallSurvived -= HandleServerFallSurvived;
            fallController.ServerFallDeath -= HandleServerFallDeath;
        }
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// 서버: 추락으로 사망한 경우에도 몸을 안전지점으로 되돌린다.
    /// 되돌리지 않으면 Soul이 추락 지점(경계 아래)에 그대로 남는데, 추락 복귀는 Alive 전용이라
    /// 스스로 올라올 수단이 없다. 생존 복귀와 달리 무적·충전 리셋·낙하 연출은 걸지 않는다
    /// — 이미 사망 처리가 진행 중이고 Soul 전환 연출과 겹치면 안 되기 때문.
    /// </summary>
    private void HandleServerFallDeath(FallDeathContext context)
    {
        Vector3 returnPoint = safePointTracker != null
            ? safePointTracker.ResolveReturnPoint(context.FallPoint)
            : transform.position;

        ReturnAfterFallDeathRpc(returnPoint);
    }

    [Rpc(SendTo.Owner)]
    private void ReturnAfterFallDeathRpc(Vector3 returnPoint)
    {
        StopAllCoroutines(); // 생존 복귀 연출이 돌고 있었다면 중단

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

        // 낙하 뷰로 전환돼 있을 수 있으므로 일반 추적 카메라로 되돌린다.
        // 입력 잠금은 건드리지 않는다 — 사망/Soul 전환은 PlayerLifeInputPolicy가 소유한다.
        CameraTargetSwitcher.Active?.ReturnToPlayerView();
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
        // 서버가 확정한 안전 복귀지점의 월드 Y를 고정하고, Player의 X/Z만 계속 추적한다.
        CameraTargetSwitcher.Active?.EnterFallView(returnPoint.y);
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

        // 5. 물리 프로브가 복귀 지점 접지를 확인한 뒤 Follow Camera로 복귀.
        // Rigidbody.position 반영과 GroundingSensor.FixedUpdate 사이의 실행 순서에 의존하지 않도록
        // 최소 한 번의 물리 프레임을 넘긴 후 즉시 샘플을 갱신한다.
        if (grounding != null)
        {
            yield return new WaitForFixedUpdate();
            grounding.RefreshNow();

            while (!grounding.IsGrounded)
            {
                yield return new WaitForFixedUpdate();
                grounding.RefreshNow();
            }
        }

        // 착지가 화면에 먼저 보이도록 잠시 Fall View를 유지한 뒤 Follow Camera로 복귀한다.
        if (landedFollowCameraDelay > 0f)
            yield return new WaitForSeconds(landedFollowCameraDelay);

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
        if (grounding == null) grounding = GetComponent<PlayerGroundingSensor>();
    }
}
