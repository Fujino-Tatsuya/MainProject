using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

// 콤보 입력 베이스는 하나다: ComboWindowOpen~Close(또는 End) 사이에
// 버튼이 눌려 있(었)으면 다음 타가 예약된다(래치).
// 이 정책은 체인이 마지막 타까지 간 뒤의 동작만 결정한다.
public enum DefaultAttackChainPolicy
{
    // 한 바퀴 돌면 종료. 재시작하려면 버튼을 뗐다가 다시 눌러야 한다.
    Once,
    // 누르고 있는 동안 마지막 타 이후 첫 타로 순환.
    Loop
}

public enum DefaultAttackMovementType
{
    None,
    ScriptedForwardDistance,
    AnimationRootMotionProjected
}

public enum DefaultAttackRotationType
{
    None,
    SnapOnStart,
    TrackAimDuringAttack
}

public enum DefaultAttackAnimationEventType
{
    Hit = 0,
    ComboWindowOpen = 1,
    ComboWindowClose = 2,
    End = 3
}

public enum DefaultAttackHitType
{
    Overlap,
    Projectile,
    Raycast
}

[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(PlayerDefaultAttack))]
public class DefaultAttackController : BaseNetworkBehaviour
{
    private static readonly int DefaultAttackHash = Animator.StringToHash("DefaultAttack");
    private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    // 공격 상태 이름 컨벤션: 모든 캐릭터 컨트롤러는 Default_Attack0..N 상태를 가진다.
    // 체인 수는 attackSteps.Length가 결정하며, ValidateAttackStates에서 컨트롤러와 대조한다.
    private static int GetAttackStateHash(int index)
    {
        return Animator.StringToHash($"Default_Attack{index}");
    }

    [SerializeField] private Animator animator;
    [SerializeField] private DefaultAttackData attackData;
    [SerializeField] private PlayerDefaultAttack playerDefaultAttack;
    [SerializeField] private DefaultAttackChainPolicy chainPolicy = DefaultAttackChainPolicy.Loop;
    [SerializeField] private DefaultAttackStep[] attackSteps =
    {
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep()
    };
    [SerializeField] private ColliderInfo defaultHitbox;
    [SerializeField] private LayerMask hittableLayers;
    [SerializeField] private float endFallbackPadding = 0.1f;

    private Player player;
    private PlayerStateController stateController;
    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;
    private Vector3 attackDirection;
    private Vector3 queuedAttackDirection;
    private int currentAttackIndex;
    private float moveRemaining;
    private float moveSpeed;
    private float attackEndFallbackTime;
    private bool isRequestingAttack;
    private bool isComboWindowOpen;
    private bool hasQueuedNextAttack;
    private bool hasStartedAttack;

    public bool IsAttacking => player != null && player.CurrentState == PlayerActionState.Attack;
    public bool CanRequestStart => HasAttackSteps && CurrentStepDuration > 0f;
    public bool CanStartApprovedAttack => HasAttackSteps && CurrentStepDuration > 0f;
    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        player = GetComponent<Player>();
        stateController = GetComponent<PlayerStateController>();
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aimIndicator = GetComponent<PlayerAimIndicator>();

        if (playerDefaultAttack == null)
            playerDefaultAttack = GetComponent<PlayerDefaultAttack>();

        if (playerDefaultAttack == null)
            playerDefaultAttack = gameObject.AddComponent<PlayerDefaultAttack>();

        if (attackData != null)
            ApplyData(attackData);
        else
            playerDefaultAttack.Configure(defaultHitbox, hittableLayers);

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null && !animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();

        if (animator != null && !animator.TryGetComponent(out PlayerRootMotionRelay _))
            animator.gameObject.AddComponent<PlayerRootMotionRelay>();

