using NUnit.Framework;

/// <summary>근접 평타 '다 돈 뒤 공격'(할 일 43) — 각 → 회전 지연 규칙.</summary>
public sealed class AttackTurnTimingTests
{
    private const float Snap = 30f;
    private const float Speed = 1500f;

    [Test]
    public void Delay_AtOrBelowSnapAngle_IsZero()
    {
        Assert.That(AttackTurnTiming.Delay(0f, Snap, Speed), Is.EqualTo(0f));
        Assert.That(AttackTurnTiming.Delay(29.9f, Snap, Speed), Is.EqualTo(0f));
        Assert.That(AttackTurnTiming.Delay(30f, Snap, Speed), Is.EqualTo(0f), "스냅 각과 같으면 즉시");
    }

    [Test]
    public void Delay_AboveSnapAngle_IsAngleOverSpeed()
    {
        Assert.That(AttackTurnTiming.Delay(30.1f, Snap, Speed), Is.EqualTo(30.1f / Speed).Within(1e-6f));
        Assert.That(AttackTurnTiming.Delay(90f, Snap, Speed), Is.EqualTo(0.06f).Within(1e-6f));
        Assert.That(AttackTurnTiming.Delay(180f, Snap, Speed), Is.EqualTo(0.12f).Within(1e-6f), "기본값: 180° ≈ 0.12초");
    }

    [Test]
    public void Delay_ZeroOrNegativeSpeed_IsImmediate()
    {
        Assert.That(AttackTurnTiming.Delay(180f, Snap, 0f), Is.EqualTo(0f));
        Assert.That(AttackTurnTiming.Delay(180f, Snap, -10f), Is.EqualTo(0f));
    }

    [Test]
    public void Delay_ZeroSnapAngle_TurnsAnyPositiveAngle()
    {
        Assert.That(AttackTurnTiming.Delay(0f, 0f, Speed), Is.EqualTo(0f));
        Assert.That(AttackTurnTiming.Delay(15f, 0f, Speed), Is.EqualTo(0.01f).Within(1e-6f));
    }

    [Test]
    public void Delay_NaNAngle_IsZero()
    {
        Assert.That(AttackTurnTiming.Delay(float.NaN, Snap, Speed), Is.EqualTo(0f));
    }

    [Test]
    public void ClampRequested_CutsToMaxDelay()
    {
        float max = AttackTurnTiming.MaxDelay(Snap, Speed);
        Assert.That(max, Is.EqualTo(0.12f).Within(1e-6f));
        Assert.That(AttackTurnTiming.ClampRequested(0.05f, Snap, Speed), Is.EqualTo(0.05f));
        Assert.That(AttackTurnTiming.ClampRequested(5f, Snap, Speed), Is.EqualTo(max));
    }

    [Test]
    public void ClampRequested_NegativeOrNaN_IsZero()
    {
        Assert.That(AttackTurnTiming.ClampRequested(-1f, Snap, Speed), Is.EqualTo(0f));
        Assert.That(AttackTurnTiming.ClampRequested(float.NaN, Snap, Speed), Is.EqualTo(0f));
        Assert.That(AttackTurnTiming.ClampRequested(0.1f, Snap, 0f), Is.EqualTo(0f), "각속도 0 = 항상 즉시라 지연도 0");
    }
}
