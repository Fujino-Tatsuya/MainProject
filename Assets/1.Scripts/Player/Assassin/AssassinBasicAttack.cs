using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 일반 평타. 거너 기본 공격과 같은 "홀드 + 입력 창" 연결 — 한 타의 End 순간 좌클릭이 눌려 있거나,
/// 그 타 재생 중 예약 창(클립 ComboWindowOpen~Close 이벤트, 가붕이와 같다) 안에서 좌클릭해 예약했으면 다음 타로 잇는다. 둘 다 아니면 그 타로 끝.
/// 입력과 방향은 오너가 보내며(눌림은 바뀔 때 즉시 + 눌린 동안 주기 신호) 시작/예약 승인(서버 자기 애니 진행 기준),
/// 연결 판단, 판정, 피해는 서버가 담당한다.
/// 모드는 <see cref="AssassinState"/> 로 고른다 — 변신 중 = Speed_Attack_Loop 4타 묶음, 일반 E 강화 준비 = 강타 1회, 그 외 = 일반 4타.
/// 일반 타는 매 타 시작 때 짧게 전진한다(할 일 96) — 이동 입력이 있으면 그 방향, 없으면 조준. 몸·판정·전진이 같은 방향이다.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(AssassinConeAttack))]
public sealed class AssassinBasicAttack : BaseNetworkBehaviour, IPlayerBasicAttack, IAnimationEventRequirementSource,
    IPlayerAttackSpeedAnimatorUser
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
    // 스킬 입력으로 지금 타에서 마무리 중(할 일 77 후속) — 오너·서버가 각자 들고 공격이 끝나면(ResetRuntime) 풀린다.
    private bool finishingChain;
    // 이번 타(일반 1타·강타 1회)의 분노 게이지 충전을 이미 받았는가 — 타 하나에 1번(§4.2).
    private bool rageClaimed;
    private int hitsFired;
    private int currentStepIndex;
    private AssassinBasicAttackMode currentMode;
    // 이 타의 방향 — 몸 회전·판정·전진이 모두 이 값이다(일반 타는 이동 입력 우선, AssassinAttackDirectionRules).
    private Vector3 attackDirection;
    // [서버] 다음 타 재료 — 조준과 이동 입력(없으면 0)을 따로 들고, 다음 타 모드가 정해진 뒤 방향을 고른다.
    private Vector3 serverNextAim;
    private Vector3 serverNextMove;
    private float fallbackEndTime;
    // 예약 창 — 이 타 클립의 ComboWindowOpen~Close(또는 End) 사이. 피어마다 자기 애니메이터 이벤트로 연다(오너 = 예약 거르기, 서버 = 승인).
    private bool comboWindowOpen;
    private PlayerActionState observedState;
    // 전진(일반 타 매 타·강타) — 시뮬레이션 피어(오너, 서버 권위 이동이면 서버도)만 모터에 낸다. 끝·취소·막힘이면 0.
    private float lungeRemaining;
    private float lungeDistance;
    private float lungeDuration;
    private PlayerMotor motor;
    private PlayerDashController dash;
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

    // 버퍼의 스킬 입력 때문에 다음 타를 잇지 않는다(할 일 77 후속) — 뗀 것처럼: 오너는 눌림·창 예약을 더 보내지 않고,
    // 서버는 눌림·예약을 지우고 이 공격이 끝날 때까지 새 눌림·예약을 받지 않는다. 이 타는 End 에서 기존 정상 종료로 끝난다.
    public void FinishWithoutChaining()
    {
        if (!active || finishingChain || !IsInputSource)
            return;

        finishingChain = true;
        ownerQueued = true; // 남은 창에서 다시 예약하지 않게(서버 거절로 풀리지도 않는다 — 아래 서버 래치)
        holdSent = false;
        if (IsNetworkActive)
            FinishChainRpc();
        else
            ServerFinishChain();
    }
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
        dash = GetComponent<PlayerDashController>();

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

    // 공격속도(할 일 91 T5) — 평타 상태에 속도 파라미터가 없어 공격 중(전 피어 — StartRpc~EndRpc) Animator.speed 로 탄다.
    // 판정·예약 창·End 는 클립 이벤트라 같이 빨라진다. 꼬리(finishingTail)는 1 배속.
    public bool ScalesAnimatorWithAttackSpeed => active;

    private float AttackSpeedRate => player != null ? player.AttackSpeedRate : 1f;

    public bool TryStart()
    {
        if (active || requesting || player == null || !CanStartApprovedAttack)
            return false;

        Vector3 aimDirection = CurrentAimDirection();
        Vector3 moveDirection = CurrentMoveDirection();
        // 첫 타의 회전 지연은 오너의 몸 방향으로 잰다 — 서버 쪽 몸 방향은 NT 로 받은 늦은 값이다.
        // 모드는 서버가 정하므로 복제된 어쌔신 상태로 같은 선택을 해 그 타의 방향으로 잰다(드문 엇갈림은 서버 상한 자르기에 맡긴다).
        TrySelectAttackMode(out AssassinBasicAttackMode predictedMode);
        float turnDelay = movement.GetAttackTurnDelay(
            Flatten(AssassinAttackDirectionRules.Resolve(predictedMode, moveDirection, aimDirection)));
        if (!IsNetworkActive)
            return StartServerAttack(aimDirection, moveDirection, turnDelay);

        if (!IsOwner)
            return false;

        requesting = true;
        RequestStartRpc(aimDirection, moveDirection, turnDelay, player.CreateStatReport());
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

    // 전진 — 일반 타 매 타(할 일 96)·강타(§8.2) 모두 승인 방향으로 판정 프레임 전까지 나눠 낸다. 적 통과 없음(벽·몸 콜라이더 켜진 적에 막힘).
    public void FixedTickMovement()
    {
        if (!active || hasPendingClip || lungeRemaining <= 0f || motor == null || data == null || !player.IsSimulating)
            return;

        // 판정 프레임이 공격속도만큼 당겨지므로 전진 시간도 배율로 나눈다 — 거리는 같다(할 일 91 T5).
        float step = AssassinLungeRules.StepDistance(
            lungeRemaining, lungeDistance,
            PlayerAttackSpeedRules.Duration(lungeDuration, AttackSpeedRate), Time.fixedDeltaTime);
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

    // 꼬리(finishingTail)는 EndLocal 에서만 켜진다 — 예약·홀드로 다음 타가 이어지면 공격 상태가 계속돼 Move 로 들어오지 않으므로,
    // 이동 입력이 있어도 콤보 도중엔 여기 오지 않는다(할 일 96 확인). 예약·홀드 없이 끝난 꼬리만 Run 으로 끊는다.
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

        // 베기 연출(일반 1~4타·변신 묶음·강타)은 클립의 EffectAnimEvents 이벤트(PlayEffect / StartEffect·StopEffect)가
        // 전 피어에서 직접 낸다 — 여기서 하지 않는다(2026-10-19·20 민경). 판정은 아래 서버만.

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

    /// <summary>
    /// [서버] 모드를 골라 첫 타(또는 다음 타·다음 묶음)를 시작한다. 실패하면 오너 요청을 거절한다.
    /// 방향은 모드가 정해진 뒤 고른다 — 일반 타는 이동 입력 우선, 강타·변신 묶음은 조준(AssassinAttackDirectionRules).
    /// <paramref name="turnDelay"/> 가 없으면 고른 방향으로 서버가 잰다(다음 타).
    /// </summary>
    private bool StartServerAttack(Vector3 aimDirection, Vector3 moveDirection, float? turnDelay)
    {
        if (!TrySelectAttackMode(out AssassinBasicAttackMode mode))
        {
            RejectStartRpcIfNeeded();
            return false;
        }

        Vector3 direction = Flatten(AssassinAttackDirectionRules.Resolve(mode, moveDirection, aimDirection));
        float delay = turnDelay ?? movement.GetAttackTurnDelay(direction);

        // 강타·변신 묶음은 일반 4타 순서와 별개다.
        int stepIndex = mode == AssassinBasicAttackMode.Normal ? combo.Begin(Time.time) : 0;
        return StartServer(stepIndex, direction, mode, delay);
    }

    private void TickInputSource()
    {
        // 스킬 입력으로 마무리 중이면 눌림·예약을 더 보내지 않는다(FinishWithoutChaining).
        if (finishingChain)
            return;

        TickHoldSignal();

        // 창 안에서 누른 프레임은 예약한다 — 홀드와 별개로, End 전에 손을 떼도 이 예약으로 이어진다. 오너도 자기 재생 기준 창으로 거른다.
        if (ownerQueued || !input.AttackPressed || !IsInCurrentComboWindow())
            return;

        // 예약에 쓴 입력은 선입력 버퍼에서 지운다 — 공격이 끝난 뒤 새 평타로 한 번 더 나가지 않게(할 일 44).
        // 창 밖 입력은 남긴다: 홀드로 End 까지 들고 있으면 이어가기에 쓰이고(그동안 0.2초 만료), 떼면 공격 종료 뒤 새 평타가 된다.
        input.ConsumeBufferedInput(PlayerBufferedInput.Attack);
        ownerQueued = true;
        Vector3 aimDirection = CurrentAimDirection();
        Vector3 moveDirection = CurrentMoveDirection();

        if (!IsNetworkActive)
        {
            hasQueuedNextAttack = true;
            SetServerNextDirection(aimDirection, moveDirection);
            return;
        }

        RequestQueueNextAttackRpc(aimDirection, moveDirection, player.CreateStatReport());
    }

    // 홀드 — 눌림이 바뀌면 즉시, 눌린 동안은 HoldSignalInterval 마다 알린다(거너 HoldSignal).
    // 조준·이동 입력도 실어 End 순간 이어질 다음 타 방향이 된다.
    private void TickHoldSignal()
    {
        bool held = input.AttackHeld;
        bool changed = held != holdSent;
        if (!changed && (!held || Time.time < nextHoldSignalTime))
            return;

        holdSent = held;
        nextHoldSignalTime = Time.time + HoldSignalInterval;
        Vector3 aimDirection = CurrentAimDirection();
        Vector3 moveDirection = CurrentMoveDirection();

        if (!IsNetworkActive)
        {
            if (changed)
                ServerSetHold(held, aimDirection, moveDirection);
            else
                ServerHoldSignal(aimDirection, moveDirection);
            return;
        }

        if (changed)
            SetHoldRpc(held, aimDirection, moveDirection);
        else
            HoldSignalRpc(aimDirection, moveDirection);
    }

    [Rpc(SendTo.Server)]
    private void SetHoldRpc(bool held, Vector3 aimDirection, Vector3 moveDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerSetHold(held, aimDirection, moveDirection);
    }

    [Rpc(SendTo.Server)]
    private void HoldSignalRpc(Vector3 aimDirection, Vector3 moveDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerHoldSignal(aimDirection, moveDirection);
    }

    // 눌림 변경 — 누름이면 홀드를 (다시) 켠다. 뗐다 다시 눌러도 End 순간 눌려 있으면 잇는다.
    private void ServerSetHold(bool held, Vector3 aimDirection, Vector3 moveDirection)
    {
        if (!active || finishingChain)
            return;

        serverHeld = held;
        lastHoldSignalTime = Time.time;
        if (held)
            SetServerNextDirection(aimDirection, moveDirection);
    }

    // 주기 신호는 시각·방향만 갱신한다 — 홀드를 켜지는 않는다(뗀 뒤 늦게 온 신호가 홀드를 되살리지 않게, 거너와 같음).
    private void ServerHoldSignal(Vector3 aimDirection, Vector3 moveDirection)
    {
        if (!active || !serverHeld)
            return;

        lastHoldSignalTime = Time.time;
        SetServerNextDirection(aimDirection, moveDirection);
    }

    // 이동 입력은 펴지 않고 그대로 둔다 — 0 이면 "입력 없음"이라 조준으로 간다(Flatten 은 0 을 몸 정면으로 바꾼다).
    private void SetServerNextDirection(Vector3 aimDirection, Vector3 moveDirection)
    {
        serverNextAim = Flatten(aimDirection);
        serverNextMove = moveDirection;
    }

    private bool IsServerHoldFresh() =>
        AssassinComboWindowRules.IsHoldFresh(serverHeld, lastHoldSignalTime, Time.time, HoldStaleSeconds);

    // 예약 창 = 이 타 클립의 ComboWindowOpen~Close 이벤트 사이. 회전 대기 중엔 이 타 클립이 아직 시작 전이라 창 밖이다.
    private bool IsInCurrentComboWindow()
    {
        return active && !hasPendingClip && comboWindowOpen;
    }

    [Rpc(SendTo.Server)]
    private void RequestStartRpc(
        Vector3 aimDirection, Vector3 moveDirection, float turnDelay, PlayerStatReport statReport, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        player.ValidateStatReport(statReport, rpcParams.Receive.SenderClientId, "어쌔신 기본 공격 시작");

        if (active || stateController == null || !stateController.CanAttack || !CanStartApprovedAttack)
        {
            RejectStartRpc();
            return;
        }

        // 각은 다시 재지 않고 상한만 자른다 — 지연이 길면 보낸 쪽만 손해다.
        StartServerAttack(aimDirection, moveDirection, movement.ClampAttackTurnDelay(turnDelay));
    }

    [Rpc(SendTo.Server)]
    private void RequestQueueNextAttackRpc(
        Vector3 aimDirection, Vector3 moveDirection, PlayerStatReport statReport, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        player.ValidateStatReport(statReport, rpcParams.Receive.SenderClientId, "어쌔신 기본 공격 다음 타");

        // 스킬 입력으로 마무리 중이면 받지 않는다(오너도 이미 멈췄다 — 도착 순서가 바뀐 요청만 여기 걸린다).
        if (finishingChain)
            return;

        // 창 판정은 서버 자기 애니 진행으로 한다(가붕이 isComboWindowOpen 과 같은 권한). 창 밖이면 오너 예약을 푼다.
        if (!IsInCurrentComboWindow())
        {
            RejectQueueRpc();
            return;
        }

        hasQueuedNextAttack = true;
        SetServerNextDirection(aimDirection, moveDirection);
    }

    [Rpc(SendTo.Server)]
    private void FinishChainRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;
        ServerFinishChain();
    }

    // 뗀 것처럼 — 받은 눌림·예약을 지우고 이 공격이 끝날 때까지 새것을 받지 않는다. 서버가 이미 다음 타를 시작했으면 그 타에서 마무리된다.
    private void ServerFinishChain()
    {
        if (!active)
            return;

        finishingChain = true;
        serverHeld = false;
        hasQueuedNextAttack = false;
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
        serverNextAim = attackDirection;
        serverNextMove = Vector3.zero;
        // 재생 속도(Animator.speed)는 도중에 바뀔 수 있어 감속만 늘린다(할 일 91 T5, SafetyNetDuration).
        fallbackEndTime = Time.time + turnDelay + PlayerAttackSpeedRules.SafetyNetDuration(step.ClipLength, AttackSpeedRate) +
            data.EndFallbackPadding;

        // 전진 — 일반 타 = 매 타 전진(할 일 96), 강타 = 강타 전진(§8.2), 변신 묶음 = 없음. 방향은 attackDirection(몸·판정과 같다).
        StopLunge();
        lungeDistance = AssassinLungeRules.Distance(mode, data.NormalLungeDistance, data.EnhancedLungeDistance);
        lungeDuration = AssassinLungeRules.Duration(mode, data.NormalLungeDuration, data.EnhancedLungeDuration);
        if (lungeDistance > 0f && motor != null && player.IsSimulating)
        {
            lungeRemaining = lungeDistance;
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

    // 특성 피해 배율 키의 형태(할 일 125) — 강타 = Enhanced, 변신 묶음 = Alternate(변신 Q·E 대체 스킬과 같은 구분).
    private static TraitAttackForm TraitFormOf(AssassinBasicAttackMode mode) => mode switch
    {
        AssassinBasicAttackMode.Enhanced => TraitAttackForm.Enhanced,
        AssassinBasicAttackMode.Transformed => TraitAttackForm.Alternate,
        _ => TraitAttackForm.Normal,
    };

    // 타격마다 현재 범위를 다시 판정한다(§3.3). 변신 묶음은 Hit 이벤트 4번 = 4타.
    private void FireCurrentHit()
    {
        if (!active || !TryGetStep(currentMode, currentStepIndex, out AssassinBasicAttackStepData step) ||
            hitsFired >= step.HitCount)
            return;

        hitsFired++;
        int damage = player.ResolveDamage(step.AttackDamageMultiplier, 0, new TraitAttackKey(AttackType.Default, TraitFormOf(currentMode)));
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
            StartServerAttack(serverNextAim, serverNextMove, null))
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
        finishingChain = false;
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

    // 대시로 끊긴 공격은 Idle 로 돌리지 않는다 — 오너는 같은 프레임에 대시 상태가 Dodge 를 틀고, 관찰자는 CancelRpc 보다
    // NetworkAnimator 의 Dodge 가 먼저 올 수 있다. 어느 순서든 Dodge 가 최종으로 남는다(PlayerDashController.HoldsBasePose).
    private void ReturnToIdlePose()
    {
        if (dash != null && dash.HoldsBasePose)
            return;

        if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, IdleHash))
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    private Vector3 CurrentAimDirection()
    {
        return Flatten(aim != null ? aim.AimDirection : transform.forward);
    }

    // 이동 입력의 월드 방향 — 이동과 같은 입력→월드 변환(PlayerMovement.GetInputWorldDirection). 입력이 없으면 0.
    private Vector3 CurrentMoveDirection()
    {
        return movement != null ? movement.GetInputWorldDirection() : Vector3.zero;
    }

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
    }
}
