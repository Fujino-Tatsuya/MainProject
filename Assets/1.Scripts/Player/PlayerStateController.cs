using System.Runtime.CompilerServices;
using BeaverLobby.Player.Dash;
using UnityEngine;

[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerMotor))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(StatusEffectController))]
public class PlayerStateController : MonoBehaviour, IRestraintReceiver
{
    [SerializeField] private PlayerActionState currentStateDebug;
    [SerializeField, Min(0f)] private float serverKnockbackReportGraceTime = 0.25f;

    [Tooltip("평타·대시·스킬·인터럽트가 차단 효과(침묵·속박·약화 등 봉인)로 거부됐을 때 오너 로컬에서 재생할 사운드 키(Sound 시트 key). 비우면 무음 — Docs/tech/skill-reject-feedback.md")]
    [SerializeField] private string effectBlockedSoundKey = "";

    /// <summary>
    /// [오너 로컬] 평타·대시·스킬·인터럽트 입력이 차단 효과(그 행동만 막는 상태이상 — 침묵·속박·약화)로 거부됐다.
    /// 인자 = 거부된 입력 종류, 그때 걸려 있던 차단·CC류 상태이상(<see cref="StatusEffectImmunityPolicy.BlockedCrowdControl"/> 범위).
    /// 입력 1회당 1회(그 입력은 버퍼에서 버린다). 기절·에어본(행동 불능 CC) 중에는 나지 않는다 — 입력을 보관한다.
    /// 쿨타임 거부(<see cref="PlayerSkillController.CooldownRejected"/>)와 같은 입력에 겹쳐 나지 않는다. 문서: Docs/tech/skill-reject-feedback.md
    /// </summary>
    public event System.Action<PlayerBufferedInput, StatusEffectType> ActionBlockedByEffect;

    private const float DefaultMinKnockbackTime = 0.15f;
    private const float DefaultMaxKnockbackTime = 1.5f;
    private const float DefaultKnockbackStopSpeed = 0.15f;
    private const float DefaultKnockbackDeceleration = 6f;

    private IPlayerState currentState;
    private PlayerStateContext context;

    // 연출 잠금(Cinematic) 중에는 상태 전이 자체를 막는다. SetState 경로(대시/넉백/그랩)도 함께 차단한다.
    private bool cinematicLocked;

