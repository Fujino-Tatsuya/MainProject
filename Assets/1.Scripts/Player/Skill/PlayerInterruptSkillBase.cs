using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>간파 스킬 공용 판정 타이밍과 버퍼 크기를 제공하는 데이터 계약.</summary>
public interface IPlayerInterruptSkillData
{
    float HitDelay { get; }
    float SkillDuration { get; }
    int MaxHitResults { get; }
}

/// <summary>
/// 플레이어 간파 공용 베이스. 앵커 오버랩을 한 번만 판정하고, 모든 피해에 간파 공격 표식을 싣는다.
///
/// 판정·종료 시점(PLAN-anim-event-timing P0-2) — 캐릭터마다 둘 중 하나만 쓴다(<see cref="PlayerSkillBase.EndsOnClipEvent"/>):
/// - 이벤트 방식(RequiredAnimationEvents 에 <see cref="HitAndEndEvents"/> 를 선언): 상태 클립의 Hit 이벤트가 판정, End 이벤트가 종료.
///   HitDelay·SkillDuration·MaxActiveDuration 은 쓰지 않는다. 이벤트가 안 오면(애니메이터 꺼짐 등) 컨트롤러 안전망
///   (상태 클립 실제 재생 시간 + padding)만 끝낸다. 시점 튜닝 = 클립에서 이벤트를 옮긴다.
/// - 타이머 방식(선언 없음 — 이벤트를 아직 안 심은 캐릭터): HitDelay·SkillDuration 타이머와 Hit·End 이벤트 중 먼저 온 쪽.
///   안전망 = MaxActiveDuration + padding. 클립에 Hit·End 를 심으면 그 캐릭터에서 선언을 켜고, 셋 다 켜지면 타이머 경로를 지운다.
///
/// 인터럽트 슬로우 모션(PLAN-interrupt-slowmo S3): 승인 시점에 같은 앵커 오버랩으로 유효타를 예측해
/// (<see cref="IInterruptSlowMotionTarget"/> 이고 지금 인터럽트 가능한 대상이 있으면) 전역 슬로우를 시작한다.
/// 판정에서 그 인터럽트가 하나도 성공하지 않거나 판정 전에 스킬이 끝나면 실패 복귀시킨다.
/// </summary>
public abstract class PlayerInterruptSkillBase : PlayerInstantSkill
{
    private Collider[] hitResults;
    private Collider[] queryResults; // 오너 HUD 질의 전용(HasTargetInOmniRange)
    // 상자처럼 Unit이 아닌 IAttackReceiver도 있으므로 Hurtbox까지 중복 방지 키로 쓴다.
    private readonly HashSet<Object> hitTargets = new HashSet<Object>();
    private readonly List<Unit> landedUnits = new List<Unit>();
    private readonly InterruptSlowMotionTracker slowMotion = new InterruptSlowMotionTracker();
    private System.Action<Unit, ulong> counterSucceededHandler;

    private float hitTime;
    private float endTime;
    private bool hasResolvedHit;
    private bool interruptSucceeded; // 이번 판정에서 카운터 창 소비 성공이 한 건이라도 있었는가
    // 이번 판정에서 카운터 창을 소비한 첫 대상의 월드 위치 — 성공 연출 기준점(할 일 87 C5). 기록만 하고 판정엔 안 쓴다.
    private bool hasSuccessTargetPosition;
    private Vector3 successTargetPosition;

    // ── 대쉬 간파(할 일 115, 서버) — 대시에 실린 동안 Hit 은 보류, 대시 끝(Skill 전이)에 판정 ──
    private bool dashAttachArmed;       // 다음 OnServerStart 가 대쉬 간파다(컨트롤러가 승인 직전에 건다)
    private Vector3 armedDashOffset;
    private bool attachedToDash;        // 지금 대시에 실려 있다
    private bool pendingHit;            // 대시 중 Hit 이 왔다 — 대시 끝에 즉시 판정
    private bool pendingEnd;            // 판정 전에 End 가 왔다 — 판정 뒤로 미룬다
    private Vector3 predictedOffset;    // 시도 예측 오버랩을 옮길 양(예상 대시 끝 − 지금 위치, §10 Q2)

