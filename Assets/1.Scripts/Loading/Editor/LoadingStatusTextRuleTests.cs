using NUnit.Framework;

/// <summary>로딩 상태 문구 바꾸기 규칙 — 275 후속("상태 문구가 너무 자주 바뀐다").</summary>
public sealed class LoadingStatusTextRuleTests
{
    [Test]
    public void 단계_문구는_최소_표시_시간_전엔_안_바뀐다()
    {
        Assert.IsFalse(LoadingStatusTextRule.CanSwitch(NetworkLoadingPhase.LoadingGame, 0.4f, 1f));
        Assert.IsFalse(LoadingStatusTextRule.CanSwitch(NetworkLoadingPhase.WaitingForPlayers, 0.99f, 1f));
        Assert.IsTrue(LoadingStatusTextRule.CanSwitch(NetworkLoadingPhase.WaitingForPlayers, 1f, 1f));
    }

    [Test]
    public void 준비와_완료는_기다리지_않는다()
    {
        Assert.IsTrue(LoadingStatusTextRule.CanSwitch(NetworkLoadingPhase.Ready, 0f, 1f));
        Assert.IsTrue(LoadingStatusTextRule.CanSwitch(NetworkLoadingPhase.Completed, 0f, 1f));
    }

    [Test]
    public void 최소_시간_0이면_늘_바뀐다()
    {
        Assert.IsTrue(LoadingStatusTextRule.CanSwitch(NetworkLoadingPhase.LoadingGame, 0f, 0f));
    }

    [Test]
    public void 준비_문구는_준비_시작부터_첫_칸이고_간격마다_다음_칸()
    {
        Assert.That(LoadingStatusTextRule.ReadyIndex(0f, 1.5f, 3), Is.EqualTo(0));
        Assert.That(LoadingStatusTextRule.ReadyIndex(1.49f, 1.5f, 3), Is.EqualTo(0));
        Assert.That(LoadingStatusTextRule.ReadyIndex(1.5f, 1.5f, 3), Is.EqualTo(1));
        Assert.That(LoadingStatusTextRule.ReadyIndex(4.6f, 1.5f, 3), Is.EqualTo(0), "끝나면 처음으로");
    }

    [Test]
    public void 준비_문구_칸_없음_음수_시간_너무_작은_간격()
    {
        Assert.That(LoadingStatusTextRule.ReadyIndex(5f, 1.5f, 0), Is.EqualTo(0));
        Assert.That(LoadingStatusTextRule.ReadyIndex(-1f, 1.5f, 3), Is.EqualTo(0));
        Assert.That(LoadingStatusTextRule.ReadyIndex(0.25f, 0f, 3), Is.EqualTo(2), "간격 하한 0.1초");
    }
}
