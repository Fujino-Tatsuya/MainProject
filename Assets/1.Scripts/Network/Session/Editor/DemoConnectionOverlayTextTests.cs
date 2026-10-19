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

    [Test]
    public void 설정_파일_없음_안내는_3초만()
    {
        Assert.That(DemoConnectionOverlayText.ShouldShowConfigMissing(true, 0f), Is.True);
        Assert.That(DemoConnectionOverlayText.ShouldShowConfigMissing(true, 2.9f), Is.True);
        Assert.That(DemoConnectionOverlayText.ShouldShowConfigMissing(true, 3f), Is.False);
        Assert.That(DemoConnectionOverlayText.ShouldShowConfigMissing(false, 0f), Is.False);
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
