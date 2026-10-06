using System.Collections.Generic;

/// <summary>
/// 어쌔신 적중 보상 대상 분류(character_assassin.md §4.3). 보스·일반 몹(<see cref="MonsterBase"/>)과 송전기(<see cref="BossChargingPylon"/>)만.
/// 상자(<c>BreakableCrate</c>)는 Unit 이 아니라 적중 목록에 들어오지 않는다. 다른 기믹은 해당 기믹이 따로 지정한다.
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
}
