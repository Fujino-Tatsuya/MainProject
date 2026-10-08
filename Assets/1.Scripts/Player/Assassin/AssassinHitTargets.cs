using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 어쌔신 적중 보상 대상 분류(character_assassin.md §4.3). 보스·일반 몹(<see cref="MonsterBase"/>)과 송전기(<see cref="BossChargingPylon"/>)만.
/// 상자(<c>BreakableCrate</c>)는 Unit 이 아니라 적중 목록에 들어오지 않는다. 다른 기믹은 해당 기믹이 따로 지정한다.
/// 백어택(§4.1, A11)은 <see cref="MonsterBase"/> 만 — 송전기·기믹·상자는 MonsterBase 가 아니라 자연히 빠진다.
/// </summary>
public static class AssassinHitTargets
{
    public static bool IsRewardTarget(Unit unit) =>
        unit != null && (unit is MonsterBase || unit is BossChargingPylon);

    public static bool ContainsRewardTarget(IReadOnlyList<Unit> units)
    {
        if (units == null)
            return false;

        for (int i = 0; i < units.Count; i++)
        {
            if (IsRewardTarget(units[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// [서버] 이 타격이 백어택인가 — 판정은 <see cref="MonsterBase.IsBackAttackHit"/> 한 곳. 피해 적용 <b>전에</b> 같은 입력으로 묻는다
    /// (적중 처리가 대상 상태를 바꾸기 전에 스냅샷).
    /// </summary>
    public static bool IsBackAttack(Unit unit, AttackInfo attackInfo, AttackHitContext hitContext) =>
        unit is MonsterBase monster && monster.IsBackAttackHit(attackInfo, hitContext);

    /// <summary>[서버] 백어택 적중 연출 — 공격자 쪽 몸통 표면(높이는 콜라이더 중심)에 1회.</summary>
    public static void NotifyBackAttackHit(Component owner, Collider hit, Unit unit, AttackHitContext hitContext)
    {
        Vector3 point;
        if (hit != null)
        {
            Bounds bounds = hit.bounds;
            point = bounds.ClosestPoint(BackAttackRules.ResolveAttackerPosition(hitContext));
            point.y = bounds.center.y;
        }
        else
        {
            point = unit != null ? unit.transform.position : hitContext.sourcePosition;
        }

        AssassinSkillView.Play(owner, view => view.ServerBackAttackHit(point));
    }
}