    public PlayerActionState CurrentState => currentState?.StateType ?? PlayerActionState.Idle;
    public bool CanMove => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move || AllowsSkillMovement) && !context.StatusEffects.BlocksMovement;
    public bool CanMovementRotate => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move || AllowsSkillMovementRotate) && !context.StatusEffects.BlocksMovement;
    public bool CanAttack => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksAttack;
    public bool CanInterrupt => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksInterrupt;
    public bool CanUseSkill => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksSkill;

    // 스킬 실행 중 이동/회전 허용 여부는 스킬 정의에 위임 (단일 Skill 상태)
    private bool AllowsSkillMovement => currentState is PlayerSkillState skillState && skillState.AllowsMovement;
    private bool AllowsSkillMovementRotate => currentState is PlayerSkillState skillState && skillState.AllowsMovementRotate;
    private PlayerGameRuleData GameRule => context != null && context.Motor != null ? context.Motor.GameRule : null;
    public float MinKnockbackTime => GameRule != null ? GameRule.MinKnockbackTime : DefaultMinKnockbackTime;
    public float MaxKnockbackTime => GameRule != null ? GameRule.MaxKnockbackTime : DefaultMaxKnockbackTime;
    public float ServerKnockbackReportGraceTime => serverKnockbackReportGraceTime;
    public float KnockbackStopSpeed => GameRule != null ? GameRule.KnockbackStopSpeed : DefaultKnockbackStopSpeed;
    public float KnockbackDeceleration => GameRule != null ? GameRule.KnockbackDeceleration : DefaultKnockbackDeceleration;

    private void Awake()
    {
        // StatusEffectController는 NetworkBehaviour라 런타임 추가가 불가 — 프리팹에 미리 부착돼 있어야 한다
        StatusEffectController statusEffects = GetComponent<StatusEffectController>();
        if (statusEffects == null)
            Debug.LogError("[Player] StatusEffectController가 프리팹에 부착되어 있지 않습니다.", this);

        context = new PlayerStateContext(
            this,
            GetComponent<Player>(),
            GetComponent<PlayerInputReader>(),
            GetComponent<PlayerMovement>(),
            GetComponent<PlayerAimIndicator>(),
            GetComponent<IPlayerBasicAttack>(),
            statusEffects,
            GetComponent<PlayerMotor>(),
            GetComponentInChildren<Animator>(),
            GetComponent<PlayerSkillController>(),
            GetComponent<PlayerDashController>(),
            GetComponent<PlayerGroundingSensor>()
        );

        currentState = CreateState(PlayerActionState.Idle);
        currentStateDebug = currentState.StateType;
        currentState.Enter(PlayerActionState.Idle);
    }

    /// <summary>
    /// 대시 진단용. 마지막 상태 전이를 요청한 호출 지점(파일:줄 + 멤버명).
    /// 대시가 중단됐을 때 "누가 상태를 바꿨는가"를 <see cref="PlayerDashState.Exit"/>가 로그에 남긴다.
    /// </summary>
    public string LastTransitionCause { get; private set; } = "(없음)";

    // Idle/Move만 PlayerStateBase.TryConsumeActionInput을 호출한다 —
    // 그 외 상태(공격·스킬·인터럽트·넉백·그랩·연출·대시)에서 누른 입력은 선입력 버퍼에 남아 Idle/Move 로 돌아온 첫 틱에 실행된다(할 일 44).
    private bool CurrentStateReadsActionInput =>
        CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move;

    /// <summary>
    /// 지금 행동을 공용 대시로 끊을 수 있는가(대시 우선 — 거너 D1). 행동 쪽이 정한다:
    /// 기본 공격 = <see cref="IPlayerBasicAttack.CanBeCanceledByDash"/>, 스킬 = <see cref="PlayerSkillBase.CanBeCanceledByDash"/>.
    /// 기본 공격은 세 캐릭터 모두 언제든 예다(할 일 9).
    /// </summary>
    public bool CurrentActionAllowsDashCancel => CurrentState switch
    {
        PlayerActionState.AttackReady or PlayerActionState.Attack =>
            context.DefaultAttack != null && context.DefaultAttack.CanBeCanceledByDash,
        PlayerActionState.Skill or PlayerActionState.Focus =>
            currentState is PlayerSkillState skillState && skillState.Skill != null &&
            skillState.Skill.CanBeCanceledByDash(CurrentState),
        _ => false,
    };

    /// <summary>행동을 끊는 대시의 이동 가능 조건 — 상태 제한(Idle/Move)만 빼고 CC·연출 잠금·사망은 그대로 막는다.</summary>
    public bool CanDashFromAction =>
        CurrentState != PlayerActionState.Dead && !cinematicLocked && !context.StatusEffects.BlocksMovement;

    /// <summary>
    /// 상태이상으로 막힌 입력을 처리한다 — 행동 불능 CC 면 보관(Blocked), 봉인이면 버리고 차단 피드백 1회(BlockedByEffect).
    /// 호출자는 그 행동의 차단 판정이 참일 때만 부른다. 반환값을 그대로 <see cref="PlayerInputReader.ResolveBufferedInput"/> 에 넘긴다.
    /// </summary>
    internal PlayerActionAttempt RejectByEffect(PlayerBufferedInput input)
    {
        StatusEffectType active = context.StatusEffects.ActiveEffects;
        PlayerActionAttempt attempt = PlayerActionRejectPolicy.ClassifyEffectBlock(active);
        if (attempt == PlayerActionAttempt.BlockedByEffect)
            NotifyBlockedByEffect(input, active & StatusEffectImmunityPolicy.BlockedCrowdControl);

        return attempt;
    }

    private void NotifyBlockedByEffect(PlayerBufferedInput input, StatusEffectType effects)
    {
        if (context.Player == null || !context.Player.IsInputSource)
            return;

        Edit.Log($"[Input] {input} 차단 효과 거부 — {effects}(입력 버림)", this);
        ActionBlockedByEffect?.Invoke(input, effects);

        if (!string.IsNullOrEmpty(effectBlockedSoundKey) && AudioManager.Instance != null)
            AudioManager.Instance.Play(effectBlockedSoundKey, transform.position);
    }

    /// <summary>대시가 거부된 이유를 버퍼 처리로 바꾼다 — 상태이상(CC 보관·봉인 버림) → 충전 없음 → 그 밖(비접지 등, 버림) 순.</summary>
    internal PlayerActionAttempt ClassifyDashReject()
    {
        if (context.StatusEffects.BlocksMovement)
            return RejectByEffect(PlayerBufferedInput.Dash);

        return context.Dash != null && context.Dash.IsOutOfCharge
            ? PlayerActionAttempt.OutOfResource
            : PlayerActionAttempt.Rejected;
    }

    public void Tick()
    {
        // 대시 우선 — 행동이 허락하면 그 행동을 끊고 대시(BeginDash 가 현재 상태를 덮는다: 공격은 CancelCurrentAttack,
        // 스킬은 HandleSkillStateExit(Dash) → SkillEndReason.DashCancelled).
        // 선입력 버퍼에 남은 대시(끊을 수 없는 행동 중에 누른 것)도 같은 식으로 받는다 — 성공하면 비우고,
        // 행동 불능 CC 면 남겨 다시 시도하고, 그 밖의 거부(속박 등 봉인·충전 없음·비접지)는 그 대시만 버린다(재시도·거부 로그 반복 없음).
        bool dashHandled = false;
        bool dashDropped = false;
        if (context.Input != null && !CurrentStateReadsActionInput &&
            (context.Input.DashPressed || context.Input.HasBufferedInput(PlayerBufferedInput.Dash)) &&
            CurrentActionAllowsDashCancel && context.Dash != null)
        {
            dashHandled = context.Dash.TryBeginPredictedDash(cancelsAction: true);
            PlayerActionAttempt attempt = dashHandled ? PlayerActionAttempt.Started : ClassifyDashReject();
            dashDropped = !dashHandled && attempt != PlayerActionAttempt.Blocked;
            context.Input.ResolveBufferedInput(PlayerBufferedInput.Dash, attempt);
        }

        // 우클릭 → 대시 전환(할 일 115 §12) — 대시로 끊을 수 없는 일반 간파 중이라도, 대시 누른 시각이 간파 시작 후 창 안이고
        // Hit 전이면 간파를 끝내지 않고 대시에 옮겨 싣는다(TryArmDashTransfer → 기존 대시 게이트·피드백 그대로).
        // 전환 조건이 아니면 대시 입력은 지금처럼 버퍼에 남는다.
        if (!dashHandled && !dashDropped && context.Input != null && context.Dash != null && context.Skills != null &&
            CurrentState == PlayerActionState.Skill &&
            context.Input.TryGetPressTime(PlayerBufferedInput.Dash, out float dashPressTime) &&
            context.Skills.TryArmDashTransfer(dashPressTime))
        {
            dashHandled = context.Dash.TryBeginPredictedDash(cancelsAction: true);
            if (!dashHandled)
                context.Skills.DisarmDashTransfer();
            PlayerActionAttempt attempt = dashHandled ? PlayerActionAttempt.Started : ClassifyDashReject();
            dashDropped = !dashHandled && attempt != PlayerActionAttempt.Blocked;
            context.Input.ResolveBufferedInput(PlayerBufferedInput.Dash, attempt);
        }

        // ⚠️ "대시가 됐다 말았다"의 가장 흔한 무증상 경로: 대시 입력이 상태 때문에 바로 실행되지 않는 경우.
        // 공격·스킬 모션 중 Shift를 누르면 TryBeginPredictedDash가 호출조차 되지 않아
        // 시작 로그도, 거부 로그도 남지 않는다(누른 프레임에만 한 줄이므로 스팸이 아니다).
        if (!dashHandled && !dashDropped && context.Input != null && context.Input.DashPressed && !CurrentStateReadsActionInput)
        {
            Edit.LogWarning(
                $"[Dash] 입력 보류: 현재 상태 {CurrentState}는 액션 입력을 읽지 않습니다(Idle/Move에서만 대시 입력 처리). " +
                (context.Input.InputBufferSeconds > 0f
                    ? $"이 Shift는 선입력 버퍼에 {context.Input.InputBufferSeconds:0.##}초 보관돼 Idle/Move 로 돌아오면 실행되고, 그 전에 지나면 버려집니다."
                    : "선입력 버퍼가 꺼져 있어(유지 0초) 이 Shift는 버려집니다."), this);
        }

        currentState?.Tick();
        context.Input?.ConsumeStateTickEdges();
    }

    /// <summary>상태 판단과 입력 소비는 Update에 두고, 물리 이동 의도만 고정 틱에 제출한다.</summary>
    public void FixedTick()
    {
        currentState?.FixedTick();
    }

    public bool ShouldTickForNetwork(bool isOwner, bool hasStateAuthority)
    {
        return isOwner || (hasStateAuthority && currentState.RequiresStateAuthorityTick);
    }

    // 진단용 Caller 인자는 전부 컴파일 타임에 채워지는 선택 인자다 — 기존 호출부는 그대로 동작한다.
    public bool ChangeState(
        PlayerActionState nextState,
        [CallerMemberName] string callerMember = null,
        [CallerFilePath] string callerFile = null,
        [CallerLineNumber] int callerLine = 0)
    {
        if (CurrentState == nextState)
            return true;

        if (!CanEnter(nextState))
        {
            // 대시 중 전이 거부는 "대시가 안 끝난다"로 나타난다 — 사망/연출 잠금뿐이지만 남겨 둔다.
            if (CurrentState == PlayerActionState.Dash)
            {
                Edit.LogWarning(
                    $"[Dash] 상태 전이 거부: Dash→{nextState} 실패(연출잠금={cinematicLocked}) " +
                    $"요청={DescribeCaller(callerMember, callerFile, callerLine)}", this);
            }

            return false;
        }

        LastTransitionCause = DescribeCaller(callerMember, callerFile, callerLine);

        PlayerActionState previousState = CurrentState;
        ExitCurrentState(nextState);
        currentState = CreateState(nextState);
        currentStateDebug = currentState.StateType;
        currentState.Enter(previousState);
        OnActionStateChanged();
        return true;
    }

    /// <summary>
    /// 지금 나가는 상태의 Exit 가 도는 동안만 값이 있다 — 들어갈 상태. 나가는 쪽 정리 코드(공격 취소의 Idle 복귀 등)가
    /// 다음 상태가 곧 틀 애니(대시 Dodge)를 같은 프레임에 덮지 않게 묻는다(할 일 92-3).
    /// </summary>
    public PlayerActionState? TransitionTarget { get; private set; }

    private void ExitCurrentState(PlayerActionState nextState)
    {
        PlayerActionState? outer = TransitionTarget;
        TransitionTarget = nextState;
        currentState?.Exit(nextState);
        TransitionTarget = outer;
    }

    // 상태 전이 직후 공통 — 선입력 버퍼 진단(관측 전용)과 '기본 공격 뒤 스킬' 예약 정리(할 일 44).
    // 예약은 공격이 정상 종료돼 Idle/Move 로 돌아올 때까지만 든다 — 대시·넉백·구속·사망·연출 등으로 끊기면 버린다.
    private void OnActionStateChanged()
    {
        if (context.Input == null)
            return;

        context.Input.DiagnoseActionStateChanged(CurrentState);

        PlayerActionState state = CurrentState;
        if (state != PlayerActionState.Idle && state != PlayerActionState.Move &&
            state != PlayerActionState.Attack && state != PlayerActionState.AttackReady)
            context.Input.ClearSkillReservation();
    }

    /// <summary>전이 요청 지점을 "멤버() @ 파일:줄" 형태로 표기한다(대시 중단 원인 추적용).</summary>
    private static string DescribeCaller(string callerMember, string callerFile, int callerLine)
    {
        string fileName = string.IsNullOrEmpty(callerFile)
            ? "?"
            : System.IO.Path.GetFileName(callerFile);
        return $"{callerMember}() @ {fileName}:{callerLine}";
    }

    public bool TryReceiveRestraint(RestraintContext restraintContext)
    {
        if (!CanReceiveServerInteraction())
            return false;

        // 슈퍼아머 거부는 서버 진입에서만 판정한다. 오너는 서버 결정(ApplyRestrainedFromServer)을
        // 그대로 따라야 하며, 복제 지연으로 상태이상 목록이 어긋난 순간에 각자 판단하면 상태가 갈린다.
        //
        // Push(밀기)와 Carry(잡기) 모두 SuperArmor면 거부한다.
        // 보스 Grab 체인 영향은 경석 확인 대기(은희가 직접 공유함).
        if (PlayerRestraintPolicy.IsBlockedBySuperArmor(restraintContext.Mode, context.Player.HasSuperArmor))
            return false;

        return ApplyRestrained(restraintContext);
    }

    public bool ApplyRestrainedFromServer(
        GameObject instigator = null, RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f)
    {
        return ApplyRestrained(new RestraintContext(instigator, gameObject, mode, frontOffset));
    }

    private bool ApplyRestrained(RestraintContext restraintContext)
    {
        if (!CanReceiveRestraint(restraintContext))
            return false;

        SetState(new PlayerRestrainedState(
            context, restraintContext.Instigator, restraintContext.Mode, restraintContext.FrontOffset));
        return true;
    }

    public bool BeginRestrained(
        GameObject instigator = null, RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f)
    {
        return TryReceiveRestraint(new RestraintContext(instigator, gameObject, mode, frontOffset));
    }

    public bool EndRestrained()
    {
        if (CurrentState != PlayerActionState.Restrained)
            return false;

        ChangeState(PlayerActionState.Idle);
        return true;
    }

    // 슈퍼아머 거부는 Unit.Knockback 공통 진입점에서 처리 — 여기서는 사망·연출 잠금만 거부한다
    public bool BeginKnockback(Vector3 direction, float strength)
    {
        if (CurrentState == PlayerActionState.Dead || cinematicLocked)
            return false;

        SetState(new PlayerKnockbackState(context, direction, strength));
        return true;
    }

    public bool ApplyKnockbackFromServer(Vector3 direction, float strength)
    {
        return BeginKnockback(direction, strength);
    }

    public void EndKnockback()
    {
        if (CurrentState == PlayerActionState.Knockback)
            ChangeState(PlayerActionState.Idle);
    }

    public void EndInterrupt()
    {
        if (CurrentState == PlayerActionState.Interrupt)
            ChangeState(PlayerActionState.Idle);
    }

    /// <summary>
    /// 연출 잠금 진입. <see cref="PlayerEncounterLock"/>이 전 피어에서 호출한다(복제된 잠금 값 기준).
    /// 진행 중이던 상태의 Exit 정리는 그대로 돌고, 이후 모든 전이는 <see cref="EndCinematic"/>까지 막힌다.
    /// 사망 상태는 잠금보다 우선한다 — 연출 참가자는 생존자만이라 정상 경로에서는 발생하지 않는다.
    /// </summary>
    public bool BeginCinematic()
    {
        if (CurrentState == PlayerActionState.Dead)
            return false;

        if (cinematicLocked)
            return true;

        cinematicLocked = true;
        SetState(CreateState(PlayerActionState.Cinematic));
        return true;
    }

    /// <summary>연출 잠금 해제 후 Idle 복귀. 잠금이 없으면 아무것도 하지 않는다.</summary>
    public bool EndCinematic()
    {
        if (!cinematicLocked)
            return false;

        cinematicLocked = false;

        if (CurrentState == PlayerActionState.Cinematic)
            ChangeState(PlayerActionState.Idle);

        return true;
    }

    /// <summary>연출 잠금 여부(로컬 FSM 기준). 권위 값은 <see cref="PlayerEncounterLock"/>가 갖는다.</summary>
    public bool IsCinematicLocked => cinematicLocked;

    // Dash는 방향·속도·지속시간이 필수라 인스턴스 주입 경로로만 진입한다. (예측 게이트는 PlayerDashController가 확인)
    public bool BeginDash(Vector3 planarDirection, float speed, float duration)
    {
        if (CurrentState == PlayerActionState.Dead || cinematicLocked)
        {
            Edit.LogWarning(
                $"[Dash] 상태 진입 거부: {(CurrentState == PlayerActionState.Dead ? "사망 상태" : "연출 잠금(Cinematic) 중")}입니다. " +
                $"현재상태={CurrentState}", this);
            return false;
        }

        // 대시 진입은 CanEnter를 거치지 않는 강제 전이라 진행 중인 상태를 덮는다 — 무엇을 덮었는지 남긴다.
        if (CurrentState != PlayerActionState.Idle && CurrentState != PlayerActionState.Move)
        {
            Edit.LogWarning($"[Dash] 상태 진입: {CurrentState} 상태를 덮어쓰고 대시로 전이합니다.", this);
        }

        SetState(new PlayerDashState(context, planarDirection, speed, duration));
        return true;
    }

    public void EndDash()
    {
        if (CurrentState == PlayerActionState.Dash)
        {
            ChangeState(PlayerActionState.Idle);
            return;
        }

        // 서버 취소가 대시 종료 후에 도착한 경우 — 대시는 이미 끝나 있어 아무 일도 일어나지 않는다.
        Edit.Log($"[Dash] EndDash 무효: 현재 상태가 Dash가 아닙니다(={CurrentState}).", this);
    }

    // ── 대쉬 간파(할 일 115) — FSM 은 Dash 그대로, 간파 스킬은 대시 상태에 '실린다'. 대시가 끝나면 Skill 로 바로 넘어간다 ──

    /// <summary>지금 대시 상태. 대시 중이 아니면 null.</summary>
    public PlayerDashState ActiveDash => currentState as PlayerDashState;

    /// <summary>대쉬 간파를 막는 차단 효과(스킬 또는 간파 봉인)가 걸려 있다.</summary>
    public bool BlocksDashInterruptByEffect =>
        context.StatusEffects.BlocksSkill || context.StatusEffects.BlocksInterrupt;

    /// <summary>지금 대시에 간파 스킬을 싣는다. 대시 중이 아니거나 이미 실려 있으면 false.</summary>
    public bool AttachDashInterrupt(PlayerSkillBase skill) =>
        skill != null && currentState is PlayerDashState dash && dash.Attach(skill);

    /// <summary>지금 대시에 실린 그 스킬을 내린다(대시 중 스킬 종료). 실려 있지 않았으면 false.</summary>
    public bool DetachDashInterrupt(PlayerSkillBase skill) =>
        skill != null && currentState is PlayerDashState dash && dash.Detach(skill);

    /// <summary>
    /// 대시가 끝나(시간 만료·유닛 막힘) 실린 간파를 Skill 상태로 잇는다 — Idle/Move 를 거치지 않고, 애니는 재시작하지 않는다.
    /// CanUseSkill 을 보지 않는다(이미 승인돼 실행 중인 스킬). 그 대시가 지금 상태가 아니면 false.
    /// </summary>
    internal bool BeginSkillFromDash(PlayerDashState dash, PlayerSkillBase skill)
    {
        if (skill == null || currentState != dash)
            return false;

        dash.MarkHandOff();
        SetState(new PlayerSkillState(context, skill, skill.EntryActionState));
        context.Skills?.CompleteDashInterruptHandOff(skill);
        return true;
    }

    /// <summary>스킬 실행 상태 계열(Skill·Focus)인가. 스킬 수명은 이 계열 안에서는 유지된다.</summary>
    public bool IsInSkillState => IsSkillState(CurrentState);

    public static bool IsSkillState(PlayerActionState state) =>
        state == PlayerActionState.Skill || state == PlayerActionState.Focus;

    // Skill 상태는 실행할 스킬 인스턴스가 필요해 BeginKnockback처럼 인스턴스 주입 경로로만 진입한다.
    // 진입 상태는 스킬이 정한다(기본 Skill, 거너 Q = Focus).
    public bool BeginSkill(PlayerSkillBase skill)
    {
        if (skill == null || !CanUseSkill)
            return false;

        SetState(new PlayerSkillState(context, skill, skill.EntryActionState));
        return true;
    }

    /// <summary>
    /// 같은 스킬을 유지한 채 스킬 상태 계열 안에서 단계만 바꾼다(Focus → Skill). 스킬은 취소되지 않는다.
    /// 이미 그 단계면 아무것도 안 한다. 스킬 상태가 아니면 false.
    /// </summary>
    public bool ChangeSkillPhase(PlayerActionState phase)
    {
        if (!IsSkillState(phase) || !(currentState is PlayerSkillState skillState))
            return false;

        if (CurrentState == phase)
            return true;

        SetState(new PlayerSkillState(context, skillState.Skill, phase));
        return true;
    }

    public void EndSkill()
    {
        if (IsInSkillState)
            ChangeState(PlayerActionState.Idle);
    }

    private bool CanEnter(PlayerActionState nextState)
    {
        if (CurrentState == PlayerActionState.Dead)
            return false;

        // 연출 잠금 중에는 어떤 상태로도 나가지 못한다. 해제는 EndCinematic만 할 수 있다.
        if (cinematicLocked)
            return false;

        return nextState switch
        {
            // 준비 자세에서 연사로 넘어가는 것은 Idle/Move 조건과 무관하다(이미 승인된 같은 공격의 다음 단계).
            PlayerActionState.Attack => (CanAttack || CurrentState == PlayerActionState.AttackReady) &&
                                        context.DefaultAttack != null && context.DefaultAttack.CanStartApprovedAttack,
            PlayerActionState.AttackReady => CanAttack && context.DefaultAttack != null && context.DefaultAttack.CanStartApprovedAttack,
            PlayerActionState.Focus => false, // 스킬 인스턴스가 필수라 BeginSkill(skill)으로만 진입
            PlayerActionState.Interrupt => CanInterrupt && PlayerInterruptState.CanStart(context),
            PlayerActionState.Skill => false, // 스킬 인스턴스가 필수라 BeginSkill(skill)으로만 진입
            PlayerActionState.Move => !context.StatusEffects.BlocksMovement,
            PlayerActionState.Idle => true,
            PlayerActionState.Restrained => true,
            PlayerActionState.Knockback => false, // 방향·세기가 필수라 BeginKnockback(direction, strength)으로만 진입
            PlayerActionState.Dash => false, // 방향·속도·지속이 필수라 BeginDash(...)로만 진입
            PlayerActionState.Dead => true,
            _ => false
        };
    }

    private IPlayerState CreateState(PlayerActionState state)
    {
        return state switch
        {
            PlayerActionState.Idle => new PlayerIdleState(context),
            PlayerActionState.Move => new PlayerMoveState(context),
            PlayerActionState.Attack => new PlayerAttackState(context, PlayerActionState.Attack),
            PlayerActionState.AttackReady => new PlayerAttackState(context, PlayerActionState.AttackReady),
            PlayerActionState.Interrupt => new PlayerInterruptState(context),
            PlayerActionState.Restrained => new PlayerRestrainedState(context),
            PlayerActionState.Dead => new PlayerLockedState(context, PlayerActionState.Dead),
            PlayerActionState.Cinematic => new PlayerLockedState(context, PlayerActionState.Cinematic),
            _ => new PlayerIdleState(context)
        };
    }

    // SetState는 CanEnter를 거치지 않는 강제 전이 경로다(넉백·그랩·연출·대시). 대시를 끊는 주범이라
    // 요청 지점을 반드시 기록한다 — 기록된 값은 PlayerDashState.Exit 로그에 그대로 실린다.
    private void SetState(
        IPlayerState nextState,
        [CallerMemberName] string callerMember = null,
        [CallerFilePath] string callerFile = null,
        [CallerLineNumber] int callerLine = 0)
    {
        LastTransitionCause = DescribeCaller(callerMember, callerFile, callerLine);

        PlayerActionState previousState = CurrentState;
        ExitCurrentState(nextState.StateType);
        currentState = nextState;
        currentStateDebug = currentState.StateType;
        currentState.Enter(previousState);
        OnActionStateChanged();
    }

    // 서버·오너 양쪽에서 도는 공통 가드. 슈퍼아머 판정은 여기 두지 않는다(TryReceiveRestraint 주석 참고).
    private bool CanReceiveRestraint(RestraintContext restraintContext)
    {
        return CurrentState != PlayerActionState.Dead &&
            CurrentState != PlayerActionState.Restrained &&
            !cinematicLocked;
    }

    private bool CanReceiveServerInteraction()
    {
        if (context?.Player == null)
            return false;

        return !context.Player.IsSpawned || context.Player.IsServer;
    }
}

