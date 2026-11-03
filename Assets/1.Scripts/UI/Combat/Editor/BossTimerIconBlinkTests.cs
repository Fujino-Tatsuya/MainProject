using NUnit.Framework;

/// <summary>보스 타이머 해골 아이콘 점멸(할 일 166) 고정 — 주기 = Lerp(시작, 최소, EaseInExpo(경과 비율)), 알파 펄스.</summary>
public sealed class BossTimerIconBlinkTests
{
    const float StartPeriod = 10f;
    const float MinPeriod = 0.2f;

    [Test]
    public void EaseInExpo_Endpoints()
    {
        Assert.That(BossTimerHUD.EaseInExpo(0f), Is.EqualTo(0f));
        Assert.That(BossTimerHUD.EaseInExpo(1f), Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void Period_AtStart_IsStartPeriod()
    {
        Assert.That(BossTimerHUD.BlinkPeriod(0f, StartPeriod, MinPeriod), Is.EqualTo(StartPeriod).Within(1e-5f));
    }

    [Test]
    public void Period_AtExpiry_IsMinPeriod()
    {
        Assert.That(BossTimerHUD.BlinkPeriod(1f, StartPeriod, MinPeriod), Is.EqualTo(MinPeriod).Within(1e-5f));
    }

    [Test]
    public void Period_NonIncreasingAsTimePasses()
    {
        float prev = BossTimerHUD.BlinkPeriod(0f, StartPeriod, MinPeriod);
        for (int i = 1; i <= 100; i++)
        {
            float p = BossTimerHUD.BlinkPeriod(i / 100f, StartPeriod, MinPeriod);
            Assert.That(p, Is.LessThanOrEqualTo(prev), $"t={i / 100f}");
            prev = p;
        }
    }

    [Test]
    public void ElapsedFraction_ClampedZeroToOne()
    {
        Assert.That(BossTimerHUD.ElapsedFraction(300f, 300f), Is.EqualTo(0f));
        Assert.That(BossTimerHUD.ElapsedFraction(0f, 300f), Is.EqualTo(1f));
        Assert.That(BossTimerHUD.ElapsedFraction(150f, 300f), Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(BossTimerHUD.ElapsedFraction(400f, 300f), Is.EqualTo(0f));
        Assert.That(BossTimerHUD.ElapsedFraction(10f, 0f), Is.EqualTo(1f));
    }

    [Test]
    public void Alpha_PulsesBetweenOneAndMin()
    {
        Assert.That(BossTimerHUD.BlinkAlpha(0f, 0.25f), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(BossTimerHUD.BlinkAlpha(0.5f, 0.25f), Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(BossTimerHUD.BlinkAlpha(1f, 0.25f), Is.EqualTo(1f).Within(1e-5f));
    }
}
