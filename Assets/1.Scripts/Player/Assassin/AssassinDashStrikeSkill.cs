using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 어쌔신 Q 관통 돌진 — 일반(Main 슬롯)·변신(alternateSkills[Main]) 공용(character_assassin.md §7, PLAN-assassin A8).
/// 둘의 차이는 데이터뿐이다: 쿨타임·애니 상태 + 변신 데이터(<see cref="AssassinTransformedDashStrikeSkillData"/>)의 적중 쿨 차감.
///
/// 이동 = 오너 권위. 서버 승인 시점의 조준 방향으로 고정하고, 시뮬레이션 피어가 <see cref="PlayerMotor.AddGroundedDisplacement"/> 로
/// 20m/s × 4m 를 낸다(루트모션 끔 — Armature Animator applyRootMotion=false). 적은 통과(<see cref="PlayerMotor.PassThroughEnemiesOverride"/>),
/// 벽·지형에 막히면(<c>MovementResolved</c> wasBlocked) 그 자리에서 돌진을 끝내고 실제 이동 거리를 서버에 보고한다.
///
/// 판정 = 서버. 시작 위치·방향·경과 시간으로 경로를 재구성해 매 틱 새로 나아간 구간을 폭 1.2m 상자로 훑는다
/// (가붕이 Q 가 오너 조향을 서버에서 재현하는 것과 같은 정책 — 위치 오차는 판정 허용 범위). 벽 조기 종료는
/// 오너가 보고한 거리(0~최대 거리로 검증)로 경로 상한을 줄인다. 같은 대상은 사용당 1회, 상자 포함 일반 피해 경로.
/// 🔸 원격 오너의 보고가 도착하기 전(≈왕복 지연 × 20m/s)만큼은 벽 너머 경로를 이미 훑었을 수 있다 — 호스트 본인은 0.
///
/// 돌진 중 슈퍼아머(A1 — 넉백·밀기·스턴·잡기 거부), 무적 없음. 쿨타임은 승인 = 돌진 시작에 시작한다.
/// E 강화를 소모하지 않고 스택도 주지 않는다. 평타 순서 초기화는 스킬 상태 진입으로 <see cref="AssassinBasicAttack"/> 가 한다.
/// </summary>
public sealed class AssassinDashStrikeSkill : PlayerSkillBase
{
    private PlayerMotor motor;
    private AssassinState assassinState;
    private AssassinSkillView view;
    private Collider[] hitResults;
    private readonly AssassinDashHitLedger ledger = new AssassinDashHitLedger();
    private readonly List<Unit> tickLandedUnits = new List<Unit>();

    private Vector3 dashDirection = Vector3.forward;

    // 서버
    private Vector3 serverOrigin;
    private float serverApprovalTime;
    private float serverStartTime;   // 실제 돌진 시작 = 승인 + 준비
    private float serverTraveled;
    private float serverCap;
    private bool isServerSweeping;
    private bool hasSuperArmor;

    // 시뮬레이션 피어(오너, 서버 권위 이동이면 서버도)
    private bool isLocallyDashing;
    private float localPrepareRemaining;
    private float localRemaining;
    private float localTraveled;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Main;
    public override bool CanMoveWhileActive => false;

