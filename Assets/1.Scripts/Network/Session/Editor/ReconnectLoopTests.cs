using NUnit.Framework;

/// <summary>
/// 부스 데모 자동 접속·재접속 상태기계 — 클라 정상 접속, 실패 → 백오프 → 재시도, 끊김 → Dropped → 대기 씬 → 재접속,
/// 거절 사유 보존, 호스트 시작 실패 재시도, 중복 이벤트 무시, now 주입 백오프 경계.
/// </summary>
public sealed class ReconnectLoopTests
{
    const float Retry = 3f;
    const float Timeout = 5f;

    static ReconnectLoop Client() => new ReconnectLoop(DemoConnectionRole.Client, Retry, Timeout);
    static ReconnectLoop Host() => new ReconnectLoop(DemoConnectionRole.Host, Retry, Timeout);

    /// <summary>Connecting 에서 거절받고 Shutdown 이 끝나 Backoff 에 들어간 상태로 만든다.</summary>
    static ReconnectLoop ClientInBackoff(DemoJoinRejectReason reason, double shutdownDoneAt)
    {
        var loop = Client();
        loop.Start(0);
        loop.OnStopped(reason, 1);
        loop.OnShutdownComplete(shutdownDoneAt);
        return loop;
    }

    // ─── 클라 ──────────────────────────────────────────────────────────

    [Test]
    public void 클라_정상_접속()
    {
        var loop = Client();

        Assert.AreEqual(ReconnectState.Idle, loop.State);
        Assert.AreEqual(ReconnectCommand.StartClient, loop.Start(0));
        Assert.AreEqual(ReconnectState.Connecting, loop.State);
        Assert.AreEqual(1, loop.AttemptCount);

        Assert.AreEqual(ReconnectCommand.None, loop.OnClientConnected(1));
        Assert.AreEqual(ReconnectState.Connected, loop.State);
        Assert.AreEqual(0, loop.AttemptCount);
        Assert.AreEqual(DemoJoinRejectReason.None, loop.LastReason);
    }

    [Test]
    public void 접속_실패하면_백오프_후_재시도하고_횟수가_는다()
    {
        var loop = Client();
        loop.Start(0);

        Assert.AreEqual(ReconnectCommand.Shutdown, loop.OnStopped(DemoJoinRejectReason.None, 1));
        Assert.AreEqual(ReconnectState.ShuttingDown, loop.State);
        Assert.AreEqual(ReconnectCommand.None, loop.OnShutdownComplete(2));
        Assert.AreEqual(ReconnectState.Backoff, loop.State);
        Assert.AreEqual(Retry, loop.SecondsUntilRetry(2), 1e-4);

        Assert.AreEqual(ReconnectCommand.StartClient, loop.Tick(2 + Retry));
        Assert.AreEqual(ReconnectState.Connecting, loop.State);
        Assert.AreEqual(2, loop.AttemptCount);

        loop.OnStopped(DemoJoinRejectReason.None, 6);
        loop.OnShutdownComplete(6);
        loop.Tick(6 + Retry);
        Assert.AreEqual(3, loop.AttemptCount);
    }

    [Test]
    public void 접속_제한시간을_넘기면_Timeout_으로_끊는다()
    {
        var loop = Client();
        loop.Start(10);

        Assert.AreEqual(ReconnectCommand.None, loop.Tick(10 + Timeout - 0.01));
        Assert.AreEqual(ReconnectState.Connecting, loop.State);

        Assert.AreEqual(ReconnectCommand.Shutdown, loop.Tick(10 + Timeout));
        Assert.AreEqual(ReconnectState.ShuttingDown, loop.State);
        Assert.AreEqual(DemoJoinRejectReason.Timeout, loop.LastReason);
    }

    [Test]
    public void 연결_중_끊기면_Dropped_대기씬_후_재접속한다()
    {
        var loop = Client();
        loop.Start(0);
        loop.OnClientConnected(1);

        Assert.AreEqual(ReconnectCommand.CleanupAndLoadWaitScene, loop.OnStopped(DemoJoinRejectReason.None, 50));
        Assert.AreEqual(ReconnectState.Dropped, loop.State);
        Assert.AreEqual(DemoJoinRejectReason.Unknown, loop.LastReason);

        Assert.AreEqual(ReconnectCommand.Shutdown, loop.OnDropCleanupComplete(51));
        Assert.AreEqual(ReconnectState.ShuttingDown, loop.State);
        loop.OnShutdownComplete(52);
        Assert.AreEqual(ReconnectState.Backoff, loop.State);

        Assert.AreEqual(ReconnectCommand.StartClient, loop.Tick(52 + Retry));
        Assert.AreEqual(1, loop.AttemptCount, "접속 성공 뒤 끊김은 1회째부터 다시 센다");

        loop.OnClientConnected(56);
        Assert.AreEqual(ReconnectState.Connected, loop.State);
    }

