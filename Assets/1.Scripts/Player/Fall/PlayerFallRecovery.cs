using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 생존 추락 복귀 흐름. (PLAN §13, §14 / W11·W12)
///
/// 서버가 PlayerFallController.ServerFallSurvived를 받아 복귀 지점·복귀 무적·충전 리셋을 확정하고,
/// 오너에게 로컬 연출(Float Camera → 지연 → 응시 → Follow Camera → 입력 잠금)을 지시한다.
/// 서버가 복귀 위치를 먼저 확정하고 Motor를 순간이동한 뒤 오너에게 forceSnap 보정을 보낸다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerMotor))]
public sealed class PlayerFallRecovery : NetworkBehaviour
{
    [SerializeField] private PlayerFallController fallController;
    [SerializeField] private PlayerSafePointTracker safePointTracker;
    [SerializeField] private PlayerLandingProtection protection;
    [SerializeField] private PlayerDashController dashController;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private PlayerGroundingSensor grounding;
    [SerializeField] private PlayerMotor motor;

    [Header("Timing (PLAN §5)")]
    [SerializeField, Min(0f)] private float fallReturnDelay = 0.75f;
    [SerializeField, Min(0f)] private float landedFollowCameraDelay = 0.5f;
    [SerializeField, Min(0f)] private float fallReturnInputLock = 0.5f;
    [SerializeField, Min(0f)] private float fallReturnInvulnerability = 1.5f;

    private Player player;
    private Coroutine serverReturnRoutine;
    private Coroutine ownerRecoveryRoutine;

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
        CancelServerReturnRoutine();
        CancelOwnerRecoveryRoutine();

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

