using NUnit.Framework;

/// <summary>
/// 소스 씬 언로드 분기 — 없음/로딩·타깃과 같음은 None, 로컬 소스는 서버·클라 모두 Local,
/// NGO 소스는 서버만 Network 이고 클라는 None(NGO 동기화가 내린다).
/// </summary>
public sealed class SourceSceneUnloadRuleTests
{
    const string Loading = "LoadingScene";
    const string Target = "4.MapScene";
    const string Lobby = "3.LobbyScene";

    static SourceSceneUnloadMode Decide(string source, bool network, bool server) =>
        SourceSceneUnloadRule.Decide(source, Loading, Target, network, server);

    [TestCase(null)]
    [TestCase("")]
    public void 소스_없음은_None(string source)
    {
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(source, false, true));
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(source, true, true));
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(source, false, false));
    }

    [TestCase(Loading)]
    [TestCase(Target)]
    public void 소스가_로딩_또는_타깃이면_None(string source)
    {
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(source, false, true));
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(source, true, true));
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(source, false, false));
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(source, true, false));
    }

    [Test]
    public void 로컬_소스는_서버_클라_모두_Local()
    {
        Assert.AreEqual(SourceSceneUnloadMode.Local, Decide(Lobby, false, true));
        Assert.AreEqual(SourceSceneUnloadMode.Local, Decide(Lobby, false, false));
    }

    [Test]
    public void NGO_소스는_서버만_Network()
    {
        Assert.AreEqual(SourceSceneUnloadMode.Network, Decide(Lobby, true, true));
    }

    [Test]
    public void NGO_소스의_클라는_로컬_언로드하지_않는다()
    {
        Assert.AreEqual(SourceSceneUnloadMode.None, Decide(Lobby, true, false));
    }
}
