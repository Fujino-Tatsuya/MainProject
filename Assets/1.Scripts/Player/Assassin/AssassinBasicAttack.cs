using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 일반 평타. 추가 클릭은 예약하지 않고, 한 타가 끝나는 순간까지 처음 누른 좌클릭을 계속 유지한 경우에만 다음 타로 이어진다.
/// 입력과 방향은 오너가 보내며 시작/연결 승인, 판정, 피해는 서버가 담당한다.
/// 모드는 <see cref="AssassinState"/> 로 고른다 — 변신 중 = Speed_Attack_Loop 4타 묶음, 일반 E 강화 준비 = 강타 1회, 그 외 = 일반 4타.
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

    // Animator AttackIndex — 일반 0~3 뒤에 강타·변신 묶음 상태를 둔다(AssassinShellAuthoring 이 같은 값으로 전이를 만든다).
    public const int EnhancedAnimatorIndex = 4;
    public const int TransformedAnimatorIndex = 5;

    [SerializeField] private AssassinBasicAttackData data;
    [SerializeField] private Animator animator;

    private Player player;
    private PlayerStateController stateController;
    private PlayerInputReader input;
    private PlayerMovement movement;
    private PlayerAimIndicator aim;
    private AssassinConeAttack coneAttack;
    private AssassinCombatIdle combatIdle;
    private AssassinState assassinState;
    private AssassinSkillView view;
    private AssassinComboModel combo;

    private bool active;
    private bool requesting;
    private bool endingGracefully;
    private bool finishingTail;
    private bool releaseLatched;
    private bool serverContinueHeld;
    // 이번 타(일반 1타·강타 1회)의 분노 게이지 충전을 이미 받았는가 — 타 하나에 1번(§4.2).
    private bool rageClaimed;
    // 대시 취소 경계 — 이번 타 클립의 ComboWindowOpen 이벤트를 받았는가. 전 피어가 자기 애니메이터 이벤트로 세운다.
    private bool comboWindowOpenedThisStep;
    private int hitsFired;
    private int currentStepIndex;
    private AssassinBasicAttackMode currentMode;
    private Vector3 attackDirection;
    private Vector3 serverNextDirection;
    private float fallbackEndTime;
    private float nextInputSyncTime;
    private PlayerActionState observedState;

    public bool CanStartApprovedAttack => data != null && data.TryGetNormalStep(0, out _);
    // 이번 타(일반 1타·강타·변신 묶음) 클립의 ComboWindowOpen 전까지만 대시로 끊는다(할 일 9).
    // 이 창은 대시 취소 경계로만 쓴다 — 다음 타 연결은 여전히 End 시점 serverContinueHeld 다.
    public bool CanBeCanceledByDash => BasicAttackDashCancel.BeforeComboWindowEvent(comboWindowOpenedThisStep);
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
        assassinState = GetComponent<AssassinState>();
        view = GetComponent<AssassinSkillView>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        int stepCount = data != null && data.NormalSteps != null ? data.NormalSteps.Length : 4;
        combo = new AssassinComboModel(stepCount, data != null ? data.ComboContinuationSeconds : 0f);
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
            return StartServerAttack(direction);

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
        // 베기 연출은 전 피어 — 클립 Hit 이벤트는 모든 피어에서 재생된다(판정은 아래 서버만).
        if (eventType == DefaultAttackAnimationEventType.Hit && active && view != null)
            view.PlayAttackSlash(currentMode, currentStepIndex);

        // 대시 취소 경계는 오너가 판단하므로 서버 가드 앞에서 세운다.
        if (eventType == DefaultAttackAnimationEventType.ComboWindowOpen && active)
            comboWindowOpenedThisStep = true;

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
    /// [서버] 강화 준비/변신 상태를 이 한 지점에서만 읽어 공격 모드를 고른다 — 변신 > 강화 > 일반.
    /// 시작 후에는 <see cref="currentMode"/>를 고정해 타 도중 상태 변화가 현재 판정을 바꾸지 않는다.
    /// 변신 종료 대기 중(만료·수동 해제)에는 새 묶음을 시작하지 않는다(§6·§10.2).
    /// </summary>
    private bool TrySelectAttackMode(out AssassinBasicAttackMode mode)
    {
        mode = AssassinBasicAttackMode.Normal;
        if (assassinState == null)
            return true;

        if (assassinState.IsTransformed)
        {
            mode = AssassinBasicAttackMode.Transformed;
            return !assassinState.IsTransformEndPending;
        }

        if (assassinState.IsEnhancedReady)
            mode = AssassinBasicAttackMode.Enhanced;

        return true;
    }

    /// <summary>[서버] 모드를 골라 첫 타(또는 다음 타·다음 묶음)를 시작한다. 실패하면 오너 요청을 거절한다.</summary>
    private bool StartServerAttack(Vector3 direction)
    {
        if (!TrySelectAttackMode(out AssassinBasicAttackMode mode))
        {
            RejectStartRpcIfNeeded();
            return false;
        }

        // 강타·변신 묶음은 일반 4타 순서와 별개다.
        int stepIndex = mode == AssassinBasicAttackMode.Normal ? combo.Begin(Time.time) : 0;
        return StartServer(stepIndex, direction, mode);
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

        StartServerAttack(direction);
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

    private bool StartServer(int stepIndex, Vector3 direction, AssassinBasicAttackMode mode)
    {
        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step))
        {
            RejectStartRpcIfNeeded();
            return false;
        }

        if (!player.BeginAttackState())
        {
            RejectStartRpcIfNeeded();
            return false;
        }

        // 강타가 실제로 시작되는 순간에만 강화 소모 + 일반 E 쿨(§8.2). 무시된 입력은 위에서 이미 빠졌다.
        // 강타 뒤 일반 평타는 1타부터(§5.1).
        if (mode == AssassinBasicAttackMode.Enhanced)
        {
            combo.Reset();
            assassinState?.ServerConsumeEnhancement();
        }

        BeginRuntime(stepIndex, direction, mode, step);
        if (IsNetworkActive)
            StartRpc(stepIndex, attackDirection, (byte)mode);
        return true;
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
        rageClaimed = false;
        comboWindowOpenedThisStep = false;
        hitsFired = 0;
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
            animator.SetInteger(AttackIndexHash, AnimatorIndexFor(mode, currentStepIndex));
            animator.SetTrigger(DefaultAttackHash);
        }
    }

    private static int AnimatorIndexFor(AssassinBasicAttackMode mode, int stepIndex) => mode switch
    {
        AssassinBasicAttackMode.Enhanced => EnhancedAnimatorIndex,
        AssassinBasicAttackMode.Transformed => TransformedAnimatorIndex,
        _ => stepIndex,
    };

    // 타격마다 현재 범위를 다시 판정한다(§3.3). 변신 묶음은 Hit 이벤트 4번 = 4타.
    private void FireCurrentHit()
    {
        if (!active || !TryGetStep(currentMode, currentStepIndex, out AssassinBasicAttackStepData step) ||
            hitsFired >= step.HitCount)
            return;

        hitsFired++;
        int damage = Mathf.Max(0, Mathf.RoundToInt(player.FinalAttackDamage * step.AttackDamageMultiplier));
        // 백어택(A11) — 일반·강타·변신 묶음 모두 배율을 싣고, 변신 중이면 위치 무관(타격 시점의 변신 여부).
        if (assassinState != null)
            coneAttack.SetBackAttack(assassinState.BackAttackMultiplier, assassinState.IsTransformed);
        coneAttack.Fire(
            attackDirection,
            step.Range,
            step.Angle,
            damage,
            data.TriggersOnHit);

        // 일반 1타·강타가 유효 대상(§4.3)을 맞히면 대상 수와 무관하게 1번 충전(§4.2). 변신 묶음은 충전 없음 — 모델도 막는다.
        if (!rageClaimed && currentMode != AssassinBasicAttackMode.Transformed && assassinState != null &&
            AssassinHitTargets.ContainsRewardTarget(coneAttack.LastLandedUnits))
        {
            rageClaimed = true;
            assassinState.ServerTryGainRage(currentMode == AssassinBasicAttackMode.Enhanced
                ? AssassinRageSource.EnhancedStrike
                : AssassinRageSource.BasicAttack);
        }
    }

    private void CompleteCurrentStep()
    {
        if (!active || !HasGameplayAuthority)
            return;

        if (currentMode == AssassinBasicAttackMode.Normal)
            combo.Complete(currentStepIndex, Time.time);

        // 계속 누르고 있으면 다음 타·다음 묶음. 변신 종료 대기면 시작하지 못하고 여기서 끝난다.
        if (serverContinueHeld && StartServerAttack(serverNextDirection))
            return;

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
        comboWindowOpenedThisStep = false;
        hitsFired = 0;
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
        step = null;
        if (data == null)
            return false;

        switch (mode)
        {
            case AssassinBasicAttackMode.Normal:
                return data.TryGetNormalStep(stepIndex, out step);
            case AssassinBasicAttackMode.Enhanced:
                return data.TryGetEnhancedStep(out step);
            case AssassinBasicAttackMode.Transformed:
                return data.TryGetTransformedStep(out step);
            default:
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
