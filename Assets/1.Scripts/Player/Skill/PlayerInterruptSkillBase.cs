using System.Collections.Generic;
using UnityEngine;

/// <summary>간파 스킬 공용 판정 타이밍과 버퍼 크기를 제공하는 데이터 계약.</summary>
public interface IPlayerInterruptSkillData
{
    float HitDelay { get; }
    float SkillDuration { get; }
    int MaxHitResults { get; }
}

/// <summary>
/// 플레이어 간파 공용 베이스. Hit 애니메이션 이벤트와 HitDelay 타이머 중 먼저 온 경로에서
/// 앵커 오버랩을 한 번만 판정하고, 모든 피해에 간파 공격 표식을 싣는다.
///
/// 인터럽트 슬로우 모션(PLAN-interrupt-slowmo S3): 승인 시점에 같은 앵커 오버랩으로 유효타를 예측해
/// (<see cref="IInterruptSlowMotionTarget"/> 이고 지금 인터럽트 가능한 대상이 있으면) 전역 슬로우를 시작한다.
/// 판정에서 그 인터럽트가 하나도 성공하지 않거나 판정 전에 스킬이 끝나면 실패 복귀시킨다.
/// </summary>
public abstract class PlayerInterruptSkillBase : PlayerInstantSkill
{
    private Collider[] hitResults;
    // 상자처럼 Unit이 아닌 IAttackReceiver도 있으므로 Hurtbox까지 중복 방지 키로 쓴다.
    private readonly HashSet<Object> hitTargets = new HashSet<Object>();
    private readonly List<Unit> landedUnits = new List<Unit>();
    private readonly InterruptSlowMotionTracker slowMotion = new InterruptSlowMotionTracker();

    private float hitTime;
    private float endTime;
    private bool hasResolvedHit;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Interrupt;
    public override bool CanMoveWhileActive => false;

    protected IPlayerInterruptSkillData InterruptData => Data as IPlayerInterruptSkillData;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);
        slowMotion.Begin();

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
        if (!hasResolvedHit && Time.time >= hitTime)
            ResolveHit();

        // 같은 프레임에 판정과 종료가 겹치면 판정을 먼저 처리한다.
        if (Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Hit)
            ResolveHit();

        base.OnAnimationEvent(eventType);
    }

    // 모든 피어에서 불리지만 발동 기록은 서버에만 있다. 판정 전 종료(완료·취소·사망)면 실패 복귀.
    public override void OnEnd(SkillEndReason reason)
    {
        FailSlowMotion(slowMotion.Abort());
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

    // 서버 전용. 애니메이션 이벤트와 타이머가 겹쳐도 래치 때문에 한 번만 실행된다.
    private void ResolveHit()
    {
        if (hasResolvedHit)
            return;

        hasResolvedHit = true;

        if (HitboxAnchor == null || owner == null)
        {
            FailSlowMotion(slowMotion.Resolve(false));
            OnMissingHitboxAnchor();
            return;
        }

        int hitCount = OverlapHitboxAnchor(hitResults);
        hitTargets.Clear();
        landedUnits.Clear();
        bool onHitBonusTaken = false;
        bool interruptLanded = false;
        int resolvedCount = 0;

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
            IInterruptSlowMotionTarget slowTarget = slowMotion.IsPending ? FindSlowMotionTarget(unit) : null;
            int successBefore = slowTarget != null ? slowTarget.ServerInterruptSuccessCount : 0;

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (slowTarget != null && slowTarget.ServerInterruptSuccessCount != successBefore)
                interruptLanded = true;

            if (!resolved)
                continue;

            resolvedCount++;
            if (unit != null)
                landedUnits.Add(unit);

            OnInterruptTargetResolved(target, attackInfo);
        }

        FailSlowMotion(slowMotion.Resolve(interruptLanded));

        if (CompleteBeforeAttackLanded)
            OnInterruptResolutionCompleted(resolvedCount);

        owner.RaiseServerAttackLanded(
            DamageAttackType, Data != null && Data.TriggersOnHit, landedUnits, this);

        if (!CompleteBeforeAttackLanded)
            OnInterruptResolutionCompleted(resolvedCount);
    }

    // ── 인터럽트 슬로우 모션 (서버) ──

    // 승인 시점 예측(D1·D2): 판정과 같은 앵커 오버랩을 한 번 돌려, 지금 인터럽트 가능한 예측 대상이 있으면 발동한다.
    // 각도 검사는 따로 하지 않는다 — 앵커 형태가 곧 판정 범위다. 시간 레이어가 없는 씬(오프라인 테스트)은 건너뛴다.
    private void TryStartSlowMotion()
    {
        GlobalTimeScale timeScale = GlobalTimeScale.Instance;
        if (timeScale == null || HitboxAnchor == null || owner == null || !PredictsInterruptHit())
            return;

        slowMotion.Started(timeScale.ServerStart(timeScale.InterruptProfile, owner.OwnerClientId));
    }

    private bool PredictsInterruptHit()
    {
        int hitCount = OverlapHitboxAnchor(hitResults);
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

    private static IInterruptSlowMotionTarget FindSlowMotionTarget(Unit unit) =>
        unit != null && unit.TryGetComponent(out IInterruptSlowMotionTarget slowTarget) ? slowTarget : null;

    // ServerFail 은 현재 발동 번호일 때만 먹으므로 늦게 와도 남의 슬로우를 끊지 않는다.
    private static void FailSlowMotion(uint triggerId)
    {
        if (triggerId == 0)
            return;

        GlobalTimeScale timeScale = GlobalTimeScale.Instance;
        if (timeScale != null)
            timeScale.ServerFail(triggerId);
    }
}
