using NUnit.Framework;

public sealed class AssassinLungeModelTests
{
    [Test]
    public void StepIsDistanceOverDurationTimesDelta()
    {
        // 1.2m / 0.2s = 6m/s, 0.02s 틱 = 0.12m
        Assert.That(AssassinLungeRules.StepDistance(1.2f, 1.2f, 0.2f, 0.02f), Is.EqualTo(0.12f).Within(1e-5f));
    }

    [Test]
    public void StepStopsAtRemaining()
    {
        Assert.That(AssassinLungeRules.StepDistance(0.05f, 1.2f, 0.2f, 0.02f), Is.EqualTo(0.05f));
    }

    [Test]
    public void TicksSumToExactDistanceWithinDuration()
    {
        float remaining = 1.2f;
        int ticks = 0;
        while (remaining > 0f && ticks < 100)
        {
            remaining -= AssassinLungeRules.StepDistance(remaining, 1.2f, 0.2f, 0.02f);
            ticks++;
        }

        Assert.That(remaining, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(ticks, Is.LessThanOrEqualTo(11)); // 0.2s / 0.02s = 10틱(부동소수 오차로 마지막 1틱 여유)
    }

    [Test]
    public void ZeroDurationMovesAllAtOnce()
    {
        Assert.That(AssassinLungeRules.StepDistance(1.2f, 1.2f, 0f, 0.02f), Is.EqualTo(1.2f));
    }

    [Test]
    public void NothingWhenFinishedOrDisabled()
    {
        Assert.That(AssassinLungeRules.StepDistance(0f, 1.2f, 0.2f, 0.02f), Is.EqualTo(0f));
        Assert.That(AssassinLungeRules.StepDistance(1.2f, 0f, 0.2f, 0.02f), Is.EqualTo(0f));
        Assert.That(AssassinLungeRules.StepDistance(1.2f, 1.2f, 0.2f, 0f), Is.EqualTo(0f));
    }
}
