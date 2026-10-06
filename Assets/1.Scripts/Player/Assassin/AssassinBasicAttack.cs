using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 일반 평타. 추가 클릭은 예약하지 않고, 한 타가 끝나는 순간까지 처음 누른 좌클릭을 계속 유지한 경우에만 다음 타로 이어진다.
/// 입력과 방향은 오너가 보내며 시작/연결 승인, 판정, 피해는 서버가 담당한다.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(AssassinConeAttack))]
public sealed class AssassinBasicAttack : BaseNetworkBehaviour, IPlayerBasicAttack
{
    private static readonly int DefaultAttackHash = Animator.StringToHash("DefaultAttack");
    private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int RunLoopHash = Animator.StringToHash("Run_Loop");

    private const float InputSyncInterval = 0.08f;

    [SerializeField] private AssassinBasicAttackData data;
    [SerializeField] private Animator animator;

    private Player player;
    private PlayerStateController stateController;
    private PlayerInputReader input;
    private PlayerMovement movement;
    private PlayerAimIndicator aim;
    private AssassinConeAttack coneAttack;
    private AssassinCombatIdle combatIdle;
    private AssassinComboModel combo;

    private bool active;
    private bool requesting;
    private bool endingGracefully;
    private bool finishingTail;
    private bool releaseLatched;
    private bool serverContinueHeld;
    private bool hitConsumed;
    private int currentStepIndex;
    private AssassinBasicAttackMode currentMode;
    private Vector3 attackDirection;
    private Vector3 serverNextDirection;
    private float fallbackEndTime;
    private float nextInputSyncTime;
    private PlayerActionState observedState;

    public bool CanStartApprovedAttack => data != null && data.TryGetNormalStep(0, out _);
    public bool CanBeCanceledByDash => true;
    public AssassinBasicAttackData Data => data;

    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;
    private bool IsInputSource => !IsNetworkActive || IsOwner;

    private void Awake()
    {
        player = GetComponent<Player>();
        stateController = GetComponent<PlayerStateController>();
        input = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aim = GetComponent<PlayerAimIndicator>();
        coneAttack = GetComponent<AssassinConeAttack>();
        combatIdle = GetComponent<AssassinCombatIdle>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        int stepCount = data != null && data.NormalSteps != null ? data.NormalSteps.Length : 4;
        combo = new AssassinComboModel(stepCount, data != null ? data.ComboContinuationSeconds : 0.8f);
        if (data != null)
            coneAttack.Configure(data.HittableLayers, data.MaxHitResults);

        observedState = player != null ? player.CurrentState : PlayerActionState.Idle;
    }

    private void Update()
    {
        if (player == null || combo == null)
            return;

        PlayerActionState state = player.CurrentState;
        if (state != observedState)
        {
            // Q·E·R은 모두 Skill/Focus 상태를 거친다. 간파(Interrupt)는 기획상 콤보 리셋 대상이 아니다.
            if (PlayerStateController.IsSkillState(state))
                combo.Reset();
            observedState = state;
        }
    }

    public bool TryStart()
    {
        if (active || requesting || player == null || !CanStartApprovedAttack)
            return false;

        Vector3 direction = CurrentAimDirection();
        if (!IsNetworkActive)
        {
            StartServer(combo.Begin(Time.time), direction, SelectAttackMode());
            return true;
        }

        if (!IsOwner)
            return false;

        requesting = true;
        RequestStartRpc(direction);
        return true;
    }

    public void BeginFromState() { }

    public void Tick()
    {
        if (!active)
            return;

        if (IsInputSource)
            TickInputSource();

        if (HasGameplayAuthority && Time.time >= fallbackEndTime)
            CompleteCurrentStep();
    }

    public void FixedTickMovement() { }

    public void CancelCurrentAttack()
    {
        if (endingGracefully)
            return;

        bool hadAttack = active || requesting;
        ResetRuntime();
        combo?.Reset();
        finishingTail = false;

        if (!hadAttack)
            return;

        ReturnToIdlePose();
        if (IsNetworkActive && IsServer)
            CancelRpc();
    }

