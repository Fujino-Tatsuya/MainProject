using NUnit.Framework;

/// <summary>
/// 클라 로딩 상태 메시지 flowId 규칙 — 같은 세션의 지난 흐름은 버리고, 새 세션(StartClient)에서 번호를 되돌리면
/// 다시 켜진 호스트(flowId 1부터)의 상태를 받는다(C14 R2c, 메모 §1.3 부수 발견).
/// </summary>
public sealed class LoadingStateFlowRuleTests
{
    [Test]
    public void 같은_세션의_지난_흐름은_버린다()
    {
        Assert.IsTrue(LoadingStateFlowRule.IsStale(1, 2));
    }

    [Test]
    public void 같은_흐름과_새_흐름은_받는다()
    {
        Assert.IsFalse(LoadingStateFlowRule.IsStale(2, 2), "같은 흐름의 다음 단계(Completed 포함)");
        Assert.IsFalse(LoadingStateFlowRule.IsStale(3, 2), "판 재시작 = 새 흐름");
    }

    [Test]
    public void 되돌리지_않으면_다시_켜진_호스트의_첫_흐름을_버린다()
    {
        // 앞 세션에서 판 3번(flowId 3)을 본 클라, 호스트 exe 재시작 → 새 호스트 첫 흐름 flowId 1.
        uint clientFlowId = 3;
        Assert.IsTrue(LoadingStateFlowRule.IsStale(1, clientFlowId), "버그 재현: 리셋 없으면 LoadingScene·Completed 를 전부 버린다");
    }

    [Test]
    public void 새_세션에서_되돌리면_새_호스트의_첫_흐름을_받는다()
    {
        uint clientFlowId = 3;
        clientFlowId = LoadingStateFlowRule.SessionStartFlowId; // StartClient → ResetClientFlowForNewSession
        Assert.IsFalse(LoadingStateFlowRule.IsStale(1, clientFlowId));
        Assert.That(LoadingStateFlowRule.SessionStartFlowId, Is.LessThan(1u), "서버 첫 흐름(1)보다 작아야 한다");
    }
}