/// <summary>
/// 서버가 플레이어의 위치·입력을 잠시 통제하는 방식.
/// ⚠️ 값은 반드시 끝에만 추가할 것 — RPC로 byte 전송된다.
/// </summary>
public enum RestraintMode : byte
{
    /// <summary>잡기 — 시전자의 <see cref="GrabController.GrabSocket"/>에 종속된다.</summary>
    Carry = 0,

    /// <summary>돌진 밀기 — 시전자 정면 offset 지점을 따라간다. 소켓이 필요 없다.</summary>
    Push = 1
}

/// <summary>서버 구속 진입 전에 사용하는 순수 SuperArmor 판정.</summary>
public static class PlayerRestraintPolicy
{
    public static bool IsBlockedBySuperArmor(RestraintMode mode, bool hasSuperArmor)
    {
        if (!hasSuperArmor)
            return false;

        return mode == RestraintMode.Carry || mode == RestraintMode.Push;
    }
}

public readonly struct RestraintContext
{
    public RestraintContext(
        GameObject instigator, GameObject receiver,
        RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f)
    {
        Instigator = instigator;
        Receiver = receiver;
        Mode = mode;
        FrontOffset = frontOffset;
    }

    public GameObject Instigator { get; }
    public GameObject Receiver { get; }
    public RestraintMode Mode { get; }

    /// <summary>Push 전용 — 시전자 정면으로 이 거리만큼 앞에 붙는다. Carry는 무시.</summary>
    public float FrontOffset { get; }
}

public interface IRestraintReceiver
{
    bool TryReceiveRestraint(RestraintContext context);
    bool BeginRestrained(
        GameObject instigator = null, RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f);
    bool EndRestrained();
}

public enum PlayerActionState
{
    Idle,
    Move,
    Attack,
    Interrupt,

    /// <summary>서버가 위치·입력을 통제하는 구속 상태(잡기 = Carry / 돌진 밀기 = Push).</summary>
    Restrained,
    Knockback,
    Dead,
    Skill,
    Dash,

    /// <summary>보스 등장 등 연출 잠금. 서버가 PlayerEncounterLock으로 진입·해제한다.</summary>
    Cinematic,

    // 🔸 아래 둘은 끝에 붙인다 — currentStateDebug 가 정수로 직렬화되므로 중간 삽입은 기존 값을 밀어낸다.

    /// <summary>기본 공격 준비 자세(거너 — 연사 전 1회). 시간이 차면 Attack 으로 넘어간다. 행동 제한은 Attack 과 같다.</summary>
    AttackReady,

    /// <summary>정신 집중(거너 Q 충전). Skill 과 같은 스킬 상태 계열 — 발사하면 Skill 로 넘어간다(스킬은 끊기지 않는다).</summary>
    Focus
}

public sealed class PlayerStateContext
{
    public PlayerStateContext(
        PlayerStateController controller,
        Player player,
        PlayerInputReader input,
        PlayerMovement movement,
        PlayerAimIndicator aim,
        IPlayerBasicAttack defaultAttack,
        StatusEffectController statusEffects,
        PlayerMotor motor,
        Animator animator,
        PlayerSkillController skills,
        PlayerDashController dash,
        PlayerGroundingSensor groundingSensor)
    {
        Controller = controller;
        Player = player;
        Input = input;
        Movement = movement;
        Aim = aim;
        DefaultAttack = defaultAttack;
        StatusEffects = statusEffects;
        Motor = motor;
        Animator = animator;
        Skills = skills;
        Dash = dash;
        GroundingSensor = groundingSensor;
    }

    public PlayerStateController Controller { get; }
    public Player Player { get; }
    public PlayerInputReader Input { get; }
    public PlayerMovement Movement { get; }
    public PlayerAimIndicator Aim { get; }
    // 기본 공격이 없는 프리팹(base 단독)에서는 null — 사용처는 null 허용으로 다룬다
    public IPlayerBasicAttack DefaultAttack { get; }
    public StatusEffectController StatusEffects { get; }
    public PlayerMotor Motor { get; }
    public Animator Animator { get; }
    // 스킬 시스템 미장착 프리팹에서는 null — 사용처는 전부 null 허용으로 다룬다
    public PlayerSkillController Skills { get; }
    // 대시 컨트롤러 미장착 프리팹에서는 null — 사용처는 null 허용으로 다룬다
    public PlayerDashController Dash { get; }
    // 접지 센서 미장착 프리팹에서는 null — 사용처는 null 허용으로 다룬다
    public PlayerGroundingSensor GroundingSensor { get; }
}