        ValidateAttackStates();
    }

    // 데이터의 콤보 스텝 수만큼 Default_Attack0..N 상태가 컨트롤러에 있는지 검증한다.
    // 어긋나면 CrossFade가 조용히 실패하므로 초기화 시점에 에러로 드러낸다.
    private void ValidateAttackStates()
    {
        if (animator == null || animator.runtimeAnimatorController == null || attackSteps == null)
            return;

        for (int i = 0; i < attackSteps.Length; i++)
        {
            if (!animator.HasState(0, GetAttackStateHash(i)))
            {
                Debug.LogError(
                    $"Animator Controller '{animator.runtimeAnimatorController.name}'에 'Default_Attack{i}' 상태가 없습니다. " +
                    $"공격 데이터는 {attackSteps.Length}타 콤보를 요구합니다.",
                    this);
            }

            AnimationClip stepClip = attackSteps[i]?.Clip;
            if (stepClip != null && !HasComboWindowOpenEvent(stepClip))
            {
                Debug.LogWarning(
                    $"클립 '{stepClip.name}'에 ComboWindowOpen 이벤트" +
                    $"(HandleDefaultAttackEvent, int={(int)DefaultAttackAnimationEventType.ComboWindowOpen})가 없습니다. " +
                    "윈도우가 열리지 않으면 이 스텝에서 다음 타를 예약할 수 없습니다.",
                    this);
            }
        }
    }

    private static bool HasComboWindowOpenEvent(AnimationClip clip)
    {
        foreach (AnimationEvent clipEvent in clip.events)
        {
            if (clipEvent.functionName == nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent) &&
                clipEvent.intParameter == (int)DefaultAttackAnimationEventType.ComboWindowOpen)
                return true;
        }

        return false;
    }

    public bool TryStart()
    {
        if (IsAttacking || isRequestingAttack || player == null || !CanRequestStart)
        {
            BeforeMergeTestLog.Info(
                "ATTACK",
                "REQUEST_BLOCKED_OWNER",
                $"state={(player == null ? "null" : player.CurrentState.ToString())}, isAttacking={IsAttacking}, isRequesting={isRequestingAttack}, hasPlayer={player != null}, canRequest={CanRequestStart}, IsOwner={IsOwner}, IsServer={IsServer}",
                this);
            return false;
        }

        Vector3 requestedDirection = GetCurrentAimDirection();

        if (!IsNetworkActive)
        {
            StartAttackServer(0, requestedDirection, true);
            return true;
        }

        if (!IsOwner)
            return false;

        isRequestingAttack = true;
        BeforeMergeTestLog.Info(
            "ATTACK",
            "REQUEST_TX_OWNER",
            $"state={player.CurrentState}, ownerClientId={OwnerClientId}, direction={requestedDirection}, isRequesting={isRequestingAttack}",
            this);
        RequestStartAttackRpc(requestedDirection);
        return true;
    }

    public void BeginFromState()
    {
        // Attack state entry is driven by StartAttackServer/PlayAttackClientRpc.
    }

    public void ApplyData(DefaultAttackData data)
    {
        if (data == null)
            return;

        attackData = data;
        chainPolicy = data.ChainPolicy;
        attackSteps = data.AttackSteps;
        hittableLayers = data.HittableLayers;

        if (playerDefaultAttack != null)
            playerDefaultAttack.Configure(defaultHitbox, hittableLayers, data.MaxHitResults);

        ValidateAttackStates();
    }

    public void SetAnimator(Animator newAnimator)
    {
        if (newAnimator == null)
            return;

        animator = newAnimator;

        if (!animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();

        if (!animator.TryGetComponent(out PlayerRootMotionRelay _))
            animator.gameObject.AddComponent<PlayerRootMotionRelay>();

        ValidateAttackStates();
    }

    public void Tick()
    {
        if ((!IsNetworkActive || IsOwner) && IsAttacking)
            TryQueueNextAttackFromInput();

        if (HasGameplayAuthority && IsAttacking)
            TickServerFallbacks();

        if (!IsNetworkActive || IsServer || IsOwner)
            TickMovementAndRotation();
    }

    public void CancelCurrentAttack()
    {
        ResetAttackRuntime();

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    public void HandleAnimationEvent(DefaultAttackAnimationEventType eventType)
    {
        BeforeMergeTestLog.Info(
            "ATTACK",
            "ANIM_EVENT",
            $"event={eventType}, IsServer={IsServer}, 처리={!(IsNetworkActive && !IsServer)}, state={(player == null ? "null" : player.CurrentState.ToString())}",
            this);

        if (IsNetworkActive && !IsServer)
            return;

        switch (eventType)
        {
            case DefaultAttackAnimationEventType.Hit:
                playerDefaultAttack.HitCurrentStep();
                break;

            case DefaultAttackAnimationEventType.ComboWindowOpen:
                isComboWindowOpen = true;
                break;

            case DefaultAttackAnimationEventType.ComboWindowClose:
                isComboWindowOpen = false;
                break;

            case DefaultAttackAnimationEventType.End:
                CompleteCurrentAttackStep();
                break;
        }
    }

    public void EndCurrentAttack()
    {
        if (HasGameplayAuthority)
            CompleteCurrentAttackStep();
    }

    public void HitCurrentAttack()
    {
        if (HasGameplayAuthority)
            playerDefaultAttack.HitCurrentStep();
    }

    public void HandleAnimatorMove(Vector3 deltaPosition, Vector3 animatorForward)
    {
        // OnAnimatorMove는 Walk/Idle 중에도 매 프레임 호출되므로,
        // 공격 상태의 루트모션만 이동으로 변환한다.
        if (!IsAttacking)
            return;

        if (!HasAttackStep(currentAttackIndex))
            return;

        if (IsNetworkActive && !IsServer && !IsOwner)
            return;

        DefaultAttackStep step = attackSteps[currentAttackIndex];
        if (step.MovementType != DefaultAttackMovementType.AnimationRootMotionProjected)
            return;

        animatorForward.y = 0f;
        if (animatorForward.sqrMagnitude < 0.001f)
            animatorForward = attackDirection;

        float forwardDistance = Vector3.Dot(deltaPosition, animatorForward.normalized);
        if (Mathf.Abs(forwardDistance) <= 0.0001f)
            return;

        movement.MoveRoot(attackDirection * forwardDistance);
    }

    [Rpc(SendTo.Server)]
    private void RequestStartAttackRpc(Vector3 requestedDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        bool canApprove = CanApproveServerAttack();
        BeforeMergeTestLog.Info(
            "ATTACK",
            "REQUEST_RX_SERVER",
            $"sender={rpcParams.Receive.SenderClientId}, ownerClientId={OwnerClientId}, state={(player == null ? "null" : player.CurrentState.ToString())}, canApprove={canApprove}",
            this);

        if (!canApprove)
        {
            RejectStartAttackClientRpc(CreateOwnerClientRpcParams());
            return;
        }

        StartAttackServer(0, requestedDirection, true);
    }

    [Rpc(SendTo.Server)]
    private void RequestQueueNextAttackRpc(Vector3 requestedDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsAttacking)
            return;

        if (!isComboWindowOpen)
            return;

        hasQueuedNextAttack = true;
        queuedAttackDirection = ResolveAttackDirection(requestedDirection);
    }

    [ClientRpc]
    private void PlayDefaultAttackClientRpc(int attackIndex, Vector3 direction, bool triggerAttack)
    {
        if (IsServer)
            return;

        BeforeMergeTestLog.Info(
            "ATTACK",
            "PLAY_RX_OWNER",
            $"index={attackIndex}, stateBefore={(player == null ? "null" : player.CurrentState.ToString())}, trigger={triggerAttack}, IsOwner={IsOwner}",
            this);
        isRequestingAttack = false;
        StartAttackPresentation(attackIndex, direction, triggerAttack);

        if (player != null && player.CurrentState != PlayerActionState.Attack)
            player.SetState(PlayerActionState.Attack);
    }

    [ClientRpc]
    private void EndDefaultAttackClientRpc()
    {
        if (IsServer)
            return;

        ResetAttackRuntime();

        if (player != null && player.CurrentState == PlayerActionState.Attack)
            player.SetState(PlayerActionState.Idle);

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    [ClientRpc]
    private void RejectStartAttackClientRpc(ClientRpcParams clientRpcParams = default)
    {
        BeforeMergeTestLog.Info(
            "ATTACK",
            "REJECT_RX_OWNER",
            $"IsOwner={IsOwner}, state={(player == null ? "null" : player.CurrentState.ToString())}, isRequestingBefore={isRequestingAttack}",
            this);
        if (IsOwner)
            isRequestingAttack = false;
    }

    private void StartAttackServer(int attackIndex, Vector3 direction, bool triggerAttack)
    {
        if (!HasAttackStep(attackIndex))
        {
            BeforeMergeTestLog.Warning(
                "ATTACK",
                "START_ABORT_SERVER",
                $"reason=missingStep, index={attackIndex}, state={(player == null ? "null" : player.CurrentState.ToString())} — 오너 래치 고착 경로",
                this);
            return;
        }

        DefaultAttackStep step = attackSteps[attackIndex];
        if (step.MotionDuration <= 0f)
        {
            BeforeMergeTestLog.Warning(
                "ATTACK",
                "START_ABORT_SERVER",
                $"reason=invalidMotionDuration, index={attackIndex}, duration={step.MotionDuration}, state={(player == null ? "null" : player.CurrentState.ToString())} — 오너 래치 고착 경로",
                this);
            return;
        }

        isRequestingAttack = false;
        hasStartedAttack = true;
        hasQueuedNextAttack = false;
        isComboWindowOpen = false;
        currentAttackIndex = attackIndex;
        attackDirection = ResolveAttackDirection(direction);
        queuedAttackDirection = attackDirection;
        attackEndFallbackTime = Time.time + step.MotionDuration + Mathf.Max(0f, endFallbackPadding);

        int damageSnapshot = CalculateDamageSnapshot(step);
        BeforeMergeTestLog.Info(
            "ATTACK",
            "START_APPROVED_SERVER",
            $"index={attackIndex}, stateBefore={(player == null ? "null" : player.CurrentState.ToString())}, damageSnapshot={damageSnapshot}, hitType={step.HitType}",
            this);
        playerDefaultAttack.PrepareStep(step, damageSnapshot, attackDirection);

        if (player != null && player.CurrentState != PlayerActionState.Attack)
        {
            PlayerActionState stateBefore = player.CurrentState;
            bool stateChanged = player.SetState(PlayerActionState.Attack);
            BeforeMergeTestLog.Info(
                "ATTACK",
                "STATE_SET_SERVER",
                $"requested=Attack, stateBefore={stateBefore}, changed={stateChanged}, stateAfter={player.CurrentState}",
                this);
        }

        StartAttackPresentation(attackIndex, attackDirection, triggerAttack);

        if (IsNetworkActive)
            PlayDefaultAttackClientRpc(attackIndex, attackDirection, triggerAttack);
    }

    private void StartAttackPresentation(int attackIndex, Vector3 direction, bool triggerAttack)
    {
        if (!HasAttackStep(attackIndex))
            return;

        DefaultAttackStep step = attackSteps[attackIndex];
        currentAttackIndex = attackIndex;
        attackDirection = ResolveAttackDirection(direction);
        moveRemaining = step.MovementType == DefaultAttackMovementType.ScriptedForwardDistance
            ? Mathf.Max(step.ForwardDistance, 0f)
            : 0f;
        moveSpeed = step.MotionDuration > 0f ? moveRemaining / step.MotionDuration : 0f;

        if (step.RotationType == DefaultAttackRotationType.SnapOnStart)
            movement.RotateImmediately(attackDirection);

        player.SetAnimatorMoving(false);

        if (animator == null)
            return;

        animator.SetInteger(AttackIndexHash, currentAttackIndex);

        if (triggerAttack)
            animator.SetTrigger(DefaultAttackHash);
        else
            animator.CrossFadeInFixedTime(GetAttackStateHash(currentAttackIndex), 0.05f);
    }

    private void CompleteCurrentAttackStep()
    {
        if (!HasGameplayAuthority || !IsAttacking || !hasStartedAttack)
            return;

        if (ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection))
        {
            StartAttackServer(nextIndex, nextDirection, false);
            return;
        }

        EndAttackServer();
    }

    private bool ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection)
    {
        nextIndex = currentAttackIndex + 1;
        nextDirection = queuedAttackDirection;
        bool hasNext = nextIndex < attackSteps.Length;

        if (!hasQueuedNextAttack)
            return false;

        switch (chainPolicy)
        {
            case DefaultAttackChainPolicy.Once:
                if (!hasNext)
                    return false;
                break;

            case DefaultAttackChainPolicy.Loop:
                if (!hasNext)
                    nextIndex = 0;
                break;

            default:
                return false;
        }

        return HasAttackStep(nextIndex);
    }

    private void EndAttackServer()
    {
        ResetAttackRuntime();

        if (player != null && player.CurrentState == PlayerActionState.Attack)
            player.SetState(PlayerActionState.Idle);

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);

        if (IsNetworkActive)
            EndDefaultAttackClientRpc();
    }

    private void TickServerFallbacks()
    {
        if (hasStartedAttack && attackEndFallbackTime > 0f && Time.time >= attackEndFallbackTime)
            CompleteCurrentAttackStep();
    }

    private void TickMovementAndRotation()
    {
        if (!HasAttackStep(currentAttackIndex))
            return;

        DefaultAttackStep step = attackSteps[currentAttackIndex];

        if (step.RotationType == DefaultAttackRotationType.TrackAimDuringAttack)
            movement.RotateToward(GetCurrentAimDirection(), step.TrackRotationSpeed);

        if (moveRemaining <= 0f)
            return;

        float moveDistance = Mathf.Min(moveSpeed * Time.deltaTime, moveRemaining);
        moveRemaining -= moveDistance;
        movement.MoveRoot(attackDirection * moveDistance);
    }

    private void TryQueueNextAttackFromInput()
    {
        // 콤보 윈도우 안에서 한 순간이라도 눌려 있었으면 예약되는 래치.
        // 윈도우 밖 입력은 아래 가드(오프라인)와 서버 RPC 가드에서 걸러진다.
        if (!inputReader.AttackHeld && !inputReader.AttackPressed)
            return;

        Vector3 direction = GetCurrentAimDirection();

        if (!IsNetworkActive)
        {
            if (!isComboWindowOpen)
                return;

            hasQueuedNextAttack = true;
            queuedAttackDirection = direction;
            return;
        }

        RequestQueueNextAttackRpc(direction);
    }

    private bool CanApproveServerAttack()
    {
        if (!HasAttackStep(0))
            return false;

        if (player == null || stateController == null || !stateController.CanAttack)
            return false;

        return true;
    }

    private int CalculateDamageSnapshot(DefaultAttackStep step)
    {
        int baseDamage = player != null ? player.AttackDamage : 0;
        int calculatedDamage = Mathf.RoundToInt(baseDamage * step.AttackDamageMultiplier) + step.FlatDamageBonus;

        return Mathf.Max(0, calculatedDamage);
    }

    private Vector3 GetCurrentAimDirection()
    {
        if (aimIndicator != null)
            return ResolveAttackDirection(aimIndicator.AimDirection);

        return ResolveAttackDirection(attackDirection);
    }

    private Vector3 ResolveAttackDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude >= 0.001f)
            return direction.normalized;

        if (attackDirection.sqrMagnitude >= 0.001f)
            return attackDirection.normalized;

        return transform.forward;
    }

    private void ResetAttackRuntime()
    {
        moveRemaining = 0f;
        moveSpeed = 0f;
        attackEndFallbackTime = 0f;
        currentAttackIndex = 0;
        hasQueuedNextAttack = false;
        isComboWindowOpen = false;
        hasStartedAttack = false;
        isRequestingAttack = false;
    }

    private ClientRpcParams CreateOwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }

    private bool HasAttackSteps => attackSteps != null && attackSteps.Length > 0;

    private bool HasAttackStep(int attackIndex)
    {
        return HasAttackSteps &&
            attackIndex >= 0 &&
            attackIndex < attackSteps.Length &&
            attackSteps[attackIndex] != null;
    }

    private float CurrentStepDuration => HasAttackStep(0) ? attackSteps[0].MotionDuration : 0f;
}

