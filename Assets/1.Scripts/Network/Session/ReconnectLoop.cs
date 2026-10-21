using System;

public enum ReconnectState
{
    Idle,
    /// <summary>클라: StartClient 를 냈고 OnClientConnected 를 기다린다.</summary>
    Connecting,
    Connected,
    /// <summary>클라: 접속 중에 끊겼다. 정리 + 대기 씬 로드가 끝나기를 기다린다.</summary>
    Dropped,
    /// <summary>호스트: StartHost 를 냈고 그 결과를 기다린다.</summary>
    StartingHost,
    Hosting,
    /// <summary>Shutdown 을 냈고 NGO 정리가 끝나기를 기다린다.</summary>
    ShuttingDown,
    /// <summary>재시도 간격을 기다린다. <see cref="ReconnectLoop.Tick"/> 이 때가 되면 다시 시작한다.</summary>
    Backoff,
}

/// <summary>
/// <see cref="ReconnectLoop"/> 가 내는 명령. 실행은 DemoSessionDirector(C5)가 한다.
/// </summary>
public enum ReconnectCommand
{
    None,
    StartClient,
    StartHost,
    Shutdown,
    /// <summary>GlobalTimeScale 1 복구·SessionResult 비우기 등 정리 후 대기 씬(6.DemoConnectScene)을 로드.
    /// 끝나면 <see cref="ReconnectLoop.OnDropCleanupComplete"/>.</summary>
    CleanupAndLoadWaitScene,
    /// <summary>호스트: NGO LoadScene("3.LobbyScene", Single).</summary>
    LoadLobbyScene,
}

/// <summary>
/// 부스 데모 자동 접속·재접속 상태기계 — PLAN-48 §4.2. 이벤트를 받아 명령을 돌려주는 순수 C# 이다(EditMode 테스트 대상).
/// 시간은 호출자가 넘기는 <c>now</c>(초)로만 판단한다. 현재 상태에 맞지 않는 이벤트(중복 끊김, ShuttingDown 중 Connected 등)는
/// 무시하고 <see cref="ReconnectCommand.None"/> 을 돌려준다.
/// <code>
/// Client: Idle ─StartClient→ Connecting ─Connected→ Connected
///         Connecting ─Stopped/거절/타임아웃→ ShuttingDown ─완료→ Backoff ─n초→ Connecting
///         Connected ─Stopped→ Dropped ─정리·대기 씬 완료→ ShuttingDown → …
/// Host:   Idle ─StartHost→ StartingHost ─ok→ Hosting(LoadLobbyScene) / 실패→ Backoff ─n초→ StartingHost
///         Hosting ─Stopped→ ShuttingDown → …
/// </code>
/// </summary>
public sealed class ReconnectLoop
{
    public DemoConnectionRole Role { get; }
    public float RetryIntervalSeconds { get; }
    /// <summary>클라 Connecting 이 이 시간을 넘기면 Timeout 으로 끊는다.</summary>
    public float ConnectTimeoutSeconds { get; }

    public ReconnectState State { get; private set; } = ReconnectState.Idle;
    /// <summary>이번 연결 시도가 몇 번째인지(1부터). 접속·호스팅에 성공하면 0 으로 돌아간다.</summary>
    public int AttemptCount { get; private set; }
    /// <summary>마지막으로 실패·끊긴 사유. 접속·호스팅에 성공하면 None 으로 돌아간다.</summary>
    public DemoJoinRejectReason LastReason { get; private set; }

    double _connectStartedAt;
    double _nextRetryAt;

    public ReconnectLoop(DemoConnectionRole role, float retryIntervalSeconds, float connectTimeoutSeconds)
    {
        Role = role;
        RetryIntervalSeconds = Math.Max(0f, retryIntervalSeconds);
        ConnectTimeoutSeconds = Math.Max(0f, connectTimeoutSeconds);
    }

    public static ReconnectLoop FromConfig(DemoConnectionConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        return new ReconnectLoop(config.Role, config.RetryIntervalSeconds, config.ConnectTimeoutSeconds);
    }

    bool IsHost => Role == DemoConnectionRole.Host;

