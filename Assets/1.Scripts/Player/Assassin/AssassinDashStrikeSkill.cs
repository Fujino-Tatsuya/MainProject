using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 어쌔신 Q 관통 돌진 — 일반(Main 슬롯)·변신(alternateSkills[Main]) 공용(character_assassin.md §7, PLAN-assassin A8).
/// 둘의 차이는 데이터뿐이다: 쿨타임·애니 상태 + 변신 데이터(<see cref="AssassinTransformedDashStrikeSkillData"/>)의 적중 쿨 차감.
///
/// 이동 = 오너 권위. 서버 승인 시점의 조준 방향으로 고정하고, 시뮬레이션 피어가 <see cref="PlayerMotor.AddGroundedDisplacement"/> 로
/// 20m/s × 4m 를 낸다(루트모션 끔 — Armature Animator applyRootMotion=false). 적·아군은 통과(<see cref="PlayerMotor.PassThroughUnitsOverride"/>),
/// 벽·지형에 막히면(<c>MovementResolved</c> wasBlocked) 그 자리에서 돌진을 끝내고 실제 이동 거리를 서버에 보고한다.
///
/// 판정 = 서버. 시작 위치·방향·경과 시간으로 경로를 재구성해 매 틱 새로 나아간 구간을 폭 1.2m 상자로 훑는다
/// (가붕이 Q 가 오너 조향을 서버에서 재현하는 것과 같은 정책 — 위치 오차는 판정 허용 범위). 벽 조기 종료는
/// 오너가 보고한 거리(0~최대 거리로 검증)로 경로 상한을 줄인다. 같은 대상은 사용당 1회, 상자 포함 일반 피해 경로.
/// 🔸 원격 오너의 보고가 도착하기 전(≈왕복 지연 × 20m/s)만큼은 벽 너머 경로를 이미 훑었을 수 있다 — 호스트 본인은 0.
///
/// 돌진 중 슈퍼아머(A1 — 넉백·밀기·스턴·잡기 거부), 무적 없음. 쿨타임은 승인 = 돌진 시작에 시작한다.
/// E 강화를 소모하지 않는다. 일반 Q 는 유효 대상 적중 시 분노 게이지를 1회 충전한다(§4.2). 평타 순서 초기화는 스킬 상태 진입으로 <see cref="AssassinBasicAttack"/> 가 한다.
///
/// 단계 시점 = 클립 이벤트(PLAN-anim-event-timing P2-3): 준비 → Custom0 = 돌진 시작(시뮬레이션 피어는 이동, 서버는 경로 재구성 시작 —
/// 각자 자기 애니메이터 기준) → 경로 끝 → End = 종료. 클립 Hit 은 쓰지 않는다(판정은 경로 스윕). 안전망 = 컨트롤러(상태 클립 실제 길이 + padding).
///
/// 그림자 회귀(ASN_2_RETURN, 할 일 128 5b): 특성 보유 시 이 Q 사용이 후속 칸(<see cref="PlayerSkillBase.FollowUpSkill"/> = <see cref="AssassinShadowReturnSkill"/>)을 연다
/// — 쿨은 복귀 사용·창 만료·강제 복귀 때 장치가 시작한다. 이 Q 는 돌진 시작 위치를 출발점으로 저장하고, 복귀 스킬이 같은 돌진 '단계' 코드를 방향·거리·피해만 바꿔 쓴다.
/// </summary>
public class AssassinDashStrikeSkill : PlayerSkillBase
{
    private static readonly SkillAnimationEventType[] RequiredEvents =
        { SkillAnimationEventType.Custom0, SkillAnimationEventType.End };

    private PlayerMotor motor;
    private AssassinState assassinState;
    private AssassinSkillView view;
    private Collider[] hitResults;
    private readonly AssassinDashHitLedger ledger = new AssassinDashHitLedger();
    private readonly List<Unit> tickLandedUnits = new List<Unit>();

    private Vector3 dashDirection = Vector3.forward;

