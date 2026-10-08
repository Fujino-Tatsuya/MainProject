using UnityEngine;

/// <summary>
/// 어쌔신 우클릭 간파(character_assassin.md §11, PLAN-assassin A10) — 공용 간파 베이스 그대로.
/// 일반·변신 공통(대체 세트 없음). 애니 = Attack_Up_01("Interrupt" 상태), 앵커 = Assassin_Armature/InterruptAttack.
/// 다른 동작 중 우클릭 무시·예약 없음은 공통 FSM 이 처리한다. 변신 종료 대기 중이면 간파(Skill 상태)가 끝난 뒤
/// <see cref="AssassinState"/> 가 변신을 끝낸다(§10.3 "현재 간파 공격 동작 완료").
/// 간파 성공 방향 조건은 보스 쪽 판정이다 — 변신 중 백어택과 별개(배율만 싣고 간파 판정에는 관여하지 않는다, A11).
/// </summary>
public sealed class AssassinInterruptSkill : PlayerInterruptSkillBase
{
    private AssassinState assassinState;

    // 애니메이션은 PlayerSkillController 가 animatorStateName 으로 재생한다. 연출은 임시(AssassinSkillView — 민경 교체).
    public override void OnClientPlay(Vector3 direction) =>
        AssassinSkillView.Play(owner, v => v.PlayInterruptCast());

    protected override AttackInfo DecorateAttackInfo(AttackInfo attackInfo)
    {
        AssassinState state = AssassinStateComponent;
        return state != null ? state.WithBackAttack(attackInfo) : attackInfo;
    }

    // 백어택 적중 연출. 베이스가 수신 전 판정 지점을 주지 않아 수신 직후 같은 입력으로 묻는다 —
    // 위치 판정은 피해 전후 대상 자세가 같고, 변신 강제는 위치와 무관하다.
    protected override void OnInterruptTargetResolved(Object target, AttackInfo attackInfo)
    {
        if (!(target is Unit unit) || owner == null)
            return;

        AttackHitContext hitContext = new AttackHitContext(owner.transform.position, owner.transform, null, owner);
        if (AssassinHitTargets.IsBackAttack(unit, attackInfo, hitContext))
            AssassinHitTargets.NotifyBackAttackHit(owner, null, unit, hitContext);
    }

    private AssassinState AssassinStateComponent
    {
        get
        {
            if (assassinState == null && owner != null)
                assassinState = owner.GetComponent<AssassinState>();
            return assassinState;
        }
    }
}
