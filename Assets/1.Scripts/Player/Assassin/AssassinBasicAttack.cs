using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 일반 평타. 거너 기본 공격과 같은 "홀드 + 입력 창" 연결 — 한 타의 End 순간 좌클릭이 눌려 있거나,
/// 그 타 재생 중 예약 창(클립 ComboWindowOpen~Close 이벤트, 가붕이와 같다) 안에서 좌클릭해 예약했으면 다음 타로 잇는다. 둘 다 아니면 그 타로 끝.
/// 입력과 방향은 오너가 보내며(눌림은 바뀔 때 즉시 + 눌린 동안 주기 신호) 시작/예약 승인(서버 자기 애니 진행 기준),
/// 연결 판단, 판정, 피해는 서버가 담당한다.
/// 모드는 <see cref="AssassinState"/> 로 고른다 — 변신 중 = Speed_Attack_Loop 4타 묶음, 일반 E 강화 준비 = 강타 1회, 그 외 = 일반 4타.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(AssassinConeAttack))]
public sealed class AssassinBasicAttack : BaseNetworkBehaviour, IPlayerBasicAttack, IAnimationEventRequirementSource
{
    private static readonly int DefaultAttackHash = Animator.StringToHash("DefaultAttack");
    private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int RunLoopHash = Animator.StringToHash("Run_Loop");

    // Animator AttackIndex — 일반 0~3 뒤에 강타·변신 묶음 상태를 둔다(AssassinShellAuthoring 이 같은 값으로 전이를 만든다).
    public const int EnhancedAnimatorIndex = 4;
    public const int TransformedAnimatorIndex = 5;

    // 눌린 동안 오너가 "아직 누르고 있다"(+ 조준)를 보내는 주기. 신호가 HoldStaleSeconds 넘게 끊기면 서버는 안 눌림으로 본다.
    private const float HoldSignalInterval = 0.1f;
    private const float HoldStaleSeconds = 0.5f;

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
    // 오너 자기 예약 — 이 타에서 이미 보냈으면 다시 보내지 않는다. 서버 거절(RejectQueueRpc)·다음 타·종료에서 풀린다.
    private bool ownerQueued;
    // [서버] 다음 타 예약 — 서버가 자기 애니 진행으로 창 안이라고 판단한 경우만.
    private bool hasQueuedNextAttack;
    // 오너 — 서버가 "누르고 있다"로 알고 있는가. 타가 이어지는 동안 유지하고 종료·취소(ResetRuntime)에서 푼다.
    private bool holdSent;
    private float nextHoldSignalTime;
    // [서버] 오너가 알린 눌림과 마지막 신호 시각 — End 순간 이 값(신선할 때만)이 다음 타 여부에 들어간다.
    private bool serverHeld;
    private float lastHoldSignalTime;
    // 이번 타(일반 1타·강타 1회)의 분노 게이지 충전을 이미 받았는가 — 타 하나에 1번(§4.2).
    private bool rageClaimed;
    private int hitsFired;
    private int currentStepIndex;
    private AssassinBasicAttackMode currentMode;
    private Vector3 attackDirection;
    private Vector3 serverNextDirection;
    private float fallbackEndTime;
    // 예약 창 — 이 타 클립의 ComboWindowOpen~Close(또는 End) 사이. 피어마다 자기 애니메이터 이벤트로 연다(오너 = 예약 거르기, 서버 = 승인).
    private bool comboWindowOpen;
    private PlayerActionState observedState;
    // 강타 전진 — 시뮬레이션 피어(오너, 서버 권위 이동이면 서버도)만 모터에 낸다. 끝·취소·막힘이면 0.
    private float lungeRemaining;
    private PlayerMotor motor;
    // 다 돈 뒤 공격(할 일 43): 크게 꺾으면 TurnToward 가 도는 동안 클립 시작·강타 전진을 늦춘다.
    // 판정·End 는 클립 이벤트라 같이 늦춰진다. 지연은 서버가 정해 StartRpc 로 싣는다 — 첫 타는 오너가 잰 값.
    private bool hasPendingClip;
    private float pendingClipTime;

    public bool CanStartApprovedAttack => data != null && data.TryGetNormalStep(0, out _);
    // 일반 타·강타·변신 묶음 어디서든 대시로 끊는다(할 일 9). 나간 타격의 피해는 그대로, 남은 동작만 끊긴다.
    public bool CanBeCanceledByDash => true;
    // 누른 프레임으로만 시작하고, 공격 중 스킬 입력은 선입력 버퍼(0.2초) 그대로다(할 일 44 — 거너만 다름).
    public bool StartsOnHold => false;
    public bool ReservesSkillAfterAttack => false;
    public void FinishWithoutChaining() { }
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
        motor = GetComponent<PlayerMotor>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        int stepCount = data != null && data.NormalSteps != null ? data.NormalSteps.Length : 4;
        combo = new AssassinComboModel(stepCount, data != null ? data.ComboContinuationSeconds : 0f);
        if (data != null)
            coneAttack.Configure(data.HittableLayers, data.MaxHitResults);

