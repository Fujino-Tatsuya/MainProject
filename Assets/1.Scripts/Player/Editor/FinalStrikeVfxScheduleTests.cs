using NUnit.Framework;

// 할 일 52.1 — 팔라딘 R 마무리 낙뢰(칼)는 클립 이벤트 Custom0 에 시전당 1회 쏘고, 완주 못 하면 걷는다.
public sealed class FinalStrikeVfxScheduleTests
{
    [Test]
    public void FiresOnlyOncePerCast()
    {
        var schedule = new FinalStrikeVfxSchedule();

        Assert.That(schedule.TryFire(), Is.True);
        Assert.That(schedule.Fired, Is.True);
        Assert.That(schedule.TryFire(), Is.False, "같은 시전에 Custom0 이 다시 와도 쏘지 않는다");

        schedule.Reset();
        Assert.That(schedule.Fired, Is.False);
        Assert.That(schedule.TryFire(), Is.True, "다음 시전은 다시 쏜다");
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