    // 서버
    private Vector3 serverOrigin;
    private float serverStartTime;   // 실제 돌진 시작 = 서버 애니메이터의 Custom0
    private float serverTraveled;
    private float serverCap;
    private bool isServerDashPending; // 승인 ~ Custom0(준비)
    private bool isServerSweeping;    // Custom0 ~ 경로 끝
    private bool hasSuperArmor;
    // 이 돌진 단계의 기준 피해 — 출발 = 시전 Dref, 복귀 = 복귀 실행 때 Dref × 비율. 경로 판정과 그 단계 궤적이 같이 쓴다.
    private int serverStageDref;
    // 이 단계가 쓰는 적중 장부 — 출발 = 자기 장부(새 사용), 복귀 = 원래 Q 장부(보상 이어 감).
    private AssassinDashHitLedger activeLedger;

    // 그림자 회귀 출발점 — 서버 = 판정, 각 피어 = 오너 이동·연출(복귀 스킬이 가져간다).
    private readonly AssassinShadowReturnOrigin serverReturnOrigin = new AssassinShadowReturnOrigin();
    private readonly AssassinShadowReturnOrigin localReturnOrigin = new AssassinShadowReturnOrigin();
    private PlayerTraitModifiers traitModifiers;

    // 서버 — 추격의 궤적 예약(할 일 128 3/5). 사망·비활성(씬 종료)에서 비운다. 대상별 1회는 궤적 하나 안에서만.
    private readonly AssassinDelayedStrikeQueue<TrailStrike> pendingTrails = new AssassinDelayedStrikeQueue<TrailStrike>();
    private readonly HashSet<Object> trailTargets = new HashSet<Object>();
    private readonly List<TraitDerivedAttack> derivedAttacks = new List<TraitDerivedAttack>();
    private PlayerTraitEffects traitEffects;

    // 시뮬레이션 피어(오너, 서버 권위 이동이면 서버도)
    private bool isLocallyDashing;
    private bool isLocalDashReleased; // 이 피어 애니메이터의 Custom0 를 받았다 — 그 전은 준비(제자리)
    private float localRemaining;
    private float localTraveled;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Main;
    public override bool CanMoveWhileActive => false;
    public override IReadOnlyList<SkillAnimationEventType> RequiredAnimationEvents => RequiredEvents;
    // 돌진 거리·시간은 고정(할 일 91 D7) — 클립만 빨라지면 End 가 이동보다 먼저 와 돌진이 잘린다.
    public override bool ScalesWithAttackSpeed => false;