    [Test]
    public void 거절_사유는_재시도_중에도_보존된다()
    {
        var loop = ClientInBackoff(DemoJoinRejectReason.GameInProgress, 2);
        Assert.AreEqual(DemoJoinRejectReason.GameInProgress, loop.LastReason);

        loop.Tick(2 + Retry);
        Assert.AreEqual(ReconnectState.Connecting, loop.State);
        Assert.AreEqual(DemoJoinRejectReason.GameInProgress, loop.LastReason, "다음 시도 중에도 직전 사유를 보여 준다");

        loop.OnStopped(DemoJoinRejectReason.LobbyFull, 6);
        Assert.AreEqual(DemoJoinRejectReason.LobbyFull, loop.LastReason, "새 사유가 오면 바뀐다");

        loop.OnShutdownComplete(6);
        loop.Tick(6 + Retry);
        loop.OnClientConnected(10);
        Assert.AreEqual(DemoJoinRejectReason.None, loop.LastReason, "접속에 성공하면 비운다");
    }

    // ─── 호스트 ────────────────────────────────────────────────────────

    [Test]
    public void 호스트_시작_성공이면_로비를_로드한다()
    {
        var loop = Host();

        Assert.AreEqual(ReconnectCommand.StartHost, loop.Start(0));
        Assert.AreEqual(ReconnectState.StartingHost, loop.State);
        Assert.AreEqual(ReconnectCommand.LoadLobbyScene, loop.OnHostStartResult(true, 0));
        Assert.AreEqual(ReconnectState.Hosting, loop.State);
        Assert.AreEqual(0, loop.AttemptCount);
    }

    [Test]
    public void 호스트_시작_실패면_백오프_후_재시도한다()
    {
        var loop = Host();
        loop.Start(0);

        Assert.AreEqual(ReconnectCommand.None, loop.OnHostStartResult(false, 0));
        Assert.AreEqual(ReconnectState.Backoff, loop.State);
        Assert.AreEqual(DemoJoinRejectReason.Unknown, loop.LastReason);

        Assert.AreEqual(ReconnectCommand.None, loop.Tick(Retry - 0.01));
        Assert.AreEqual(ReconnectCommand.StartHost, loop.Tick(Retry));
        Assert.AreEqual(2, loop.AttemptCount);

        Assert.AreEqual(ReconnectCommand.LoadLobbyScene, loop.OnHostStartResult(true, Retry));
        Assert.AreEqual(DemoJoinRejectReason.None, loop.LastReason);
    }

    [Test]
    public void 호스팅_중_서버가_멈추면_정리_후_다시_호스팅한다()
    {
        var loop = Host();
        loop.Start(0);
        loop.OnHostStartResult(true, 0);

        Assert.AreEqual(ReconnectCommand.Shutdown, loop.OnStopped(DemoJoinRejectReason.None, 10));
        loop.OnShutdownComplete(11);
        Assert.AreEqual(ReconnectCommand.StartHost, loop.Tick(11 + Retry));
    }

    [Test]
    public void 호스트는_접속_제한시간이_없다()
    {
        var loop = Host();
        loop.Start(0);

        Assert.AreEqual(ReconnectCommand.None, loop.Tick(Timeout * 10));
        Assert.AreEqual(ReconnectState.StartingHost, loop.State);
    }

    // ─── 중복 · 순서 어긋난 이벤트 ──────────────────────────────────────

    [Test]
    public void 같은_끊김이_두_번_와도_한_번만_처리한다()
    {
        var loop = Client();
        loop.Start(0);
        loop.OnClientConnected(1);

        Assert.AreEqual(ReconnectCommand.CleanupAndLoadWaitScene, loop.OnStopped(DemoJoinRejectReason.None, 5));
        Assert.AreEqual(ReconnectCommand.None, loop.OnStopped(DemoJoinRejectReason.Timeout, 5));
        Assert.AreEqual(ReconnectState.Dropped, loop.State);
        Assert.AreEqual(DemoJoinRejectReason.Unknown, loop.LastReason);

        loop.OnDropCleanupComplete(6);
        Assert.AreEqual(ReconnectCommand.None, loop.OnStopped(DemoJoinRejectReason.None, 6));
        Assert.AreEqual(ReconnectCommand.None, loop.OnDropCleanupComplete(6));
        Assert.AreEqual(ReconnectState.ShuttingDown, loop.State);
    }

