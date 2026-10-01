using NUnit.Framework;

public sealed class FloatingDamageTierTests
{
    // 기획 §3 판정 사례: 기준 최대 체력 1,000 일반 몬스터(중간 3% · 높음 12%).
    [TestCase(30, FloatingDamageTier.Mid)]
    [TestCase(120, FloatingDamageTier.High)]
    [TestCase(20, FloatingDamageTier.Low)]
    [TestCase(29, FloatingDamageTier.Low)]
    public void Classify_NormalMonsterExamples(int amount, FloatingDamageTier expected)
    {
        Assert.That(FloatingDamageTierPolicy.Classify(amount, 1000, 3f, 12f), Is.EqualTo(expected));
    }

    [Test]
    public void Classify_BoundaryBelongsToUpperTier()
    {
        Assert.That(FloatingDamageTierPolicy.Classify(3, 100, 3f, 12f), Is.EqualTo(FloatingDamageTier.Mid));
        Assert.That(FloatingDamageTierPolicy.Classify(12, 100, 3f, 12f), Is.EqualTo(FloatingDamageTier.High));
    }

    [TestCase(0, 1000)]
    [TestCase(50, 0)]
    public void Classify_InvalidInput_IsLow(int amount, int maxHp)
    {
        Assert.That(FloatingDamageTierPolicy.Classify(amount, maxHp, 3f, 12f), Is.EqualTo(FloatingDamageTier.Low));
    }

    [Test]
    public void EmphasisGate_BlocksWithinInterval_PerAttackerAndTarget()
    {
        var gate = new FloatingDamageEmphasisGate();

        Assert.That(gate.TryConsume(1UL, 10, 0f, 0.3f), Is.True);
        Assert.That(gate.TryConsume(1UL, 10, 0.29f, 0.3f), Is.False);
        Assert.That(gate.TryConsume(1UL, 11, 0.29f, 0.3f), Is.True, "다른 대상은 따로 센다");
        Assert.That(gate.TryConsume(2UL, 10, 0.29f, 0.3f), Is.True, "다른 공격자는 따로 센다");
        Assert.That(gate.TryConsume(1UL, 10, 0.30f, 0.3f), Is.True);
    }

    [Test]
    public void EmphasisGate_BlockedAttemptDoesNotExtendInterval()
    {
        var gate = new FloatingDamageEmphasisGate();

        gate.TryConsume(1UL, 10, 0f, 0.3f);
        gate.TryConsume(1UL, 10, 0.2f, 0.3f);

        Assert.That(gate.TryConsume(1UL, 10, 0.3f, 0.3f), Is.True);
    }
}
