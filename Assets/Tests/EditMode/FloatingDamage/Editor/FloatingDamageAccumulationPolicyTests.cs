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

    // ── 할 일 174 — 제압 가속 중엔 Multi 여부·공격 종류와 무관하게 공격자·대상별 숫자 하나 ──

    [TestCase(AttackHitPattern.Single, false, FloatingDamageAccumulation.None)]
    [TestCase(AttackHitPattern.Multi, false, FloatingDamageAccumulation.Multi)]
    [TestCase(AttackHitPattern.Single, true, FloatingDamageAccumulation.SuppressHaste)]
    [TestCase(AttackHitPattern.Multi, true, FloatingDamageAccumulation.SuppressHaste)]
    public void Resolve_SuppressHasteMergesEveryPattern(
        AttackHitPattern pattern, bool haste, FloatingDamageAccumulation expected)
    {
        Assert.That(FloatingDamageAccumulationPolicy.Resolve(pattern, haste), Is.EqualTo(expected));
    }

    [Test]
    public void SuppressHaste_DifferentAttackTypes_ShareOneKey()
    {
        FloatingDamageAccumulationKey q = CreateHasteKey(1UL, AttackType.SkillQ, 10, PopupKind.Damage);
        FloatingDamageAccumulationKey e = CreateHasteKey(1UL, AttackType.SkillE, 10, PopupKind.Damage);
        FloatingDamageAccumulationKey basic = CreateHasteKey(1UL, AttackType.Default, 10, PopupKind.Damage);

        Assert.That(q, Is.EqualTo(e));
        Assert.That(q, Is.EqualTo(basic));
        Assert.That(q.GetHashCode(), Is.EqualTo(e.GetHashCode()));
    }

    [TestCase(2UL, 10, PopupKind.Damage, TestName = "SuppressHaste_DifferentAttacker")]
    [TestCase(1UL, 11, PopupKind.Damage, TestName = "SuppressHaste_DifferentTarget")]
    [TestCase(1UL, 10, PopupKind.ShieldDamage, TestName = "SuppressHaste_DifferentPopupKind")]
    public void SuppressHaste_DifferentAttackerTargetOrKind_DifferentKey(ulong attacker, int target, PopupKind kind)
    {
        FloatingDamageAccumulationKey baseline = CreateHasteKey(1UL, AttackType.SkillQ, 10, PopupKind.Damage);
        Assert.That(CreateHasteKey(attacker, AttackType.SkillQ, target, kind), Is.Not.EqualTo(baseline));
    }

    [Test]
    public void SuppressHasteKey_DoesNotCollideWithMultiKey()
    {
        FloatingDamageAccumulationKey haste = CreateHasteKey(1UL, AttackType.Default, 10, PopupKind.Damage);
        FloatingDamageAccumulationKey multi = CreateKey(1UL, AttackType.Default, 10, PopupKind.Damage);

        Assert.That(haste, Is.Not.EqualTo(multi));
    }

    [Test]
    public void HeldLastHitAt_HoldsWhileHasteThenStops()
    {
        // 가속 중 — 마지막 타격 시각이 지금으로 밀려 유지(accumulateHold)가 끝나지 않는다.
        Assert.That(FloatingDamageAccumulationPolicy.HeldLastHitAt(1f, 3f, true), Is.EqualTo(3f));
        // 가속 끝 — 더 밀지 않는다(그 순간부터 평소처럼 유지 → 흐려짐).
        Assert.That(FloatingDamageAccumulationPolicy.HeldLastHitAt(3f, 3.4f, false), Is.EqualTo(3f));
        // 되돌리지 않는다.
        Assert.That(FloatingDamageAccumulationPolicy.HeldLastHitAt(5f, 4f, true), Is.EqualTo(5f));
    }

    static FloatingDamageAccumulationKey CreateHasteKey(
        ulong attackerClientId, AttackType attackType, int targetId, PopupKind kind)
    {
        bool created = FloatingDamageAccumulationPolicy.TryCreateKey(
            attackerClientId, attackType, targetId, kind, FloatingDamageAccumulation.SuppressHaste, out var key);
        Assert.That(created, Is.True);
        return key;
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