public interface IPlayerState
{
    PlayerActionState StateType { get; }
    bool RequiresStateAuthorityTick { get; }
    void Enter(PlayerActionState previousState);
    void Tick();
    void FixedTick();
    void Exit(PlayerActionState nextState);
}

public abstract class PlayerStateBase : IPlayerState
{
    protected readonly PlayerStateContext Context;

    protected PlayerStateBase(PlayerStateContext context)
    {
        Context = context;
    }

    public abstract PlayerActionState StateType { get; }
    public virtual bool RequiresStateAuthorityTick => false;

    public virtual void Enter(PlayerActionState previousState) { }
    public virtual void Tick() { }
    public virtual void FixedTick() { }
    public virtual void Exit(PlayerActionState nextState) { }

    protected bool TryConsumeActionInput()
    {
        // 각 입력은 '이번 프레임 눌림 또는 선입력 버퍼에 살아 있음'으로 본다(할 일 44).
        // 행동 시작에 성공하면 버퍼를 비운다. 바빠서(실행·요청 응답 대기) 또는 행동 불능 CC(기절·에어본)로 못 쓴 입력만
        // 만료까지 남겨 다음 틱에 다시 시도하고, 나머지 거부는 그 입력을 바로 버린다 — PlayerActionAttempt:
        // 자원 부족(쿨타임·충전 없음·과열) / 차단 효과(침묵·속박·약화 — 피드백 이벤트) / 그 밖(CanUse·빈 슬롯·비접지).
        bool dashRequested = Context.Input.DashPressed || Context.Input.HasBufferedInput(PlayerBufferedInput.Dash);

        // 조준 모드 중에는 일반 액션 입력(공격/다른 스킬/인터럽트)을 억제한다.
        // 좌클릭 확정·Esc/재입력 취소는 PlayerSkillTargeting이 직접 처리한다.
        // 대상 지정 중 대시 — 조준 중인 스킬이 허락하면(거너 R) 조준을 취소(쿨 없음)하고 아래 대시 경로로 넘어간다.
        if (Context.Skills != null && dashRequested && Context.Skills.CanCancelTargetingByDash)
            Context.Skills.CancelTargeting();

        if (Context.Skills != null && Context.Skills.IsChoosingTarget)
        {
            if (Context.Input.DashPressed)
            {
                Edit.LogWarning(
                    "[Dash] 입력 무시: 스킬 조준(타겟 선택) 중에는 액션 입력이 억제됩니다. " +
                    "조준을 확정/취소한 뒤 대시가 가능합니다.", Context.Player);
            }

            // 억제된 입력(확정 좌클릭·취소 우클릭·재입력 포함)은 버퍼에도 남기지 않는다 — 조준이 끝난 뒤 뒤늦게 실행되면 이중 실행이다.
            Context.Input.ClearInputBuffer();
            return false;
        }

        // 대시 우선: Idle/Move에서 대시와 공격·스킬이 같은 프레임이면 대시가 이긴다. (PLAN §7)
        // 버린 대시(충전 없음·봉인·비접지)는 아래 공격·스킬 후보로 넘어가고, 보관(행동 불능 CC)이면 이번 프레임은 멈춘다(할 일 77).
        if (Context.Dash != null && dashRequested)
        {
            if (Context.Dash.TryBeginPredictedDash())
            {
                Context.Input.ClearInputBufferOnActionStarted(PlayerBufferedInput.Dash);
                return true;
            }

            PlayerActionAttempt dashAttempt = Context.Controller.ClassifyDashReject();
            Context.Input.ResolveBufferedInput(PlayerBufferedInput.Dash, dashAttempt);
            if (dashAttempt == PlayerActionAttempt.Blocked)
                return false;
        }

        // 기본 공격 뒤로 예약한 스킬(거너, 할 일 44) — 공격이 끝나 돌아온 첫 틱에 다른 입력(평타 press·홀드 포함)보다 먼저 쓴다.
        // 요청 응답 대기(바쁨)면 들고 기다린다 — 그동안 다른 입력으로 넘어가지 않는다.
        PlayerBufferedInput reserved = Context.Input.ReservedSkillInput;
        switch (TryStartReservedSkill())
        {
            case PlayerActionAttempt.Started:
                Context.Input.ClearInputBufferOnActionStarted(reserved);
                return true;
            case PlayerActionAttempt.Blocked:
                return false;
        }

        // 평타·스킬·우클릭은 선입력 버퍼 우선순위 높은 순으로 시도한다(할 일 77, 기본 우클릭 > R > Q·E > 평타).
        // 버퍼는 그중 가장 높은 살아 있는 입력만 꺼내 주므로 각 후보는 '이번 프레임 눌림 또는 꺼낼 입력'일 때만 시도된다.
        // 버림 거부(쿨타임·봉인·과열·사용 조건)면 그 입력만 지워 다음 순위가 올라오고 같은 프레임에 이어서 시도한다.
        // 보관 거부(바쁨·행동 불능 CC)면 전부 그대로 두고 이번 프레임은 멈춘다 — 낮은 후보가 앞지르지 않는다.
        System.Array.Copy(ActionCandidateOrder, actionCandidates, ActionCandidateOrder.Length);
        Context.Input.SortByBufferPriority(actionCandidates);
        for (int i = 0; i < actionCandidates.Length; i++)
        {
            PlayerBufferedInput kind = actionCandidates[i];
            PlayerActionAttempt attempt = TryStartActionCandidate(kind);
            if (attempt == PlayerActionAttempt.Started)
            {
                Context.Input.ClearInputBufferOnActionStarted(kind);
                return true;
            }

            if (attempt == PlayerActionAttempt.Blocked)
                return false;
        }

        if (TryStartAttackOnHold())
        {
            Context.Input.ClearInputBufferOnActionStarted(PlayerBufferedInput.Attack);
            return true;
        }

        return false;
    }

    // 홀드 시작(할 일 44) — 홀드 연사 캐릭터(거너)는 누른 프레임이 아니어도 좌클릭을 누르고 있으면 시작한다.
    // 스킬이 끝났는데 계속 누르고 있으면 평타가 다시 이어진다. 누른 입력(평타·스킬·인터럽트)을 다 본 뒤 마지막에 봐서
    // 버퍼에 남은 스킬 입력을 앞지르지 않는다. 매 프레임 조건이라 거부(과열·차단 효과·요청 중)는 버퍼·피드백 없이 건너뛴다.
    private bool TryStartAttackOnHold()
    {
        IPlayerBasicAttack attack = Context.DefaultAttack;
        return attack != null &&
            PlayerBasicAttackInputRules.ShouldStartOnHold(
                attack.StartsOnHold, Context.Input.AttackHeld, Context.StatusEffects.BlocksAttack,
                Context.Skills != null && Context.Skills.IsBusy) &&
            attack.TryStart();
    }

    /// <summary>
    /// 기본 공격 중 스킬 입력(할 일 44) — 공격 뒤로 예약하는 캐릭터(거너)면 공격을 끊지 않고 예약한 뒤 지금 발로 마무리시킨다
    /// (<see cref="IPlayerBasicAttack.FinishWithoutChaining"/>). 공격은 기존 정상 종료로 끝나고, Idle/Move 첫 틱에
    /// <see cref="TryStartReservedSkill"/> 이 기존 경로로 요청한다 — 서버 승인 규칙은 그대로다.
    /// 차단 효과·쿨타임 거부는 누른 순간 Idle/Move 와 같은 규칙(버림·피드백)이고 예약하지 않으므로 평타는 그대로 이어진다.
    /// 예약하지 않는 캐릭터(가붕이·어쌔신)는 아무것도 하지 않는다 — 입력은 선입력 버퍼(0.2초)가 그대로 들고 있다.
    /// </summary>
    protected void TryReserveSkillDuringAttack()
    {
        IPlayerBasicAttack attack = Context.DefaultAttack;
        PlayerSkillController skills = Context.Skills;
        if (attack == null || !attack.ReservesSkillAfterAttack || skills == null || Context.Input == null ||
            !Context.Player.IsInputSource || skills.IsChoosingTarget)
            return;

        for (int i = 0; i < SkillSlotOrder.Length; i++)
        {
            PlayerSkillSlot slot = SkillSlotOrder[i];
            PlayerBufferedInput kind = PlayerInputBuffer.FromSkillSlot(slot);
            if (!Context.Input.GetSkillPressed(slot) && !Context.Input.HasBufferedInput(kind))
                continue;

            // 빈 슬롯은 버린다. 우클릭은 인터럽트 상태 경로 몫이라 버퍼에 그대로 둔다(Idle/Move 와 같음).
            if (skills.GetSkill(slot) == null)
            {
                if (slot != PlayerSkillSlot.Interrupt)
                    Context.Input.ResolveBufferedInput(kind, PlayerActionAttempt.Rejected);
                continue;
            }

            switch (PlayerBasicAttackInputRules.ClassifySkillDuringAttack(
                        attack.ReservesSkillAfterAttack, Context.StatusEffects.BlocksSkill, skills.IsBlockedByCooldown(slot)))
            {
                case PlayerSkillDuringAttackDecision.RejectByEffect:
                    Context.Input.ResolveBufferedInput(kind, Context.Controller.RejectByEffect(kind));
                    break;
                case PlayerSkillDuringAttackDecision.RejectByCooldown:
                    Context.Input.ResolveBufferedInput(kind, PlayerActionAttempt.OutOfResource);
                    skills.NotifyCooldownRejected(slot);
                    break;
                case PlayerSkillDuringAttackDecision.Reserve:
                    if (Context.Input.ReserveSkillAfterAttack(kind))
                        Edit.Log($"[Skill] {slot} — 기본 공격({Context.Controller.CurrentState}) 지금 발 마무리 뒤 시전 예약", Context.Player);
                    attack.FinishWithoutChaining();
                    break;
            }
        }
    }