        CancelServerReturnRoutine();
        if (!TeleportOnServer(returnPoint))
            return;
        ReturnAfterFallDeathRpc();
    }

    [Rpc(SendTo.Owner)]
    private void ReturnAfterFallDeathRpc()
    {
        CancelOwnerRecoveryRoutine();

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

        CancelServerReturnRoutine();
        serverReturnRoutine = StartCoroutine(ServerReturnRoutine(returnPoint, fallPoint));
        BeginRecoveryRpc(returnPoint, fallPoint);
    }

    [Rpc(SendTo.Owner)]
    private void BeginRecoveryRpc(Vector3 returnPoint, Vector3 fallPoint)
    {
        CancelOwnerRecoveryRoutine();
        ownerRecoveryRoutine = StartCoroutine(OwnerRecoveryRoutine(returnPoint, fallPoint));
    }

    private IEnumerator ServerReturnRoutine(Vector3 returnPoint, Vector3 fallPoint)
    {
        yield return new WaitForSeconds(fallReturnDelay);

        if (!TeleportOnServer(returnPoint, sendForceSnap: false))
        {
            serverReturnRoutine = null;
            yield break;
        }

        Vector3 look = fallPoint - returnPoint;
        look.y = 0f;
        if (look.sqrMagnitude > 0.0001f)
            movement?.RotateImmediately(look);

        // 회전까지 Motor 상태에 반영한 뒤 보내야 forceSnap이 오너의 동일한 응시 방향을 보존한다.
        if (!IsOwner)
            player.ForceOwnerReconciliation();

        serverReturnRoutine = null;
    }

    private IEnumerator OwnerRecoveryRoutine(Vector3 returnPoint, Vector3 fallPoint)
    {
        // 1. 추락 판정 즉시 Float Camera + 입력·전투 잠금(중력·관성 유지).
        // 서버가 확정한 안전 복귀지점의 월드 Y를 고정하고, Player의 X/Z만 계속 추적한다.
        CameraTargetSwitcher.Active?.EnterFallView(returnPoint.y);
        input?.SetInputEnabled(false);

        // 2. fallReturnDelay 동안 계속 낙하.
        yield return new WaitForSeconds(fallReturnDelay);

        // 3. 서버가 같은 시점에 순간이동하고 forceSnap을 보낸다. 오너는 위치를 직접 쓰지 않는다.
        // 4. 보정 도착 전 한 프레임에도 기존 연출 방향이 유지되도록 로컬 응시도 즉시 맞춘다.
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
        ownerRecoveryRoutine = null;
    }

    private bool TeleportOnServer(Vector3 returnPoint, bool sendForceSnap = true)
    {
        if (!IsServer || motor == null || (!IsOwner && player == null))
        {
            Debug.LogError(
                $"[PlayerFallRecovery] 서버 순간이동 불가: IsServer={IsServer}, " +
                $"motor={(motor != null ? "present" : "missing")}, " +
                $"player={(player != null ? "present" : "missing")}",
                this);
            return false;
        }

        // 🔴 오너 권위 브랜치: 위치의 주인은 오너다. 서버가 자기 사본을 옮겨봐야 오너 권위
        // NetworkTransform 이 오너의 위치를 복제하므로 되돌아온다. 오너에게 직접 옮기라고 지시한다.
        // (서버 권위 브랜치에서는 반대다 — 서버가 옮기고 보정 채널의 forceSnap 으로 오너를 맞춘다.)
        if (!Player.UsesServerAuthoritativeMovement)
        {
            if (IsOwner)
                motor.TeleportAuthoritative(returnPoint);
            else
                TeleportOwnerRpc(returnPoint);
            return true;
        }

        motor.TeleportAuthoritative(returnPoint);
        if (sendForceSnap && !IsOwner)
            player.ForceOwnerReconciliation();
        return true;
    }

    /// <summary>오너 권위에서만 쓴다. 위치를 확정하는 주체가 오너이므로 오너가 직접 옮긴다.</summary>
    [Rpc(SendTo.Owner)]
    private void TeleportOwnerRpc(Vector3 returnPoint)
    {
        if (motor != null)
            motor.TeleportAuthoritative(returnPoint);
    }

    /// <summary>
    /// 진행 중인 낙하 복구를 **서버에서** 중단한다(멱등). 보스룸 강제/패드 이동처럼
    /// 다른 주체가 위치를 확정할 때 부른다 — 안 부르면 지연 복귀가 나중에 **안전지점으로 되돌린다.**
    /// 오너 쪽 연출(낙하 카메라·입력 잠금)도 함께 되돌린다.
    /// </summary>
    public void CancelRecoveryServer()
    {
        if (!IsServer)
            return;

        // 🔴 여기서 로컬 코루틴 필드로 "복구 중인가" 를 판정하면 안 된다.
        //    서버 코루틴은 fallReturnDelay 뒤 끝나지만, **오너 쪽 연출(접지 대기·카메라 복귀·입력 잠금)은
        //    그보다 오래 간다.** 게다가 원격 플레이어의 `ownerRecoveryRoutine` 은 그 클라에만 있어서
        //    서버 사본에서는 항상 null 이다. 두 필드를 보고 조기 반환하면 **원격 오너가 낙하 카메라와
        //    입력 잠금에 갇힌 채** 보스룸으로 끌려간다.
        //    → 항상 보낸다. 오너 쪽 처리는 멱등하다.
        CancelServerReturnRoutine();
        CancelRecoveryOwnerRpc();
    }

    /// <summary>오너의 복구 연출을 중단하고 카메라·입력을 정상으로 돌린다.</summary>
    [Rpc(SendTo.Owner)]
    private void CancelRecoveryOwnerRpc()
    {
        CancelOwnerRecoveryRoutine();

        // 코루틴이 중간에 끊기면 카메라와 입력이 잠긴 채로 남는다 — 둘 다 명시적으로 되돌린다.
        CameraTargetSwitcher.Active?.ReturnToPlayerView();
        input?.SetInputEnabled(true);
    }

    private void CancelServerReturnRoutine()
    {
        if (serverReturnRoutine == null)
            return;

        StopCoroutine(serverReturnRoutine);
        serverReturnRoutine = null;
    }

    private void CancelOwnerRecoveryRoutine()
    {
        if (ownerRecoveryRoutine == null)
            return;

        StopCoroutine(ownerRecoveryRoutine);
        ownerRecoveryRoutine = null;
    }

    private void ResolveReferences()
    {
        if (fallController == null) fallController = GetComponent<PlayerFallController>();
        if (safePointTracker == null) safePointTracker = GetComponent<PlayerSafePointTracker>();
        if (protection == null) protection = GetComponent<PlayerLandingProtection>();
        if (dashController == null) dashController = GetComponent<PlayerDashController>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (input == null) input = GetComponent<PlayerInputReader>();
        if (grounding == null) grounding = GetComponent<PlayerGroundingSensor>();
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (player == null) player = GetComponent<Player>();
    }
}