    /// <summary>Backoff 중 다음 재시도까지 남은 초. 다른 상태에선 0.</summary>
    public float SecondsUntilRetry(double now)
    {
        if (State != ReconnectState.Backoff)
            return 0f;
        return (float)Math.Max(0d, _nextRetryAt - now);
    }

    /// <summary>Idle 에서만 첫 시도를 시작한다.</summary>
    public ReconnectCommand Start(double now)
    {
        if (State != ReconnectState.Idle)
            return ReconnectCommand.None;
        return BeginAttempt(now);
    }

    /// <summary>매 프레임. Backoff 가 끝나면 재시도, 클라 Connecting 이 제한 시간을 넘기면 Timeout 으로 끊는다.</summary>
    public ReconnectCommand Tick(double now)
    {
        switch (State)
        {
            case ReconnectState.Backoff when now >= _nextRetryAt:
                return BeginAttempt(now);
            case ReconnectState.Connecting when now - _connectStartedAt >= ConnectTimeoutSeconds:
                return BeginShutdown(DemoJoinRejectReason.Timeout);
            default:
                return ReconnectCommand.None;
        }
    }

    public ReconnectCommand OnClientConnected(double now)
    {
        if (State != ReconnectState.Connecting)
            return ReconnectCommand.None;
        State = ReconnectState.Connected;
        AttemptCount = 0;
        LastReason = DemoJoinRejectReason.None;
        return ReconnectCommand.None;
    }

    /// <summary>NGO 가 멈췄다(클라: 거절·끊김, 호스트: 서버 정지). <paramref name="reason"/> 이 None 이면 Unknown 으로 기록한다.</summary>
    public ReconnectCommand OnStopped(DemoJoinRejectReason reason, double now)
    {
        if (reason == DemoJoinRejectReason.None)
            reason = DemoJoinRejectReason.Unknown;

        switch (State)
        {
            case ReconnectState.Connecting:
            case ReconnectState.Hosting:
                return BeginShutdown(reason);
            case ReconnectState.Connected:
                State = ReconnectState.Dropped;
                LastReason = reason;
                return ReconnectCommand.CleanupAndLoadWaitScene;
            default:
                return ReconnectCommand.None;
        }
    }

    /// <summary>Dropped 정리·대기 씬 로드가 끝났다.</summary>
    public ReconnectCommand OnDropCleanupComplete(double now)
    {
        if (State != ReconnectState.Dropped)
            return ReconnectCommand.None;
        State = ReconnectState.ShuttingDown;
        return ReconnectCommand.Shutdown;
    }

    /// <summary>StartHost 결과. 실패면 바로 Backoff.</summary>
    public ReconnectCommand OnHostStartResult(bool succeeded, double now)
    {
        if (State != ReconnectState.StartingHost)
            return ReconnectCommand.None;

        if (succeeded)
        {
            State = ReconnectState.Hosting;
            AttemptCount = 0;
            LastReason = DemoJoinRejectReason.None;
            return ReconnectCommand.LoadLobbyScene;
        }

        LastReason = DemoJoinRejectReason.Unknown;
        EnterBackoff(now);
        return ReconnectCommand.None;
    }

    /// <summary>Shutdown 이 끝났다(NetworkManager.ShutdownInProgress 해제).</summary>
    public ReconnectCommand OnShutdownComplete(double now)
    {
        if (State != ReconnectState.ShuttingDown)
            return ReconnectCommand.None;
        EnterBackoff(now);
        return ReconnectCommand.None;
    }

    ReconnectCommand BeginAttempt(double now)
    {
        AttemptCount++;
        if (IsHost)
        {
            State = ReconnectState.StartingHost;
            return ReconnectCommand.StartHost;
        }

        State = ReconnectState.Connecting;
        _connectStartedAt = now;
        return ReconnectCommand.StartClient;
    }

    ReconnectCommand BeginShutdown(DemoJoinRejectReason reason)
    {
        State = ReconnectState.ShuttingDown;
        LastReason = reason;
        return ReconnectCommand.Shutdown;
    }

    void EnterBackoff(double now)
    {
        State = ReconnectState.Backoff;
        _nextRetryAt = now + RetryIntervalSeconds;
    }
}
