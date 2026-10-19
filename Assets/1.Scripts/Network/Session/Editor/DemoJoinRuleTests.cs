using System;
using NUnit.Framework;

/// <summary>
/// 부스 데모 접속 승인 — 로비·진행 중·카운트다운·인원 3·호스트·playerId, 거절 Reason 문자열 왕복.
/// </summary>
public sealed class DemoJoinRuleTests
{
    static DemoJoinRequest Request(
        bool isHostSelf = false,
        bool isDemoFlow = true,
        bool isInLobby = true,
        bool isCountdownActive = false,
        int connectedCount = 1,
        bool isPlayerIdValid = true,
        bool isPlayerIdConnected = false)
    {
        return new DemoJoinRequest(
            isHostSelf, isDemoFlow, isInLobby, isCountdownActive, connectedCount, DemoJoinRule.MaxPlayers,
            isPlayerIdValid, isPlayerIdConnected);
    }

    // ─── Evaluate ──────────────────────────────────────────────────────

    [Test]
    public void 로비이고_자리가_있으면_승인()
    {
        Assert.That(DemoJoinRule.Evaluate(Request(connectedCount: 1)), Is.EqualTo(DemoJoinRejectReason.None));
        Assert.That(DemoJoinRule.Evaluate(Request(connectedCount: 2)), Is.EqualTo(DemoJoinRejectReason.None));
    }

    [Test]
    public void 로비가_아니면_게임_진행_중()
    {
        Assert.That(DemoJoinRule.Evaluate(Request(isInLobby: false)), Is.EqualTo(DemoJoinRejectReason.GameInProgress));
    }

    [Test]
    public void 카운트다운_중이면_게임_진행_중()
    {
        Assert.That(DemoJoinRule.Evaluate(Request(isCountdownActive: true)), Is.EqualTo(DemoJoinRejectReason.GameInProgress));
    }

    [Test]
    public void 세_명이_차면_가득()
    {
        Assert.That(DemoJoinRule.Evaluate(Request(connectedCount: 3)), Is.EqualTo(DemoJoinRejectReason.LobbyFull));
        Assert.That(DemoJoinRule.Evaluate(Request(connectedCount: 4)), Is.EqualTo(DemoJoinRejectReason.LobbyFull));
    }

    [Test]
    public void 호스트_자신은_어떤_상태든_승인()
    {
        var request = Request(isHostSelf: true, isInLobby: false, isCountdownActive: true, connectedCount: 3,
            isPlayerIdValid: false, isPlayerIdConnected: true);
        Assert.That(DemoJoinRule.Evaluate(request), Is.EqualTo(DemoJoinRejectReason.None));
    }

    [Test]
    public void 데모_흐름이_아니면_종전처럼_항상_승인()
    {
        var request = Request(isDemoFlow: false, isInLobby: false, isCountdownActive: true, connectedCount: 5,
            isPlayerIdValid: false, isPlayerIdConnected: true);
        Assert.That(DemoJoinRule.Evaluate(request), Is.EqualTo(DemoJoinRejectReason.None));
    }

    [Test]
    public void 같은_playerId_가_접속_중이면_거절()
    {
        Assert.That(DemoJoinRule.Evaluate(Request(isPlayerIdConnected: true)), Is.EqualTo(DemoJoinRejectReason.DuplicatePlayerId));
    }

    [Test]
    public void playerId_가_없거나_깨졌으면_거절()
    {
        Assert.That(DemoJoinRule.Evaluate(Request(isPlayerIdValid: false)), Is.EqualTo(DemoJoinRejectReason.InvalidPlayerId));
    }

    // ─── Reason 문자열 ─────────────────────────────────────────────────

    [Test]
    public void 거절_사유는_문자열로_왕복한다()
    {
        foreach (DemoJoinRejectReason reason in Enum.GetValues(typeof(DemoJoinRejectReason)))
        {
            if (reason == DemoJoinRejectReason.None)
                continue;

            var text = DemoJoinRule.ToApprovalReason(reason);
            Assert.That(text, Does.StartWith(DemoJoinRule.ReasonPrefix));
            Assert.That(DemoJoinRule.TryParseApprovalReason(text, out var parsed), Is.True, text);
            Assert.That(parsed, Is.EqualTo(reason));
        }
    }

    [Test]
    public void 승인이면_Reason_은_빈_문자열()
    {
        Assert.That(DemoJoinRule.ToApprovalReason(DemoJoinRejectReason.None), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Client-1 disconnected by server.")]
    [TestCase("GameInProgress")]
    [TestCase("demo-join:")]
    [TestCase("demo-join:None")]
    [TestCase("demo-join:2")]
    [TestCase("demo-join:gameinprogress")]
    [TestCase("demo-join:NoSuchReason")]
    public void 우리_코드가_아니면_해석하지_않는다(string text)
    {
        Assert.That(DemoJoinRule.TryParseApprovalReason(text, out var parsed), Is.False);
        Assert.That(parsed, Is.EqualTo(DemoJoinRejectReason.None));
    }
}
