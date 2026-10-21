using NUnit.Framework;

/// <summary>
/// 로비 결과 패널 열고 닫기 판단(할 일 48) — 열기 가능 여부·자동 닫힘 시각·버튼/ESC 닫기·재열기.
/// </summary>
public sealed class LobbyResultPanelRulesTests
{
    [Test]
    public void TryOpen_WithoutResult_DoesNothing()
    {
        var rules = new LobbyResultPanelRules();

        Assert.IsFalse(rules.TryOpen(false, 3f));
        Assert.IsFalse(rules.IsOpen);
        Assert.IsFalse(LobbyResultPanelRules.CanOpen(false));
        Assert.IsTrue(LobbyResultPanelRules.CanOpen(true));
    }

    [Test]
    public void TryOpen_WithResult_OpensAndSetsAutoCloseAt()
    {
        var rules = new LobbyResultPanelRules();

        Assert.IsTrue(rules.TryOpen(true, 10f));
        Assert.IsTrue(rules.IsOpen);
        Assert.AreEqual(180f, LobbyResultPanelRules.DefaultAutoCloseSeconds);
        Assert.AreEqual(190f, rules.AutoCloseAt, 1e-4f);
    }

    [Test]
    public void Tick_ClosesOnlyAtOrAfterAutoCloseAt()
    {
        var rules = new LobbyResultPanelRules(15f);
        rules.TryOpen(true, 100f);

        Assert.IsFalse(rules.Tick(114.99f));
        Assert.IsTrue(rules.IsOpen);
        Assert.IsTrue(rules.Tick(115f));
        Assert.IsFalse(rules.IsOpen);
        Assert.IsFalse(rules.Tick(200f), "이미 닫힌 뒤에는 다시 닫힘을 내지 않는다");
    }

    [Test]
    public void Reopen_RestartsTimer()
    {
        var rules = new LobbyResultPanelRules(15f);
        rules.TryOpen(true, 0f);
        Assert.IsTrue(rules.Tick(15f));

        Assert.IsTrue(rules.TryOpen(true, 40f), "결과가 남아 있으면 닫은 뒤 다시 연다");
        Assert.AreEqual(55f, rules.AutoCloseAt, 1e-4f);
        Assert.IsFalse(rules.Tick(54f));
        Assert.IsTrue(rules.Tick(55f));
    }

    [Test]
    public void TryOpen_WhileOpen_RestartsTimer()
    {
        var rules = new LobbyResultPanelRules(15f);
        rules.TryOpen(true, 0f);
        rules.TryOpen(true, 10f);

        Assert.IsTrue(rules.IsOpen);
        Assert.IsFalse(rules.Tick(15f));
        Assert.IsTrue(rules.Tick(25f));
    }

    [Test]
    public void CloseButton_And_Escape_CloseOnlyWhenOpen()
    {
        var rules = new LobbyResultPanelRules();

        Assert.IsFalse(rules.HandleEscape(), "닫혀 있을 때 ESC 는 패널이 쓰지 않는다");
        Assert.IsFalse(rules.HandleCloseButton());

        rules.TryOpen(true, 0f);
        Assert.IsTrue(rules.HandleEscape());
        Assert.IsFalse(rules.IsOpen);

        rules.TryOpen(true, 1f);
        Assert.IsTrue(rules.HandleCloseButton());
        Assert.IsFalse(rules.IsOpen);
        Assert.IsFalse(rules.Close(), "두 번 닫아도 한 번만 true");
    }

    [Test]
    public void ZeroAutoClose_NeverClosesByTime()
    {
        var rules = new LobbyResultPanelRules(0f);
        rules.TryOpen(true, 0f);

        Assert.IsFalse(rules.AutoCloseEnabled);
        Assert.IsTrue(float.IsPositiveInfinity(rules.AutoCloseAt));
        Assert.IsFalse(rules.Tick(1e6f));
        Assert.IsTrue(rules.IsOpen);
    }
}