    // ── 간파 지연 측정(할 일 74 후속 — 측정만, 판정은 안 바꾼다. Docs/tech/networking.md "Interrupt lag measurement") ──
    [Header("간파 지연 측정")]
    [SerializeField, Min(0f)]
    [Tooltip("'보정했으면 성공했을 것' 로그의 지연 상한(ms). 판정 시각 − min(지연, 상한) 시점의 창 상태로 계산한다. 측정 전용 — 판정에 영향 없음.")]
    private float lagCompensationCapMs = InterruptLagJudge.DefaultCapMs;

    private struct LagPress
    {
        public bool Valid;
        public ulong ClientId;
        public bool IsHost;
        public double PressServerTime;
        public double ArriveServerTime;
        public double RttMs;
    }

    private LagPress pendingLagPress;
    private LagPress activeLagPress;

    /// <summary>
    /// 이벤트 방식 간파의 요구 이벤트. 클립에 Hit·End 를 심은 캐릭터가 RequiredAnimationEvents 로 이걸 돌려준다(가붕이·거너·어쌔신 전부).
    /// 선언하지 않으면 HitDelay·SkillDuration 타이머 방식.
    /// </summary>
    protected static readonly SkillAnimationEventType[] HitAndEndEvents =
        { SkillAnimationEventType.Hit, SkillAnimationEventType.End };

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Interrupt;
    public override bool CanMoveWhileActive => false;

    protected IPlayerInterruptSkillData InterruptData => Data as IPlayerInterruptSkillData;

    /// <summary>
    /// [서버] 다음 <see cref="OnServerStart"/> 를 대쉬 간파로 시작한다(할 일 115) — 컨트롤러가 승인 직전에 부른다.
    /// <paramref name="predictedEndOffset"/> = 예상 대시 끝 − 지금 위치. 시도 사건 예측 오버랩만 이만큼 옮긴다.
    /// </summary>
    internal void ArmDashAttach(Vector3 predictedEndOffset)
    {
        dashAttachArmed = true;
        armedDashOffset = predictedEndOffset;
    }

    /// <summary>[서버] 이 시전이 대시에 실려 있다(대시 끝 전).</summary>
    internal bool IsAttachedToDash => attachedToDash;

    /// <summary>[서버] 이 시전의 Hit 판정이 이미 났다(래치).</summary>
    internal bool HasResolvedHit => hasResolvedHit;

    /// <summary>
    /// [서버] 우클릭 → 대시 전환(할 일 115 §12) — 이미 시작된 이 시전이 대시에 옮겨 실렸다. 이후는 대쉬 간파와 같다
    /// (대시 중 Hit 보류 → 대시 끝 Skill 전이에서 판정). 새 시전이 아니므로 시도 사건·슬로우 예측(TryStartSlowMotion)과
    /// 준비 타이머(hitTime)는 다시 하지 않는다 — 준비 기준점은 처음 승인 시각 그대로.
    /// </summary>
    internal void OnTransferredToDash()
    {
        attachedToDash = true;
        pendingHit = false;
        pendingEnd = false;
        Edit.Log("[DashInterrupt] 우클릭 → 대시 전환 — 진행 중인 간파를 대시에 실음(시도 사건 재발동 없음)", this);
    }

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        attachedToDash = dashAttachArmed;
        predictedOffset = dashAttachArmed ? armedDashOffset : Vector3.zero;
        dashAttachArmed = false;
        armedDashOffset = Vector3.zero;
        pendingHit = false;
        pendingEnd = false;

        base.OnServerStart(direction, target);
        slowMotion.Begin();
        activeLagPress = pendingLagPress;
        pendingLagPress = default;

        IPlayerInterruptSkillData data = InterruptData;
        if (data == null)
        {
            OnMissingInterruptData();
            EndSelf(SkillEndReason.Completed);
            return;
        }

