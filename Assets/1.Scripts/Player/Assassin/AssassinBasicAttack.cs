using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 일반 평타. "홀드 + 입력 창" 연결 — 한 타의 End 순간 좌클릭이 눌려 있거나,
/// 그 타 재생 중 예약 창(클립 ComboWindowOpen~Close 이벤트, 가붕이와 같다) 안에서 좌클릭해 예약했으면 다음 타로 잇는다. 둘 다 아니면 그 타로 끝.
/// 오너 예측(할 일 256) — 오너가 자기 애니 이벤트(창·Hit·End)와 자기 입력으로 체인을 주도하고 누르는 즉시 재생한다.
/// 모드(변신 &gt; 강화 &gt; 일반)도 오너가 복제된 <see cref="AssassinState"/> 로 1차 판단해 타 시작 통지에 싣고, 서버가 확인한다
/// (강화 소모·일반 E 쿨은 서버, 어긋나면 서버 끊기 경로). 판정은 오너 Hit 시점 판정 요청(위치·방향) → 서버 2차 검증(<see cref="BasicAttackServerVerifier"/>) →
/// 기존 서버 판정(백어택·급소 파괴·끝없는 칼날·분노·돌파의 여세). 서버·관전자의 애니 이벤트는 쓰지 않는다. 문서: Docs/tech/owner-predicted-basic-attack.md.
/// 일반 타는 매 타 시작 때 짧게 전진한다(할 일 96) — 방향은 모든 모드가 조준(할 일 137). 몸·판정·전진이 같은 방향이다.
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

    // 서버 안전망 — 오너 End 시각 + 이만큼 지나도 종료 통지가 없으면 서버가 혼자 끝낸다(오너 끊김·패킷 유실).
    private const float ServerFollowGraceSeconds = 1f;
    // 판정 요청의 오너 위치가 서버가 보는 위치(NT)와 이만큼(수평 m) 넘게 다르면 경고만 남긴다.
    private const float OwnerPositionWarnDistance = 2f;

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
    private bool endingGracefully;
    private bool finishingTail;
    // [체인 주도] 이 타의 창 안 클릭 예약 — 다음 타·종료·취소에서 풀린다.
    private bool queuedNext;
    // [체인 주도] 스킬 입력으로 지금 타에서 마무리 중(할 일 77 후속) — 공격이 끝나면(ResetRuntime) 풀린다.
    private bool finishingChain;
    // [서버] 이번 타(일반 1타·강타 1회)의 분노 게이지 충전을 이미 받았는가 — 타 하나에 1번(§4.2).
    private bool rageClaimed;
    private int hitsFired;
    // [서버] 이 동작(일반 한 타·강타 1회·변신 묶음)의 시전 ID — 판정마다 특성 사건에 실어 '동작당 1회'로 묶는다(할 일 128 2/5).
    private int serverActionCastId;
    private PlayerTraitEffects traitEffects;
    // [서버] 끝없는 칼날 예약(할 일 128 3/5) — 사망·비활성(씬 종료)에서 비운다.
    private readonly AssassinDelayedStrikeQueue<ComboExtraStrike> pendingComboExtras = new AssassinDelayedStrikeQueue<ComboExtraStrike>();
    private readonly System.Collections.Generic.List<TraitDerivedAttack> derivedAttacks = new System.Collections.Generic.List<TraitDerivedAttack>();
    private int currentStepIndex;
    private AssassinBasicAttackMode currentMode;
    // 이 타의 방향 — 몸 회전·판정·전진이 모두 이 값이다(모든 모드 조준, AssassinAttackDirectionRules).
    private Vector3 attackDirection;
    private float fallbackEndTime;
    // 예약 창 — 이 타 클립의 ComboWindowOpen~Close(또는 End) 사이. 체인 주도 쪽(오너·오프라인)만 자기 애니메이터 이벤트로 연다.
    private bool comboWindowOpen;
    private PlayerActionState observedState;
    // 전진(일반 타 매 타·강타) — 시뮬레이션 피어(오너, 서버 권위 이동이면 서버도)만 모터에 낸다. 끝·취소·막힘이면 0.
    private float lungeRemaining;
    private float lungeDistance;
    private float lungeDuration;
    private PlayerMotor motor;
    private PlayerDashController dash;
    // 다 돈 뒤 공격(할 일 43): 크게 꺾으면 TurnToward 가 도는 동안 클립 시작·강타 전진을 늦춘다.
    // 판정·End 는 클립 이벤트라 같이 늦춰진다. 지연은 오너가 정해(할 일 256 — 서버 상한 자르기 없음) 통지 → StartRpc 로 싣는다.
    private bool hasPendingClip;
    private float pendingClipTime;

    // ─── 오너 예측(할 일 256) ──────────────────────────────────────────
    // 오너 = 체인 주도. 타마다 일련번호를 매겨 서버에 시작·판정·종료를 알린다.
    private byte swingSerial;
    private byte comboStartSerial;
    private int hitNumber;
    // 서버(원격 오너를 따라가는 쪽) — 따라가는 휘두름 번호·끊은 콤보의 뒤늦은 이어짐 무시·통지가 끊겼을 때의 안전망·직전 모드(검증 이어짐).
    private byte serverSwingSerial;
    private bool serverIgnoreContinues;
    private float serverFollowFallbackTime;
    private bool hasServerLastMode;
    private AssassinBasicAttackMode serverLastMode;
    private readonly BasicAttackServerVerifier verifier = new BasicAttackServerVerifier();
    private BasicAttackStepTiming[] normalTimings;
    private BasicAttackStepTiming[] enhancedTimings;
    private BasicAttackStepTiming[] transformedTimings;

    public bool CanStartApprovedAttack => data != null && data.TryGetNormalStep(0, out _);
    // 일반 타·강타·변신 묶음 어디서든 대시로 끊는다(할 일 9). 나간 타격의 피해는 그대로, 남은 동작만 끊긴다.
    public bool CanBeCanceledByDash => true;
    // 누른 프레임으로만 시작하고, 공격 중 스킬 입력은 선입력 버퍼(0.2초) 그대로다(할 일 44 — 거너만 다름).
    public bool StartsOnHold => false;
    public bool ReservesSkillAfterAttack => false;

    // 버퍼의 스킬 입력 때문에 다음 타를 잇지 않는다(할 일 77 후속) — 뗀 것처럼: 남은 창 예약·End 순간 눌림을 무시한다.
    // 체인은 오너가 주도하므로(할 일 256) 오너 래치만 세운다. 이 타는 End 에서 기존 정상 종료로 끝난다.
    public void FinishWithoutChaining()
    {
        if (!active || finishingChain || !IsChainAuthority)
            return;

        finishingChain = true;
        queuedNext = false;
    }
    public AssassinBasicAttackData Data => data;

    // 서버 처리(판정·강화 소모·특성)를 이 인스턴스가 직접 하는가 — 호스트·오프라인.
    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;
    // 체인(창·다음 타·End)을 주도하는 쪽 — 오너(호스트 오너 포함)·오프라인. 서버(원격 오너)·관전자는 통지를 따라간다.
    private bool IsChainAuthority => !IsNetworkActive || IsOwner;

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

    /// <summary>
    /// 서버 2차 검증용 그 모드의 단별 클립 이벤트 시각(공속 1 기준 실제 초 = 클립 시각 ÷ 데이터의 상태 Speed 복제값).
    /// 일반 = 4타, 강타·변신 묶음 = 1단(변신 묶음은 Hit 4개 — 히트 번호로 구분). 루프백 진입 없음(매 타 처음부터).
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<BasicAttackStepTiming> StepTimings(AssassinBasicAttackMode mode)
    {
        if (data == null)
            return System.Array.Empty<BasicAttackStepTiming>();

        switch (mode)
        {
            case AssassinBasicAttackMode.Enhanced:
                return enhancedTimings ??= new[] { TimingOf(data.EnhancedStep) };
            case AssassinBasicAttackMode.Transformed:
                return transformedTimings ??= new[] { TimingOf(data.TransformedStep) };
            default:
                if (normalTimings == null)
                {
                    int count = data.NormalSteps != null ? data.NormalSteps.Length : 0;
                    normalTimings = new BasicAttackStepTiming[count];
                    for (int i = 0; i < count; i++)
                        normalTimings[i] = TimingOf(data.NormalSteps[i]);
                }

                return normalTimings;
        }
    }

    private static BasicAttackStepTiming TimingOf(AssassinBasicAttackStepData step) =>
        BasicAttackStepTiming.FromClip(step?.Clip, nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent),
            (int)DefaultAttackAnimationEventType.Hit, (int)DefaultAttackAnimationEventType.End,
            step != null ? step.AnimatorStateSpeed : 1f);

    private void Update()
    {
        if (hasPendingClip && Time.time >= pendingClipTime)
        {
            hasPendingClip = false;
            PlayAttackClip();
        }

        if (pendingComboExtras.Count > 0 && HasGameplayAuthority)
            FireDueComboExtras();

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

    // 공격속도(할 일 91 T5) — 평타 상태에 속도 파라미터가 없어 공격 중(전 피어 — 시작~종료) Animator.speed 로 탄다.
    // 판정·예약 창·End 는 클립 이벤트라 같이 빨라진다. 꼬리(finishingTail)는 1 배속.
    public bool ScalesAnimatorWithAttackSpeed => active;

    private float AttackSpeedRate => player != null ? player.AttackSpeedRate : 1f;

    // 오너 1차 판단 후 즉시 재생(할 일 256) — 서버 승인을 기다리지 않는다. 서버는 시작 통지를 받고 모드를 확인한 뒤 따라간다.
    public bool TryStart()
    {
        if (active || player == null || !CanStartApprovedAttack || !IsChainAuthority)
            return false;

        if (stateController != null && !stateController.CanAttack)
            return false;

        return StartSwingAsChainAuthority(CurrentAimDirection(), CurrentMoveDirection(), false);
    }

    public void BeginFromState() { }

    public void Tick()
    {
        if (!active)
            return;

        if (IsChainAuthority)
        {
            TickInputSource();
            // End 이벤트가 빠졌을 때의 안전망 — 체인 주도 쪽이 스스로 이 타를 마무리한다.
            if (Time.time >= fallbackEndTime)
                CompleteCurrentStep();
            return;
        }

        if (IsNetworkActive && IsServer && serverFollowFallbackTime > 0f && Time.time >= serverFollowFallbackTime)
        {
            Edit.LogWarning($"[Assassin/평타] {name} 종료 통지가 오지 않아 서버가 공격 상태를 끝낸다(안전망).", this);
            EndFollowServer();
        }
    }

    // 전진 — 일반 타 매 타(할 일 96)·강타(§8.2) 모두 이 타 방향으로 판정 프레임 전까지 나눠 낸다. 적 통과 없음(벽·몸 콜라이더 켜진 적에 막힘).
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
        // EndLocal·CancelRpc 가 부른 EndAttackState → PlayerAttackState.Exit 의 되돌이 호출 — 이미 처리했다.
        if (endingGracefully)
            return;

        bool hadAttack = active;
        byte ownerSerial = swingSerial;
        byte followedSerial = serverSwingSerial;
        ResetRuntime();
        combo?.Reset();
        finishingTail = false;

        if (!hadAttack)
            return;

        ReturnToIdlePose();
        if (!IsNetworkActive)
            return;

        if (IsServer)
        {
            // 서버가 끊는 경로(넉백·구속·연출 잠금·숨김·대시·다운) — 오너(원격이면)·관전자 모두 즉시 멈춘다.
            EndServerVerification();
            if (!IsOwner)
                serverIgnoreContinues = true;
            CancelRpc(IsOwner ? ownerSerial : followedSerial);
        }
        else if (IsOwner)
        {
            // 원격 오너가 스스로 끊었다(대시 등) — 서버가 따라 끝낸다(이미 끊었으면 무시).
            NotifyAttackEndRpc(ownerSerial);
        }
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
        if (IsChainAuthority)
            CompleteCurrentStep();
    }

    public void HitCurrentAttack()
    {
        if (IsChainAuthority && active)
            FireOwnerHit();
    }

    // 애니 이벤트는 체인 주도 쪽(오너·오프라인)만 쓴다(할 일 256). 서버(원격 오너)·관전자의 애니메이터도 같은 클립을 돌려
    // 이벤트가 늦게 또 터지므로 무시한다 — 서버 판정은 오너 판정 요청(RequestHitRpc)으로만 난다.
    public void HandleAnimationEvent(DefaultAttackAnimationEventType eventType)
    {
        // 베기 연출(일반 1~4타·변신 묶음·강타)은 클립의 EffectAnimEvents 이벤트(PlayEffect / StartEffect·StopEffect)가
        // 전 피어에서 직접 낸다 — 여기서 하지 않는다(2026-10-19·20 민경).

        // 휘두름 소리(사운드 명세 Assassin_Attack_01~04 · E_Skill_Common_Start, PLAN-sound-spec-apply 3.10·3.12) — 🔴 체인 주도 가드 **앞**(전 피어).
        // 일반 = 타수별 해당 파일, 변신 = 매 Hit(묶음당 4회) 직전 제외 랜덤, 강타(E 강화 후 좌클릭) = 강화 공격음만(기본 공격음 없음).
        // 적중 여부와 무관 — 판정이 아니라 이 피어 클립의 Hit 이벤트다.
        if (active && eventType == DefaultAttackAnimationEventType.Hit)
            PlaySwingSound();

        if (!IsChainAuthority)
            return;

        // 회전 대기 중 이벤트는 이 타가 아니라 직전 클립의 것이다.
        if (!active || hasPendingClip)
            return;

        switch (eventType)
        {
            case DefaultAttackAnimationEventType.Hit:
                FireOwnerHit();
                break;
            case DefaultAttackAnimationEventType.ComboWindowOpen:
                comboWindowOpen = true;
                break;
            case DefaultAttackAnimationEventType.ComboWindowClose:
                comboWindowOpen = false;
                break;
            case DefaultAttackAnimationEventType.End:
                comboWindowOpen = false;
                CompleteCurrentStep();
                break;
        }
    }

    // 이번 타에 낸 휘두름 소리 수(이 피어). 일반·강타는 타당 1번, 변신은 Hit 마다.
    private int swingSoundsPlayed;

    private void PlaySwingSound()
    {
        Vector3 position = transform.position;
        switch (currentMode)
        {
            case AssassinBasicAttackMode.Transformed:
                GameSoundHooks.Play(GameSoundHooks.AssassinAttackTransformed, position, this);
                break;
            case AssassinBasicAttackMode.Enhanced:
                if (swingSoundsPlayed == 0) GameSoundHooks.Play(GameSoundHooks.AssassinEStart, position, this);
                break;
            default:
                if (swingSoundsPlayed == 0) GameSoundHooks.Play(GameSoundHooks.AssassinAttackKey(currentStepIndex + 1), position, this);
                break;
        }
        swingSoundsPlayed++;
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
    /// 강화 준비/변신 상태를 이 한 지점에서만 읽어 공격 모드를 고른다 — 변신 &gt; 강화 &gt; 일반.
    /// 오너는 복제된 상태로 1차 판단, 서버는 같은 함수로 확인한다(<see cref="AssassinOwnerPredictionRules.AcceptsClaimedMode"/>).
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

    private void TickInputSource()
    {
        // 스킬 입력으로 마무리 중이면 예약하지 않는다(FinishWithoutChaining).
        if (finishingChain)
            return;

        // 창 안에서 누른 프레임은 예약한다 — 홀드와 별개로, End 전에 손을 떼도 이 예약으로 이어진다. 창은 오너 자기 애니 이벤트다(할 일 256).
        if (queuedNext || !input.AttackPressed || !IsInCurrentComboWindow())
            return;

        // 예약에 쓴 입력은 선입력 버퍼에서 지운다 — 공격이 끝난 뒤 새 평타로 한 번 더 나가지 않게(할 일 44).
        // 창 밖 입력은 남긴다: 홀드로 End 까지 들고 있으면 이어가기에 쓰이고(그동안 0.2초 만료), 떼면 공격 종료 뒤 새 평타가 된다.
        input.ConsumeBufferedInput(PlayerBufferedInput.Attack);
        queuedNext = true;
    }

    // 예약 창 = 이 타 클립의 ComboWindowOpen~Close 이벤트 사이. 회전 대기 중엔 이 타 클립이 아직 시작 전이라 창 밖이다.
    private bool IsInCurrentComboWindow()
    {
        return active && !hasPendingClip && comboWindowOpen;
    }

    // ─── 체인 주도(오너·오프라인) ─────────────────────────────────────

    /// <summary>
    /// [체인 주도] 모드를 골라 한 타(또는 다음 타·다음 묶음)를 지금 시작한다 — 자기 상태·연출을 바로 열고,
    /// 호스트·오프라인이면 서버 처리도 여기서, 원격 오너면 시작 통지. 방향은 모드와 무관하게 조준이다(AssassinAttackDirectionRules, 할 일 137).
    /// <paramref name="continuesCombo"/> = 직전 타의 창 예약·End 순간 눌림으로 이은 타(서버 순서 검증).
    /// </summary>
    private bool StartSwingAsChainAuthority(Vector3 aimDirection, Vector3 moveDirection, bool continuesCombo)
    {
        if (!TrySelectAttackMode(out AssassinBasicAttackMode mode))
            return false;

        Vector3 direction = Flatten(AssassinAttackDirectionRules.Resolve(mode, moveDirection, aimDirection));
        // 회전 지연은 오너의 몸 방향으로 잰다(오너가 정한다 — 할 일 256).
        float turnDelay = movement.GetAttackTurnDelay(direction);

        // 강타·변신 묶음은 일반 4타 순서와 별개다.
        int stepIndex = mode == AssassinBasicAttackMode.Normal ? combo.Begin(Time.time) : 0;
        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step) || !player.BeginAttackState())
            return false;

        // 강타 뒤 일반 평타는 1타부터(§5.1). 강화 소모·일반 E 쿨은 서버(ServerOnSwingStarted).
        if (mode == AssassinBasicAttackMode.Enhanced)
            combo.Reset();

        swingSerial++;
        if (!continuesCombo)
            comboStartSerial = swingSerial;

        BeginRuntime(stepIndex, direction, mode, step, turnDelay);

        if (HasGameplayAuthority)
            ServerOnSwingStarted(swingSerial, stepIndex, mode, turnDelay, continuesCombo);
        else
            NotifySwingStartRpc(swingSerial, (byte)stepIndex, (byte)mode, attackDirection, turnDelay, continuesCombo,
                player.CreateStatReport());

        return true;
    }

    // [체인 주도] Hit 시점 — 호스트·오프라인은 바로 판정, 원격 오너는 자기 위치·방향으로 판정 요청(Reliable). 변신 묶음 = Hit 4번 = 히트 번호 0~3.
    private void FireOwnerHit()
    {
        if (HasGameplayAuthority)
            ServerFireHit(transform.position, attackDirection, hitNumber);
        else
            RequestHitRpc(swingSerial, (byte)currentStepIndex, (byte)Mathf.Clamp(hitNumber, 0, 255), transform.position, attackDirection);

        hitNumber++;
    }

    private void CompleteCurrentStep()
    {
        if (!active || !IsChainAuthority)
            return;

        if (currentMode == AssassinBasicAttackMode.Normal)
            combo.Complete(currentStepIndex, Time.time);

        // 창 안에서 예약됐거나 지금 눌려 있으면 다음 타·다음 묶음(4타 뒤 1타 포함) — 방향은 지금 조준. 변신 종료 대기면 시작하지 못하고 여기서 끝난다.
        bool held = !finishingChain && input.AttackHeld;
        if (AssassinComboWindowRules.ShouldContinueAtEnd(queuedNext && !finishingChain, held) &&
            StartSwingAsChainAuthority(CurrentAimDirection(), CurrentMoveDirection(), true))
            return;

        EndAsChainAuthority();
    }

    private void EndAsChainAuthority()
    {
        byte serial = swingSerial;
        EndLocal();
        if (HasGameplayAuthority)
            EndServerVerification();

        if (!IsNetworkActive)
            return;

        if (IsServer)
            EndRpc();
        else
            NotifyAttackEndRpc(serial);
    }

    // ─── 서버 ────────────────────────────────────────────────────────

    /// <summary>
    /// [서버] 한 타가 시작됐다(호스트 오너는 직접, 원격 오너는 시작 통지로) — 강타면 강화 소모 + 일반 E 쿨(§8.2), 시전 ID, 2차 검증 상태,
    /// 튜토리얼 훅, 그리고 오너를 뺀 피어에 기존 재생 RPC(StartRpc)로 전달(NetworkAnimator 에 기대지 않는다).
    /// </summary>
    private void ServerOnSwingStarted(byte serial, int stepIndex, AssassinBasicAttackMode mode, float turnDelay, bool continuesCombo)
    {
        if (mode == AssassinBasicAttackMode.Enhanced)
            assassinState?.ServerConsumeEnhancement();

        serverActionCastId = player.NextServerCastId();

        verifier.RecordAttackSpeed(Time.unscaledTimeAsDouble, AttackSpeedRate);
        verifier.BeginSwing(serial, stepIndex, AssassinOwnerPredictionRules.VerifyStepCount(mode, NormalStepCount), true,
            AssassinOwnerPredictionRules.ContinuesForVerify(continuesCombo, hasServerLastMode, serverLastMode, mode));
        hasServerLastMode = true;
        serverLastMode = mode;

        player.RaiseServerBasicAttackStarted();   // 튜토리얼 훅 — 한 타 시작(적중 무관)
        if (IsNetworkActive)
            StartRpc(stepIndex, attackDirection, (byte)mode, turnDelay);
    }

    private void EndServerVerification()
    {
        verifier.EndCombo();
        hasServerLastMode = false;
    }

    // [서버] 원격 오너의 타 시작 통지 — 모드를 확인하고 공격 상태를 따라간다. 모드가 어긋나거나(서버엔 강화 없음 등) 서버가 상태에
    // 못 들어가면(넉백·구속 중 등) 끊기 경로로 돌려보낸다.
    [Rpc(SendTo.Server)]
    private void NotifySwingStartRpc(byte serial, byte stepIndex, byte modeValue, Vector3 direction, float turnDelay,
        bool continuesCombo, PlayerStatReport statReport, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        player.ValidateStatReport(statReport, rpcParams.Receive.SenderClientId, "어쌔신 기본 공격 타 시작");

        // 서버가 끊은 콤보의 뒤늦은 '이어짐' — 오너는 끊기 RPC 로 이미 멈췄거나 곧 멈춘다. 따라가지 않는다.
        if (continuesCombo && serverIgnoreContinues)
            return;
        if (!continuesCombo)
            serverIgnoreContinues = false;

        var mode = (AssassinBasicAttackMode)modeValue;
        bool canStart = TrySelectAttackMode(out AssassinBasicAttackMode serverMode);
        if (!AssassinOwnerPredictionRules.AcceptsClaimedMode(mode, serverMode, canStart))
        {
            Edit.LogWarning($"[Assassin/평타] {name} 오너 모드 {mode} ≠ 서버 {serverMode}(시작 가능 {canStart}) — 끊는다.", this);
            ServerRejectSwing(serial);
            return;
        }

        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step) || !player.BeginAttackState())
        {
            ServerRejectSwing(serial);
            return;
        }

        serverSwingSerial = serial;
        BeginRuntime(stepIndex, direction, mode, step, turnDelay);
        ServerOnSwingStarted(serial, stepIndex, mode, turnDelay, continuesCombo);
    }

    // [서버] 통지를 따라갈 수 없다 — 서버가 따라가던 공격도 멈추고 오너·관전자를 끊기 경로로 멈춘다.
    private void ServerRejectSwing(byte serial)
    {
        EndServerVerification();
        serverIgnoreContinues = true;

        bool hadAttack = active;
        ResetRuntime();
        combo?.Reset();
        finishingTail = false;
        if (hadAttack)
        {
            ReturnToIdlePose();
            EndAttackStateGracefully();
        }

        CancelRpc(serial);
    }

    // [서버] 원격 오너 Hit 시점 판정 요청(Reliable). 오너가 보낸 위치·방향으로 판정, 2차 검증에 걸리면 데미지만 없다(모션은 그대로).
    [Rpc(SendTo.Server)]
    private void RequestHitRpc(byte serial, byte stepIndex, byte hit, Vector3 casterPosition, Vector3 direction,
        RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!active)
            return;

        BasicAttackHitVerdict verdict = verifier.VerifyHit(Time.unscaledTimeAsDouble, serial, stepIndex, hit, StepTimings(currentMode),
            AttackSpeedRate, BasicAttackVerifySettings.Current);
        if (verdict != BasicAttackHitVerdict.Accepted)
        {
            Edit.LogWarning($"[평타 검증] {name} 어쌔신 {currentMode} {stepIndex + 1}타 히트{hit} 데미지 없음 — {verdict}", this);
            return;
        }

        Vector3 offset = casterPosition - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > OwnerPositionWarnDistance * OwnerPositionWarnDistance)
            Edit.LogWarning($"[평타 검증] {name} 판정 위치가 서버가 보는 위치와 {offset.magnitude:0.00}m 다르다(경고만, 오너 위치로 판정).", this);

        ServerFireHit(casterPosition, Flatten(direction), hit);
    }

    // [서버] 원격 오너의 공격 종료(정상 종료·스스로 끊음). 지금 따라가는 휘두름이 아니면 무시(이미 끊었다).
    [Rpc(SendTo.Server)]
    private void NotifyAttackEndRpc(byte serial, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!active || serial != serverSwingSerial)
            return;

        EndFollowServer();
    }

    private void EndFollowServer()
    {
        EndServerVerification();
        EndLocal();
        if (IsNetworkActive)
            EndRpc();
    }

    // ─── 다른 피어 ────────────────────────────────────────────────────

    // [오너 뺀 피어] 타 재생. 오너는 이미 자기 화면에서 틀었다. 서버(원격 오너)는 통지 처리에서 직접 튼다.
    [Rpc(SendTo.NotOwner)]
    private void StartRpc(int stepIndex, Vector3 direction, byte modeValue, float turnDelay)
    {
        if (IsServer)
            return;

        AssassinBasicAttackMode mode = (AssassinBasicAttackMode)modeValue;
        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step) || !player.BeginAttackState())
        {
            ResetRuntime();
            return;
        }

        BeginRuntime(stepIndex, direction, mode, step, turnDelay);
    }

    // [오너 뺀 피어] 정상 종료 — 남은 클립은 자연 재생.
    [Rpc(SendTo.NotOwner)]
    private void EndRpc()
    {
        if (IsServer)
            return;

        EndLocal();
    }

    // [서버 뺀 피어] 서버가 끊었다 — 오너(원격)·관전자 즉시 멈춤. 오너는 끊긴 콤보 뒤에 새로 시작한 콤보면 무시한다.
    [Rpc(SendTo.NotServer)]
    private void CancelRpc(byte serial)
    {
        if (IsOwner)
        {
            if (!active)
                return;

            // 일련번호가 지금 콤보 시작보다 앞이면(바이트 순환 비교) 이미 끝난 옛 콤보에 대한 끊기다.
            if ((byte)(serial - comboStartSerial) >= 128)
                return;
        }

        bool hadAttack = active;
        ResetRuntime();
        combo?.Reset();
        finishingTail = false;
        if (hadAttack)
        {
            ReturnToIdlePose();
            EndAttackStateGracefully();
        }
    }

    // ─── 공용 ────────────────────────────────────────────────────────

    private void BeginRuntime(
        int stepIndex,
        Vector3 direction,
        AssassinBasicAttackMode mode,
        AssassinBasicAttackStepData step,
        float turnDelay)
    {
        active = true;
        queuedNext = false;
        comboWindowOpen = false;
        rageClaimed = false;
        hitsFired = 0;
        hitNumber = 0;
        swingSoundsPlayed = 0;
        currentStepIndex = stepIndex;
        currentMode = mode;
        attackDirection = Flatten(direction);
        // 재생 속도(Animator.speed)는 도중에 바뀔 수 있어 감속만 늘린다(할 일 91 T5, SafetyNetDuration).
        fallbackEndTime = Time.time + turnDelay + PlayerAttackSpeedRules.SafetyNetDuration(step.ClipLength, AttackSpeedRate) +
            data.EndFallbackPadding;
        serverFollowFallbackTime = fallbackEndTime + ServerFollowGraceSeconds;

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
        RaiseBladeDomainFx(mode, stepIndex);

        hasPendingClip = turnDelay > 0f;
        if (hasPendingClip)
        {
            pendingClipTime = Time.time + turnDelay;
            return;
        }

        PlayAttackClip();
    }

    /// <summary>
    /// [전 피어] 특성 연출 사건(할 일 145) — 칼날의 영역을 가진 어쌔신의 일반 E 강타 시작(오너 자기 시작·서버 통지 처리·관전자 StartRpc(NotOwner) — 피어마다 1번).
    /// 도형 = 넓어진 강타 판정 도형, 포즈 = 시작 순간 발밑 + 강타 방향(판정은 클립 Hit 순간 위치 — 전진만큼 앞이다). Variant 0.
    /// </summary>
    private void RaiseBladeDomainFx(AssassinBasicAttackMode mode, int stepIndex)
    {
        if (mode != AssassinBasicAttackMode.Enhanced || !TraitSkillFx.Has(player, TraitFxTrait.AssassinBladeDomain) ||
            !TryGetHitArea(mode, stepIndex, attackDirection, out AttackShapeSet area, out AttackShapePose pose))
            return;

        TraitSkillFx.Raise(new TraitFxEvent(TraitFxTrait.AssassinBladeDomain, TraitFxKind.Activated, player, transform.position,
            attackDirection, transform.position, shape: area, pose: pose, variant: 0));
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

    /// <summary>그 묶음의 특성 키(피해 배율·범위 배율·파생 공격 공용) — 판정과 특성 랩 공격 범위 기즈모(할 일 152)가 같이 쓴다.</summary>
    public static TraitAttackKey TraitKeyFor(AssassinBasicAttackMode mode) => new TraitAttackKey(AttackType.Default, TraitFormOf(mode));

    // 특성 피해 배율 키의 형태(할 일 125) — 강타 = Enhanced, 변신 묶음 = Alternate(변신 Q·E 대체 스킬과 같은 구분).
    private static TraitAttackForm TraitFormOf(AssassinBasicAttackMode mode) => mode switch
    {
        AssassinBasicAttackMode.Enhanced => TraitAttackForm.Enhanced,
        AssassinBasicAttackMode.Transformed => TraitAttackForm.Alternate,
        _ => TraitAttackForm.Normal,
    };

    /// <summary>
    /// [서버] 히트 하나 — 타격마다 현재 범위를 다시 판정한다(§3.3). 변신 묶음은 Hit 이벤트 4번 = 히트 번호 0~3.
    /// 위치·방향 = 오너가 Hit 시점에 보낸 값(호스트·오프라인은 자기 값, 할 일 256).
    /// </summary>
    private void ServerFireHit(Vector3 casterPosition, Vector3 direction, int hitIndex)
    {
        if (!active || !TryGetStep(currentMode, currentStepIndex, out AssassinBasicAttackStepData step) ||
            hitIndex < 0 || hitIndex >= step.HitCount)
            return;

        hitsFired = hitIndex + 1;
        TraitAttackKey traitKey = TraitKeyFor(currentMode);
        // 돌파의 여세 — 이 동작의 첫 판정에서만 소모한다(빗나가도). 같은 판정의 모든 대상이 같은 배율, 변신 묶음 2~4타는 그대로.
        float nextAttackMultiplier = AssassinBasicAttackTraitRules.ConsumesNextAttackBuff(hitsFired)
            ? ConsumeNextAttackBuff(traitKey)
            : 1f;
        int damage = ResolveHitDamage(step.AttackDamageMultiplier, traitKey, nextAttackMultiplier);
        if (!TryGetHitArea(currentMode, currentStepIndex, casterPosition, direction, out AttackShapeSet area, out AttackShapePose pose))
        {
            Debug.LogError($"[Assassin/평타] {currentMode} {currentStepIndex + 1}타 판정 도형(hitArea)이 비어 있습니다 — 판정 생략.", this);
            return;
        }

        // 백어택(A11) — 일반·강타·변신 묶음 모두 배율을 싣고, 변신 중이면 위치 무관(타격 시점의 변신 여부).
        if (assassinState != null)
            coneAttack.SetBackAttack(assassinState.BackAttackMultiplier, assassinState.IsTransformed);
        // 급소 파괴 — 대상마다 적중 직전 특성 수정자(밀치기)를 거친다. 강타 키(Default/Enhanced)만 효과가 맞는다(칼날의 영역으로 넓어진 범위 포함).
        bool landed = coneAttack.Fire(
            direction,
            area,
            pose,
            damage,
            data.TriggersOnHit,
            serverActionCastId,
            traitKey);

        // 끝없는 칼날 — 일반 4타·변신 묶음 마지막 판정을 실행했으면(빗나가도) 같은 도형·포즈에 추가 베기 1회 예약.
        if (AssassinComboExtraRules.Schedules(currentMode, currentStepIndex, NormalStepCount, hitsFired, step.HitCount))
            ScheduleComboExtra(step, traitKey, area, pose);

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

    // [서버] 다음 기본 공격 1회 강화(⑨ 돌파의 여세)를 이 동작 시전 ID 로 소모한다. 없거나 서버가 아니면 1.
    private float ConsumeNextAttackBuff(TraitAttackKey key)
    {
        traitEffects ??= PlayerTraitEffects.For(this);
        float multiplier = traitEffects != null ? traitEffects.ConsumeNextAttackMultiplierServer(key, serverActionCastId) : 1f;
        if (multiplier != 1f)
            Edit.Log($"[Assassin/평타] 다음 공격 강화 소모 ×{multiplier:F2} — {key}, 동작 {serverActionCastId}", this);
        return multiplier;
    }

    public int NormalStepCount => data != null && data.NormalSteps != null ? data.NormalSteps.Length : 0;

    /// <summary>
    /// 그 타의 판정 도형(할 일 153) — 단 데이터 <see cref="AssassinBasicAttackStepData.HitArea"/> 에 칼날의 영역 길이·폭 배율을 바른 것과,
    /// 기준 포즈(시전자 발밑 + <paramref name="direction"/> — 몸 회전이 아니라 그 타의 조준 방향).
    /// 서버 판정(<see cref="ServerFireHit"/>)과 특성 랩 공격 범위 기즈모(할 일 152)가 같이 쓴다. 단이 없거나 도형이 비면 false.
    /// </summary>
    public bool TryGetHitArea(AssassinBasicAttackMode mode, int stepIndex, Vector3 direction, out AttackShapeSet area, out AttackShapePose pose) =>
        TryGetHitArea(mode, stepIndex, transform.position, direction, out area, out pose);

    /// <summary>기준 위치를 정해 준다 — 오너 판정 요청의 위치(할 일 256).</summary>
    public bool TryGetHitArea(AssassinBasicAttackMode mode, int stepIndex, Vector3 casterPosition, Vector3 direction,
        out AttackShapeSet area, out AttackShapePose pose)
    {
        area = null;
        pose = default;
        if (!TryGetStep(mode, stepIndex, out AssassinBasicAttackStepData step) || step.HitArea == null || step.HitArea.IsEmpty)
            return false;

        // 칼날의 영역 — 판정 도형 길이·폭만 배율. 전진·판정 기준점(몸)은 그대로.
        area = AssassinTraitAreaRules.ScaleArea(player != null ? player.TraitModifiers : null, TraitKeyFor(mode), step.HitArea);
        pose = area.ResolvePose(casterPosition, direction, casterPosition, direction);
        return true;
    }

    // [서버] 끝없는 칼날 예약 — 피해 = 마지막 원래 한 타의 Dref × 비율(다음 공격 강화·4타 합 아님). 위치·방향·도형은 지금 값으로 고정(새 조준·이동 없음).
    // 지연만 공격 가속기를 탄다. VFX 없음(판정만).
    private void ScheduleComboExtra(AssassinBasicAttackStepData step, TraitAttackKey key, AttackShapeSet area, in AttackShapePose pose)
    {
        traitEffects ??= PlayerTraitEffects.For(this);
        if (traitEffects == null)
            return;

        traitEffects.GetDerivedAttacks(key, derivedAttacks);
        if (!AssassinDerivedAttackKeys.TryFind(derivedAttacks, AssassinDerivedAttackKeys.ComboExtra, out TraitDerivedAttack extra))
            return;

        int sourceDref = player.ResolveDamage(step.AttackDamageMultiplier, 0, key);
        var strike = new ComboExtraStrike(area, pose, extra.DamageFrom(sourceDref), ComboExtraFxVariant(currentMode, currentStepIndex));
        float delay = AssassinComboExtraRules.Delay(extra.Delay, AttackSpeedRate);
        pendingComboExtras.Schedule(Time.timeAsDouble, delay, strike);
        Edit.Log($"[Assassin/평타] 끝없는 칼날 예약 — {key}, 기준 {sourceDref} → {strike.Damage}, {delay:F3}초 뒤", this);
    }

    // [서버] 시각이 된 추가 베기를 낸다(파생 판정 — 특성·분노·쿨감·CC·백어택 연출 없음).
    private void FireDueComboExtras()
    {
        while (pendingComboExtras.TryPopDue(Time.timeAsDouble, out ComboExtraStrike strike))
        {
            if (assassinState != null)
                coneAttack.SetBackAttack(assassinState.BackAttackMultiplier, assassinState.IsTransformed);
            comboExtraFxLanded.Clear();
            int landed = coneAttack.FireDerived(strike.Area, strike.Pose, strike.Damage, comboExtraFxLanded);
            Edit.Log($"[Assassin/평타] 끝없는 칼날 판정 — 피해 {strike.Damage}, 적중 {landed}", this);

            // 특성 연출 사건(할 일 145) — 추가 베기 판정 순간 1번(빗나가도), 맞은 유닛마다 Hit. 도형은 받는 피어가 Variant(모드·단)로 다시 만든다.
            TraitSkillFxNetwork.ServerSend(player, TraitFxTrait.AssassinEndlessBlade, TraitFxKind.Activated, strike.Pose.Position,
                strike.Pose.Forward, strike.Pose.Position, strike.Pose, variant: strike.FxVariant);
            for (int i = 0; i < comboExtraFxLanded.Count; i++)
            {
                Unit unit = comboExtraFxLanded[i];
                if (unit != null)
                    TraitSkillFxNetwork.ServerSend(player, TraitFxTrait.AssassinEndlessBlade, TraitFxKind.Hit, unit.transform.position,
                        strike.Pose.Forward, unit.transform.position, target: unit);
            }
            comboExtraFxLanded.Clear();
        }
    }

    // 끝없는 칼날 연출 Variant = 모드 × 16 + 단 번호 — 받는 피어가 TryGetHitArea 로 같은 도형을 만든다(TraitSkillFxNetwork).
    private static int ComboExtraFxVariant(AssassinBasicAttackMode mode, int stepIndex) => (int)mode * 16 + Mathf.Clamp(stepIndex, 0, 15);
    private readonly System.Collections.Generic.List<Unit> comboExtraFxLanded = new System.Collections.Generic.List<Unit>();

    // 사망(살아 있음 → 그 밖)이면 예약을 버린다(명세 §9). 부활 뒤 재개 없음.
    private void HandleLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (player != null && clientId == player.OwnerClientId && previous == PlayerLifeState.Alive && current != PlayerLifeState.Alive)
            pendingComboExtras.Clear();
    }

    private readonly struct ComboExtraStrike
    {
        public readonly AttackShapeSet Area;
        public readonly AttackShapePose Pose;
        public readonly int Damage;
        public readonly int FxVariant;

        public ComboExtraStrike(AttackShapeSet area, AttackShapePose pose, int damage, int fxVariant)
        {
            Area = area;
            Pose = pose;
            Damage = damage;
            FxVariant = fxVariant;
        }
    }

    // 원래 타격 = Dref × 다음 기본 공격 강화(명세 §5) — 특성 배율과 함께 곱한 뒤 마지막에 한 번만 정수화한다.
    private int ResolveHitDamage(float coefficient, TraitAttackKey key, float nextAttackMultiplier)
    {
        if (nextAttackMultiplier == 1f || player.Stats == null)
            return player.ResolveDamage(coefficient, 0, key);

        return player.Stats.ResolveDamage(coefficient, 0, player.TraitDamageMultiplier(key) * nextAttackMultiplier);
    }

    private void EndLocal()
    {
        ResetRuntime();
        finishingTail = true;
        EndAttackStateGracefully();
    }

    // 정상 종료·끊기 처리를 이미 했다 — EndAttackState → PlayerAttackState.Exit → CancelCurrentAttack 되돌이를 막는다.
    private void EndAttackStateGracefully()
    {
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

    private void ResetRuntime()
    {
        active = false;
        queuedNext = false;
        comboWindowOpen = false;
        finishingChain = false;
        hitsFired = 0;
        hitNumber = 0;
        fallbackEndTime = 0f;
        serverFollowFallbackTime = 0f;
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

    private void OnEnable()
    {
        PlayerLifeCycleController.ServerLifeStateChanged += HandleLifeStateChanged;
    }

    private void OnDisable()
    {
        StopLunge();
        PlayerLifeCycleController.ServerLifeStateChanged -= HandleLifeStateChanged;
        pendingComboExtras.Clear();   // 씬 종료·비활성 — 예약이 남지 않게
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
