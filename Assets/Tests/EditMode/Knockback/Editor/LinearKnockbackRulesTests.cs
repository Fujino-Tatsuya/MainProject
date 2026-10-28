using NUnit.Framework;

// 할 일 136 — 몬스터 넉백(LinearKnockback) 비물리화: 세기 → 속도·시간·거리 규칙.
public sealed class LinearKnockbackRulesTests
{
    private const float Decel = LinearKnockbackRules.DefaultDeceleration;
    private const float MaxTime = 1.5f;

    [Test]
    public void Speed_StartsAtStrength_DeceleratesLinearly_NeverNegative()
    {
        Assert.That(LinearKnockbackRules.Speed(5f, Decel, 0f), Is.EqualTo(5f), "출발 속도 = 세기(예전 임펄스 ÷ 질량 1)");
        Assert.That(LinearKnockbackRules.Speed(5f, Decel, 0.5f), Is.EqualTo(2f).Within(1e-5f));
        Assert.That(LinearKnockbackRules.Speed(5f, Decel, 10f), Is.EqualTo(0f));
        Assert.That(LinearKnockbackRules.Speed(0f, Decel, 0f), Is.EqualTo(0f));
    }

    [TestCase(5f, 25f / 12f)]   // 23호 점프·돌진 넉백 세기 5 ≈ 2.08m
    [TestCase(3f, 9f / 12f)]
    public void Distance_IsStrengthSquaredOverTwiceDecel(float strength, float expected)
    {
        Assert.That(LinearKnockbackRules.Distance(strength, Decel, MaxTime), Is.EqualTo(expected).Within(1e-4f));
        Assert.That(LinearKnockbackRules.Duration(strength, Decel, MaxTime), Is.EqualTo(strength / Decel).Within(1e-5f));
    }

    [Test]
    public void StrongKnockback_CappedByMaxTime()
    {
        // 세기 12 — 2초짜리 감속이 1.5초에서 잘린다: 12×1.5 − ½×6×1.5² = 11.25m
        Assert.That(LinearKnockbackRules.Duration(12f, Decel, MaxTime), Is.EqualTo(MaxTime));
        Assert.That(LinearKnockbackRules.Distance(12f, Decel, MaxTime), Is.EqualTo(11.25f).Within(1e-4f));
    }
}
