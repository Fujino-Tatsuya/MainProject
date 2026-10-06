using NUnit.Framework;

/// <summary>플레이어/몬스터 공통 SuperArmor 의미 고정(PLAN-assassin.md A1).</summary>
public sealed class StatusEffectImmunityTests
{
    [TestCase(StatusEffectType.Airborne)]
    [TestCase(StatusEffectType.Stunned)]
    [TestCase(StatusEffectType.Slowed)]
    [TestCase(StatusEffectType.Rooted)]
    [TestCase(StatusEffectType.Silenced)]
    [TestCase(StatusEffectType.Debilitated)]
    public void SuperArmor_BlocksCrowdControlApplication(StatusEffectType incomingType)
    {
        Assert.That(
            StatusEffectImmunityPolicy.ShouldIgnoreApplication(StatusEffectType.SuperArmor, incomingType),
            Is.True);
    }

    [Test]
    public void SuperArmorRemoved_AllowsCrowdControlApplication()
    {
        Assert.That(
            StatusEffectImmunityPolicy.ShouldIgnoreApplication(StatusEffectType.None, StatusEffectType.Stunned),
            Is.False);
    }

    [TestCase(StatusEffectType.MoveSpeedModifier)]
    [TestCase(StatusEffectType.AttackDamageModifier)]
    [TestCase(StatusEffectType.AttackSpeedModifier)]
    [TestCase(StatusEffectType.DefenseModifier)]
    [TestCase(StatusEffectType.MaxHpModifier)]
    public void SuperArmor_AllowsStatModifierDebuffs(StatusEffectType incomingType)
    {
        Assert.That(StatusEffectCategories.Of(incomingType, 0.5f), Is.EqualTo(StatusEffectCategory.Debuff));
        Assert.That(
            StatusEffectImmunityPolicy.ShouldIgnoreApplication(StatusEffectType.SuperArmor, incomingType),
            Is.False);
    }

    [TestCase(StatusEffectType.SuperArmor)]
    [TestCase(StatusEffectType.PassiveCharge)]
    [TestCase(StatusEffectType.Focus)]
    public void SuperArmor_AllowsNonCrowdControlEffects(StatusEffectType incomingType)
    {
        Assert.That(
            StatusEffectImmunityPolicy.ShouldIgnoreApplication(StatusEffectType.SuperArmor, incomingType),
            Is.False);
    }

    [TestCase(RestraintMode.Carry)]
    [TestCase(RestraintMode.Push)]
    public void SuperArmor_BlocksPhysicalRestraint(RestraintMode mode)
    {
        Assert.That(PlayerRestraintPolicy.IsBlockedBySuperArmor(mode, hasSuperArmor: true), Is.True);
        Assert.That(PlayerRestraintPolicy.IsBlockedBySuperArmor(mode, hasSuperArmor: false), Is.False);
    }
}
