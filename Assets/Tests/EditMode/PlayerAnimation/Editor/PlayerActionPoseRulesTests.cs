using NUnit.Framework;

// 할 일 92 — 행동 종료 때 0층 복귀 포즈(Idle / Run_Loop / 건드리지 않음) 규칙.
public sealed class PlayerActionPoseRulesTests
{
    [Test]
    public void NormalEnd_NotMoving_GoesIdle()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(false, true, false, true), Is.EqualTo(PlayerActionExitPose.Idle));
    }

    [Test]
    public void NormalEnd_Moving_GoesRunLoop_OnlyIfControllerHasIt()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(false, true, true, true), Is.EqualTo(PlayerActionExitPose.RunLoop));
        Assert.That(PlayerActionPoseRules.ResolveExit(false, true, true, false), Is.EqualTo(PlayerActionExitPose.Idle),
            "Run_Loop 이 없는 컨트롤러(팔라딘·거너)는 기존처럼 Idle");
    }

    [Test]
    public void ForcedEnd_NeverPicksRunLoop()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(false, false, true, true), Is.EqualTo(PlayerActionExitPose.Idle),
            "넉백·그랩 강제 종료는 이동 입력이 있어도 Idle");
    }

    [Test]
    public void NextStateWithOwnAnimation_IsLeftAlone()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(true, true, true, true), Is.EqualTo(PlayerActionExitPose.None));
        Assert.That(PlayerActionPoseRules.ResolveExit(true, false, false, false), Is.EqualTo(PlayerActionExitPose.None));
    }

    [TestCase(PlayerActionState.Dead, SkillEndReason.CasterDied)]
    [TestCase(PlayerActionState.Dash, SkillEndReason.DashCancelled)]
    [TestCase(PlayerActionState.Knockback, SkillEndReason.Cancelled)]
    [TestCase(PlayerActionState.Restrained, SkillEndReason.Cancelled)]
    [TestCase(PlayerActionState.Idle, SkillEndReason.Cancelled)]
    public void ForcedExitReason_MapsNextState(PlayerActionState next, SkillEndReason expected)
    {
        Assert.That(PlayerActionPoseRules.ForcedExitReason(next), Is.EqualTo(expected));
    }

    [TestCase(SkillEndReason.Completed, true)]
    [TestCase(SkillEndReason.Released, true)]
    [TestCase(SkillEndReason.MaxDurationReached, true)]
    [TestCase(SkillEndReason.Cancelled, false)]
    [TestCase(SkillEndReason.CasterDied, false)]
    [TestCase(SkillEndReason.DashCancelled, false)]
    public void ReturnsToLocomotion_OnlyNormalEnds(SkillEndReason reason, bool expected)
    {
        Assert.That(PlayerActionPoseRules.ReturnsToLocomotion(reason), Is.EqualTo(expected));
    }

    [Test]
    public void Death_KeepsBaseLayer()
    {
        Assert.That(PlayerActionPoseRules.EndsIntoOwnAnimation(SkillEndReason.CasterDied), Is.True);
        Assert.That(PlayerActionPoseRules.EndsIntoOwnAnimation(SkillEndReason.Cancelled), Is.False);
    }
}