    /// <summary>
    /// 예약하지 않는 기본 공격(가붕이·어쌔신) 중 버퍼에 평타보다 우선순위가 높은 스킬 입력(우클릭·R·Q·E)이 살아 있으면
    /// 다음 타를 잇지 않고 지금 타로 마무리시킨다(<see cref="IPlayerBasicAttack.FinishWithoutChaining"/>, 할 일 77 후속).
    /// 평타를 누르고 있으면 콤보 창마다 다음 타가 예약돼 체인이 끝나지 않고, 그동안 스킬 입력이 0.2초 만에 만료됐다.
    /// 입력은 버퍼에 그대로 두고 — 현재 타가 끝나 Idle/Move 로 돌아온 첫 틱에 기존 우선순위 순서로 시도된다.
    /// 쓸 수 없어 보이는 입력(빈 슬롯·차단 효과·쿨타임)은 체인을 끊지 않는다 — 거부 처리는 Idle/Move 의 기존 규칙 그대로.
    /// </summary>
    protected void TryFinishAttackForBufferedSkill()
    {
        IPlayerBasicAttack attack = Context.DefaultAttack;
        PlayerSkillController skills = Context.Skills;
        if (attack == null || attack.ReservesSkillAfterAttack || Context.Input == null ||
            !Context.Player.IsInputSource || (skills != null && skills.IsChoosingTarget))
            return;

        for (int i = 0; i < SkillSlotOrder.Length; i++)
        {
            PlayerSkillSlot slot = SkillSlotOrder[i];
            PlayerBufferedInput kind = PlayerInputBuffer.FromSkillSlot(slot);
            PlayerSkillBase skill = skills != null ? skills.GetSkill(slot) : null;
            // 우클릭에 스킬이 없으면 인터럽트 상태 경로가 같은 입력을 쓴다 — 그 차단 판정으로 본다.
            bool interruptState = skill == null && slot == PlayerSkillSlot.Interrupt;
            bool finish = PlayerBasicAttackInputRules.ShouldFinishChainForBufferedSkill(
                attack.ReservesSkillAfterAttack,
                Context.Input.HasBufferedInputAbove(kind, PlayerBufferedInput.Attack),
                skill != null || interruptState,
                interruptState ? Context.StatusEffects.BlocksInterrupt : Context.StatusEffects.BlocksSkill,
                skill != null && skills.IsBlockedByCooldown(slot));
            if (!finish)
                continue;

            attack.FinishWithoutChaining();
            return;
        }
    }

    /// <summary>
    /// 기본 공격 뒤로 예약한 스킬을 Idle/Move 에서 쓴다(할 일 44). 판정은 버퍼 입력과 같다 — 바쁨(요청 응답 대기)이면 들고 기다리고(Blocked),
    /// 차단 효과·쿨타임·사용 조건 거부는 예약을 버리고 피드백 규칙도 같다. 행동 불능 CC 면 예약을 버린다(만료 없는 예약을 CC 너머로 끌고 가지 않는다).
    /// 예약이 없거나 버렸으면 Rejected — 호출자는 다음 입력으로 넘어간다.
    /// </summary>
    private PlayerActionAttempt TryStartReservedSkill()
    {
        PlayerBufferedInput kind = Context.Input.ReservedSkillInput;
        PlayerSkillController skills = Context.Skills;
        if (kind == PlayerBufferedInput.None)
            return PlayerActionAttempt.Rejected;

        if (skills == null || !PlayerInputBuffer.TryGetSkillSlot(kind, out PlayerSkillSlot slot) || skills.GetSkill(slot) == null)
        {
            Context.Input.ClearSkillReservation();
            return PlayerActionAttempt.Rejected;
        }

        if (skills.IsBusy)
            return PlayerActionAttempt.Blocked;

        PlayerActionAttempt attempt = UseSkillSlot(skills, slot, kind);
        if (attempt != PlayerActionAttempt.Started)
        {
            Edit.Log($"[Skill] 예약한 {slot} 버림 — {attempt}", Context.Player);
            Context.Input.ClearSkillReservation();
            return PlayerActionAttempt.Rejected;
        }

        return PlayerActionAttempt.Started;
    }

    private static readonly PlayerSkillSlot[] SkillSlotOrder =
    {
        PlayerSkillSlot.Main, PlayerSkillSlot.Sub, PlayerSkillSlot.Interrupt, PlayerSkillSlot.Ultimate
    };

    // Idle/Move 후보 기본 순서 — 버퍼 우선순위로 정렬하고, 같은 우선순위면 이 순서(할 일 77). 대시는 위에서 먼저 본다.
    private static readonly PlayerBufferedInput[] ActionCandidateOrder =
    {
        PlayerBufferedInput.SkillInterrupt, PlayerBufferedInput.SkillUltimate,
        PlayerBufferedInput.SkillMain, PlayerBufferedInput.SkillSub, PlayerBufferedInput.Attack
    };

    private readonly PlayerBufferedInput[] actionCandidates = new PlayerBufferedInput[ActionCandidateOrder.Length];

    /// <summary>
    /// 후보 하나를 시도한다. 요청되지 않았거나(안 눌림·꺼낼 입력 아님) 버림 거부면 다음 후보로 넘어가도록 Started·Blocked 가 아닌 값을 돌려준다.
    /// 버퍼 반영(<see cref="PlayerInputReader.ResolveBufferedInput"/>)은 시작 성공을 뺀 나머지를 여기서 한다.
    /// </summary>
    private PlayerActionAttempt TryStartActionCandidate(PlayerBufferedInput kind)
    {
        switch (kind)
        {
            case PlayerBufferedInput.Attack:
                return TryStartAttackInput();
            case PlayerBufferedInput.SkillInterrupt:
                return TryStartInterruptInput();
            default:
                return Context.Skills != null && PlayerInputBuffer.TryGetSkillSlot(kind, out PlayerSkillSlot slot)
                    ? TryStartSkill(Context.Skills, slot)
                    : PlayerActionAttempt.Rejected;
        }
    }

    // 공격 시작은 누른 프레임(press)이나 버퍼된 press 에만 허용한다. 홀드 상태로는 시작되지 않으므로,
    // Once 정책에서 체인이 끝난 뒤 계속 누르고 있어도 재시작되지 않는다(릴리즈 요구).
    // 예외: 홀드 시작 캐릭터(거너)는 맨 끝(TryStartAttackOnHold)에서 누르고 있기만 해도 시작한다.
    private PlayerActionAttempt TryStartAttackInput()
    {
        if ((!Context.Input.AttackPressed && !Context.Input.HasBufferedInput(PlayerBufferedInput.Attack)) ||
            Context.DefaultAttack == null)
            return PlayerActionAttempt.Rejected;

        // 상태이상 차단은 시작 요청 전에 가린다 — TryStart 는 이를 보지 않고 요청부터 보낸다(서버가 거부해도 입력은 사라졌다).
        PlayerActionAttempt attempt;
        if (Context.StatusEffects.BlocksAttack)
            attempt = Context.Controller.RejectByEffect(PlayerBufferedInput.Attack);
        else if (Context.DefaultAttack.TryStart())
            return PlayerActionAttempt.Started;
        else // 과열은 버림, 그 밖(공격·요청 중)은 바쁨이라 남긴다.
            attempt = Context.DefaultAttack.IsOutOfResource ? PlayerActionAttempt.OutOfResource : PlayerActionAttempt.Blocked;

        Context.Input.ResolveBufferedInput(PlayerBufferedInput.Attack, attempt);
        return attempt;
    }

    // 우클릭 — 스킬(단죄의 방패 등)이 배정돼 있으면 스킬 경로가 먼저 판정하고, 시작·보관이 아니면 기존 인터럽트 상태 경로로 넘어간다.
    private PlayerActionAttempt TryStartInterruptInput()
    {
        if (Context.Skills != null)
        {
            PlayerActionAttempt skillAttempt = TryStartSkill(Context.Skills, PlayerSkillSlot.Interrupt);
            if (skillAttempt == PlayerActionAttempt.Started || skillAttempt == PlayerActionAttempt.Blocked)
                return skillAttempt;
        }

        if (!Context.Input.InterruptPressed && !Context.Input.HasBufferedInput(PlayerBufferedInput.SkillInterrupt))
            return PlayerActionAttempt.Rejected;

        // 인터럽트 상태 경로는 우클릭에 스킬이 없을 때만 쓴다. 스킬이 배정돼 있으면 위 스킬 경로가 이 입력을 이미 판정했다 —
        // 여기서 인터럽트 상태로 새면 원격 클라는 서버가 승인한 간파를 스킬 상태로 받지 못해
        // (PlaySkillClientRpc 의 BeginSkill 거부) 간파 모션이 씹힌다(할 일 74). 다시 버리거나 피드백하지도 않는다.
        if (Context.Skills != null && Context.Skills.GetSkill(PlayerSkillSlot.Interrupt) != null)
            return PlayerActionAttempt.Rejected;

        if (Context.Controller.ChangeState(PlayerActionState.Interrupt))
            return PlayerActionAttempt.Started;

        PlayerActionAttempt attempt = Context.StatusEffects.BlocksInterrupt
            ? Context.Controller.RejectByEffect(PlayerBufferedInput.SkillInterrupt)
            : PlayerActionAttempt.Rejected;
        Context.Input.ResolveBufferedInput(PlayerBufferedInput.SkillInterrupt, attempt);
        return attempt;
    }

    // Interrupt 슬롯에 스킬(단죄의 방패)이 배정되면 여기서 소비되어 기존 Interrupt 상태 경로를 자연히 대체한다.
    // 안 눌렸거나 빈 슬롯이면 Rejected(다음 후보로), 바쁨이면 Blocked(보관 — 이번 프레임 중단).
    private PlayerActionAttempt TryStartSkill(PlayerSkillController skills, PlayerSkillSlot slot)
    {
        PlayerBufferedInput kind = PlayerInputBuffer.FromSkillSlot(slot);
        if (!Context.Input.GetSkillPressed(slot) && !Context.Input.HasBufferedInput(kind))
            return PlayerActionAttempt.Rejected;

        // 빈 슬롯은 버린다. 단 우클릭은 인터럽트 상태 경로가 같은 입력을 쓰므로 그쪽에 맡긴다.
        if (skills.GetSkill(slot) == null)
        {
            if (slot != PlayerSkillSlot.Interrupt)
                Context.Input.ResolveBufferedInput(kind, PlayerActionAttempt.Rejected);
            return PlayerActionAttempt.Rejected;
        }

        // 실행·요청 응답 대기(바쁨) — 남겨 두었다가 풀린 첫 틱에 다시 판정한다.
        if (skills.IsBusy)
            return PlayerActionAttempt.Blocked;

        PlayerActionAttempt attempt = UseSkillSlot(skills, slot, kind);
        if (attempt != PlayerActionAttempt.Started)
            Context.Input.ResolveBufferedInput(kind, attempt);
        return attempt;
    }

    // 스킬 시전 시도와 거부 피드백 — 버퍼 반영은 호출자 몫(버퍼 입력 / 공격 뒤 예약).
    private PlayerActionAttempt UseSkillSlot(PlayerSkillController skills, PlayerSkillSlot slot, PlayerBufferedInput kind)
    {
        // 상태이상 차단 — 기절·에어본(CC)은 남기고, 침묵 등 봉인은 버리고 차단 피드백 1회. 요청은 보내지 않는다
        // (원격 오너도 복제된 상태이상 목록으로 판정 — 서버 최종 검증은 그대로). 쿨타임보다 먼저 본다:
        // CC 중 입력은 풀린 뒤에 쿨타임을 다시 판정하고, 봉인 + 쿨타임이면 봉인 피드백 하나만 낸다.
        if (Context.StatusEffects.BlocksSkill)
            return Context.Controller.RejectByEffect(kind);

        // 쿨타임 거부 — 시전 요청을 보내지 않고 입력을 버린 뒤 피드백을 한 번 낸다. 버렸으니 다음 틱에 같은 입력이
        // 다시 거부·피드백되지 않는다(입력 1회 = 피드백 1회).
        if (skills.IsBlockedByCooldown(slot))
        {
            skills.NotifyCooldownRejected(slot);
            return PlayerActionAttempt.OutOfResource;
        }

        // 사용 조건(CanUse)·조준 진입 실패 등은 버린다(피드백 없음).
        return skills.TryUse(slot) ? PlayerActionAttempt.Started : PlayerActionAttempt.Rejected;
    }
}

