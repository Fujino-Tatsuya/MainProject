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
/// </summary>
public abstract class PlayerInterruptSkillBase : PlayerInstantSkill
{
    private Collider[] hitResults;
    // 상자처럼 Unit이 아닌 IAttackReceiver도 있으므로 Hurtbox까지 중복 방지 키로 쓴다.
    private readonly HashSet<Object> hitTargets = new HashSet<Object>();
    private readonly List<Unit> landedUnits = new List<Unit>();

    private float hitTime;
    private float endTime;
    private bool hasResolvedHit;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Interrupt;
    public override bool CanMoveWhileActive => false;

    protected IPlayerInterruptSkillData InterruptData => Data as IPlayerInterruptSkillData;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

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
            OnMissingHitboxAnchor();
            return;
        }

        int hitCount = OverlapHitboxAnchor(hitResults);
        hitTargets.Clear();
        landedUnits.Clear();
        bool onHitBonusTaken = false;
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

            AttackInfo attackInfo = new AttackInfo(resolvedDamage, DamageAttackType,
                isInterruptAttack: true, hitPattern: DamageHitPattern);
            AttackHitContext hitContext =
                new AttackHitContext(owner.transform.position, owner.transform, hit, owner);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (!resolved)
                continue;

            resolvedCount++;
            if (unit != null)
                landedUnits.Add(unit);

            OnInterruptTargetResolved(target, attackInfo);
        }

        if (CompleteBeforeAttackLanded)
            OnInterruptResolutionCompleted(resolvedCount);

        owner.RaiseServerAttackLanded(
            DamageAttackType, Data != null && Data.TriggersOnHit, landedUnits, this);

        if (!CompleteBeforeAttackLanded)
            OnInterruptResolutionCompleted(resolvedCount);
    }
}
