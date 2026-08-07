using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 우클릭 단죄의 방패 — 전방 방패 강타 1회. 서버가 앵커 범위에 인터럽트 히트를 넣는다
/// (<see cref="AttackInfo.isInterruptAttack"/>).
///
/// 이 스킬의 계약은 "이 히트는 인터럽트 스킬이다"를 실어 보내는 것까지다.
/// 카운터 창 여부·정면 각도·그로기/Break 전이는 <b>전부 맞는 쪽(보스) 책임</b>이다 — 여기서 판단하지 않는다.
///
/// 판정 타이밍은 두 경로를 모두 받되 <b>1회만</b> 발동한다:
/// 클립의 Hit 애니메이션 이벤트(정밀) 또는 <see cref="FirstMeleeInterruptSkillData.HitDelay"/> 타이머(폴백).
/// 클립은 아트/SVN 관할이라 이벤트 없이도 성립해야 한다.
/// </summary>
public class FirstMeleeInterruptSkill : PlayerInstantSkill
{
    private Collider[] hitResults;
    private readonly HashSet<Unit> hitTargets = new HashSet<Unit>();

    private float hitTime;
    private float endTime;
    private bool hasResolvedHit;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Interrupt;

    // 강타 중 이동 잠금 — 방향을 확정한 뒤 때린다
    public override bool CanMoveWhileActive => false;

    private FirstMeleeInterruptSkillData InterruptData => Data as FirstMeleeInterruptSkillData;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        FirstMeleeInterruptSkillData data = InterruptData;
        if (data == null)
        {
            Debug.LogError("[Player] 단죄의 방패에는 FirstMeleeInterruptSkillData가 필요합니다.", this);
            EndSelf(SkillEndReason.Completed);
            return;
        }

        hasResolvedHit = false;
        hitTime = Time.time + data.HitDelay;
        endTime = Time.time + data.SkillDuration;

        if (hitResults == null || hitResults.Length != data.MaxHitResults)
            hitResults = new Collider[data.MaxHitResults];
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 연출은 PlayerSkillController가 AnimatorStateName으로 재생한다. 전용 VFX는 확정 후 추가.
    }

    public override void OnTick()
    {
        if (!hasResolvedHit && Time.time >= hitTime)
            ResolveHit();

        // 종료 순서 주의: 같은 프레임에 둘 다 만족해도 판정이 먼저다
        if (Time.time >= endTime)
            EndSelf(SkillEndReason.Completed);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.Hit)
            ResolveHit();

        // End 이벤트 → EndSelf
        base.OnAnimationEvent(eventType);
    }

    // 서버 전용. 래치가 있어 애니 이벤트와 타이머가 겹쳐도 한 번만 들어간다.
    private void ResolveHit()
    {
        if (hasResolvedHit)
            return;

        hasResolvedHit = true;

        if (HitboxAnchor == null || owner == null)
        {
            Debug.LogError("[Player] 단죄의 방패에 판정 앵커(hitboxAnchor)가 배정되지 않았습니다.", this);
            return;
        }

        int hitCount = OverlapHitboxAnchor(hitResults);
        hitTargets.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit unit = ResolveHitUnit(hit, out Hurtbox hurtbox);
            if (unit == null || unit == owner || !hitTargets.Add(unit))
                continue;

            // isInterruptAttack = 보스가 카운터 판정에 쓰는 유일한 근거.
            // 소비 방식은 맞는 쪽이 정한다 — 몬스터는 누적→그로기, No.23은 카운터 창 판정.
            AttackInfo attackInfo = new AttackInfo(damageSnapshot, AttackType.Skill, isInterruptAttack: true);
            AttackHitContext hitContext =
                new AttackHitContext(owner.transform.position, owner.transform, hit, owner);

            bool resolved = hurtbox != null
                ? hurtbox.ReceiveAttack(attackInfo, hitContext)
                : unit.ReceiveAttack(attackInfo, hitContext);

            if (resolved)
                Edit.Log($"[Skill] 단죄의 방패 적중 — {unit.name} 피해 {attackInfo.damage} (Interrupt)", this);
        }
    }
}
