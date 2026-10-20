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
        bool isPlayerIdConnected = false,
        bool isMatchActive = false,
        bool isInMatchRoster = false,
        bool isSceneTransitionInProgress = false)
    {
        return new DemoJoinRequest(
            isHostSelf, isDemoFlow, isInLobby, isCountdownActive, connectedCount, DemoJoinRule.MaxPlayers,
            isPlayerIdValid, isPlayerIdConnected, isMatchActive, isInMatchRoster, isSceneTransitionInProgress);
    }

    /// <summary>판 진행 중(호스트가 맵) — 명단 안/밖, 전환 여부만 바꾼다.</summary>
    static DemoJoinRequest InMatch(
        bool inRoster,
        bool transition = false,
        bool isPlayerIdConnected = false,
        int connectedCount = 2,
        bool isDemoFlow = true,
        bool isPlayerIdValid = true)
    {
        return Request(isDemoFlow: isDemoFlow, isInLobby: false, connectedCount: connectedCount,
            isPlayerIdValid: isPlayerIdValid, isPlayerIdConnected: isPlayerIdConnected,
            isMatchActive: true, isInMatchRoster: inRoster, isSceneTransitionInProgress: transition);
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

    // ─── 판 도중 재합류(C14) ───────────────────────────────────────────

    [Test]
    public void 판_도중_명단_안_사람은_재합류_승인()
    {
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: true)), Is.EqualTo(DemoJoinRejectReason.None));
    }

    [Test]
    public void 재합류는_인원을_보지_않는다()
    {
        // 명단 ≤ 3 이고 같은 playerId 중복은 따로 막힌다 — 승인 직후 접속 수 집계가 겹쳐도 거절하지 않는다.
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: true, connectedCount: 3)), Is.EqualTo(DemoJoinRejectReason.None));
    }

    [Test]
    public void 판_도중_명단_밖_새_사람은_게임_진행_중()
    {
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: false)), Is.EqualTo(DemoJoinRejectReason.GameInProgress));
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: false, connectedCount: 1)), Is.EqualTo(DemoJoinRejectReason.GameInProgress),
            "자리가 비어도 백필하지 않는다");
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: false, transition: true)), Is.EqualTo(DemoJoinRejectReason.GameInProgress));
    }

    [Test]
    public void 명단_안_사람도_씬_전환_중이면_재합류_대기()
    {
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: true, transition: true)), Is.EqualTo(DemoJoinRejectReason.RejoinWaiting));
    }

    [Test]
    public void 명단_안_사람의_옛_연결이_남았으면_재합류_대기()
    {
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: true, isPlayerIdConnected: true)), Is.EqualTo(DemoJoinRejectReason.RejoinWaiting));
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: true, transition: true, isPlayerIdConnected: true)),
            Is.EqualTo(DemoJoinRejectReason.RejoinWaiting));
    }

    [Test]
    public void 명단_밖_중복은_종전대로_중복_거절()
    {
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: false, isPlayerIdConnected: true)), Is.EqualTo(DemoJoinRejectReason.DuplicatePlayerId));
    }

    [Test]
    public void 판_도중에도_playerId_가_깨졌으면_거절()
    {
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: false, isPlayerIdValid: false)), Is.EqualTo(DemoJoinRejectReason.InvalidPlayerId));
    }

    [Test]
    public void 데모_흐름_밖이면_명단과_무관하게_승인()
    {
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: false, isDemoFlow: false)), Is.EqualTo(DemoJoinRejectReason.None));
        Assert.That(DemoJoinRule.Evaluate(InMatch(inRoster: true, transition: true, isPlayerIdConnected: true, isDemoFlow: false)),
            Is.EqualTo(DemoJoinRejectReason.None));
    }

    [Test]
    public void 명단이_비활성이면_명단_표시가_있어도_종전_규칙()
    {
        // IsInMatchRoster 는 명단이 활성일 때만 뜻이 있다.
        Assert.That(DemoJoinRule.Evaluate(Request(isInLobby: false, isInMatchRoster: true)), Is.EqualTo(DemoJoinRejectReason.GameInProgress));
        Assert.That(DemoJoinRule.Evaluate(Request(isPlayerIdConnected: true, isInMatchRoster: true)), Is.EqualTo(DemoJoinRejectReason.DuplicatePlayerId));
    }

    [Test]
    public void 로비_기존_동작은_그대로()
    {
        // 로비(명단 비활성): 자리 있음 승인, 3명 가득, 카운트다운 = 진행 중, 중복 거절.
        Assert.That(DemoJoinRule.Evaluate(Request(connectedCount: 2)), Is.EqualTo(DemoJoinRejectReason.None));
        Assert.That(DemoJoinRule.Evaluate(Request(connectedCount: 3)), Is.EqualTo(DemoJoinRejectReason.LobbyFull));
        Assert.That(DemoJoinRule.Evaluate(Request(isCountdownActive: true)), Is.EqualTo(DemoJoinRejectReason.GameInProgress));
        Assert.That(DemoJoinRule.Evaluate(Request(isPlayerIdConnected: true)), Is.EqualTo(DemoJoinRejectReason.DuplicatePlayerId));
        // 로비면 명단이 남아 있어도 로비 규칙이 먼저다(가득 = 가득).
        Assert.That(DemoJoinRule.Evaluate(Request(connectedCount: 3, isMatchActive: true, isInMatchRoster: true)),
            Is.EqualTo(DemoJoinRejectReason.LobbyFull));
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
