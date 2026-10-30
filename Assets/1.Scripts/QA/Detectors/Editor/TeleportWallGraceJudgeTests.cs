#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 187 — 큰 순간이동(15m 초과) 뒤 0.5초는 P8 벽 겹침을 봐준다.</summary>
public sealed class TeleportWallGraceJudgeTests
{
    private const float TeleportDistance = 15f;

    [Test]
    public void 순간이동_0_3초_뒤면_억제한다()
    {
        var judge = new TeleportWallGraceJudge(TeleportDistance);
        judge.NotifyMove(24.0, 116f);
        Assert.IsTrue(judge.IsSuppressed(24.0));
        Assert.IsTrue(judge.IsSuppressed(24.3));
    }

    [Test]
    public void 순간이동_0_6초_뒤면_보고한다()
    {
        var judge = new TeleportWallGraceJudge(TeleportDistance);
        judge.NotifyMove(24.0, 116f);
        Assert.IsFalse(judge.IsSuppressed(24.6));
    }

    [Test]
    public void 순간이동이_없으면_보고한다()
    {
        var judge = new TeleportWallGraceJudge(TeleportDistance);
        judge.NotifyMove(24.0, 0.2f);
        judge.NotifyMove(24.1, TeleportDistance);
        Assert.IsFalse(judge.IsSuppressed(24.0));
        Assert.IsFalse(judge.IsSuppressed(24.1));
    }

    [Test]
    public void 다시_순간이동하면_유예가_갱신된다()
    {
        var judge = new TeleportWallGraceJudge(TeleportDistance);
        judge.NotifyMove(24.0, 116f);
        judge.NotifyMove(24.4, 30f);
        Assert.IsTrue(judge.IsSuppressed(24.8));
        Assert.IsFalse(judge.IsSuppressed(25.0));
    }

    [Test]
    public void Reset_하면_유예가_사라진다()
    {
        var judge = new TeleportWallGraceJudge(TeleportDistance);
        judge.NotifyMove(24.0, 116f);
        judge.Reset();
        Assert.IsFalse(judge.IsSuppressed(24.1));
    }
}
#endif