        observedState = player != null ? player.CurrentState : PlayerActionState.Idle;
        AnimationEventRequirements.WarnMissing(this, animator, this);
    }

    // 일반 4타·강타·변신 묶음 클립마다 판정(Hit)·예약 창(ComboWindowOpen·Close)·종료(End).
    public void CollectAnimationEventRequirements(System.Collections.Generic.List<AnimationEventRequirement> requirements)
    {
        if (data == null)
            return;

        if (data.NormalSteps != null)
        {
            foreach (AssassinBasicAttackStepData step in data.NormalSteps)
                AddStepRequirements(step, requirements);
        }

        AddStepRequirements(data.EnhancedStep, requirements);
        AddStepRequirements(data.TransformedStep, requirements);
    }

    private static void AddStepRequirements(
        AssassinBasicAttackStepData step, System.Collections.Generic.List<AnimationEventRequirement> requirements)
    {
        if (step == null || step.Clip == null)
            return;

        requirements.Add(AnimationEventRequirement.ForAttackClip(step.Clip, DefaultAttackAnimationEventType.Hit));
        requirements.Add(AnimationEventRequirement.ForAttackClip(step.Clip, DefaultAttackAnimationEventType.ComboWindowOpen));
        requirements.Add(AnimationEventRequirement.ForAttackClip(step.Clip, DefaultAttackAnimationEventType.ComboWindowClose));
        requirements.Add(AnimationEventRequirement.ForAttackClip(step.Clip, DefaultAttackAnimationEventType.End));
    }

    private void Update()
    {
        if (hasPendingClip && Time.time >= pendingClipTime)
        {
            hasPendingClip = false;
            PlayAttackClip();
        }

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

    public bool IsOutOfResource => false;

    public bool TryStart()
    {
        if (active || requesting || player == null || !CanStartApprovedAttack)
            return false;

        Vector3 direction = CurrentAimDirection();
        // 첫 타의 회전 지연은 오너의 몸 방향으로 잰다 — 서버 쪽 몸 방향은 NT 로 받은 늦은 값이다.
        float turnDelay = movement.GetAttackTurnDelay(direction);
        if (!IsNetworkActive)
            return StartServerAttack(direction, turnDelay);

        if (!IsOwner)
            return false;

        requesting = true;
        RequestStartRpc(direction, turnDelay);
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

    // 강타 전진(§8.2) — 승인 방향으로 판정 프레임 전까지 나눠 낸다. 적 통과 없음(벽·몸 콜라이더 켜진 적에 막힘).
    public void FixedTickMovement()
    {
        if (!active || hasPendingClip || lungeRemaining <= 0f || motor == null || data == null || !player.IsSimulating)
            return;

        float step = AssassinLungeRules.StepDistance(
            lungeRemaining, data.EnhancedLungeDistance, data.EnhancedLungeDuration, Time.fixedDeltaTime);
        lungeRemaining -= step;
        motor.AddGroundedDisplacement(attackDirection * step);
    }

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
        // 회전 대기 중 이벤트는 이 타가 아니라 직전 클립의 것이다.
        if (hasPendingClip)
            return;

        // 베기 연출은 전 피어 — 클립 Hit 이벤트는 모든 피어에서 재생된다(판정은 아래 서버만).
        // 일반 1~4타·변신 묶음 베기는 클립의 EffectAnimEvents 이벤트(PlayEffect / StartEffect·StopEffect)가 직접 낸다 —
        // 여기로 오는 건 강타(임시 프리팹)뿐이다(2026-10-19 민경).
        if (eventType == DefaultAttackAnimationEventType.Hit && active && view != null)
            view.PlayAttackSlash(currentMode, currentStepIndex);

        // 예약 창도 전 피어 — 오너는 자기 예약을, 서버는 승인을 각자 자기 애니 진행으로 거른다(가붕이 isComboWindowOpen 과 같다).
        if (active)
        {
            if (eventType == DefaultAttackAnimationEventType.ComboWindowOpen)
                comboWindowOpen = true;
            else if (eventType == DefaultAttackAnimationEventType.ComboWindowClose ||
                     eventType == DefaultAttackAnimationEventType.End)
                comboWindowOpen = false;
        }

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
    private bool StartServerAttack(Vector3 direction, float turnDelay)
    {
        if (!TrySelectAttackMode(out AssassinBasicAttackMode mode))
        {
            RejectStartRpcIfNeeded();
            return false;
        }

        // 강타·변신 묶음은 일반 4타 순서와 별개다.
        int stepIndex = mode == AssassinBasicAttackMode.Normal ? combo.Begin(Time.time) : 0;
        return StartServer(stepIndex, direction, mode, turnDelay);
    }

    private void TickInputSource()
    {
        TickHoldSignal();

        // 창 안에서 누른 프레임은 예약한다 — 홀드와 별개로, End 전에 손을 떼도 이 예약으로 이어진다. 오너도 자기 재생 기준 창으로 거른다.
        if (ownerQueued || !input.AttackPressed || !IsInCurrentComboWindow())
            return;

        // 예약에 쓴 입력은 선입력 버퍼에서 지운다 — 공격이 끝난 뒤 새 평타로 한 번 더 나가지 않게(할 일 44).
        // 창 밖 입력은 남긴다: 홀드로 End 까지 들고 있으면 이어가기에 쓰이고(그동안 0.2초 만료), 떼면 공격 종료 뒤 새 평타가 된다.
        input.ConsumeBufferedInput(PlayerBufferedInput.Attack);
        ownerQueued = true;
        Vector3 direction = CurrentAimDirection();

        if (!IsNetworkActive)
        {
            hasQueuedNextAttack = true;
            serverNextDirection = direction;
            return;
        }

        RequestQueueNextAttackRpc(direction);
    }

    // 홀드 — 눌림이 바뀌면 즉시, 눌린 동안은 HoldSignalInterval 마다 알린다(거너 HoldSignal). 조준도 실어 End 순간 이어질 다음 타 방향이 된다.
    private void TickHoldSignal()
    {
        bool held = input.AttackHeld;
        bool changed = held != holdSent;
        if (!changed && (!held || Time.time < nextHoldSignalTime))
            return;

        holdSent = held;
        nextHoldSignalTime = Time.time + HoldSignalInterval;
        Vector3 direction = CurrentAimDirection();

        if (!IsNetworkActive)
        {
            if (changed)
                ServerSetHold(held, direction);
            else
                ServerHoldSignal(direction);
            return;
        }

        if (changed)
            SetHoldRpc(held, direction);
        else
            HoldSignalRpc(direction);
    }

    [Rpc(SendTo.Server)]
    private void SetHoldRpc(bool held, Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerSetHold(held, direction);
    }

    [Rpc(SendTo.Server)]
    private void HoldSignalRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerHoldSignal(direction);
    }

    // 눌림 변경 — 누름이면 홀드를 (다시) 켠다. 뗐다 다시 눌러도 End 순간 눌려 있으면 잇는다.
    private void ServerSetHold(bool held, Vector3 direction)
    {
        if (!active)
            return;

        serverHeld = held;
        lastHoldSignalTime = Time.time;
        if (held)
            serverNextDirection = Flatten(direction);
    }

    // 주기 신호는 시각·조준만 갱신한다 — 홀드를 켜지는 않는다(뗀 뒤 늦게 온 신호가 홀드를 되살리지 않게, 거너와 같음).
    private void ServerHoldSignal(Vector3 direction)
    {
        if (!active || !serverHeld)
            return;

        lastHoldSignalTime = Time.time;
        serverNextDirection = Flatten(direction);
    }

    private bool IsServerHoldFresh() =>
        AssassinComboWindowRules.IsHoldFresh(serverHeld, lastHoldSignalTime, Time.time, HoldStaleSeconds);

    // 예약 창 = 이 타 클립의 ComboWindowOpen~Close 이벤트 사이. 회전 대기 중엔 이 타 클립이 아직 시작 전이라 창 밖이다.
    private bool IsInCurrentComboWindow()
    {
        return active && !hasPendingClip && comboWindowOpen;
    }

    [Rpc(SendTo.Server)]
    private void RequestStartRpc(Vector3 direction, float turnDelay, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (active || stateController == null || !stateController.CanAttack || !CanStartApprovedAttack)
        {
            RejectStartRpc();
            return;
        }

        // 각은 다시 재지 않고 상한만 자른다 — 지연이 길면 보낸 쪽만 손해다.
        StartServerAttack(direction, movement.ClampAttackTurnDelay(turnDelay));
    }

    [Rpc(SendTo.Server)]
    private void RequestQueueNextAttackRpc(Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        // 창 판정은 서버 자기 애니 진행으로 한다(가붕이 isComboWindowOpen 과 같은 권한). 창 밖이면 오너 예약을 푼다.
        if (!IsInCurrentComboWindow())
        {
            RejectQueueRpc();
            return;
        }

        hasQueuedNextAttack = true;
        serverNextDirection = Flatten(direction);
    }

    private bool StartServer(int stepIndex, Vector3 direction, AssassinBasicAttackMode mode, float turnDelay)
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

        BeginRuntime(stepIndex, direction, mode, step, turnDelay);
        if (IsNetworkActive)
            StartRpc(stepIndex, attackDirection, (byte)mode, turnDelay);
        player.RaiseServerBasicAttackStarted();   // 튜토리얼 훅 — 한 타 시작(적중 무관)
        return true;
    }

    [Rpc(SendTo.NotServer)]
    private void StartRpc(int stepIndex, Vector3 direction, byte modeValue, float turnDelay)
    {
        requesting = false;
        AssassinBasicAttackMode mode = (AssassinBasicAttackMode)modeValue;
        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step) || !player.BeginAttackState())
        {
            ResetRuntime();
            return;
        }

        BeginRuntime(stepIndex, direction, mode, step, turnDelay);
    }

    [Rpc(SendTo.Owner)]
    private void RejectStartRpc()
    {
        requesting = false;
    }

    // 서버가 창 밖이라 예약을 받지 않았다 — 남은 창에서 다시 누를 수 있게 오너 예약만 푼다.
    [Rpc(SendTo.Owner)]
    private void RejectQueueRpc()
    {
        ownerQueued = false;
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
        AssassinBasicAttackStepData step,
        float turnDelay)
    {
        active = true;
        requesting = false;
        ownerQueued = false;
        hasQueuedNextAttack = false;
        comboWindowOpen = false;
        rageClaimed = false;
        hitsFired = 0;
        currentStepIndex = stepIndex;
        currentMode = mode;
        attackDirection = Flatten(direction);
        serverNextDirection = attackDirection;
        fallbackEndTime = Time.time + turnDelay + step.ClipLength + data.EndFallbackPadding;

        StopLunge();
        if (mode == AssassinBasicAttackMode.Enhanced && motor != null && player.IsSimulating)
        {
            lungeRemaining = data.EnhancedLungeDistance;
            motor.MovementResolved += HandleLungeMovementResolved;
        }

        movement.TurnToward(attackDirection);
        player.SetAnimatorMoving(false);
        combatIdle?.NotifyAttackStarted();

        hasPendingClip = turnDelay > 0f;
        if (hasPendingClip)
        {
            pendingClipTime = Time.time + turnDelay;
            return;
        }

        PlayAttackClip();
    }

    private void PlayAttackClip()
    {
        // 변신 묶음 베기 루프는 클립의 StartEffect/StopEffect 이벤트(EffectAnimEvents)가 켜고 끈다.
        // 안전망 — 일반·강타 클립이 시작되면 남은 루프를 끈다. 끝·취소(StopEffect 프레임에 못 닿는 경우)는 ResetRuntime 이 끈다.
        if (view != null && currentMode != AssassinBasicAttackMode.Transformed)
            view.EndTransformedSlash();
        if (animator != null)
        {
            animator.SetInteger(AttackIndexHash, AnimatorIndexFor(currentMode, currentStepIndex));
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
        bool landed = coneAttack.Fire(
            attackDirection,
            step.Range,
            step.Angle,
            damage,
            data.TriggersOnHit);

        // 일반 타가 무언가를 맞히면 전 피어에서 그 타의 성공 연출을 일반 베기 위에 겹친다(강타·변신 묶음은 없음).
        if (landed && currentMode == AssassinBasicAttackMode.Normal && view != null)
            view.ServerNormalSlashHit(currentStepIndex);

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

        // 창 안에서 예약됐거나 지금 눌려 있으면 다음 타·다음 묶음(4타 뒤 1타 포함). 변신 종료 대기면 시작하지 못하고 여기서 끝난다.
        // 다음 타의 회전 지연은 서버가 잰다 — 이 타 내내 몸이 이 타 방향에 고정돼 있어 NT 값도 이미 따라와 있다.
        if (AssassinComboWindowRules.ShouldContinueAtEnd(hasQueuedNextAttack, IsServerHoldFresh()) &&
            StartServerAttack(serverNextDirection, movement.GetAttackTurnDelay(serverNextDirection)))
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
        ownerQueued = false;
        hasQueuedNextAttack = false;
        comboWindowOpen = false;
        holdSent = false;
        serverHeld = false;
        hitsFired = 0;
        fallbackEndTime = 0f;
        hasPendingClip = false;
        StopLunge();
        if (view != null)
            view.EndTransformedSlash();   // 묶음 끝·취소·시작 실패 — StopEffect 프레임 전에 끊겨도 변신 베기 루프가 남지 않게
    }

    // 벽·적 몸에 막히면 남은 전진을 버린다 — 막힌 자리에서 옆으로 미끄러지지 않게(Q 돌진과 같은 규칙).
    private void HandleLungeMovementResolved(Vector3 requested, Vector3 resolved, bool wasBlocked)
    {
        if (wasBlocked || lungeRemaining <= 0f)
            StopLunge();
    }

    private void StopLunge()
    {
        lungeRemaining = 0f;
        if (motor != null)
            motor.MovementResolved -= HandleLungeMovementResolved;
    }

    private void OnDisable()
    {
        StopLunge();
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