    [Test]
    public void ShuttingDown_중_Connected_는_무시한다()
    {
        var loop = Client();
        loop.Start(0);
        loop.Tick(Timeout);

        Assert.AreEqual(ReconnectCommand.None, loop.OnClientConnected(Timeout));
        Assert.AreEqual(ReconnectState.ShuttingDown, loop.State);
        Assert.AreEqual(DemoJoinRejectReason.Timeout, loop.LastReason);
    }

    [Test]
    public void 맞지_않는_상태의_이벤트는_모두_무시한다()
    {
        var loop = Client();

        Assert.AreEqual(ReconnectCommand.None, loop.OnClientConnected(0));
        Assert.AreEqual(ReconnectCommand.None, loop.OnStopped(DemoJoinRejectReason.Unknown, 0));
        Assert.AreEqual(ReconnectCommand.None, loop.OnShutdownComplete(0));
        Assert.AreEqual(ReconnectCommand.None, loop.OnHostStartResult(true, 0));
        Assert.AreEqual(ReconnectCommand.None, loop.Tick(1000));
        Assert.AreEqual(ReconnectState.Idle, loop.State);

        loop.Start(0);
        Assert.AreEqual(ReconnectCommand.None, loop.Start(0), "두 번째 Start 는 무시");
        Assert.AreEqual(1, loop.AttemptCount);

        loop.OnClientConnected(1);
        Assert.AreEqual(ReconnectCommand.None, loop.OnClientConnected(1));
        Assert.AreEqual(ReconnectCommand.None, loop.OnShutdownComplete(1));
        Assert.AreEqual(ReconnectCommand.None, loop.Tick(1000), "Connected 는 제한시간이 없다");
        Assert.AreEqual(ReconnectState.Connected, loop.State);
    }

    [Test]
    public void Backoff_중_Shutdown_완료가_또_와도_재시도_시각은_밀리지_않는다()
    {
        var loop = ClientInBackoff(DemoJoinRejectReason.Unknown, 2);

        Assert.AreEqual(ReconnectCommand.None, loop.OnShutdownComplete(4));
        Assert.AreEqual(ReconnectCommand.StartClient, loop.Tick(2 + Retry));
    }

    // ─── 백오프 경계 ───────────────────────────────────────────────────

    [Test]
    public void 백오프_경계_직전은_대기하고_경계에서_재시도()
    {
        var loop = ClientInBackoff(DemoJoinRejectReason.Unknown, 100);

        Assert.AreEqual(ReconnectCommand.None, loop.Tick(100 + Retry - 0.001));
        Assert.AreEqual(ReconnectState.Backoff, loop.State);
        Assert.AreEqual(0.001, loop.SecondsUntilRetry(100 + Retry - 0.001), 1e-4);

        Assert.AreEqual(ReconnectCommand.StartClient, loop.Tick(100 + Retry));
        Assert.AreEqual(0f, loop.SecondsUntilRetry(100 + Retry), "Backoff 가 아니면 0");
    }

    [Test]
    public void 남은_초는_음수가_되지_않는다()
    {
        var loop = ClientInBackoff(DemoJoinRejectReason.Unknown, 0);
        Assert.AreEqual(0f, loop.SecondsUntilRetry(Retry + 10));
    }

    [Test]
    public void FromConfig_는_설정의_역할과_초_값을_쓴다()
    {
        var config = new DemoConnectionConfig(DemoConnectionRole.Client, "192.168.0.10", 7777, "p1", 2.5f, 4f, 10f);
        var loop = ReconnectLoop.FromConfig(config);

        Assert.AreEqual(DemoConnectionRole.Client, loop.Role);
        Assert.AreEqual(2.5f, loop.RetryIntervalSeconds);
        Assert.AreEqual(4f, loop.ConnectTimeoutSeconds);
    }

    // ─── 사유 문구 ─────────────────────────────────────────────────────

    [Test]
    public void 사유마다_문구가_있고_None_은_빈_문자열()
    {
        Assert.AreEqual(string.Empty, DemoJoinRejectReasonText.ToDisplayText(DemoJoinRejectReason.None));
        StringAssert.Contains("판이 끝나면 자동 입장", DemoJoinRejectReasonText.ToDisplayText(DemoJoinRejectReason.GameInProgress));
        StringAssert.Contains("3/3", DemoJoinRejectReasonText.ToDisplayText(DemoJoinRejectReason.LobbyFull));
        foreach (DemoJoinRejectReason r in System.Enum.GetValues(typeof(DemoJoinRejectReason)))
        {
            if (r != DemoJoinRejectReason.None)
                Assert.IsNotEmpty(DemoJoinRejectReasonText.ToDisplayText(r), r.ToString());
        }
    }
}