        hasResolvedHit = false;
        hitTime = Time.time + data.HitDelay;
        endTime = Time.time + data.SkillDuration;

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];

        TryStartSlowMotion();
    }

    public override void OnTick()
    {
        // 이벤트 방식은 Hit·End 이벤트만 본다 — 타이머를 겹쳐 두지 않는다(안전망 = 컨트롤러, 상태 클립 길이 + padding).
        if (EndsOnClipEvent)
            return;

        if (!hasResolvedHit && Time.time >= hitTime)
            ResolveHit();

        // 같은 프레임에 판정과 종료가 겹치면 판정을 먼저 처리한다.
        if (Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Hit)
        {
            // 대시 중(상체 레이어 클립)에 온 Hit 은 판정하지 않고 대시 끝까지 보류한다(할 일 115).
            if (DashInterruptRules.EvaluateResolveTiming(!attachedToDash, true) == DashInterruptResolveTiming.HoldHit)
            {
                if (!pendingHit)
                    Edit.Log("[DashInterrupt] 대시 중 Hit — 대시 끝까지 판정 보류", this);
                pendingHit = true;
            }
            else
            {
                ResolveHit();
            }
        }

        if (eventType == SkillAnimationEventType.End && (attachedToDash || pendingEnd || pendingHit))
        {
            // 판정 전 End 는 판정 뒤로 미룬다 — 대시 끝 판정(또는 다음 Hit) 직후 끝낸다.
            pendingEnd = true;
            return;
        }

        base.OnAnimationEvent(eventType);

        if (eventType == SkillAnimationEventType.Hit && pendingEnd && hasResolvedHit)
            EndDeferred();
    }

    /// <summary>
    /// [서버] 대시가 끝나 Skill 상태로 넘어왔다(할 일 115). 보류된 Hit 이 있으면 지금(= 대시 끝 지점) 판정하고,
    /// 아직 Hit 전이면 클립 Hit(남은 준비 시간)을 기다린다. 판정 전에 온 End 는 판정 뒤에 처리한다.
    /// </summary>
    internal void OnDashHandOff()
    {
        if (!attachedToDash)
            return;

        attachedToDash = false;
        bool resolveNow = DashInterruptRules.EvaluateResolveTiming(true, pendingHit) == DashInterruptResolveTiming.ResolveNow;
        pendingHit = false;
        Edit.Log($"[DashInterrupt] 대시 끝 → Skill 전이 — {(resolveNow ? "보류된 Hit 즉시 판정" : "클립 Hit 대기")}", this);
        if (resolveNow)
            ResolveHit();

        if (pendingEnd && hasResolvedHit)
            EndDeferred();
    }

    private void EndDeferred()
    {
        pendingEnd = false;
        EndSelf(SkillEndReason.Completed);
    }

    // 모든 피어에서 불리지만 발동 기록은 서버에만 있다. 판정 전 종료(완료·취소·사망)면 실패 사건 → 디렉터가 실패 복귀.
    public override void OnEnd(SkillEndReason reason)
    {
        attachedToDash = false;
        pendingHit = false;
        pendingEnd = false;
        predictedOffset = Vector3.zero;

        InterruptProductionDirector.ServerFail(InterruptProductionEvent.Fail(owner, this,
            InterruptFailReason.EndedBeforeResolve, slowMotion.Abort()));
        base.OnEnd(reason);
    }

    protected virtual void OnMissingInterruptData()
    {
        Debug.LogError("[Player] 간파 스킬에는 IPlayerInterruptSkillData 데이터가 필요합니다.", this);
    }

    protected virtual void OnMissingHitboxAnchor()
    {
        Debug.LogError("[Player] 간파 스킬에 판정 앵커(hitboxAnchor)가 배정되지 않았습니다.", this);
    }

    // 기존 구현 사이의 세부 계약도 보존한다. 거너는 죽은 Unit을 수신 호출 전에 제외하고,
    // 적중 보너스 공급자를 살아 있는 Unit마다 호출했다(소모형 공급자는 첫 호출 뒤 0을 반환).
    protected virtual bool ShouldSkipTarget(Unit unit) => false;
    protected virtual bool ConsumeOnHitBonusOnce => true;
    protected virtual bool CompleteBeforeAttackLanded => false;

    /// <summary>수신 직전 캐릭터별 AttackInfo 가공 훅(어쌔신 백어택 배율 — PLAN-assassin A11). 기본 = 그대로.</summary>
    protected virtual AttackInfo DecorateAttackInfo(AttackInfo attackInfo) => attackInfo;

    /// <summary>개별 적중이 실제 수신됐을 때의 캐릭터별 연출·로그 훅.</summary>
    protected virtual void OnInterruptTargetResolved(Object target, AttackInfo attackInfo) { }

    /// <summary>한 번의 판정이 끝난 뒤의 캐릭터별 후폭풍·RPC 훅. 빗나가도 호출된다.</summary>
    protected virtual void OnInterruptResolutionCompleted(int resolvedCount) { }

    /// <summary>
    /// [서버] 간파 판정 1회가 끝났다. 인자 = 시전자, 수신이 받아들여진 대상 수(0 = 빗나감, 상자 같은 비 Unit 수신자 포함).
    /// <see cref="OnInterruptResolutionCompleted"/> 바로 뒤에 난다. 판정 앵커 누락으로 판정을 못 한 경우는 나지 않는다. 튜토리얼 판정 훅.
    /// </summary>
    public static event System.Action<Player, int> ServerInterruptResolved;

    /// <summary>
    /// [서버] 간파 시전 하나가 열린 카운터 창을 맞혀 소비했다(23호·중간보스 <see cref="CombatStatsEvents.ServerCounterSucceeded"/>,
    /// 허수아비 인터럽트 성공). 여러 대상이 성공해도 시전당 1회. 인자 = 시전자, 그 간파 스킬 인스턴스(대체 슬롯 포함).
    /// 같은 판정 안에서 <see cref="ServerInterruptResolved"/> 보다 먼저 난다. 시전자 쿨 감소는 없다(할 일 90 에서 제거).
    /// </summary>
    public static event System.Action<Player, PlayerInterruptSkillBase> ServerInterruptSucceeded;

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 구독자가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents()
    {
        ServerInterruptResolved = null;
        ServerInterruptSucceeded = null;
    }

    private void CompleteResolution(int resolvedCount)
    {
        OnInterruptResolutionCompleted(resolvedCount);
        ServerInterruptResolved?.Invoke(owner, resolvedCount);
    }

    // 서버 전용. 애니메이션 이벤트와 타이머가 겹쳐도 래치 때문에 한 번만 실행된다.
    private void ResolveHit()
    {
        if (hasResolvedHit)
            return;

        hasResolvedHit = true;
        interruptSucceeded = false;
        hasSuccessTargetPosition = false;

        if (HitboxAnchor == null || owner == null)
        {
            InterruptProductionDirector.ServerFail(InterruptProductionEvent.Fail(owner, this,
                InterruptFailReason.NoHitboxAnchor, slowMotion.Resolve(false)));
            OnMissingHitboxAnchor();
            return;
        }

        int hitCount = OverlapHitboxAnchor(hitResults);
        hitTargets.Clear();
        landedUnits.Clear();
        bool onHitBonusTaken = false;
        bool interruptLanded = false;
        int resolvedCount = 0;
        LagHit lagHit = BeginLagHit();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || ShouldSkipTarget(unit) || !hitTargets.Add(target))
                continue;

            int resolvedDamage = damageSnapshot;
            // 적중 보너스는 공격 1회에서 살아 있는 첫 Unit 하나만 소비한다.
            // 수신이 거절돼도 이미 선택한 첫 대상 뒤로 보너스를 넘기지 않는 기존 계약을 보존한다.
            if ((!ConsumeOnHitBonusOnce || !onHitBonusTaken) && unit != null && unit.CurrentHealth > 0)
            {
                if (ConsumeOnHitBonusOnce)
                    onHitBonusTaken = true;
                int bonus = owner.ServerTakeOnHitBonus(Data != null && Data.TriggersOnHit, unit);
                resolvedDamage = bonus >= int.MaxValue - resolvedDamage
                    ? int.MaxValue
                    : resolvedDamage + bonus;
            }

            AttackInfo attackInfo = DecorateAttackInfo(new AttackInfo(resolvedDamage, DamageAttackType,
                isInterruptAttack: true, hitPattern: DamageHitPattern));
            AttackHitContext hitContext =
                new AttackHitContext(owner.transform.position, owner.transform, hit, owner);

            // 성공 수를 이 수신 호출 하나의 전후로 비교한다 — 다른 공격의 성공이 섞이지 않는다.
            // 슬로우 모션 판별과 간파 성공 판정(허수아비)이 같은 비교를 쓴다.
            IInterruptSlowMotionTarget slowTarget = FindSlowMotionTarget(unit);
            int successBefore = slowTarget != null ? slowTarget.ServerInterruptSuccessCount : 0;
            bool openAtHit = slowTarget != null && slowTarget.ServerIsInterruptible; // 측정 전용 읽기
            // 보정 시각 창 상태도 수신 전에 읽는다 — 성공하면 수신 안에서 창이 소비돼 같은 서버 시각에 닫힘이 기록된다(지연 0 이면 보정 시각 = 판정 시각).
            InterruptWindowState correctedWindow =
                slowTarget != null ? lagHit.CorrectedWindowOf(unit) : InterruptWindowState.Unknown;

            bool resolved = ReceiveListeningForCounter(hurtbox, unit, attackInfo, hitContext);

            bool counterSucceeded = slowTarget != null && slowTarget.ServerInterruptSuccessCount != successBefore;
            if (counterSucceeded)
            {
                interruptSucceeded = true;
                RecordSuccessTarget(unit);
                if (slowMotion.IsPending)
                    interruptLanded = true;
            }

            if (slowTarget != null)
                lagHit.Observe(unit, openAtHit, correctedWindow, counterSucceeded);

            if (!resolved)
                continue;

            resolvedCount++;
            if (unit != null)
                landedUnits.Add(unit);

            OnInterruptTargetResolved(target, attackInfo);
        }

        // 간파 연출 사건(할 일 118) — 판정 → 성공 순서로 디렉터에 알린다(빗나감 실패 복귀 → 성공 슬로우 재시작).
        InterruptProductionDirector.ServerResolve(InterruptProductionEvent.Resolve(owner, this,
            slowMotion.Resolve(interruptLanded), interruptLanded, interruptSucceeded, resolvedCount));
        if (interruptSucceeded)
            InterruptProductionDirector.ServerSuccess(InterruptProductionEvent.Success(owner, this,
                hasSuccessTargetPosition, successTargetPosition));
        ReportLag(lagHit);

        // 판정 1회 = 시전 1회라 여러 대상이 성공해도 한 번만 알린다.
        if (interruptSucceeded)
            ServerInterruptSucceeded?.Invoke(owner, this);

        if (CompleteBeforeAttackLanded)
            CompleteResolution(resolvedCount);

        owner.RaiseServerAttackLanded(
            DamageAttackType, Data != null && Data.TriggersOnHit, landedUnits, this);

        if (!CompleteBeforeAttackLanded)
            CompleteResolution(resolvedCount);
    }

    // ── 간파 지연 측정 (서버, 측정만) ──

    /// <summary>
    /// [서버] 요청 RPC 가 들고 온 누른 시각을 다음 <see cref="OnServerStart"/> 에 넘긴다. 호스트 자신은 지연 0(누른 시각 = 도착 시각).
    /// </summary>
    internal void ServerSetLagPress(ulong senderClientId, double pressedServerTime)
    {
        if (!InterruptLagMonitor.TryServerNow(out double now))
        {
            pendingLagPress = default;
            return;
        }

        bool isHost = senderClientId == Unity.Netcode.NetworkManager.ServerClientId;
        pendingLagPress = new LagPress
        {
            Valid = true,
            ClientId = senderClientId,
            IsHost = isHost,
            PressServerTime = isHost ? now : pressedServerTime,
            ArriveServerTime = now,
            RttMs = InterruptLagMonitor.RttMs(senderClientId),
        };
    }

    /// <summary>[서버] 승인 여부와 관계없이 요청 처리 뒤 남은 누른 시각을 버린다.</summary>
    internal void ServerClearLagPress() => pendingLagPress = default;

    // 판정 1회에서 로그에 쓸 카운터 창 소유자 하나 — 성공한 첫 대상, 없으면 처음 만난 대상.
    private struct LagHit
    {
        public bool Measuring;               // 누른 시각이 있고 서버라 기록할 판정인가
        public double HitServerTime;
        public double CorrectedServerTime;   // 판정 시각 − 상한 적용 지연
        public Unit Target;
        public bool OpenAtHit;
        public InterruptWindowState CorrectedWindow;
        public bool Success;

        public InterruptWindowState CorrectedWindowOf(Unit unit) =>
            Measuring ? InterruptLagMonitor.WindowStateAt(unit.gameObject, CorrectedServerTime) : InterruptWindowState.Unknown;

        public void Observe(Unit unit, bool openAtHit, InterruptWindowState correctedWindow, bool success)
        {
            if (Target != null && (Success || !success))
                return;

            Target = unit;
            OpenAtHit = openAtHit;
            CorrectedWindow = correctedWindow;
            Success = success;
        }
    }

    private LagHit BeginLagHit()
    {
        LagPress press = activeLagPress;
        if (!press.Valid || !InterruptLagMonitor.TryServerNow(out double hitServerTime))
            return default;

        return new LagHit
        {
            Measuring = true,
            HitServerTime = hitServerTime,
            CorrectedServerTime = InterruptLagJudge.CorrectedJudgeTime(
                press.PressServerTime, press.ArriveServerTime, hitServerTime, lagCompensationCapMs / 1000.0),
        };
    }

    private void ReportLag(in LagHit hit)
    {
        LagPress press = activeLagPress;
        activeLagPress = default;
        if (!press.Valid || !hit.Measuring)
            return;

        double hitServerTime = hit.HitServerTime;
        InterruptWindowState pressWindow = hit.Target != null
            ? InterruptLagMonitor.WindowStateAt(hit.Target.gameObject, press.PressServerTime)
            : InterruptWindowState.Unknown;
        InterruptWindowState correctedWindow = hit.Target != null ? hit.CorrectedWindow : InterruptWindowState.Unknown;
        bool wouldSucceed = hit.Target != null && InterruptLagJudge.WouldSucceed(correctedWindow);

        InterruptLagMonitor.ReportJudgement(new InterruptLagSample
        {
            ClientId = press.ClientId,
            IsHost = press.IsHost,
            Skill = GetType().Name,
            Target = hit.Target != null ? hit.Target.name : null,
            PressServerTime = press.PressServerTime,
            ArriveServerTime = press.ArriveServerTime,
            HitServerTime = hitServerTime,
            RttMs = press.RttMs,
            PressWindow = pressWindow,
            HitWindow = hit.Target == null
                ? InterruptWindowState.Unknown
                : hit.OpenAtHit ? InterruptWindowState.Open : InterruptWindowState.Closed,
            CorrectedWindow = correctedWindow,
            Success = hit.Success,
            WouldSucceed = wouldSucceed,
            CapMs = lagCompensationCapMs,
        });
    }

    // ── 간파 성공 판정 (서버) ──

    // 23호·중간보스는 카운터 창 소비 성공을 피격 처리(ReceiveAttack) 안에서 동기로 발행한다.
    // 이 수신 호출 동안만 들으면 들어온 성공은 전부 이 타격의 것이라 공격자 clientId → 시전자 매핑이 필요 없다(호스트·오프라인 동일).
    private bool ReceiveListeningForCounter(Hurtbox hurtbox, Unit unit, AttackInfo attackInfo, AttackHitContext hitContext)
    {
        counterSucceededHandler ??= HandleCounterSucceeded;
        CombatStatsEvents.ServerCounterSucceeded += counterSucceededHandler;
        try
        {
            return hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);
        }
        finally
        {
            CombatStatsEvents.ServerCounterSucceeded -= counterSucceededHandler;
        }
    }

    private void HandleCounterSucceeded(Unit target, ulong attackerClientId)
    {
        interruptSucceeded = true;
        RecordSuccessTarget(target);
    }

    // 성공 연출 기준점 — 이번 판정에서 처음 성공한 대상만 남긴다(기록만, 판정 불변).
    private void RecordSuccessTarget(Unit target)
    {
        if (hasSuccessTargetPosition || target == null)
            return;

        hasSuccessTargetPosition = true;
        successTargetPosition = target.transform.position;
    }

    // ── 인터럽트 슬로우 모션 (서버) ──

    // 승인 시점 예측(D1·D2): 판정과 같은 앵커 오버랩을 한 번 돌려, 지금 인터럽트 가능한 예측 대상이 있으면 발동한다.
    // 각도 검사는 따로 하지 않는다 — 앵커 형태가 곧 판정 범위다. 시간 레이어가 없는 씬(오프라인 테스트)은 건너뛴다.
    // 예측(게임 질의)만 여기서 하고, 슬로우를 걸지는 디렉터 시도 단계가 정한다(할 일 118).
    private void TryStartSlowMotion()
    {
        if (GlobalTimeScale.Instance == null || InterruptProductionDirector.Instance == null || HitboxAnchor == null || owner == null)
            return;

        slowMotion.Started(InterruptProductionDirector.ServerAttempt(
            InterruptProductionEvent.Attempt(owner, this, PredictsInterruptHit())));
    }

    // 대쉬 간파면 예상 대시 끝으로 옮겨 예측한다(할 일 115 §10 Q2) — 판정은 대시 끝 위치에서 나기 때문이다.
    private bool PredictsInterruptHit()
    {
        int hitCount = OverlapHitboxAnchor(hitResults, predictedOffset);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out _);
            if (unit == null || unit == owner || ShouldSkipTarget(unit))
                continue;

            IInterruptSlowMotionTarget slowTarget = FindSlowMotionTarget(unit);
            if (slowTarget != null && slowTarget.ServerIsInterruptible)
                return true;
        }

        return false;
    }

    // ── 인터럽트 강조 사거리 (오너 HUD, 할 일 5) ──

    /// <summary>
    /// [오너 HUD] 바라보는 방향과 상관없이(전방위) 인터럽트 사거리 안에 <paramref name="match"/> 를 만족하는 대상 Unit 이 있는가.
    /// 사거리 = 판정 앵커 형상을 플레이어 중심 수직축으로 어느 방향으로 돌려도 닿는 최대 수평 거리,
    /// 대상까지 거리 = 대상 콜라이더 표면까지 수평 거리(<see cref="InterruptOmniRangeModel"/>).
    /// 후보는 판정 형상의 높이 띠 × 사거리 반경 캡슐로 모은다. 서버 판정·슬로우 예측과는 버퍼도 경로도 따로다.
    /// </summary>
    public bool HasTargetInOmniRange(System.Predicate<Unit> match)
    {
        IPlayerInterruptSkillData data = InterruptData;
        if (data == null || match == null || HitboxAnchor == null || owner == null)
            return false;

        Vector3 pivot = owner.transform.position;
        if (!TryGetOmniReach(pivot, out float reach, out float minY, out float maxY))
            return false;

        if (queryResults == null || queryResults.Length != data.MaxHitResults)
            queryResults = new Collider[data.MaxHitResults];

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            new Vector3(pivot.x, minY, pivot.z), new Vector3(pivot.x, maxY, pivot.z), reach, queryResults,
            Data.HittableLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = queryResults[i];
            if (hit == null || !InterruptOmniRangeModel.IsInReach(pivot, hit.bounds, reach))
                continue;

            Unit unit = ResolveHitUnit(hit, out _);
            if (unit == null || unit == owner || ShouldSkipTarget(unit))
                continue;

            if (match(unit))
                return true;
        }

        return false;
    }

    // 판정 앵커의 지금 월드 형상에서 전방위 사거리와 높이 띠를 구한다.
    private bool TryGetOmniReach(Vector3 pivot, out float reach, out float minY, out float maxY)
    {
        switch (HitboxAnchor.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo box = default;
                HitboxAnchor.GetBoxColliderInfo(ref box);
                reach = InterruptOmniRangeModel.BoxReach(pivot, box.center, box.halfExtents, box.orientation);
                Vector3 h = box.halfExtents;
                float halfHeight = Mathf.Abs((box.orientation * Vector3.right).y) * h.x +
                                   Mathf.Abs((box.orientation * Vector3.up).y) * h.y +
                                   Mathf.Abs((box.orientation * Vector3.forward).y) * h.z;
                minY = box.center.y - halfHeight;
                maxY = box.center.y + halfHeight;
                return reach > 0f;

            case OverlapCollider.Sphere:
                SphereColliderInfo sphere = default;
                HitboxAnchor.GetSphereColliderInfo(ref sphere);
                reach = InterruptOmniRangeModel.SphereReach(pivot, sphere.center, sphere.radius);
                minY = sphere.center.y - sphere.radius;
                maxY = sphere.center.y + sphere.radius;
                return reach > 0f;

            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsule = default;
                HitboxAnchor.GetCapsuleColliderInfo(ref capsule);
                reach = InterruptOmniRangeModel.CapsuleReach(pivot, capsule.point0, capsule.point1, capsule.radius);
                minY = Mathf.Min(capsule.point0.y, capsule.point1.y) - capsule.radius;
                maxY = Mathf.Max(capsule.point0.y, capsule.point1.y) + capsule.radius;
                return reach > 0f;

            default:
                reach = minY = maxY = 0f;
                return false;
        }
    }

    private static IInterruptSlowMotionTarget FindSlowMotionTarget(Unit unit) =>
        unit != null && unit.TryGetComponent(out IInterruptSlowMotionTarget slowTarget) ? slowTarget : null;
}