public sealed class PlayerIdleState : PlayerStateBase
{
    public PlayerIdleState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Idle;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
    }

    public override void Tick()
    {
        if (TryConsumeActionInput())
            return;

        if (Context.Input.HasMoveInput)
            Context.Controller.ChangeState(PlayerActionState.Move);
    }
}

public sealed class PlayerMoveState : PlayerStateBase
{
    public PlayerMoveState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Move;

    public override void Enter(PlayerActionState previousState)
    {
        // 공격이 끝나고 남은 클립(꼬리)이 재생 중이면 여기서 끊는다.
        // 애니메이터의 공격 상태에는 IsMoving 전환이 없어 클립이 끝날 때까지(ExitTime 1.0)
        // 이동 애니메이션으로 못 넘어간다. 이동이 시작되는 이 지점이 끊기에 가장 정확한 자리다.
        Context.DefaultAttack?.CancelFinishingTailForMovement();

        Context.Player.SetAnimatorMoving(true);
    }

    public override void Tick()
    {
        if (TryConsumeActionInput())
            return;

        if (!Context.Input.HasMoveInput || Context.StatusEffects.BlocksMovement)
        {
            Context.Controller.ChangeState(PlayerActionState.Idle);
            return;
        }

        Context.Player.SetAnimatorMoving(true);
    }

    public override void Exit(PlayerActionState nextState)
    {
        if (nextState != PlayerActionState.Move)
            Context.Player.SetAnimatorMoving(false);
    }
}

/// <summary>
/// 기본 공격 상태. Attack(공격 중)과 AttackReady(준비 자세 — 거너)가 같은 동작을 공유한다:
/// 이동 없음, 기본 공격 컴포넌트 틱. AttackReady ↔ Attack 사이 전이는 공격을 끊지 않는다.
/// </summary>
public sealed class PlayerAttackState : PlayerStateBase
{
    private readonly PlayerActionState stateType;

    public PlayerAttackState(PlayerStateContext context, PlayerActionState stateType) : base(context)
    {
        this.stateType = stateType;
    }

    public override PlayerActionState StateType => stateType;
    public override bool RequiresStateAuthorityTick => true;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
        Context.DefaultAttack?.BeginFromState();
    }

    public override void Tick()
    {
        // 공격 뒤로 스킬을 예약하는 공격(거너)이면 스킬 입력을 먼저 본다 — 예약되면 이번 틱부터 다음 발을 잇지 않는다(할 일 44).
        TryReserveSkillDuringAttack();
        // 예약하지 않는 공격(가붕이·어쌔신)은 버퍼에 평타보다 높은 스킬 입력이 있으면 다음 타를 잇지 않는다(할 일 77 후속).
        TryFinishAttackForBufferedSkill();

        Context.Player.SetAnimatorMoving(false);
        Context.DefaultAttack?.Tick();
    }

    public override void FixedTick()
    {
        Context.DefaultAttack?.FixedTickMovement();
    }

    public override void Exit(PlayerActionState nextState)
    {
        if (nextState != PlayerActionState.Attack && nextState != PlayerActionState.AttackReady)
            Context.DefaultAttack?.CancelCurrentAttack();
    }
}

public sealed class PlayerInterruptState : PlayerStateBase
{
    private static readonly int InterruptHash = Animator.StringToHash("Interrupt");

    private Vector3 actionDirection;
    private float actionEndTime;
    private float actionMoveRemaining;
    private float actionMoveSpeed;

    public PlayerInterruptState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Interrupt;

    public static bool CanStart(PlayerStateContext context)
    {
        return context.Player.InterruptDuration > 0f &&
            context.Aim.AimDirection.sqrMagnitude >= 0.001f;
    }

    public override void Enter(PlayerActionState previousState)
    {
        Vector3 aimDirection = Context.Aim.AimDirection;

        if (aimDirection.sqrMagnitude < 0.001f || Context.Player.InterruptDuration <= 0f)
        {
            Context.Controller.ChangeState(PlayerActionState.Idle);
            return;
        }

        actionDirection = aimDirection.normalized;
        actionEndTime = Time.time + Context.Player.InterruptDuration;
        actionMoveRemaining = Mathf.Max(Context.Player.InterruptForwardDistance, 0f);
        actionMoveSpeed = actionMoveRemaining / Context.Player.InterruptDuration;

        Context.Movement.RotateImmediately(actionDirection);
        Context.Player.SetAnimatorMoving(false);

        if (Context.Animator != null)
            Context.Animator.SetTrigger(InterruptHash);
    }

    public override void Tick()
    {
        Context.Player.SetAnimatorMoving(false);

        if (Time.time >= actionEndTime)
            Context.Controller.ChangeState(PlayerActionState.Idle);
    }

    public override void FixedTick()
    {
        MoveDuringAction();
    }

    private void MoveDuringAction()
    {
        if (actionMoveRemaining <= 0f || Time.fixedTime >= actionEndTime)
            return;

        float moveDistance = Mathf.Min(actionMoveSpeed * Time.fixedDeltaTime, actionMoveRemaining);
        actionMoveRemaining -= moveDistance;
        Context.Motor.AddDisplacement(actionDirection * moveDistance);
    }
}

public sealed class PlayerLockedState : PlayerStateBase
{
    public PlayerLockedState(PlayerStateContext context, PlayerActionState stateType) : base(context)
    {
        StateType = stateType;
    }

    public override PlayerActionState StateType { get; }

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);

        // 사망·연출 잠금 전에 눌린 선입력은 풀린 뒤에 실행하지 않는다.
        Context.Input.ClearInputBuffer();
    }
}

/// <summary>
/// 서버가 플레이어의 위치·입력을 잠시 통제하는 상태. 두 모드를 한 상태로 묶는다:
/// <see cref="RestraintMode.Carry"/>(잡기 — 소켓 종속) / <see cref="RestraintMode.Push"/>(돌진 밀기 — 시전자 정면 추종).
///
/// 권위 분담(networking.md 유지): 상태 진입·해제 결정은 <b>서버</b>, 위치를 실제로 옮기는 것은 <b>오너</b>다.
/// 서버가 위치를 직접 쓰지 않으므로 "플레이어 이동은 오너 권한" 원칙이 깨지지 않는다.
/// 입력 차단은 상태 진입만으로 성립한다 — CanMove/CanUseSkill/CanAttack이 Idle|Move에서만 참이다.
/// </summary>
public sealed class PlayerRestrainedState : PlayerStateBase
{
    private readonly GameObject instigator;
    private readonly RestraintMode mode;
    private readonly float frontOffset;
    private readonly PlayerMotor motor;

    // Carry 전용. null이면 위치 추종만 건너뛰고 물리 위임·입력 차단은 그대로 간다
    // (보스에 잡기 소켓이 아직 없는 동안 "제자리에 붙잡힘"이 이 성질로 성립한다 — 유지할 것).
    private Transform followTarget;

    // Push 전용. 시전자 피벗 높이를 따라가지 않도록 Y는 진입 시점 값으로 고정한다.
    // 시전자 Y를 그대로 쓰면 피벗 높이가 다를 때 플레이어가 조용히 뜨거나 잠긴다.
    private float pushHeight;

    public PlayerRestrainedState(
        PlayerStateContext context, GameObject instigator = null,
        RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f) : base(context)
    {
        this.instigator = instigator;
        this.mode = mode;
        this.frontOffset = frontOffset;

        motor = context.Motor;
    }

    public override PlayerActionState StateType => PlayerActionState.Restrained;
    public override bool RequiresStateAuthorityTick => true;

    public override void Enter(PlayerActionState previousState)
    {
        Context.DefaultAttack?.CancelCurrentAttack();
        Context.Player.SetAnimatorMoving(false);

        if (mode == RestraintMode.Carry)
        {
            GrabController grabController = instigator != null
                ? instigator.GetComponentInChildren<GrabController>()
                : null;
            followTarget = grabController != null ? grabController.GrabSocket : null;
        }

        pushHeight = Context.Player.transform.position.y;

        FaceInstigator();
        // TODO: Play the restrained animation here after the Animator parameter/clip is configured.
        // Example: Context.Animator.SetBool("IsGrabbed", true);
    }

    public override void Exit(PlayerActionState nextState)
    {
        FaceInstigator();

        // 놓는 자리도 같은 규칙 — 시전자 중심에서 벽 앞까지만(할 일 136). 마지막 포즈가 이미 잘려 있으면 할 일이 없다.
        if (Context.Player.IsMotionAuthority && motor != null && instigator != null)
        {
            Vector3 current = Context.Player.transform.position;
            Vector3 placed = ClampToStatic(current);
            if ((placed - current).sqrMagnitude > 0.0001f)
                motor.SetPoseTarget(placed, Context.Player.transform.rotation);
        }
        // TODO: Stop the restrained animation here after the Animator parameter/clip is configured.
        // Example: Context.Animator.SetBool("IsGrabbed", false);
    }

    public override void FixedTick()
    {
        // 구속 포즈는 서버가 확정해 NT로 복제한다. 오너가 별도로 따라가면 서버 포즈와 경쟁한다.
        if (!Context.Player.IsMotionAuthority)
            return;

        if (!TryGetTargetPose(out Vector3 position, out Quaternion rotation))
            return;

        motor?.SetPoseTarget(ClampToStatic(position), rotation);
    }

    /// <summary>
    /// 포즈 목표점을 시전자 중심 → 목표점 정적 스윕(플레이어 몸)으로 잘라 벽 앞에 둔다(할 일 136).
    /// Push 정면점(grabRadius × 0.6 ≈ 2.2m)·Carry 손 소켓이 벽 너머로 나가 몸이 벽에 박히던 것을 막는다.
    /// 스윕 높이는 목표점 높이 — 시전자 피벗 높이를 쓰면 바닥·턱에 걸린다.
    /// </summary>
    private Vector3 ClampToStatic(Vector3 target)
    {
        if (motor == null || instigator == null)
            return target;

        Vector3 origin = instigator.transform.position;
        origin.y = target.y;
        return motor.ClampPlacementToStatic(origin, target);
    }

    /// <summary>추종할 목표 위치·회전. 대상이 사라졌거나 소켓이 없으면 false — 이때 위치만 놓아준다.</summary>
    private bool TryGetTargetPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;

        if (mode == RestraintMode.Carry)
        {
            if (followTarget == null)
                return false;

            position = followTarget.position;
            rotation = followTarget.rotation;
            return true;
        }

        // Push: 시전자 정면 offset 지점. 시전자 루트는 NetworkTransform으로 복제되므로
        // 오너 클라에서도 월드 위치가 성립한다 — 별도 소켓 오브젝트가 필요 없다.
        if (instigator == null)
            return false;

        Transform source = instigator.transform;
        Vector3 forward = source.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        position = source.position + forward * frontOffset;
        position.y = pushHeight;

