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
    public static readonly UiString JoiningMatch = new UiString("UI.DemoConnect.JoiningMatch", "판에 합류하는 중…", Where + " · 판 도중 재합류 — 맵에 들어왔지만 내 캐릭터가 아직 스폰되지 않음(서버 스폰 보류 중)");

    public static readonly UiString AutoHostMatch = new UiString("UI.DemoConnect.AutoHostMatch", "자동: 호스트 ({address} 일치)", Where + " · role 자동 판정 — 이 PC IP 가 hostAddress 와 같음({address} = 일치한 IP)");
    public static readonly UiString AutoHostLoopback = new UiString("UI.DemoConnect.AutoHostLoopback", "자동: 호스트 (hostAddress {address})", Where + " · role 자동 판정 — hostAddress 가 비었거나 loopback({address} = hostAddress)");
    public static readonly UiString AutoClient = new UiString("UI.DemoConnect.AutoClient", "자동: 클라 (이 PC 에 {address} 없음)", Where + " · role 자동 판정 — 이 PC 에 hostAddress 가 없어 그 주소로 접속({address} = hostAddress)");

    // 접속 안 된 마지막 사유 — DemoJoinRejectReasonText.ToDisplayText(RejoinWaiting 은 위 키)
    private const string ReasonWhere = Where + " · 거절 사유 ";
    public static readonly UiString GameInProgress = new UiString("UI.DemoConnect.GameInProgress", "게임 진행 중 — 판이 끝나면 자동 입장", ReasonWhere + "GameInProgress(판 진행·카운트다운 중, 이 PC 는 판 명단 밖)");
    public static readonly UiString LobbyFull = new UiString("UI.DemoConnect.LobbyFull", "인원 가득 ({max}/{max})", ReasonWhere + "LobbyFull({max} = 최대 인원)");
    public static readonly UiString HashMismatch = new UiString("UI.DemoConnect.HashMismatch", "버전 불일치 — 호스트와 같은 빌드인지 확인", ReasonWhere + "HashMismatch(NetworkConfig 해시 다름)");
    public static readonly UiString Timeout = new UiString("UI.DemoConnect.Timeout", "응답 없음 — 호스트를 기다리는 중", ReasonWhere + "Timeout(호스트 꺼짐·주소 틀림)");
    public static readonly UiString DuplicatePlayerId = new UiString("UI.DemoConnect.DuplicatePlayerId", "같은 플레이어 ID 가 이미 접속 중 — 설정 파일의 playerId 확인", ReasonWhere + "DuplicatePlayerId");
    public static readonly UiString InvalidPlayerId = new UiString("UI.DemoConnect.InvalidPlayerId", "플레이어 ID 가 올바르지 않음 — 설정 파일의 playerId 확인", ReasonWhere + "InvalidPlayerId");
    public static readonly UiString UnknownReason = new UiString("UI.DemoConnect.UnknownReason", "알 수 없는 이유로 연결되지 않음", ReasonWhere + "Unknown");

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        WaitingForHost, StartingHost, Disconnected, Address, Connecting, RetryIn, ConfigMissing, BuildInfo, RejoinWaiting,
        JoiningMatch, AutoHostMatch, AutoHostLoopback, AutoClient,
        GameInProgress, LobbyFull, HashMismatch, Timeout, DuplicatePlayerId, InvalidPlayerId, UnknownReason,
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
        string configHash,
        DemoAutoRoleDecision? autoRole = null)
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
        AutoRole = autoRole;
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
    /// <summary>role 을 자동 판정했으면 그 근거(제목 아래 한 줄). 명시 role 이면 null.</summary>
    public DemoAutoRoleDecision? AutoRole { get; }
}

/// <summary>DemoConnectionOverlay 의 표시 판단·문구 조립 — 순수 함수(EditMode 테스트 대상).</summary>
public static class DemoConnectionOverlayText
{
    public const float BootNoticeSeconds = 3f;

    /// <summary>접속·호스팅이 된 상태(Connected·Hosting)에서는 상태 패널을 숨긴다.</summary>
    public static bool ShouldShowStatus(ReconnectState state)
    {
        return state != ReconnectState.Connected && state != ReconnectState.Hosting;
    }

    /// <summary>
    /// 판 도중 재합류 — 접속됐고 활성 씬이 맵인데 내 플레이어가 없으면 "판에 합류하는 중…" 을 띄운다(C14 §3.6).
    /// 이 맵에서 한 번이라도 내 플레이어가 있었으면 띄우지 않는다 — 판 끝 로비 전환 직전 디스폰 때 깜빡이지 않게.
    /// 내 플레이어가 생기면(LocalPlayerChanged) 닫힌다.
    /// </summary>
    public static bool ShouldShowJoiningMatch(ReconnectState state, bool isMapActive, bool hasLocalPlayer, bool hadLocalPlayerInThisMap)
    {
        return state == ReconnectState.Connected && isMapActive && !hasLocalPlayer && !hadLocalPlayerInThisMap;
    }

    /// <summary>부팅 안내(<see cref="BootNotice"/> — 설정 파일 없음 Q5 · role 자동 판정 근거)를 3초 동안 띄운다.</summary>
    public static bool ShouldShowBootNotice(bool hasNotice, float secondsSinceBoot)
    {
        return hasNotice && secondsSinceBoot >= 0f && secondsSinceBoot < BootNoticeSeconds;
    }

    public static string BuildStatus(in DemoConnectionOverlayModel model)
    {
        var sb = new StringBuilder();
        sb.Append(Title(model));

        if (model.AutoRole.HasValue)
            sb.Append('\n').Append(AutoRoleText(model.AutoRole.Value));

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

    /// <summary>"자동: 호스트 (100.79.198.77 일치)" 같은 판정 근거 한 줄.</summary>
    public static string AutoRoleText(in DemoAutoRoleDecision decision)
    {
        var key = decision.Basis switch
        {
            DemoAutoRoleBasis.LocalAddressMatch => DemoConnectUiStrings.AutoHostMatch,
            DemoAutoRoleBasis.LoopbackHostAddress => DemoConnectUiStrings.AutoHostLoopback,
            _ => DemoConnectUiStrings.AutoClient,
        };
        return key.Format(new Dictionary<string, string>
        {
            ["address"] = string.IsNullOrEmpty(decision.Address) ? "?" : decision.Address,
        });
    }

    /// <summary>
    /// 부팅 직후 3초 안내 문구 — 설정 파일이 없으면 "설정 파일 없음 — 호스트 모드", role 을 자동 판정했으면 그 근거.
    /// 띄울 게 없으면 빈 문자열. 호스트는 곧 상태 패널이 숨으므로 판정 근거를 여기서 한 번 더 보여 준다.
    /// </summary>
    public static string BootNotice(bool isFileMissing, DemoAutoRoleDecision? autoRole)
    {
        if (isFileMissing)
            return DemoConnectUiStrings.ConfigMissing.Text;
        return autoRole.HasValue ? AutoRoleText(autoRole.Value) : string.Empty;
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
