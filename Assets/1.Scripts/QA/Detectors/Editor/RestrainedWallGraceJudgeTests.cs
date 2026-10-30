#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 185 — 구속 중·풀린 뒤 0.5초는 벽 겹침(P8·QA103 벽 재배치)을 봐준다.</summary>
public sealed class RestrainedWallGraceJudgeTests
{
    [Test]
    public void 구속_중이면_억제한다()
    {
        var judge = new RestrainedWallGraceJudge();
        Assert.IsTrue(judge.Step(10.0, PlayerActionState.Restrained));
        Assert.IsTrue(judge.Step(11.0, PlayerActionState.Restrained));
    }

    [Test]
    public void 풀린_직후_0_3초는_억제한다()
    {
        var judge = new RestrainedWallGraceJudge();
        judge.Step(10.0, PlayerActionState.Restrained);
        Assert.IsTrue(judge.Step(10.3, PlayerActionState.Idle));
    }

    [Test]
    public void 풀린_뒤_0_6초면_억제하지_않는다()
    {
        var judge = new RestrainedWallGraceJudge();
        judge.Step(10.0, PlayerActionState.Restrained);
        Assert.IsFalse(judge.Step(10.6, PlayerActionState.Idle));
    }

    [Test]
    public void 처음부터_구속이_아니면_억제하지_않는다()
    {
        var judge = new RestrainedWallGraceJudge();
        Assert.IsFalse(judge.Step(0.0, PlayerActionState.Idle));
        Assert.IsFalse(judge.Step(0.1, PlayerActionState.Move));
    }

    [Test]
    public void 다시_구속되면_유예가_갱신된다()
    {
        var judge = new RestrainedWallGraceJudge();
        judge.Step(10.0, PlayerActionState.Restrained);
        judge.Step(10.2, PlayerActionState.Idle);
        Assert.IsTrue(judge.Step(10.4, PlayerActionState.Restrained));
        Assert.IsTrue(judge.Step(10.8, PlayerActionState.Idle));
        Assert.IsFalse(judge.Step(11.0, PlayerActionState.Idle));
    }

    [Test]
    public void Reset_하면_유예가_사라진다()
    {
        var judge = new RestrainedWallGraceJudge();
        judge.Step(10.0, PlayerActionState.Restrained);
        judge.Reset();
        Assert.IsFalse(judge.Step(10.1, PlayerActionState.Idle));
    }
}
#endif
