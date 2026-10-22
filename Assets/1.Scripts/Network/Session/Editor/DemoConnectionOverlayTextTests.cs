using NUnit.Framework;

/// <summary>
/// 부스 데모 연결 오버레이 — 언제 보이는지(접속 후 숨김·설정 파일 없음 3초)와 문구에 주소·재시도·사유·해시가 들어가는지.
/// 문구 자체는 Strings 시트가 바꿀 수 있으므로 값(숫자·주소·사유 문구)이 들어갔는지만 본다.
/// </summary>
public sealed class DemoConnectionOverlayTextTests
{
    static DemoConnectionOverlayModel Model(
        ReconnectState state,
        DemoConnectionRole role = DemoConnectionRole.Client,
        int attempt = 2,
        float secondsUntilRetry = 2.2f,
        DemoJoinRejectReason reason = DemoJoinRejectReason.None,
        string hash = "123456789")
    {
        return new DemoConnectionOverlayModel(role, state, "192.168.0.10", 7777, attempt, secondsUntilRetry, reason, "0.1.0", hash);
    }

    [TestCase(ReconnectState.Connected, false)]
    [TestCase(ReconnectState.Hosting, false)]
    [TestCase(ReconnectState.Idle, true)]
    [TestCase(ReconnectState.Connecting, true)]
    [TestCase(ReconnectState.Backoff, true)]
    [TestCase(ReconnectState.ShuttingDown, true)]
    [TestCase(ReconnectState.Dropped, true)]
    [TestCase(ReconnectState.StartingHost, true)]
    public void 접속_호스팅_중에만_숨긴다(ReconnectState state, bool expected)
    {
        Assert.That(DemoConnectionOverlayText.ShouldShowStatus(state), Is.EqualTo(expected));
    }

    [TestCase(ReconnectState.Connected, true, false, false, true)]
    [TestCase(ReconnectState.Connected, true, true, false, false)]   // 내 플레이어가 생기면 닫는다
    [TestCase(ReconnectState.Connected, true, false, true, false)]   // 이 맵에서 이미 있었다(판 끝 디스폰) — 깜빡이지 않게
    [TestCase(ReconnectState.Connected, false, false, false, false)] // 맵이 아님(로비·로딩)
    [TestCase(ReconnectState.Hosting, true, false, false, false)]    // 호스트는 띄우지 않는다
    [TestCase(ReconnectState.Connecting, true, false, false, false)]
    public void 판에_합류하는_중은_맵에서_내_플레이어가_아직_없을_때만(
        ReconnectState state, bool isMapActive, bool hasLocalPlayer, bool hadLocalPlayer, bool expected)
    {
        Assert.That(
            DemoConnectionOverlayText.ShouldShowJoiningMatch(state, isMapActive, hasLocalPlayer, hadLocalPlayer),
            Is.EqualTo(expected));
    }

    [Test]
    public void 판에_합류하는_중_문구_키가_목록에_있다()
    {
        Assert.That(DemoConnectUiStrings.All, Does.Contain(DemoConnectUiStrings.JoiningMatch));
        Assert.That(DemoConnectUiStrings.JoiningMatch.Key, Is.EqualTo("UI.DemoConnect.JoiningMatch"));
    }

    [Test]
    public void 부팅_안내는_3초만()
    {
        Assert.That(DemoConnectionOverlayText.ShouldShowBootNotice(true, 0f), Is.True);
        Assert.That(DemoConnectionOverlayText.ShouldShowBootNotice(true, 2.9f), Is.True);
        Assert.That(DemoConnectionOverlayText.ShouldShowBootNotice(true, 3f), Is.False);
        Assert.That(DemoConnectionOverlayText.ShouldShowBootNotice(false, 0f), Is.False);
    }

    [Test]
    public void 백오프_문구에_주소_남은_초_횟수_사유_해시가_들어간다()
    {
        var text = DemoConnectionOverlayText.BuildStatus(Model(ReconnectState.Backoff, reason: DemoJoinRejectReason.Timeout));

        Assert.That(text, Does.Contain("192.168.0.10:7777"));
        var retryLine = DemoConnectUiStrings.RetryIn.Format(new System.Collections.Generic.Dictionary<string, string>
        {
            ["time"] = UiStrings.Seconds(3f, "0"), // 2.2초는 올려서 3초로 보인다
            ["attempt"] = "2",
        });
        Assert.That(text.Split('\n'), Does.Contain(retryLine));
        Assert.That(text, Does.Contain(DemoJoinRejectReasonText.ToDisplayText(DemoJoinRejectReason.Timeout)));
        Assert.That(text, Does.Contain("123456789"));
        Assert.That(text, Does.Contain("0.1.0"));
    }

