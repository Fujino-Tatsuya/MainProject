using NUnit.Framework;

// 할 일 92 — 행동 종료 때 0층 복귀 포즈(Idle / Run_Loop / 건드리지 않음) 규칙.
public sealed class PlayerActionPoseRulesTests
{
    [Test]
    public void NormalEnd_NotMoving_GoesIdle()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(false, false, true, false, true), Is.EqualTo(PlayerActionExitPose.Idle));
    }

    [Test]
    public void NormalEnd_Moving_GoesRunLoop_OnlyIfControllerHasIt()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(false, false, true, true, true), Is.EqualTo(PlayerActionExitPose.RunLoop));
        Assert.That(PlayerActionPoseRules.ResolveExit(false, false, true, true, false), Is.EqualTo(PlayerActionExitPose.Idle),
            "Run_Loop 이 없는 컨트롤러(팔라딘·거너)는 기존처럼 Idle");
    }

    [Test]
    public void ForcedEnd_NeverPicksRunLoop()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(false, false, false, true, true), Is.EqualTo(PlayerActionExitPose.Idle),
            "넉백·그랩 강제 종료는 이동 입력이 있어도 Idle");
    }

    [Test]
    public void NextStateWithOwnAnimation_IsLeftAlone()
    {
        Assert.That(PlayerActionPoseRules.ResolveExit(false, true, true, true, true), Is.EqualTo(PlayerActionExitPose.None));
        Assert.That(PlayerActionPoseRules.ResolveExit(false, true, false, false, false), Is.EqualTo(PlayerActionExitPose.None));
        Assert.That(PlayerActionPoseRules.ResolveExit(true, true, true, true, true), Is.EqualTo(PlayerActionExitPose.None));
    }

    [Test]
    public void UpperBodyOnly_NeverCrossFadesBaseLayer()
    {
        // 정상 종료 + 이동 중 → IsMoving 만 되살린다(하체 Run_Loop 유지)
        Assert.That(PlayerActionPoseRules.ResolveExit(true, false, true, true, true), Is.EqualTo(PlayerActionExitPose.KeepMoving));
        // 정상 종료 + 정지 / 강제 종료(넉백·대시) → 0층 그대로
        Assert.That(PlayerActionPoseRules.ResolveExit(true, false, true, false, true), Is.EqualTo(PlayerActionExitPose.None));
        Assert.That(PlayerActionPoseRules.ResolveExit(true, false, false, true, true), Is.EqualTo(PlayerActionExitPose.None));
    }

    [TestCase(false, true, true)]   // 상체 레이어에만 — E Buff
    [TestCase(true, false, false)]  // 0층에만 — 일반 스킬
    [TestCase(true, true, false)]   // 양쪽 — 0층 스킬로 본다
    [TestCase(false, false, false)] // 어디에도 없음
    public void UpperBodyOnly_Detection(bool inBase, bool inOther, bool expected)
    {
        Assert.That(PlayerActionPoseRules.IsUpperBodyOnly(inBase, inOther), Is.EqualTo(expected));
    }

    [Test]
    public void SkillStart_KeepsMovingOnlyForUpperBodyMovableSkill()
    {
        Assert.That(PlayerActionPoseRules.MovingOnSkillStart(true, true, true), Is.True, "E Buff 를 걸으며 시작");
        Assert.That(PlayerActionPoseRules.MovingOnSkillStart(true, true, false), Is.False);
        Assert.That(PlayerActionPoseRules.MovingOnSkillStart(true, false, true), Is.False, "이동 불가 스킬");
        Assert.That(PlayerActionPoseRules.MovingOnSkillStart(false, true, true), Is.False, "0층 스킬은 기존처럼 끈다");
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
        Assert.That(PlayerActionPoseRules.EndsIntoOwnAnimation(SkillEndReason.CasterDied, false), Is.True);
        Assert.That(PlayerActionPoseRules.EndsIntoOwnAnimation(SkillEndReason.Cancelled, true), Is.False);
    }

    [Test]
    public void DashCancel_LeavesDodge_OnlyWhenControllerHasIt()
    {
        Assert.That(PlayerActionPoseRules.EndsIntoOwnAnimation(SkillEndReason.DashCancelled, true), Is.True,
            "어쌔신 — 스킬 중 대시는 Dodge 가 최종으로 남는다");
        Assert.That(PlayerActionPoseRules.EndsIntoOwnAnimation(SkillEndReason.DashCancelled, false), Is.False,
            "팔라딘·거너 — Dodge 가 없으니 Idle 로 되돌린다");
    }

    [TestCase(PlayerActionState.Move, true, PlayerActionExitPose.RunLoop)]
    [TestCase(PlayerActionState.Move, false, PlayerActionExitPose.Idle)]
    [TestCase(PlayerActionState.Idle, true, PlayerActionExitPose.Idle)]
    [TestCase(PlayerActionState.Knockback, true, PlayerActionExitPose.Idle)]
    [TestCase(PlayerActionState.Restrained, true, PlayerActionExitPose.Idle)]
    [TestCase(PlayerActionState.Dash, true, PlayerActionExitPose.None)]
    [TestCase(PlayerActionState.Dead, true, PlayerActionExitPose.None)]
    public void DashExit_PicksPoseByNextState(PlayerActionState next, bool hasRunLoop, PlayerActionExitPose expected)
    {
        Assert.That(PlayerActionPoseRules.ResolveDashExit(next, hasRunLoop), Is.EqualTo(expected));
    }

    [TestCase(true, false, PlayerActionState.Move)]
    [TestCase(false, false, PlayerActionState.Idle)]
    [TestCase(true, true, PlayerActionState.Idle)]
    public void DashEndState_GoesStraightToMoveWhileMoving(bool hasMoveInput, bool blocked, PlayerActionState expected)
    {
        Assert.That(PlayerActionPoseRules.DashEndState(hasMoveInput, blocked), Is.EqualTo(expected));
    }
}
