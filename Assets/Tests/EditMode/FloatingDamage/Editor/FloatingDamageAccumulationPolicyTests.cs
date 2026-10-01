using NUnit.Framework;

public sealed class FloatingDamageAccumulationPolicyTests
{
    [Test]
    public void Single_DoesNotCreateAccumulationKey()
    {
        bool created = FloatingDamageAccumulationPolicy.TryCreateKey(
            1UL, AttackType.Default, 10, PopupKind.Damage, AttackHitPattern.Single, out _);

        Assert.That(created, Is.False);
    }

    [Test]
    public void Multi_WithSameDimensions_CreatesEqualKeys()
    {
        FloatingDamageAccumulationKey first = CreateKey(1UL, AttackType.SkillQ, 10, PopupKind.Damage);
        FloatingDamageAccumulationKey second = CreateKey(1UL, AttackType.SkillQ, 10, PopupKind.Damage);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
    }

    [TestCase(2UL, AttackType.SkillQ, 10, PopupKind.Damage, TestName = "DifferentAttacker")]
    [TestCase(1UL, AttackType.SkillE, 10, PopupKind.Damage, TestName = "DifferentAttackType")]
    [TestCase(1UL, AttackType.SkillQ, 11, PopupKind.Damage, TestName = "DifferentTarget")]
    [TestCase(1UL, AttackType.SkillQ, 10, PopupKind.ShieldDamage, TestName = "DifferentPopupKind")]
    public void Multi_DifferentDimension_CreatesDifferentKey(
        ulong attackerClientId, AttackType attackType, int targetId, PopupKind kind)
    {
        FloatingDamageAccumulationKey baseline =
            CreateKey(1UL, AttackType.SkillQ, 10, PopupKind.Damage);
        FloatingDamageAccumulationKey changed =
            CreateKey(attackerClientId, attackType, targetId, kind);

        Assert.That(changed, Is.Not.EqualTo(baseline));
    }

    static FloatingDamageAccumulationKey CreateKey(
        ulong attackerClientId, AttackType attackType, int targetId, PopupKind kind)
    {
        bool created = FloatingDamageAccumulationPolicy.TryCreateKey(
            attackerClientId, attackType, targetId, kind, AttackHitPattern.Multi, out var key);
        Assert.That(created, Is.True);
        return key;
    }
}