[Serializable]
public class DefaultAttackStep
{
    [SerializeField] private AnimationClip clip;
    // 이 스텝(1타)의 모션 재생 길이. End 이벤트 누락 시 종료 fallback과
    // ScriptedForwardDistance 이동 속도 계산의 기준. 0이면 클립 길이를 사용.
    [FormerlySerializedAs("duration")]
    [SerializeField] private float motionDuration = 0;
    [SerializeField] private DefaultAttackMovementType movementType = DefaultAttackMovementType.ScriptedForwardDistance;
    [SerializeField] private float forwardDistance = 0.5f;
    [SerializeField] private DefaultAttackRotationType rotationType = DefaultAttackRotationType.SnapOnStart;
    [SerializeField] private float trackRotationSpeed = 12f;
    [SerializeField] private DefaultAttackHitType hitType = DefaultAttackHitType.Overlap;
    [SerializeField] private ColliderInfo hitbox;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float raycastRange = 20f;
    [SerializeField] private float attackDamageMultiplier = 1f;
    [SerializeField] private int flatDamageBonus;

    public AnimationClip Clip => clip;
    public float MotionDuration => motionDuration > 0f ? motionDuration : ClipDuration;
    public DefaultAttackMovementType MovementType => movementType;
    public float ForwardDistance => forwardDistance;
    public DefaultAttackRotationType RotationType => rotationType;
    public float TrackRotationSpeed => trackRotationSpeed;
    public DefaultAttackHitType HitType => hitType;
    public ColliderInfo Hitbox => hitbox;
    public GameObject ProjectilePrefab => projectilePrefab;
    public float ProjectileSpeed => projectileSpeed;
    public float RaycastRange => raycastRange;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public int FlatDamageBonus => flatDamageBonus;

    private float ClipDuration => clip != null ? clip.length : 0f;
}