    public void CancelFinishingTailForMovement()
    {
        if (!finishingTail)
            return;

        finishingTail = false;
        if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, RunLoopHash))
            animator.CrossFadeInFixedTime(RunLoopHash, 0.05f);
    }

    public void EndCurrentAttack()
    {
        if (HasGameplayAuthority)
            CompleteCurrentStep();
    }

    public void HitCurrentAttack()
    {
        if (HasGameplayAuthority)
            FireCurrentHit();
    }

    public void HandleAnimationEvent(DefaultAttackAnimationEventType eventType)
    {
        if (IsNetworkActive && !IsServer)
            return;
        if (!active)
            return;

        switch (eventType)
        {
            case DefaultAttackAnimationEventType.Hit:
                FireCurrentHit();
                break;
            case DefaultAttackAnimationEventType.End:
                CompleteCurrentStep();
                break;
        }
    }

    public void HandleAnimatorMove(Vector3 deltaPosition, Vector3 animatorForward) { }

    public void SetAnimator(Animator newAnimator)
    {
        if (newAnimator == null)
            return;

        animator = newAnimator;
        combatIdle?.SetAnimator(newAnimator);
    }

    /// <summary>
    /// A7은 강화 준비/변신 상태를 이 한 지점에서만 읽어 공격 모드를 고른다.
    /// 시작 후에는 <see cref="currentMode"/>를 고정해 타 도중 상태 변화가 현재 판정을 바꾸지 않는다.
    /// </summary>
    private AssassinBasicAttackMode SelectAttackMode()
    {
        return AssassinBasicAttackMode.Normal;
    }

    private void TickInputSource()
    {
        if (!releaseLatched && !input.AttackHeld)
            releaseLatched = true;

        Vector3 direction = CurrentAimDirection();
        bool held = !releaseLatched;

        if (!IsNetworkActive)
        {
            serverContinueHeld = held;
            serverNextDirection = direction;
            return;
        }

        if (!IsOwner || Time.time < nextInputSyncTime)
            return;

        nextInputSyncTime = Time.time + InputSyncInterval;
        SyncContinuationInputRpc(held, direction);
    }

    [Rpc(SendTo.Server)]
    private void RequestStartRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (active || stateController == null || !stateController.CanAttack || !CanStartApprovedAttack)
        {
            RejectStartRpc();
            return;
        }

        StartServer(combo.Begin(Time.time), direction, SelectAttackMode());
    }

    [Rpc(SendTo.Server)]
    private void SyncContinuationInputRpc(bool held, Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || !active)
            return;

        // 한 타 도중 한 번 놓았으면 다시 누른 입력은 다음 타 예약으로 취급하지 않는다.
        if (!held)
            serverContinueHeld = false;
        if (serverContinueHeld)
            serverNextDirection = Flatten(direction);
    }

    private void StartServer(int stepIndex, Vector3 direction, AssassinBasicAttackMode mode)
    {
        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step))
        {
            RejectStartRpcIfNeeded();
            return;
        }

        if (!player.BeginAttackState())
        {
            RejectStartRpcIfNeeded();
            return;
        }

        BeginRuntime(stepIndex, direction, mode, step);
        if (IsNetworkActive)
            StartRpc(stepIndex, attackDirection, (byte)mode);
    }

    [Rpc(SendTo.NotServer)]
    private void StartRpc(int stepIndex, Vector3 direction, byte modeValue)
    {
        requesting = false;
        AssassinBasicAttackMode mode = (AssassinBasicAttackMode)modeValue;
        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step) || !player.BeginAttackState())
        {
            ResetRuntime();
            return;
        }

        BeginRuntime(stepIndex, direction, mode, step);
    }

    [Rpc(SendTo.Owner)]
    private void RejectStartRpc()
    {
        requesting = false;
    }

    [Rpc(SendTo.NotServer)]
    private void EndRpc()
    {
        EndLocal();
    }

    [Rpc(SendTo.NotServer)]
    private void CancelRpc()
    {
        bool hadAttack = active || requesting;
        ResetRuntime();
        combo?.Reset();
        finishingTail = false;
        if (hadAttack)
        {
            ReturnToIdlePose();
            endingGracefully = true;
            try
            {
                player.EndAttackState();
            }
            finally
            {
                endingGracefully = false;
            }
        }
    }

    private void BeginRuntime(
        int stepIndex,
        Vector3 direction,
        AssassinBasicAttackMode mode,
        AssassinBasicAttackStepData step)
    {
        active = true;
        requesting = false;
        releaseLatched = false;
        serverContinueHeld = true;
        hitConsumed = false;
        currentStepIndex = stepIndex;
        currentMode = mode;
        attackDirection = Flatten(direction);
        serverNextDirection = attackDirection;
        fallbackEndTime = Time.time + step.MotionDuration + data.EndFallbackPadding;
        nextInputSyncTime = Time.time;

        movement.RotateImmediately(attackDirection);
        player.SetAnimatorMoving(false);
        combatIdle?.NotifyAttackStarted();

        if (animator != null)
        {
            animator.SetInteger(AttackIndexHash, currentStepIndex);
            animator.SetTrigger(DefaultAttackHash);
        }
    }

    private void FireCurrentHit()
    {
        if (!active || hitConsumed || !TryGetStep(currentMode, currentStepIndex, out AssassinBasicAttackStepData step))
            return;

        hitConsumed = true;
        int damage = Mathf.Max(0, Mathf.RoundToInt(player.FinalAttackDamage * step.AttackDamageMultiplier));
        coneAttack.Fire(
            attackDirection,
            step.Range,
            step.Angle,
            damage,
            data.TriggersOnHit);
    }

    private void CompleteCurrentStep()
    {
        if (!active || !HasGameplayAuthority)
            return;

        combo.Complete(currentStepIndex, Time.time);
        if (serverContinueHeld)
        {
            int next = combo.Begin(Time.time);
            StartServer(next, serverNextDirection, SelectAttackMode());
            return;
        }

        EndServer();
    }

    private void EndServer()
    {
        EndLocal();
        if (IsNetworkActive)
            EndRpc();
    }

    private void EndLocal()
    {
        endingGracefully = true;
        try
        {
            ResetRuntime();
            finishingTail = true;
            player.EndAttackState();
        }
        finally
        {
            endingGracefully = false;
        }
    }

    private void ResetRuntime()
    {
        active = false;
        requesting = false;
        releaseLatched = false;
        serverContinueHeld = false;
        hitConsumed = false;
        fallbackEndTime = 0f;
    }

    private void RejectStartRpcIfNeeded()
    {
        requesting = false;
        if (IsNetworkActive && IsServer)
            RejectStartRpc();
    }

    private bool TryGetStep(
        AssassinBasicAttackMode mode,
        int stepIndex,
        out AssassinBasicAttackStepData step)
    {
        // A7은 이 switch에 Enhanced/Transformed 데이터 선택만 추가한다.
        switch (mode)
        {
            case AssassinBasicAttackMode.Normal:
                if (data != null)
                    return data.TryGetNormalStep(stepIndex, out step);
                step = null;
                return false;
            default:
                step = null;
                return false;
        }
    }

    private void ReturnToIdlePose()
    {
        if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, IdleHash))
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    private Vector3 CurrentAimDirection()
    {
        return Flatten(aim != null ? aim.AimDirection : transform.forward);
    }

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
    }
}
