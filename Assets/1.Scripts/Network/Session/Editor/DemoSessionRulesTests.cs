using NUnit.Framework;

/// <summary>
/// DemoSessionDirector 의 순수 판단 — 켜지는 조건(부트스트랩 메인 흐름·토글·MPPM 태그), 끊긴 사유, 전송 타임아웃 분할.
/// </summary>
public sealed class DemoSessionRulesTests
{
    // ─── 게이트 ────────────────────────────────────────────────────────

    [Test]
    public void 부트스트랩에서_시작한_메인_에디터는_켜진다()
    {
        Assert.That(DemoSessionRules.ShouldRunDemoFlow(true, "0.BootStrapScene", isVirtualPlayer: false, hasClientTag: false), Is.True);
    }

    [Test]
    public void 토글을_끄면_꺼진다()
    {
        Assert.That(DemoSessionRules.ShouldRunDemoFlow(false, "0.BootStrapScene", false, false), Is.False);
    }

    [TestCase("Dev_Boot")]
    [TestCase("PlayerDashTest")]
    [TestCase("4.MapScene")]
    [TestCase("")]
    [TestCase(null)]
    public void 부트스트랩이_아닌_씬에서_시작하면_꺼진다(string activeScene)
    {
        Assert.That(DemoSessionRules.ShouldRunDemoFlow(true, activeScene, false, false), Is.False);
    }

    [Test]
    public void 가상_플레이어는_Client_태그가_있을_때만_켜진다()
    {
        Assert.That(DemoSessionRules.ShouldRunDemoFlow(true, "0.BootStrapScene", isVirtualPlayer: true, hasClientTag: false), Is.False);
        Assert.That(DemoSessionRules.ShouldRunDemoFlow(true, "0.BootStrapScene", isVirtualPlayer: true, hasClientTag: true), Is.True);
    }

    // ─── 끊긴 사유 ─────────────────────────────────────────────────────

    [Test]
    public void 접속_중_사유_없이_멈추면_응답_없음()
    {
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connecting, null), Is.EqualTo(DemoJoinRejectReason.Timeout));
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connecting, "  "), Is.EqualTo(DemoJoinRejectReason.Timeout));
    }

    [Test]
    public void 거절_문구가_있거나_접속_후_끊기면_Unknown()
    {
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connecting, "Client-1 disconnected by server."),
            Is.EqualTo(DemoJoinRejectReason.Unknown));
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connected, null), Is.EqualTo(DemoJoinRejectReason.Unknown));
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Hosting, null), Is.EqualTo(DemoJoinRejectReason.Unknown));
    }

    [TestCase(DemoJoinRejectReason.GameInProgress)]
    [TestCase(DemoJoinRejectReason.LobbyFull)]
    [TestCase(DemoJoinRejectReason.DuplicatePlayerId)]
    [TestCase(DemoJoinRejectReason.InvalidPlayerId)]
    [TestCase(DemoJoinRejectReason.RejoinWaiting)]
    public void 서버_거절_코드는_그_사유로_바꾼다(DemoJoinRejectReason reason)
    {
        var text = DemoJoinRule.ToApprovalReason(reason);
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connecting, text), Is.EqualTo(reason));
    }

    [Test]
    public void 전송_계층이_연결을_못_맺으면_응답_없음()
    {
        const string maxAttempts = "[Disconnect Event][Client-0][TransportClientId-0][MaxConnectionAttempts] Connection closed due to maximum connection attempts reached.";
        const string protocolTimeout = "[Disconnect Event][Client-0][TransportClientId-0][ProtocolTimeout] Connection closed due to timed out.";
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connecting, maxAttempts), Is.EqualTo(DemoJoinRejectReason.Timeout));
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connecting, protocolTimeout), Is.EqualTo(DemoJoinRejectReason.Timeout));
    }

    [Test]
    public void 접속_중_서버가_사유_없이_끊으면_버전_불일치()
    {
        const string closedByRemote = "[Disconnect Event][Client-0][TransportClientId-0][ClosedByRemote] Connection was closed by remote endpoint.";
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connecting, closedByRemote), Is.EqualTo(DemoJoinRejectReason.HashMismatch));
        Assert.That(DemoSessionRules.ResolveStopReason(ReconnectState.Connected, closedByRemote), Is.EqualTo(DemoJoinRejectReason.Unknown),
            "접속한 뒤 끊긴 건 해시와 무관하다");
    }

    // ─── 전송 타임아웃 ─────────────────────────────────────────────────

    [Test]
    public void 정수_초는_1초씩_나눈다()
    {
        var t = DemoSessionRules.ComputeTransportTimeouts(5f, 10f);
        Assert.That(t.ConnectTimeoutMS, Is.EqualTo(1000));
        Assert.That(t.MaxConnectAttempts, Is.EqualTo(5));
        Assert.That(t.DisconnectTimeoutMS, Is.EqualTo(10000));
    }

    [Test]
    public void 소수_초는_올림_횟수로_고르게_나눈다()
    {
        var t = DemoSessionRules.ComputeTransportTimeouts(2.5f, 0.5f);
        Assert.That(t.MaxConnectAttempts, Is.EqualTo(3));
        Assert.That(t.ConnectTimeoutMS, Is.EqualTo(833));
        Assert.That(t.DisconnectTimeoutMS, Is.EqualTo(500));
    }

    [Test]
    public void 일초_미만도_최소_한_번은_시도한다()
    {
        var t = DemoSessionRules.ComputeTransportTimeouts(0.3f, 10f);
        Assert.That(t.MaxConnectAttempts, Is.EqualTo(1));
        Assert.That(t.ConnectTimeoutMS, Is.EqualTo(300));
    }
}
