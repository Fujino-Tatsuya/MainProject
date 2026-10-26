using NUnit.Framework;

/// <summary>설정창·ESC 메뉴 규칙(PLAN-settings-ddol) — 씬 모드, ESC 우선순위, 그만두기 동작.</summary>
public sealed class GameMenuRulesTests
{
    [TestCase("1.TitleScene", GameMenuMode.Title)]
    [TestCase("3.LobbyScene", GameMenuMode.Lobby)]
    [TestCase("4.MapScene", GameMenuMode.InGame)]
    [TestCase("2.LoadingScene", GameMenuMode.None)]
    [TestCase("6.DemoConnectScene", GameMenuMode.None)]
    [TestCase("0.BootStrapScene", GameMenuMode.None)]
    [TestCase("", GameMenuMode.None)]
    public void 씬_이름으로_모드를_정한다(string scene, GameMenuMode expected)
    {
        Assert.AreEqual(expected, GameMenuRules.ResolveMode(scene));
    }

    [Test]
    public void 닫혀_있으면_ESC_로_연다()
    {
        Assert.AreEqual(GameMenuEscape.Open, GameMenuRules.ResolveEscape(GameMenuMode.Lobby, GameMenuScreen.Closed, false, false));
        Assert.AreEqual(GameMenuEscape.Open, GameMenuRules.ResolveEscape(GameMenuMode.InGame, GameMenuScreen.Closed, false, false));
    }

    [Test]
    public void 다른_창이_ESC_를_썼으면_열지_않는다()
    {
        // 로비 결과·캐릭터 선택이 열려 있거나 이 프레임 닫혔다 / 스킬 조준을 ESC 로 취소했다.
        Assert.AreEqual(GameMenuEscape.Ignore, GameMenuRules.ResolveEscape(GameMenuMode.Lobby, GameMenuScreen.Closed, true, false));
        Assert.AreEqual(GameMenuEscape.Ignore, GameMenuRules.ResolveEscape(GameMenuMode.InGame, GameMenuScreen.Closed, true, false));
    }

    [Test]
    public void 열려_있으면_한_단계_뒤로()
    {
        Assert.AreEqual(GameMenuEscape.BackToMenu, GameMenuRules.ResolveEscape(GameMenuMode.InGame, GameMenuScreen.Settings, false, false));
        Assert.AreEqual(GameMenuEscape.BackToMenu, GameMenuRules.ResolveEscape(GameMenuMode.Lobby, GameMenuScreen.Confirm, false, false));
        Assert.AreEqual(GameMenuEscape.Close, GameMenuRules.ResolveEscape(GameMenuMode.InGame, GameMenuScreen.Menu, false, false));
    }

    [Test]
    public void 열려_있을_땐_다른_창의_소비_표시와_무관하게_뒤로()
    {
        Assert.AreEqual(GameMenuEscape.Close, GameMenuRules.ResolveEscape(GameMenuMode.Lobby, GameMenuScreen.Menu, true, false));
    }

    [Test]
    public void 키_변경_중_ESC_는_변경_취소만()
    {
        Assert.AreEqual(GameMenuEscape.Ignore, GameMenuRules.ResolveEscape(GameMenuMode.InGame, GameMenuScreen.Settings, false, true));
    }

    [Test]
    public void 드롭다운이_펼쳐져_있으면_ESC_는_목록만_닫는다()
    {
        Assert.AreEqual(GameMenuEscape.Ignore, GameMenuRules.ResolveEscape(GameMenuMode.InGame, GameMenuScreen.Settings, false, false, dropdownOpen: true));
        Assert.AreEqual(GameMenuEscape.BackToMenu, GameMenuRules.ResolveEscape(GameMenuMode.InGame, GameMenuScreen.Settings, false, false, dropdownOpen: false));
    }

    [Test]
    public void 타이틀과_로딩은_ESC_메뉴가_없다()
    {
        // 타이틀 ESC 는 TitleFlowDirector 가 다룬다(연출 잠금 계약).
        Assert.AreEqual(GameMenuEscape.Ignore, GameMenuRules.ResolveEscape(GameMenuMode.Title, GameMenuScreen.Closed, false, false));
        Assert.AreEqual(GameMenuEscape.Ignore, GameMenuRules.ResolveEscape(GameMenuMode.Title, GameMenuScreen.Settings, false, false));
        Assert.AreEqual(GameMenuEscape.Ignore, GameMenuRules.ResolveEscape(GameMenuMode.None, GameMenuScreen.Closed, false, false));
        Assert.IsFalse(GameMenuRules.HasPauseMenu(GameMenuMode.Title));
        Assert.IsFalse(GameMenuRules.HasPauseMenu(GameMenuMode.None));
        Assert.IsTrue(GameMenuRules.HasPauseMenu(GameMenuMode.Lobby));
        Assert.IsTrue(GameMenuRules.HasPauseMenu(GameMenuMode.InGame));
    }

    [Test]
    public void 그만두기_문구_클라는_기본_호스트는_경고()
    {
        Assert.AreEqual(GameMenuExitMessage.Default, GameMenuRules.ResolveExitMessage(GameMenuMode.Lobby, isHost: false));
        Assert.AreEqual(GameMenuExitMessage.Default, GameMenuRules.ResolveExitMessage(GameMenuMode.InGame, isHost: false));
        Assert.AreEqual(GameMenuExitMessage.HostDisconnectsOthers, GameMenuRules.ResolveExitMessage(GameMenuMode.Lobby, isHost: true));
        Assert.AreEqual(GameMenuExitMessage.HostEndsOthersGame, GameMenuRules.ResolveExitMessage(GameMenuMode.InGame, isHost: true));
    }
}