    // 돌진 설계값 — 복귀 스킬은 자기를 연 원래 Q 의 값(현재 Q 최대 거리·속도·폭)을 쓴다.
    internal virtual AssassinDashStrikeSkillData DashData => Data as AssassinDashStrikeSkillData;
    internal AssassinDashHitLedger Ledger => ledger;
    internal AssassinShadowReturnOrigin ServerReturnOrigin => serverReturnOrigin;
    internal AssassinShadowReturnOrigin LocalReturnOrigin => localReturnOrigin;
    protected PlayerTraitModifiers TraitModifiers => traitModifiers;
    private ulong SourceId => owner != null && owner.NetworkObject != null ? owner.NetworkObjectId : 0UL;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        motor = owner != null ? owner.GetComponent<PlayerMotor>() : null;
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
        view = owner != null ? owner.GetComponent<AssassinSkillView>() : null;
        traitModifiers = owner != null ? PlayerTraitModifiers.For(owner) : null;
    }

    // 그림자 회귀 — 보유 시에만 복귀 칸을 연다(서버 승인·원격 오너 예측 같은 값). 미보유 = 기존 Q(사용 시 쿨 시작).
    public override bool OpensFollowUp(Vector3 direction, Unit target) =>
        AssassinShadowReturnRules.OpensReturn(
            base.OpensFollowUp(direction, target),
            traitModifiers != null && traitModifiers.HasTrait(TraitModeIds.AssassinShadowReturn));

    // 복귀 창 = 정의 에셋 스킬 수치(없으면 인스펙터 값). 실제 시간 — 공격 가속을 타지 않는다.
    public override float FollowUpWindowSeconds =>
        traitModifiers != null
            ? Mathf.Max(0f, traitModifiers.ResolveSkillValue(AssassinShadowReturnRules.WindowKey, base.FollowUpWindowSeconds))
            : base.FollowUpWindowSeconds;

    // 변신 종료 대기 중에는 새 Q 를 시작하지 않는다(§10.3 — 종료가 "현재 공격 완료"를 기다리는 중).
    public override bool CanUse(Vector3 direction, Unit target) =>
        DashData != null && (assassinState == null || !assassinState.IsTransformEndPending);

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Active;

        AssassinDashStrikeSkillData data = DashData;
        if (data == null)
        {
            Debug.LogError("[Assassin] Q 관통 돌진에는 AssassinDashStrikeSkillData 가 필요합니다.", this);
            EndSelf(SkillEndReason.Completed);
            return;
        }

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];

        serverOrigin = owner.transform.position;
        BeginServerStage(direction, data, out dashDirection, out serverCap, out serverStageDref, out activeLedger);
        serverTraveled = 0f;
        isServerDashPending = true;
        isServerSweeping = false;

        // 서버 전용 쓰기. 클립 End 로 스킬이 끝나거나 강제 종료면 OnEnd 가 해제한다. 만료 안전망은 지속시간.
        if (owner.StatusEffects != null)
        {
            owner.StatusEffects.Apply(StatusEffectType.SuperArmor, data.MaxActiveDuration, SourceId);
            hasSuperArmor = true;
        }
    }

    /// <summary>
    /// [서버] 이번 돌진 단계를 정한다 — 출발 = 조준 방향 · Q 최대 거리 · 시전 Dref(단계 실행 때 고정해 그 단계 궤적이 쓴다, 명세 §5) · 새 사용 장부.
    /// 시작 위치는 그림자 회귀 출발점으로도 저장한다(복귀 칸이 안 열리면 다음 틱에 버린다). 복귀 스킬이 덮는다.
    /// </summary>
    protected virtual void BeginServerStage(Vector3 requestedDirection, AssassinDashStrikeSkillData data,
        out Vector3 direction, out float distance, out int stageDref, out AssassinDashHitLedger stageLedger)
    {
        direction = Flatten(requestedDirection);
        distance = data.DashDistance;
        stageDref = damageSnapshot;
        ledger.Begin();
        stageLedger = ledger;
        serverReturnOrigin.Save(owner.transform.position);
    }

    /// <summary>[전 피어] 이 피어의 돌진 방향·거리 — 출발 = 승인 조준 방향 · Q 최대 거리. 시작 위치를 복귀용 출발점으로 저장한다. 복귀 스킬이 덮는다.</summary>
    protected virtual void ResolveLocalStage(Vector3 requestedDirection, AssassinDashStrikeSkillData data,
        out Vector3 direction, out float distance)
    {
        direction = Flatten(requestedDirection);
        distance = data != null ? data.DashDistance : 0f;
        if (owner != null)
            localReturnOrigin.Save(owner.transform.position);
    }

    // 변신 Q 적중 쿨 차감을 받을 스킬 — 복귀 단계는 원래 Q.
    protected virtual PlayerSkillBase CooldownRewardSkill => this;

    // 전 피어. 방향 = 서버 승인 시점의 조준(입력이 받아들여진 순간) — 도중에 바꾸지 않는다.
    public override void OnClientPlay(Vector3 direction)
    {
        AssassinDashStrikeSkillData data = DashData;
        ResolveLocalStage(direction, data, out dashDirection, out float stageDistance);

        // 일반 Q = 관통 일섬(시작점 기억 → OnEnd 에서 경로 위로 긋기), 변신 Q = Storm Slash 루프. 데이터 타입으로 가른다
        // (변신 상태 복제보다 확실하다. 이 인스턴스가 곧 alternateSkills[Main]).
        if (data is AssassinTransformedDashStrikeSkillData)
            view?.BeginStormSlash(data.MaxActiveDuration);
        else if (owner != null)
            view?.BeginPierce(owner.transform.position, dashDirection);
        if (data == null || motor == null || owner == null || !owner.IsSimulating)
            return;

        isLocalDashReleased = false;
        localRemaining = stageDistance;
        localTraveled = 0f;
        isLocallyDashing = true;
        motor.PassThroughUnitsOverride = true;
        motor.MovementResolved -= HandleMovementResolved;
        motor.MovementResolved += HandleMovementResolved;
    }

    public override void OnFixedTick()
    {
        AssassinDashStrikeSkillData data = DashData;
        if (!isLocallyDashing || localRemaining <= 0f || data == null || motor == null)
            return;

        // 1단계 준비 — 제자리(웅크리기 애니 자리). 이 피어 클립의 Custom0 가 오면 출발한다.
        if (!isLocalDashReleased)
            return;

        float step = Mathf.Min(data.DashSpeed * Time.fixedDeltaTime, localRemaining);
        localRemaining -= step;
        motor.AddGroundedDisplacement(dashDirection * step);
    }

    public override void OnTick()
    {
        if (!isServerSweeping)
            return;

        AssassinDashStrikeSkillData data = DashData;
        float elapsed = Time.time - serverStartTime;
        SweepServerTo(AssassinDashStrikeRules.TraveledAt(elapsed, data.DashSpeed, serverCap));

        // 경로 끝 — 이후 종료 단계는 제자리(궤적 이펙트가 늦게 따라와 속도감을 준다). 종료는 클립 End 가 정한다.
        if (serverTraveled >= serverCap)
            isServerSweeping = false;
    }

    // 전 피어 — 이 피어 애니메이터의 Custom0 = 준비 끝, 시뮬레이션 피어는 여기서 출발한다(판정 없음).
    public override void OnLocalAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Custom0 && isLocallyDashing)
            isLocalDashReleased = true;
    }

    // 서버 전용 — 오너가 벽에 막혀 멈춘 실제 이동 거리. 0~최대 거리로 검증해 경로 상한만 줄인다(늘리지 않음).
    public override void OnOwnerResultReported(float value)
    {
        AssassinDashStrikeSkillData data = DashData;
        if ((!isServerSweeping && !isServerDashPending) || data == null ||
            !AssassinDashStrikeRules.TryValidateReportedDistance(value, data.DashDistance, out float reported))
        {
            return;
        }

        serverCap = Mathf.Min(serverCap, Mathf.Max(reported, serverTraveled));
        Edit.Log($"[Assassin/Q] 벽 조기 종료 보고 — {value:F2}m → 경로 상한 {serverCap:F2}m", this);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        switch (eventType)
        {
            // 서버 — 준비 끝, 경로 재구성 시작(서버 자기 애니메이터 기준. 오너 이동과의 차이는 판정 허용 범위).
            case SkillAnimationEventType.Custom0:
                if (!isServerDashPending)
                    return;
                isServerDashPending = false;
                isServerSweeping = true;
                serverStartTime = Time.time;
                break;

            // 종료. 클립 End 가 경로 끝보다 이르게 튜닝돼도 출발한 경로의 판정은 끝까지 마친다(출발 전이면 훑지 않는다).
            case SkillAnimationEventType.End:
                if (isServerSweeping)
                    SweepServerTo(serverCap);
                // 출발 돌진 단계 정상 종료(벽 앞 정지 포함) — 추격의 궤적. 출발 전이면 이동 0 이라 없다.
                ServerEndDashStage(SkillEndReason.Completed, serverOrigin, dashDirection, serverTraveled, serverStageDref);
                EndSelf(SkillEndReason.Completed);
                break;
        }
    }

    public override void OnEnd(SkillEndReason reason)
    {
        // 사망·강제 종료면 아직 훑지 않은 경로의 타격은 버린다(§12.2-3).
        isServerDashPending = false;
        isServerSweeping = false;
        StopLocalDash();
        // 전 피어 — 완료·벽 조기 종료·강제 중단 공통
        if (DashData is AssassinTransformedDashStrikeSkillData)
            view?.EndStormSlash();
        else
            view?.EndPierce();
        if (motor != null)
            motor.PassThroughUnitsOverride = false;

        RemoveSuperArmor();
        base.OnEnd(reason);
    }

    private void OnEnable()
    {
        PlayerLifeCycleController.ServerLifeStateChanged += HandleLifeStateChanged;
    }

    private void OnDisable()
    {
        StopLocalDash();
        PlayerLifeCycleController.ServerLifeStateChanged -= HandleLifeStateChanged;
        pendingTrails.Clear();   // 씬 종료·비활성 — 예약이 남지 않게
    }

    // 서버 — 예약된 궤적은 스킬이 끝난 뒤에 나가므로 OnTick(실행 중만) 이 아니라 여기서 돈다.
    private void Update()
    {
        if (pendingTrails.Count > 0)
            FireDueTrails();

        // 서버 — 복귀 칸이 닫혔으면(안 열림·만료·사망·R 전환·씬 이동·복귀 사용) 출발점을 버린다. 다른 상태 Q 가 과거 저장점을 쓰지 않는다.
        if (serverReturnOrigin.HasValue && (controller == null || FollowUpSkill == null || controller.GetSkill(Slot) != FollowUpSkill))
            serverReturnOrigin.Discard();
    }

    // 사망(살아 있음 → 그 밖)이면 예약을 버린다(명세 §9). 부활 뒤 재개 없음.
    private void HandleLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (owner != null && clientId == owner.OwnerClientId && previous == PlayerLifeState.Alive && current != PlayerLifeState.Alive)
            pendingTrails.Clear();
    }

    // ── 추격의 궤적(ASN_4_Q_TRAIL) ──

    /// <summary>
    /// [서버] 돌진 '단계' 하나가 끝났다 — 정상 종료이고 실제로 움직였으면 지나온 선분(<paramref name="start"/> → 방향 × <paramref name="traveled"/>)에
    /// 원래 Q 경로 폭으로 추격의 궤적을 예약한다. 피해 = <paramref name="stageDref"/>(그 단계 원래 기준 피해) × 비율.
    /// 출발 돌진 = 시전 Dref, 그림자 회귀 복귀 돌진은 복귀 배율을 바른 Dref 를 넘긴다(할 일 128 H5).
    /// </summary>
    private void ServerEndDashStage(SkillEndReason reason, Vector3 start, Vector3 direction, float traveled, int stageDref)
    {
        AssassinDashStrikeSkillData data = DashData;
        if (data == null || owner == null || !AssassinTrailRules.Schedules(reason, traveled))
            return;

        traitEffects ??= PlayerTraitEffects.For(owner);
        if (traitEffects == null)
            return;

        bool alternate = controller != null && controller.IsAlternateSkill(this);
        traitEffects.GetDerivedAttacks(TraitAttackKey.ForSkill(Slot, alternate), derivedAttacks);
        if (!AssassinDerivedAttackKeys.TryFind(derivedAttacks, AssassinDerivedAttackKeys.QTrail, out TraitDerivedAttack trail))
            return;

        Vector3 end = AssassinTrailRules.SegmentEnd(start, direction, traveled);
        var strike = new TrailStrike(start, end, data.PathWidth, data.PathHeight, trail.DamageFrom(stageDref));
        float delay = AssassinTrailRules.Delay(trail.Delay, owner.AttackSpeedRate);
        pendingTrails.Schedule(Time.timeAsDouble, delay, strike);
        Edit.Log($"[Assassin/Q] 추격의 궤적 예약 — {traveled:F2}m, 기준 {stageDref} → {strike.Damage}, {delay:F3}초 뒤", this);
    }

    // [서버] 시각이 된 궤적을 낸다 — 파생 판정(피해만: 특성 사건·분노·쿨감·CC·간파·다음 공격 강화·백어택 연출 없음).
    private void FireDueTrails()
    {
        while (pendingTrails.TryPopDue(Time.timeAsDouble, out TrailStrike strike))
            FireTrail(strike);
    }

    private void FireTrail(TrailStrike strike)
    {
        AssassinDashStrikeSkillData data = DashData;
        if (data == null || owner == null)
            return;

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[Mathf.Max(1, data.MaxHitResults)];

        AssassinTrailRules.Box(strike.Start, strike.End, strike.Width, strike.Height,
            out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, hitResults, rotation, data.HittableLayers, QueryTriggerInteraction.Collide);

        trailTargets.Clear();
        int landed = 0;
        for (int i = 0; i < count; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || !trailTargets.Add(target))
                continue;

            AttackInfo attackInfo = new AttackInfo(strike.Damage, DamageAttackType, hitPattern: DamageHitPattern);
            if (assassinState != null)
                attackInfo = assassinState.WithBackAttack(attackInfo);
            var hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);
            bool resolved = hurtbox != null ? hurtbox.ReceiveAttack(attackInfo, hitContext) : unit.ReceiveAttack(attackInfo, hitContext);
            if (resolved)
                landed++;
        }

        trailTargets.Clear();
        Edit.Log($"[Assassin/Q] 추격의 궤적 판정 — 피해 {strike.Damage}, 적중 {landed}", this);
    }

    private readonly struct TrailStrike
    {
        public readonly Vector3 Start;
        public readonly Vector3 End;
        public readonly float Width;
        public readonly float Height;
        public readonly int Damage;

        public TrailStrike(Vector3 start, Vector3 end, float width, float height, int damage)
        {
            Start = start;
            End = end;
            Width = width;
            Height = height;
            Damage = damage;
        }
    }

    // ── 오너 이동 ──

    private void HandleMovementResolved(Vector3 requested, Vector3 resolved, bool wasBlocked)
    {
        if (!isLocallyDashing)
            return;

        localTraveled += Mathf.Max(0f, Vector3.Dot(new Vector3(resolved.x, 0f, resolved.z), dashDirection));

        if (wasBlocked)
        {
            // 벽·통과 불가 지형 직전 정지 = 조기 종료. 쿨·계수는 그대로(§7.1).
            StopLocalDash();
            controller?.ReportOwnerSkillResult(this, localTraveled);
            return;
        }

        if (localRemaining <= 0f)
            StopLocalDash();
    }

    private void StopLocalDash()
    {
        isLocallyDashing = false;
        isLocalDashReleased = false;
        localRemaining = 0f;
        if (motor != null)
            motor.MovementResolved -= HandleMovementResolved;
    }

    // ── 서버 경로 판정 ──

    private void SweepServerTo(float target)
    {
        if (target <= serverTraveled)
            return;

        float from = serverTraveled;
        serverTraveled = target;
        SweepSegment(from, target);
    }

    /// <summary>
    /// 돌진 경로 구간 [<paramref name="from"/>, <paramref name="to"/>](출발점 기준 거리 m) 판정 상자 — 폭 PathWidth·높이 PathHeight, 회전 = 돌진 방향.
    /// 서버 경로 판정(<see cref="SweepSegment"/>)과 특성 랩 공격 범위 기즈모(할 일 152, 0 ~ DashDistance)가 같이 쓴다.
    /// </summary>
    public static void PathBox(Vector3 origin, Vector3 direction, float from, float to, AssassinDashStrikeSkillData data,
        out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        float length = to - from;
        center = origin + direction * (from + length * 0.5f) + Vector3.up * (data.PathHeight * 0.5f);
        halfExtents = new Vector3(data.PathWidth * 0.5f, data.PathHeight * 0.5f, length * 0.5f);
        rotation = Quaternion.LookRotation(direction);
    }

    // 이번 틱에 새로 나아간 구간 [from, to] 를 폭 PathWidth 상자로 훑는다(끝점만 보지 않는다).
    private void SweepSegment(float from, float to)
    {
        AssassinDashStrikeSkillData data = DashData;
        if (data == null || owner == null || hitResults == null)
            return;

        PathBox(serverOrigin, dashDirection, from, to, data, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
        int count = Physics.OverlapBoxNonAlloc(
            center, halfExtents, hitResults, rotation,
            data.HittableLayers, QueryTriggerInteraction.Collide);

        tickLandedUnits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);

            // 상자처럼 Unit 이 아닌 대상은 Hurtbox 를 키로 쓴다(가붕이 Q 와 같은 규칙).
            Object target = unit != null ? (Object)unit : hurtbox;
            if (target == null || unit == owner || activeLedger == null || !activeLedger.TryRegisterTarget(target))
                continue;

            AttackInfo attackInfo = new AttackInfo(serverStageDref, DamageAttackType, hitPattern: DamageHitPattern);
            if (assassinState != null)
                attackInfo = assassinState.WithBackAttack(attackInfo);
            AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, hit, owner);
            bool backAttack = AssassinHitTargets.IsBackAttack(unit, attackInfo, hitContext);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (!resolved)
                continue;

            if (unit != null)
                tickLandedUnits.Add(unit);
            if (backAttack)
                AssassinHitTargets.NotifyBackAttackHit(owner, hit, unit, hitContext);

            Edit.Log($"[Assassin/Q] 경로 적중 — {target.name} 피해 {attackInfo.damage}{(backAttack ? " (백어택)" : "")} ({from:F2}~{to:F2}m)", this);
        }

        if (tickLandedUnits.Count == 0)
            return;

        // 시전 ID = 이 스킬의 ServerCastId — 복귀는 원래 Q 시전 ID 를 이어받아 '시전당 1회' 특성(돌파의 여세 등)이 왕복을 한 번으로 거른다.
        owner.RaiseServerAttackLanded(DamageAttackType, data.TriggersOnHit, tickLandedUnits, this);

        bool landedRewardTarget = AssassinHitTargets.ContainsRewardTarget(tickLandedUnits);

        // 일반 Q — 사용당(그림자 회귀 왕복 전체) 최초 유효 적중 1회만 분노 게이지 충전(§4.2). 변신 Q 는 충전 없음 — 모델도 막는다.
        if (assassinState != null && !assassinState.IsTransformed && activeLedger.TryClaimRageReward(landedRewardTarget))
            assassinState.ServerTryGainRage(AssassinRageSource.DashStrike);

        // 변신 Q — 사용당 최초 유효 적중 1회만 쿨 차감(보스·몹·송전기·허수아비, 상자 제외). 여러 대상이어도 1회.
        // 그림자 회귀 출발 단계는 쿨이 아직 시작 전(복귀·만료 때 시작) — 장치가 차감을 보류했다가 쿨이 시작될 때 바른다(할 일 133 h).
        // 보상은 장부가 왕복 전체 1회로 거르므로 복귀 단계 적중은 다시 차감하지 않는다.
        if (data.HitCooldownReduction > 0f && activeLedger.TryClaimCooldownReward(landedRewardTarget))
        {
            controller?.ReduceCooldownOrHoldServer(CooldownRewardSkill, data.HitCooldownReduction);
            Edit.Log($"[Assassin/Q] 변신 Q 적중 — 쿨타임 {data.HitCooldownReduction:F1}s 차감", this);
        }
    }

    private void RemoveSuperArmor()
    {
        if (!hasSuperArmor)
            return;

        hasSuperArmor = false;
        // 클라에서는 서버 쓰기 가드로 no-op
        if (owner != null && owner.StatusEffects != null)
            owner.StatusEffects.Remove(StatusEffectType.SuperArmor, SourceId);
    }

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude >= 0.001f)
            return direction.normalized;

        Vector3 forward = owner != null ? owner.transform.forward : Vector3.forward;
        forward.y = 0f;
        return forward.sqrMagnitude >= 0.001f ? forward.normalized : Vector3.forward;
    }
}
