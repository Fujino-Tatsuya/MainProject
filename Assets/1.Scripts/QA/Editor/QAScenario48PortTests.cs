#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 88 스모크 2차 — 루트가 사람 설정 파일이면 클론은 호스트가 실제로 듣는 포트로 붙어야 한다.</summary>
public sealed class QAScenario48PortTests
{
    [Test]
    public void 추적되는_부스_기본값이면_그_포트를_쓴다()
    {
        const string booth = "{\n  \"role\": \"host\",\n  \"port\": 7777,\n  \"playerId\": \"booth-1\"\n}\n";
        Assert.AreEqual(7777, QAScenario48Menu.HumanRootPort(booth));
    }

    [Test]
    public void 사람이_바꾼_포트도_그대로_따른다()
    {
        Assert.AreEqual(8123, QAScenario48Menu.HumanRootPort("{ \"role\": \"host\", \"port\": 8123 }"));
    }

    [Test]
    public void 포트가_없거나_비었거나_깨지면_호스트와_같은_기본_포트()
    {
        Assert.AreEqual(DemoConnectionConfig.DefaultPort, QAScenario48Menu.HumanRootPort("{ \"role\": \"host\" }"));
        Assert.AreEqual(DemoConnectionConfig.DefaultPort, QAScenario48Menu.HumanRootPort(""));
        Assert.AreEqual(DemoConnectionConfig.DefaultPort, QAScenario48Menu.HumanRootPort("{ \"port\": 0 }"));
    }
}
#endif