        // 밀리는 동안 보스를 마주본다(잡기와 같은 시맨틱)
        rotation = Quaternion.LookRotation(-forward);
        return true;
    }

    // 소켓/정면 추종 중 생긴 기울어짐을 정리하고 시전자 방향(yaw만)으로 세운다.
    // instigator가 없으면 현재 바라보던 방향을 유지한 채 똑바로만 세운다.
    private void FaceInstigator()
    {
        Vector3 lookDirection = instigator != null
            ? instigator.transform.position - Context.Player.transform.position
            : Context.Player.transform.forward;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion rotation = Quaternion.LookRotation(lookDirection.normalized);

        Context.Player.transform.rotation = rotation;
    }
}

public sealed class PlayerKnockbackState : PlayerStateBase
{
    private readonly Vector3 direction;
    private readonly float strength;
    private float startTime;
    private float plannedDuration;

    public PlayerKnockbackState(PlayerStateContext context, Vector3 direction, float strength) : base(context)
    {
        this.direction = direction;
        this.strength = strength;
    }

    public override PlayerActionState StateType => PlayerActionState.Knockback;
    public override bool RequiresStateAuthorityTick => true;

    public override void Enter(PlayerActionState previousState)
    {
        Context.DefaultAttack?.CancelCurrentAttack();

        Context.Player.SetAnimatorMoving(false);

        startTime = Time.time;

        // 넉백 속도는 판정 주체인 서버가 확정하고 Motor 커밋 경로로 복제한다.
        if (!Context.Player.IsMotionAuthority || Context.Motor == null)
            return;

        Vector3 initialVelocity = direction.sqrMagnitude > 0.001f && strength > 0f
            ? direction.normalized * strength
            : Vector3.zero;
        Context.Motor.SetKnockbackVelocity(initialVelocity);

        float deceleration = Context.Controller.KnockbackDeceleration;
        float timeToStop = deceleration > 0f
            ? Mathf.Max(0f, initialVelocity.magnitude - Context.Controller.KnockbackStopSpeed) / deceleration
            : Context.Controller.MaxKnockbackTime;
        plannedDuration = Mathf.Clamp(
            timeToStop,
            Context.Controller.MinKnockbackTime,
            Context.Controller.MaxKnockbackTime);

    }

    public override void Tick()
    {
        if (!Context.Player.IsMotionAuthority)
        {
            // 비권위 오너는 서버가 복제할 종료를 기다리고, 로컬 상태만 안전 타임아웃으로 정리한다.
            float serverFallbackTimeout = Context.Controller.MaxKnockbackTime +
                                          Context.Controller.ServerKnockbackReportGraceTime;
            if (Time.time - startTime >= serverFallbackTimeout)
            {
                Context.Controller.EndKnockback();
            }
            return;
        }

        if (Context.Motor == null)
        {
            EndAndNotifyServer("motor-null", Time.time - startTime, -1f);
            return;
        }

        float elapsed = Time.time - startTime;
        if (elapsed >= plannedDuration)
            EndAndNotifyServer("planned-duration", elapsed, Context.Motor.KnockbackVelocity.magnitude);
    }

    public override void Exit(PlayerActionState nextState)
    {
        Context.Motor?.ClearKnockbackVelocity();
    }

    private void EndAndNotifyServer(string reason, float elapsed, float speed)
    {
        Context.Controller.EndKnockback();
        Context.Player.NotifyKnockbackEnded();
    }

}

// 이동 결과 권위 대시. 방향·Root Yaw를 시작 순간 확정하고 지속시간 동안 바꾸지 않는다. (PLAN §7, 불변식 6)
// W2는 평지 단순 이동만 담당한다. 경사·벽·절벽·공중 관성은 W3에서 대체·확장한다.
public sealed class PlayerDashState : PlayerStateBase
{
    private readonly Vector3 direction; // 평면 정규화 방향(시작 순간 확정)
    private readonly float speed;
    private readonly float duration;    // 요청된 지속시간(진단 로그용 원본)
    private readonly float startTime;   // 상태 생성 시각(진단용)
    private readonly float endTime;

    // ── 진단 상태(로그 전용) ──
    private int tickCount;
    private Vector3 startPosition;
    private float requestedDistance;    // 스윕 전 요청 이동량 누적
    private float appliedDistance;      // 스윕 후 실제 적용 이동량 누적
    private int blockedTickCount;       // 스윕이 요청 이동량을 대부분 깎은 tick 수
    private bool lostGroundingDuringDash;
    private bool wasGrounded;

    // ── 대쉬 간파(할 일 115) ──
    private PlayerSkillBase attachedSkill;   // 실린 간파(서버 승인 뒤). 대시 끝에 Skill 로 잇는다
    private bool dashInterruptRequested;     // [입력 주체] 이 대시에서 이미 요청했다 — 대시 1회당 1번
    private bool handingOff;                 // 대시 끝 → Skill 인수인계로 나가는 중(Exit 가 강제 종료로 보지 않게)
    private float lastLoggedPressTime = float.NaN;

    public PlayerDashState(PlayerStateContext context, Vector3 planarDirection, float speed, float duration)
        : base(context)
    {
        Vector3 planar = planarDirection;
        planar.y = 0f;
        direction = planar.sqrMagnitude > 0.0001f
            ? planar.normalized
            : Context.Movement.CurrentFacing;
        this.speed = Mathf.Max(0f, speed);
        this.duration = Mathf.Max(0f, duration);
        startTime = Time.time;
        endTime = Time.time + Mathf.Max(0f, duration);
    }

    public override PlayerActionState StateType => PlayerActionState.Dash;
    public override bool RequiresStateAuthorityTick => true;

    public Vector3 Direction => direction;
    public float Speed => speed;
    /// <summary>이 피어에서 대시가 시작된 뒤 지난 초.</summary>
    public float Elapsed => Time.time - startTime;
    /// <summary>남은 대시 시간(초, 음수 없음).</summary>
    public float Remaining => Mathf.Max(0f, endTime - Time.time);
    /// <summary>실린 간파(할 일 115). 없으면 null.</summary>
    public PlayerSkillBase AttachedSkill => attachedSkill;

    internal bool Attach(PlayerSkillBase skill)
    {
        if (attachedSkill != null)
            return false;

        attachedSkill = skill;
        dashInterruptRequested = true;
        return true;
    }

    internal bool Detach(PlayerSkillBase skill)
    {
        if (attachedSkill == null || attachedSkill != skill)
            return false;

        attachedSkill = null;
        return true;
    }

    internal void MarkHandOff() => handingOff = true;

    /// <summary>대시 이동량 계산에 쓰는 Motor/Rigidbody 기준 현재 위치.</summary>
    private Vector3 CurrentPosition =>
        Context.Motor != null ? Context.Motor.Position : Context.Player.transform.position;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
        Context.Movement.RotateImmediately(direction);
        // 나가는 상태(공격·스킬)의 정리는 Dodge 가 있으면 0층을 건드리지 않았다 — 여기서 튼 Dodge 가 최종으로 남는다.
        Context.Dash?.PlayDodgeStart(duration);
        // 우클릭 → 대시 전환이면 나가는 Skill 상태가 넘긴 간파를 이 대시에 싣는다(할 일 115 §12). 아니면 아무것도 안 한다.
        Context.Skills?.CompleteDashTransfer(this);

        startPosition = CurrentPosition;
        PlayerGroundingSensor sensor = Context.GroundingSensor;
        wasGrounded = sensor != null && sensor.IsGrounded;
        if (Context.Motor != null)
            Context.Motor.MovementResolved += OnMotorMovementResolved;

        Edit.Log(
            $"[Dash] 상태 진입 — 이전상태={previousState} 방향=({direction.x:F2}, {direction.z:F2}) " +
            $"속도={speed:F1} 지속={duration:F3}s 종료예정={endTime:F3} 접지={wasGrounded} " +
            $"이동결과권한={Context.Player.IsMotionAuthority} 시작위치={startPosition} | 충돌해결=PlayerMotor", Context.Player);

        if (Context.Motor == null)
        {
            Edit.LogWarning(
                "[Dash] PlayerMotor가 없어 대시 이동을 제출할 수 없습니다.",
                Context.Player);
        }

