#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 88 Q4 — 무인 QA 타이틀 START 대신 호출의 1회 가드.</summary>
public sealed class QATitleStartGateTests
{
    private const string Title = QATitleStartGate.TitleScene;

    [Test]
    public void 타이틀이_아니면_부르지_않는다()
    {
        var gate = new QATitleStartGate();
        Assert.IsFalse(gate.Observe("0.BootStrapScene", 1));
        Assert.IsFalse(gate.Observe("6.DemoConnectScene", 2));
        Assert.IsFalse(gate.Fired);
    }

    [Test]
    public void 타이틀이_보인_첫_프레임은_건너뛰고_다음_프레임에_부른다()
    {
        var gate = new QATitleStartGate();
        Assert.IsFalse(gate.Observe("0.BootStrapScene", 1));
        Assert.IsFalse(gate.Observe(Title, 2), "타이틀 첫 프레임");
        Assert.IsFalse(gate.Observe(Title, 2), "같은 프레임 재호출");
        Assert.IsTrue(gate.Observe(Title, 3), "다음 프레임");
        Assert.IsTrue(gate.Fired);
    }

    [Test]
    public void 한_번_부른_뒤에는_다시_부르지_않는다()
    {
        var gate = new QATitleStartGate();
        gate.Observe(Title, 1);
        Assert.IsTrue(gate.Observe(Title, 2));
        Assert.IsFalse(gate.Observe(Title, 3));
        Assert.IsFalse(gate.Observe("3.LobbyScene", 4));
        Assert.IsFalse(gate.Observe(Title, 5));
        Assert.IsFalse(gate.Observe(Title, 6), "타이틀로 돌아와도 1회뿐");
    }

    [Test]
    public void 타이틀을_벗어나면_대기_프레임을_다시_센다()
    {
        var gate = new QATitleStartGate();
        Assert.IsFalse(gate.Observe(Title, 1));
        Assert.IsFalse(gate.Observe("0.BootStrapScene", 2));
        Assert.IsFalse(gate.Observe(Title, 3), "다시 첫 프레임");
        Assert.IsTrue(gate.Observe(Title, 4));
    }
}
#endif