    [Test]
    public void 사유가_없으면_사유_줄이_없다()
    {
        var text = DemoConnectionOverlayText.BuildStatus(Model(ReconnectState.Connecting));
        Assert.That(text.Split('\n').Length, Is.EqualTo(4), "제목·주소·시도·빌드 정보");
    }

    [Test]
    public void 해시가_없으면_물음표()
    {
        Assert.That(DemoConnectionOverlayText.BuildInfo("0.1.0", null), Does.Contain("?"));
        Assert.That(DemoConnectionOverlayText.BuildInfo(null, "42"), Does.Contain("?"));
    }

    [Test]
    public void 호스트와_끊김은_제목이_다르다()
    {
        string Title(ReconnectState s, DemoConnectionRole r) => DemoConnectionOverlayText.BuildStatus(Model(s, r)).Split('\n')[0];

        Assert.That(Title(ReconnectState.Connecting, DemoConnectionRole.Client), Is.EqualTo(DemoConnectUiStrings.WaitingForHost.Text));
        Assert.That(Title(ReconnectState.StartingHost, DemoConnectionRole.Host), Is.EqualTo(DemoConnectUiStrings.StartingHost.Text));
        Assert.That(Title(ReconnectState.Dropped, DemoConnectionRole.Client), Is.EqualTo(DemoConnectUiStrings.Disconnected.Text));
    }
}

/// <summary>role 자동 판정 근거 표시(할 일 72) — 상태 패널 제목 아래 한 줄 + 부팅 안내 3초.</summary>
public sealed class DemoConnectionOverlayAutoRoleTests
{
    static readonly string[] Local = { "127.0.0.1", "100.79.198.77" };

    static DemoConnectionOverlayModel Model(DemoAutoRoleDecision? autoRole) =>
        new DemoConnectionOverlayModel(DemoConnectionRole.Client, ReconnectState.Connecting, "192.168.0.10", 7777, 1, 0f,
            DemoJoinRejectReason.None, "0.1.0", "42", autoRole);

    [Test]
    public void 일치한_IP_를_호스트_근거로_보여_준다()
    {
        var text = DemoConnectionOverlayText.AutoRoleText(DemoConnectionConfig.ResolveAutoRole("100.79.198.77", Local));

        Assert.That(text, Is.EqualTo(DemoConnectUiStrings.AutoHostMatch.Format(
            new System.Collections.Generic.Dictionary<string, string> { ["address"] = "100.79.198.77" })));
    }

    [Test]
    public void 클라와_loopback_은_각자_문구()
    {
        var client = DemoConnectionOverlayText.AutoRoleText(DemoConnectionConfig.ResolveAutoRole("10.0.0.9", Local));
        var loopback = DemoConnectionOverlayText.AutoRoleText(DemoConnectionConfig.ResolveAutoRole("", Local));

        Assert.That(client, Does.Contain("10.0.0.9"));
        Assert.That(client, Is.Not.EqualTo(loopback));
        Assert.That(loopback, Does.Contain("?"), "빈 hostAddress 는 ? 로 보인다");
    }

    [Test]
    public void 자동_판정이면_제목_아래에_근거_줄이_붙는다()
    {
        var decision = DemoConnectionConfig.ResolveAutoRole("192.168.0.10", Local);
        var lines = DemoConnectionOverlayText.BuildStatus(Model(decision)).Split('\n');

        Assert.That(lines.Length, Is.EqualTo(5), "제목·근거·주소·시도·빌드 정보");
        Assert.That(lines[1], Is.EqualTo(DemoConnectionOverlayText.AutoRoleText(decision)));
        Assert.That(DemoConnectionOverlayText.BuildStatus(Model(null)).Split('\n').Length, Is.EqualTo(4));
    }

    [Test]
    public void 부팅_안내는_파일_없음이_먼저고_명시_role_이면_비어_있다()
    {
        var decision = DemoConnectionConfig.ResolveAutoRole("100.79.198.77", Local);

        Assert.That(DemoConnectionOverlayText.BootNotice(true, null), Is.EqualTo(DemoConnectUiStrings.ConfigMissing.Text));
        Assert.That(DemoConnectionOverlayText.BootNotice(false, decision), Is.EqualTo(DemoConnectionOverlayText.AutoRoleText(decision)));
        Assert.That(DemoConnectionOverlayText.BootNotice(false, null), Is.Empty);
    }

    [Test]
    public void 근거_문구_키가_목록에_있다()
    {
        Assert.That(DemoConnectUiStrings.All, Does.Contain(DemoConnectUiStrings.AutoHostMatch));
        Assert.That(DemoConnectUiStrings.All, Does.Contain(DemoConnectUiStrings.AutoHostLoopback));
        Assert.That(DemoConnectUiStrings.All, Does.Contain(DemoConnectUiStrings.AutoClient));
    }
}