        if (sensor == null)
        {
            Edit.LogWarning(
                "[Dash] PlayerGroundingSensor가 없어 접지 전이 진단을 기록할 수 없습니다.",
                Context.Player);
        }
    }

    public override void Tick()
    {
        // 구간 속도(할 일 92-5) — 끝나는 프레임에도 먼저 쓰고, 이어지는 Exit 가 기본값 1 로 되돌린다.
        Context.Dash?.TickDodgeSpeed();
        TickDashInterruptInput();
        if (Time.time < endTime)
            return;

        if (TryHandOffDashInterrupt())
            return;

        // 이동 입력 중이면 Idle 을 거치지 않고 Move 로 — 하체가 Dodge → Run_Loop 로 바로 이어진다(PlayerDashController.PlayDodgeEnd).
        PlayerActionState next = PlayerActionPoseRules.DashEndState(
            Context.Input.HasMoveInput, Context.StatusEffects.BlocksMovement);
        if (next != PlayerActionState.Move || !Context.Controller.ChangeState(PlayerActionState.Move))
            Context.Controller.ChangeState(PlayerActionState.Idle);
    }

    /// <summary>
    /// [입력 주체] 대쉬 간파 입력(할 일 115) — 누른 시각(이번 프레임 또는 선입력 버퍼 기록)이 대시 시작 후 창 안인 우클릭만 쓴다.
    /// 창 밖·우클릭이 간파 스킬이 아닌 경우는 손대지 않는다 — 기존 선입력 버퍼 규칙대로 대시 끝난 뒤 처리된다(§10 Q1).
    /// 차단 효과·쿨타임 거부는 Idle/Move 의 UseSkillSlot 과 같다(피드백 1회 + 버림, §10 Q3). 로그는 입력 1회당 1줄.
    /// </summary>
    private void TickDashInterruptInput()
    {
        PlayerSkillController skills = Context.Skills;
        PlayerInputReader input = Context.Input;
        if (skills == null || input == null || Context.Dash == null || !Context.Player.IsInputSource)
            return;

        float window = Context.Dash.DashInterruptWindow;
        if (!DashInterruptRules.IsEnabled(window) ||
            !input.TryGetPressTime(PlayerBufferedInput.SkillInterrupt, out float pressTime))
            return;

        bool isInterruptSkill = skills.GetSkill(PlayerSkillSlot.Interrupt) is PlayerInterruptSkillBase;
        bool alreadyRequested = dashInterruptRequested || skills.IsBusy;
        DashInterruptInputDecision decision = DashInterruptRules.EvaluateInput(
            true, startTime, pressTime, window, isInterruptSkill, alreadyRequested,
            Context.Controller.BlocksDashInterruptByEffect,
            isInterruptSkill && !alreadyRequested && skills.IsBlockedByCooldown(PlayerSkillSlot.Interrupt));

        // 남겨 둔 입력은 매 프레임 다시 보므로 같은 누름은 한 번만 남긴다.
        bool firstLook = pressTime != lastLoggedPressTime;
        lastLoggedPressTime = pressTime;

        const PlayerBufferedInput kind = PlayerBufferedInput.SkillInterrupt;
        string outcome;
        switch (decision)
        {
            case DashInterruptInputDecision.ConsumeOnly:
                input.ConsumeBufferedInput(kind);
                outcome = "이미 요청 — 소비만";
                break;
            case DashInterruptInputDecision.RejectByEffect:
                input.ResolveBufferedInput(kind, Context.Controller.RejectByEffect(kind));
                outcome = "차단 효과 거부";
                break;
            case DashInterruptInputDecision.RejectByCooldown:
                skills.NotifyCooldownRejected(PlayerSkillSlot.Interrupt);
                input.ResolveBufferedInput(kind, PlayerActionAttempt.OutOfResource);
                outcome = "쿨타임 거부(버림)";
                break;
            case DashInterruptInputDecision.Request:
                if (skills.TryUseDashInterrupt(direction))
                {
                    dashInterruptRequested = true;
                    input.ConsumeBufferedInput(kind);
                    outcome = "요청";
                }
                else
                {
                    input.ResolveBufferedInput(kind, PlayerActionAttempt.Rejected);
                    outcome = "요청 실패(사전 검증 거부 — 버림)";
                }
                break;
            default: // 창 밖·간파 스킬 아님 — 버퍼 그대로
                outcome = decision == DashInterruptInputDecision.OutOfWindow
                    ? "창 밖 — 버퍼 그대로(대시 후 처리)"
                    : "우클릭이 간파 스킬 아님 — 버퍼 그대로";
                break;
        }

        if (firstLook)
        {
            Edit.Log(
                $"[DashInterrupt] 우클릭 {outcome} — 대시 경과 {pressTime - startTime:F3}s / 창 {window:F2}s",
                Context.Player);
        }
    }

    /// <summary>
    /// 대시 끝(시간 만료·유닛 막힘)에 실린 간파가 아직 실행 중이면 Idle/Move 대신 Skill 상태로 잇는다(할 일 115). 이었으면 true.
    /// 서버·오너 각자 자기 대시 끝에서 돈다.
    /// </summary>
    private bool TryHandOffDashInterrupt()
    {
        PlayerSkillBase skill = attachedSkill;
        bool stillActive = skill != null && Context.Skills != null && Context.Skills.ActiveSkill == skill;
        if (!DashInterruptRules.EndsIntoSkill(skill != null, stillActive))
        {
            attachedSkill = null;
            return false;
        }

        return Context.Controller.BeginSkillFromDash(this, skill);
    }

    public override void FixedTick()
    {
        if (Time.fixedTime >= endTime)
            return;

        tickCount++;

        // 정면 벽으로 이동이 0이 되어도 대시 상태는 원래 종료시각까지 유지한다. (불변식: 상태·무적 유지)
        // 단, 몬스터·다른 플레이어에 막히면 그 자리에서 대시를 끝낸다(할 일 11 — OnMotorMovementResolved).
        if (speed <= 0f && tickCount == 1) // 매 tick 반복될 값이라 첫 tick에만 남긴다
        {
            Edit.LogWarning("[Dash] 속도가 0이라 대시 내내 이동량이 없습니다(PlayerDashData.dashSpeed 확인).", Context.Player);
        }

        // 절벽: Grounded를 잃어도 남은 대시 수평속도는 유지한다. 수직 중력은 Motor가 항상 담당한다.
        PlayerGroundingSensor sensor = Context.GroundingSensor;
        bool grounded = sensor != null && sensor.IsGrounded;

        // 대시 중 접지 상실은 서버 스냅샷에서 NotGrounded 거부로 이어지고(다음 입력), 낙하도 시작된다.
        if (grounded != wasGrounded)
        {
            Edit.Log(
                $"[Dash] tick #{tickCount}: 접지 {wasGrounded}→{grounded} " +
                $"(경과 {(Time.time - startTime):F3}s, 지면법선={(sensor != null ? sensor.GroundNormal.ToString("F2") : "센서없음")})",
                Context.Player);
            wasGrounded = grounded;
        }

        if (!grounded)
            lostGroundingDuringDash = true;

        Context.Motor?.SetDashMotion(direction, speed, endTime - Time.fixedTime);
    }

    /// <summary>
    /// 대시가 끝난 이유를 한 줄로 확정한다 — 요청한 이동량 대비 실제로 얼마나 갔는지까지 포함한다.
    /// 지속시간 만료(정상)와 외부 전이(조기 중단)를 시간으로 구분하고, 중단이면 누가 상태를 바꿨는지
    /// (PlayerStateController.LastTransitionCause) 함께 남긴다.
    /// </summary>
    public override void Exit(PlayerActionState nextState)
    {
        Context.Motor?.ClearDashMotion();
        if (Context.Motor != null)
            Context.Motor.MovementResolved -= OnMotorMovementResolved;
        // 대쉬 간파 인수인계면 0층은 곧 간파 상태가 이어받는다 — Dodge 끝 포즈로 덮지 않는다(할 일 115).
        Context.Dash?.PlayDodgeEnd(nextState, handingOff);

        // 실린 간파를 Skill 로 잇지 못하고 대시가 끊겼다(넉백·그랩·사망·CC·서버 취소 EndDash) — 간파도 강제 종료.
        if (attachedSkill != null && !handingOff)
        {
            PlayerSkillBase skill = attachedSkill;
            attachedSkill = null;
            Context.Skills?.HandleDashInterruptAborted(skill, nextState);
        }

        float elapsed = Time.time - startTime;
        bool expired = Time.time >= endTime;
        Vector3 total = CurrentPosition - startPosition;
        float actualPlanarDistance = new Vector3(total.x, 0f, total.z).magnitude;
        string cause = Context.Controller.LastTransitionCause;

        string verdict = expired
            ? "정상 종료(지속시간 만료)"
            : $"조기 중단 — 남은시간 {Mathf.Max(0f, endTime - Time.time):F3}s / 진행률 {(duration > 0f ? elapsed / duration * 100f : 100f):F0}%";

        string interruptedBy = expired ? string.Empty : $" 중단 원인={ClassifyInterruption(nextState)} 요청지점={cause}";

        string body =
            $"[Dash] 종료: {verdict}{interruptedBy} | 다음상태={nextState} " +
            $"경과={elapsed:F3}s/{duration:F3}s tick={tickCount}회 " +
            $"요청이동={requestedDistance:F2}m 적용이동={appliedDistance:F2}m 실제이동={actualPlanarDistance:F2}m " +
            $"막힌tick={blockedTickCount} 접지상실={lostGroundingDuringDash}";

        if (expired)
            Edit.Log(body, Context.Player);
        else
            Edit.LogWarning(body, Context.Player);

        // 아래 세 경고는 "상태는 정상인데 몸이 안 움직였다"를 각각 다른 원인으로 분리해 준다.
        if (tickCount == 0)
        {
            Edit.LogWarning(
                "[Dash] 이동 없음: Tick이 한 번도 돌지 않았습니다 — 진입한 프레임에 바로 종료됐습니다. " +
                "호스트에서는 ServerRpc→ClientRpc 왕복이 같은 프레임이라 서버 취소가 여기 해당합니다. " +
                "(Player.Update의 ShouldTickForNetwork 게이트로 Tick 자체가 막힌 경우도 확인)", Context.Player);
        }
        else if (requestedDistance > 0.01f && appliedDistance < requestedDistance * 0.5f)
        {
            Edit.LogWarning(
                $"[Dash] 이동 손실(스윕): 요청 {requestedDistance:F2}m 중 {appliedDistance:F2}m만 적용됐습니다 — " +
                $"PlayerMotor의 벽/급경사 판정에 막혔습니다. 막힌tick={blockedTickCount}", Context.Player);
        }
        else if (appliedDistance > 0.01f && actualPlanarDistance < appliedDistance * 0.5f)
        {
            Edit.LogWarning(
                $"[Dash] 이동 손실(적용 후): 스윕은 {appliedDistance:F2}m를 적용했지만 실제 위치는 {actualPlanarDistance:F2}m만 움직였습니다 — " +
                "PhysX 접촉 해석 또는 비권한 피어 위치 복제(NetworkTransform)와 경쟁하는 경우입니다.", Context.Player);
        }
    }

    /// <summary>다음 상태로 대시 중단 원인을 분류한다(전이 요청 지점과 함께 읽는다).</summary>
    private static string ClassifyInterruption(PlayerActionState nextState)
    {
        switch (nextState)
        {
            case PlayerActionState.Knockback:
                return "피격 넉백(Unit.Knockback → BeginKnockback)";
            case PlayerActionState.Restrained:
                return "보스 구속(잡기 또는 돌진 밀기)";
            case PlayerActionState.Cinematic:
                return "연출 잠금(PlayerEncounterLock)";
            case PlayerActionState.Dead:
                return "사망";
            case PlayerActionState.Idle:
                return "Idle 전이(서버 취소 EndDash · 유닛 충돌 · 외부 전이)";
            default:
                return $"{nextState} 전이";
        }
    }

    private void OnMotorMovementResolved(Vector3 requestedDelta, Vector3 appliedDelta, bool wasBlocked)
    {
        float requested = new Vector3(requestedDelta.x, 0f, requestedDelta.z).magnitude;
        float applied = new Vector3(appliedDelta.x, 0f, appliedDelta.z).magnitude;
        requestedDistance += requested;
        appliedDistance += applied;

        // 유닛(몬스터·다른 플레이어)은 대시로 통과하지 못한다 — 막힌 틱에 바로 대시를 끝내 직전에 멈춘다(기획 10-11).
        // 벽·지형은 아래처럼 종전대로 종료시각까지 상태를 유지한다.
        if (Context.Motor != null &&
            PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(
                requested,
                applied,
                applied < requested - PlayerUnitCollisionRules.DashBlockedTolerance &&
                Context.Motor.IsUnitBlockingAhead(direction, requested - applied)))
        {
            Edit.Log(
                $"[Dash] tick #{tickCount}: 유닛에 막혀 대시 종료 — 요청 {requested:F3}m → 적용 {applied:F3}m",
                Context.Player);
            // 실린 간파가 있으면 이 시점이 '대시 끝' — 바로 Skill 로 잇는다(할 일 115 §7).
            if (!TryHandOffDashInterrupt())
                Context.Controller.ChangeState(PlayerActionState.Idle);
            return;
        }

        // 요청량의 10% 미만만 남았으면 사실상 벽에 막힌 tick으로 센다.
        if (wasBlocked && requested > 0.0001f && applied < requested * 0.1f)
        {
            blockedTickCount++;
            Edit.LogWarning(
                $"[Dash] tick #{tickCount}: 스윕이 이동을 차단 — 요청 {requested:F3}m → 적용 {applied:F3}m " +
                "(PlayerMotor 벽/급경사 판정). 대시 상태와 무적은 종료시각까지 유지됩니다.",
                Context.Player);
        }

        if (wasBlocked && requested > 0.0001f && applied <= 0.0001f)
        {
            Edit.LogWarning($"[Dash] tick #{tickCount}: PlayerMotor 적용 이동량이 0입니다.", Context.Player);
        }
    }
}
