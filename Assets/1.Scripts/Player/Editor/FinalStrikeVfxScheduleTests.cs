using NUnit.Framework;

// 할 일 52.1 후속 — 팔라딘 R 마무리 낙뢰(칼)를 피해 시각보다 lead 초 앞서 쏘고, 완주 못 하면 걷는다.
public sealed class FinalStrikeVfxScheduleTests
{
    private const float Channel = 1.5f;

    [Test]
    public void ZeroLeadFiresAtChannelEnd()
    {
        var schedule = new FinalStrikeVfxSchedule();

        Assert.That(schedule.TryFire(1.49f, Channel, 0f), Is.False);
        Assert.That(schedule.TryFire(1.5f, Channel, 0f), Is.True, "lead 0 = 예전처럼 완주 시각");
    }

    [Test]
    public void HalfSecondLeadFiresHalfSecondEarly()
    {
        var schedule = new FinalStrikeVfxSchedule();

        Assert.That(FinalStrikeVfxSchedule.FireTime(0.5f, Channel), Is.EqualTo(1.0f).Within(1e-5f));
        Assert.That(schedule.TryFire(0.99f, Channel, 0.5f), Is.False);
        Assert.That(schedule.TryFire(1.0f, Channel, 0.5f), Is.True);
    }

    [Test]
    public void LeadIsClampedToChannel()
    {
        Assert.That(FinalStrikeVfxSchedule.ClampLead(3f, Channel), Is.EqualTo(Channel));
        Assert.That(FinalStrikeVfxSchedule.ClampLead(-1f, Channel), Is.EqualTo(0f));
        Assert.That(FinalStrikeVfxSchedule.FireTime(3f, Channel), Is.EqualTo(0f), "채널보다 길면 시작 순간");

        var schedule = new FinalStrikeVfxSchedule();
        Assert.That(schedule.TryFire(0f, Channel, 3f), Is.True);
    }

    [Test]
    public void FiresOnlyOncePerCast()
    {
        var schedule = new FinalStrikeVfxSchedule();

        Assert.That(schedule.TryFire(1.0f, Channel, 0.5f), Is.True);
        Assert.That(schedule.TryFire(1.2f, Channel, 0.5f), Is.False);
        Assert.That(schedule.TryFire(1.5f, Channel, 0.5f), Is.False, "완주 틱에 다시 쏘지 않는다");

        schedule.Reset();
        Assert.That(schedule.TryFire(1.0f, Channel, 0.5f), Is.True, "다음 시전은 다시 쏜다");
    }

    [Test]
    public void CompletedKeepsStrike()
    {
        Assert.That(FinalStrikeVfxSchedule.ShouldRecall(SkillEndReason.Completed), Is.False);
    }

    [TestCase(SkillEndReason.Cancelled)]
    [TestCase(SkillEndReason.CasterDied)]
    [TestCase(SkillEndReason.MaxDurationReached)]
    [TestCase(SkillEndReason.Released)]
    [TestCase(SkillEndReason.DashCancelled)]
    public void NonCompletedRecallsStrike(SkillEndReason reason)
    {
        Assert.That(FinalStrikeVfxSchedule.ShouldRecall(reason), Is.True);
    }
}