    private AssassinDashStrikeSkillData DashData => Data as AssassinDashStrikeSkillData;
    private ulong SourceId => owner != null && owner.NetworkObject != null ? owner.NetworkObjectId : 0UL;

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        motor = owner != null ? owner.GetComponent<PlayerMotor>() : null;
        assassinState = owner != null ? owner.GetComponent<AssassinState>() : null;
        view = owner != null ? owner.GetComponent<AssassinSkillView>() : null;
    }

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

        dashDirection = Flatten(direction);
        serverOrigin = owner.transform.position;
        serverApprovalTime = Time.time;
        serverStartTime = Time.time + data.PrepareDuration;
        serverTraveled = 0f;
        serverCap = data.DashDistance;
        isServerSweeping = true;
        ledger.Begin();

        // 서버 전용 쓰기. 돌진이 끝나 스킬이 종료되면(OnTick → EndSelf) 또는 강제 종료면 OnEnd 가 해제한다. 만료 안전망은 지속시간.
        if (owner.StatusEffects != null)
        {
            owner.StatusEffects.Apply(StatusEffectType.SuperArmor, data.MaxActiveDuration, SourceId);
            hasSuperArmor = true;
        }
    }

    // 전 피어. 방향 = 서버 승인 시점의 조준(입력이 받아들여진 순간) — 도중에 바꾸지 않는다.
    public override void OnClientPlay(Vector3 direction)
    {
        dashDirection = Flatten(direction);
        view?.BeginDashTrail();

        AssassinDashStrikeSkillData data = DashData;
        if (data == null || motor == null || owner == null || !owner.IsSimulating)
            return;

        localPrepareRemaining = data.PrepareDuration;
        localRemaining = data.DashDistance;
        localTraveled = 0f;
        isLocallyDashing = true;
        motor.PassThroughEnemiesOverride = true;
        motor.MovementResolved -= HandleMovementResolved;
        motor.MovementResolved += HandleMovementResolved;
    }

    public override void OnFixedTick()
    {
        AssassinDashStrikeSkillData data = DashData;
        if (!isLocallyDashing || localRemaining <= 0f || data == null || motor == null)
            return;

        // 1단계 준비 — 제자리(웅크리기 애니 자리).
        if (localPrepareRemaining > 0f)
        {
            localPrepareRemaining -= Time.fixedDeltaTime;
            return;
        }

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

        // 준비 → 돌진 → 종료(10-06 은희). 경로를 다 훑고 종료 단계까지 지나면 끝낸다(클립 End 를 기다리지 않음).
        // 종료 단계는 제자리 — 궤적 이펙트가 늦게 따라와 속도감을 준다. 끝나면 컨트롤러가 Idle 로 크로스페이드한다.
        if (serverTraveled >= serverCap &&
            AssassinDashStrikeRules.IsFinished(Time.time - serverApprovalTime, data.PrepareDuration,
                serverCap, data.DashSpeed, data.EndDuration))
        {
            isServerSweeping = false;
            EndSelf(SkillEndReason.Completed);
        }
    }

    // 서버 전용 — 오너가 벽에 막혀 멈춘 실제 이동 거리. 0~최대 거리로 검증해 경로 상한만 줄인다(늘리지 않음).
    public override void OnOwnerResultReported(float value)
    {
        AssassinDashStrikeSkillData data = DashData;
        if (!isServerSweeping || data == null ||
            !AssassinDashStrikeRules.TryValidateReportedDistance(value, data.DashDistance, out float reported))
        {
            return;
        }

        serverCap = Mathf.Min(serverCap, Mathf.Max(reported, serverTraveled));
        Edit.Log($"[Assassin/Q] 벽 조기 종료 보고 — {value:F2}m → 경로 상한 {serverCap:F2}m", this);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType != SkillAnimationEventType.End)
            return;

        // 보통은 돌진 종료(OnTick)가 먼저 끝낸다. 클립 End 가 돌진보다 이르게 튜닝돼도 경로 판정은 끝까지 마친다.
        if (isServerSweeping)
            SweepServerTo(serverCap);

        EndSelf(SkillEndReason.Completed);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        // 사망·강제 종료면 아직 훑지 않은 경로의 타격은 버린다(§12.2-3).
        isServerSweeping = false;
        StopLocalDash();
        view?.EndDashTrail();
        if (motor != null)
            motor.PassThroughEnemiesOverride = false;

        RemoveSuperArmor();
        base.OnEnd(reason);
    }

    private void OnDisable()
    {
        StopLocalDash();
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

    // 이번 틱에 새로 나아간 구간 [from, to] 를 폭 PathWidth 상자로 훑는다(끝점만 보지 않는다).
    private void SweepSegment(float from, float to)
    {
        AssassinDashStrikeSkillData data = DashData;
        if (data == null || owner == null || hitResults == null)
            return;

        float length = to - from;
        Vector3 center = serverOrigin + dashDirection * (from + length * 0.5f) + Vector3.up * (data.PathHeight * 0.5f);
        Vector3 halfExtents = new Vector3(data.PathWidth * 0.5f, data.PathHeight * 0.5f, length * 0.5f);
        int count = Physics.OverlapBoxNonAlloc(
            center, halfExtents, hitResults, Quaternion.LookRotation(dashDirection),
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
            if (target == null || unit == owner || !ledger.TryRegisterTarget(target))
                continue;

            AttackInfo attackInfo = new AttackInfo(damageSnapshot, DamageAttackType, hitPattern: DamageHitPattern);
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

        owner.RaiseServerAttackLanded(DamageAttackType, data.TriggersOnHit, tickLandedUnits, this);

        // 변신 Q — 사용당 최초 유효 적중 1회만 쿨 차감(보스·몹·송전기, 상자 제외). 여러 대상이어도 1회.
        if (data.HitCooldownReduction > 0f &&
            ledger.TryClaimCooldownReward(AssassinHitTargets.ContainsRewardTarget(tickLandedUnits)))
        {
            controller?.ReduceCooldownServer(this, data.HitCooldownReduction);
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
