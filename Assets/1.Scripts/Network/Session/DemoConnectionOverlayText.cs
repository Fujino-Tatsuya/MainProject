using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 부스 데모 연결 오버레이 문구 키(UI.DemoConnect.*). Export 때 <see cref="UiStrings.All"/> 을 거쳐 Strings 시트로 간다.
/// 시트에 행이 없으면 <see cref="UiString.Ko"/> 가 화면에 나온다.
/// </summary>
public static class DemoConnectUiStrings
{
    private const string Where = "DemoConnectionOverlay — 부스 데모 연결 대기 화면(코드)";

    public static readonly UiString WaitingForHost = new UiString("UI.DemoConnect.WaitingForHost", "호스트 대기 중", Where + " · 클라 제목");
    public static readonly UiString StartingHost = new UiString("UI.DemoConnect.StartingHost", "호스트 시작 중", Where + " · 호스트 제목");
    public static readonly UiString Disconnected = new UiString("UI.DemoConnect.Disconnected", "연결 끊김 — 정리 중", Where + " · 끊김 정리 중 제목");
    public static readonly UiString Address = new UiString("UI.DemoConnect.Address", "주소 {address}:{port}", Where + " · 접속(호스트는 listen) 주소");
    public static readonly UiString Connecting = new UiString("UI.DemoConnect.Connecting", "접속 시도 중 ({attempt}회째)", Where + " · 시도 중({attempt} = 1부터 센 시도 번호)");
    public static readonly UiString RetryIn = new UiString("UI.DemoConnect.RetryIn", "{time} 후 재시도 ({attempt}회째)", Where + " · 재시도 대기({time} = UI.Unit.Seconds, {attempt} = 재시도 번호)");
    public static readonly UiString ConfigMissing = new UiString("UI.DemoConnect.ConfigMissing", "설정 파일 없음 — 호스트 모드", Where + " · demo-connection.json 없음 안내(3초)");
    public static readonly UiString BuildInfo = new UiString("UI.DemoConnect.BuildInfo", "v{version} · cfg {hash}", Where + " · 빌드 버전 + NetworkConfig 해시");
    public static readonly UiString RejoinWaiting = new UiString("UI.DemoConnect.RejoinWaiting", "판에 다시 합류하는 중 — 잠시 기다리세요", Where + " · 거절 사유 RejoinWaiting(판 명단 안 사람이 씬 전환·옛 연결 정리를 기다림)");

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        WaitingForHost, StartingHost, Disconnected, Address, Connecting, RetryIn, ConfigMissing, BuildInfo, RejoinWaiting,
    };
}

/// <summary>오버레이 한 프레임에 필요한 값.</summary>
public readonly struct DemoConnectionOverlayModel
{
    public DemoConnectionOverlayModel(
        DemoConnectionRole role,
        ReconnectState state,
        string address,
        ushort port,
        int attemptCount,
        float secondsUntilRetry,
        DemoJoinRejectReason lastReason,
        string version,
        string configHash)
    {
        Role = role;
        State = state;
        Address = address;
        Port = port;
        AttemptCount = attemptCount;
        SecondsUntilRetry = secondsUntilRetry;
        LastReason = lastReason;
        Version = version;
        ConfigHash = configHash;
    }

    public DemoConnectionRole Role { get; }
    public ReconnectState State { get; }
    public string Address { get; }
    public ushort Port { get; }
    public int AttemptCount { get; }
    public float SecondsUntilRetry { get; }
    public DemoJoinRejectReason LastReason { get; }
    public string Version { get; }
    /// <summary>비었으면 "?" 로 표시(아직 한 번도 NetworkManager 를 시작하지 않음).</summary>
    public string ConfigHash { get; }
}

/// <summary>DemoConnectionOverlay 의 표시 판단·문구 조립 — 순수 함수(EditMode 테스트 대상).</summary>
public static class DemoConnectionOverlayText
{
    public const float ConfigMissingNoticeSeconds = 3f;

    /// <summary>접속·호스팅이 된 상태(Connected·Hosting)에서는 상태 패널을 숨긴다.</summary>
    public static bool ShouldShowStatus(ReconnectState state)
    {
        return state != ReconnectState.Connected && state != ReconnectState.Hosting;
    }

    /// <summary>"설정 파일 없음 — 호스트 모드" 를 부팅 후 3초 동안 띄운다(Q5).</summary>
    public static bool ShouldShowConfigMissing(bool isFileMissing, float secondsSinceBoot)
    {
        return isFileMissing && secondsSinceBoot >= 0f && secondsSinceBoot < ConfigMissingNoticeSeconds;
    }

    public static string BuildStatus(in DemoConnectionOverlayModel model)
    {
        var sb = new StringBuilder();
        sb.Append(Title(model));

        sb.Append('\n').Append(DemoConnectUiStrings.Address.Format(new Dictionary<string, string>
        {
            ["address"] = string.IsNullOrEmpty(model.Address) ? "?" : model.Address,
            ["port"] = model.Port.ToString(CultureInfo.InvariantCulture),
        }));

        var progress = Progress(model);
        if (!string.IsNullOrEmpty(progress))
            sb.Append('\n').Append(progress);

        var reason = DemoJoinRejectReasonText.ToDisplayText(model.LastReason);
        if (!string.IsNullOrEmpty(reason))
            sb.Append('\n').Append(reason);

        sb.Append('\n').Append(BuildInfo(model.Version, model.ConfigHash));
        return sb.ToString();
    }

    public static string BuildInfo(string version, string configHash)
    {
        return DemoConnectUiStrings.BuildInfo.Format(new Dictionary<string, string>
        {
            ["version"] = string.IsNullOrEmpty(version) ? "?" : version,
            ["hash"] = string.IsNullOrEmpty(configHash) ? "?" : configHash,
        });
    }

    private static string Title(in DemoConnectionOverlayModel model)
    {
        if (model.State == ReconnectState.Dropped || model.State == ReconnectState.ShuttingDown)
            return DemoConnectUiStrings.Disconnected.Text;
        return model.Role == DemoConnectionRole.Host
            ? DemoConnectUiStrings.StartingHost.Text
            : DemoConnectUiStrings.WaitingForHost.Text;
    }

    private static string Progress(in DemoConnectionOverlayModel model)
    {
        switch (model.State)
        {
            case ReconnectState.Connecting:
            case ReconnectState.StartingHost:
                return DemoConnectUiStrings.Connecting.Format(new Dictionary<string, string>
                {
                    ["attempt"] = model.AttemptCount.ToString(CultureInfo.InvariantCulture),
                });
            case ReconnectState.Backoff:
                // 올림 — "0초 후" 가 1초 내내 보이지 않게.
                var seconds = (float)System.Math.Ceiling(System.Math.Max(0f, model.SecondsUntilRetry));
                return DemoConnectUiStrings.RetryIn.Format(new Dictionary<string, string>
                {
                    ["time"] = UiStrings.Seconds(seconds, "0"),
                    ["attempt"] = model.AttemptCount.ToString(CultureInfo.InvariantCulture),
                });
            default:
                return string.Empty;
        }
    }
}
