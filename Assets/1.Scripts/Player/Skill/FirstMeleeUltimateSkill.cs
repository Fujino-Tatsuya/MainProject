using UnityEngine;

/// <summary>
/// R 최후의 심판 — 대상 지정(SingleTarget) 채널링 스킬. 조준 모드에서 사거리 내 적을 지정해 시전하고,
/// 채널을 완주하면 지정 대상에게 단일 피해를 1회 적용한다(타겟팅 인프라 검증용 최소 효과).
/// 대상 유효성(생존·사거리)은 서버 CanUse로 권위 검증하며, 채널 중 대상이 사라지면 취소한다.
/// 상세 메커니즘·연출은 후속 작업.
/// </summary>
public class FirstMeleeUltimateSkill : PlayerChannelingSkill
{
    private Unit lockedTarget;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Ultimate;

    // 채널 중 이동/회전 잠금
    public override bool CanMoveWhileActive => false;

    // 서버 권위 시전 조건: 대상이 살아있는 적이고 사거리 내여야 한다.
    public override bool CanUse(Vector3 direction, Unit target)
    {
        if (Data == null || target == null || target == owner)
            return false;

        if (target.CurrentHealth <= 0)
            return false;

        return IsWithinRange(target.transform.position);
    }

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);
        lockedTarget = target;
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 채널/타격 연출은 VFX 확정 후 추가
    }

    public override void OnTick()
    {
        // 채널 중 대상이 사라지거나 사망하면 취소 (완주 판정은 base가 처리)
        if (State == SkillState.Channeling && (lockedTarget == null || lockedTarget.CurrentHealth <= 0))
        {
            EndSelf(SkillEndReason.Cancelled);
            return;
        }

        base.OnTick();
    }

    protected override void OnChannelCompleted()
    {
        if (lockedTarget == null || lockedTarget.CurrentHealth <= 0)
            return;

        AttackInfo attackInfo = new AttackInfo(damageSnapshot, AttackType.R);
        AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform);
        lockedTarget.ReceiveAttack(attackInfo, hitContext);

        Edit.Log($"[Skill] 최후의 심판 — {lockedTarget.name}에게 피해 {attackInfo.damage}", this);
    }

    public override void OnEnd(SkillEndReason reason)
    {
        base.OnEnd(reason);
        lockedTarget = null;
    }

    private bool IsWithinRange(Vector3 worldPoint)
    {
        Vector3 flat = worldPoint - owner.transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= Data.CastRange * Data.CastRange;
    }
}
