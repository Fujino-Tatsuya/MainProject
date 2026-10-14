using System.Collections.Generic;

/// <summary>
/// 플레이어에게 보이는 오류 코드 하나(ErrorCodes 시트 한 행, 로컬라이징 L4). <see cref="Code"/> = <c>영역_이름</c> 대문자.
/// <see cref="Ko"/> = 지금 화면 문구 — Export 때 시트 ko 로 들어가고, 시트에 행이 아직 없을 때 화면에 나오는 폴백이다.
/// <see cref="Category"/>·<see cref="Cause"/>·<see cref="Action"/> 은 시트의 <c>#분류</c>·<c>#원인(개발자용)</c>·<c>#조치(QA·CS용)</c> 메모 열로만 간다(런타임 표에 안 들어감).
/// </summary>
public readonly struct ErrorCode
{
    public ErrorCode(string code, string category, string cause, string action, string ko)
    {
        Code = code;
        Category = category;
        Cause = cause;
        Action = action;
        Ko = ko;
    }

    public string Code { get; }
    public string Category { get; }
    public string Cause { get; }
    public string Action { get; }
    public string Ko { get; }

    public bool IsNone => string.IsNullOrEmpty(Code);

    /// <summary>현재 언어 문구 + <c>{이름}</c> 치환. 시트에 행이 없으면 <see cref="Ko"/>.</summary>
    public string Message(IReadOnlyDictionary<string, string> args = null) => Loc.GetError(Code, Ko, args);

    /// <summary>화면용 — 문구 + 작은 코드 표기(<see cref="ErrorDisplay.Compose"/>).</summary>
    public string Display(IReadOnlyDictionary<string, string> args = null) => ErrorDisplay.Compose(Message(args), Code);

    public override string ToString() => Code ?? string.Empty;
}

/// <summary>오류 문구 화면 표기 — 문구 뒤 줄바꿈 + 작은 글씨 <c>[CODE]</c>. QA·CS 가 화면만 보고 ErrorCodes 시트를 찾는 열쇠.</summary>
public static class ErrorDisplay
{
    public const string CodeSize = "70%";

    public static string Compose(string message, string code)
    {
        if (string.IsNullOrEmpty(code))
            return message ?? string.Empty;

        string tag = $"<size={CodeSize}>[{code}]</size>";
        return string.IsNullOrEmpty(message) ? tag : $"{message}\n{tag}";
    }

    /// <summary>자리표시자 인자 하나 — <c>ErrorDisplay.Arg("ip", text)</c>.</summary>
    public static IReadOnlyDictionary<string, string> Arg(string name, string value) =>
        new Dictionary<string, string> { [name] = value ?? string.Empty };
}

/// <summary>
/// L4 — 로비·네트워크 오류 코드 목록. Export 는 <see cref="All"/> 을 ErrorCodes 시트에 모은다(<c>ErrorCodeKeys</c>).
/// 한 번 정한 코드는 의미를 바꾸지 않는다 — 문구가 크게 바뀌면 새 코드를 만든다. 새 코드는 필드를 만들고 <see cref="All"/> 에 넣는다.
/// 기술 상세(예외 메시지 등)는 문구에 넣지 않는다 — <c>SessionError.Detail</c> 로 로그에만 남긴다.
/// </summary>
public static class ErrorCatalog
{
    private const string Lobby = "로비";
    private const string Net = "연결";
    private const string Relay = "Relay";
    private const string Ugs = "Unity Services";

    // 로비 화면 조작 — LobbySceneManager
    public static readonly ErrorCode LobbyLauncherMissing = new ErrorCode("LOBBY_LAUNCHER_MISSING", Lobby,
        "LobbySceneManager — NetworkManager 에 NetworkSessionLauncher 가 없다(씬·프리팹 배선)",
        "로비 씬 NetworkManager 프리팹 배선 확인(개발 문제)",
        "네트워크 세션 런처를 찾을 수 없습니다.");
    public static readonly ErrorCode LobbyUiMissing = new ErrorCode("LOBBY_UI_MISSING", Lobby,
        "LobbySceneManager.ToggleReady — LobbyUIController 를 못 찾음(씬 배선)",
        "로비 씬 LobbyUIController 배선 확인(개발 문제)",
        "로비 UI 컨트롤러를 찾을 수 없습니다.");
    public static readonly ErrorCode LobbyNotAllReady = new ErrorCode("LOBBY_NOT_ALL_READY", Lobby,
        "LobbySceneManager.StartGameLoading — 호스트가 시작을 눌렀는데 준비 안 된 플레이어가 있음",
        "모든 플레이어에게 준비를 누르도록 안내",
        "모든 플레이어가 준비되지 않았습니다.");

    // 연결 입력·세션 시작 — LobbySceneManager · DirectIPv4ConnectionProvider · NetworkSessionLauncher
    public static readonly ErrorCode NetIpEmpty = new ErrorCode("NET_IP_EMPTY", Net,
        "IP 입력칸이 비어 있음(LobbySceneManager.TryApplyConnectionData · DirectIPv4ConnectionProvider)",
        "IP 입력 안내",
        "IP를 입력하세요.");
    public static readonly ErrorCode NetIpInvalid = new ErrorCode("NET_IP_INVALID", Net,
        "LobbySceneManager.TryApplyConnectionData — IPAddress.TryParse 실패. {ip} = 입력값",
        "IP 형식(예 192.168.0.10) 안내",
        "IP 형식이 올바르지 않습니다: {ip}");
    public static readonly ErrorCode NetIpv4Invalid = new ErrorCode("NET_IPV4_INVALID", Net,
        "DirectIPv4ConnectionProvider — 접속 입력이 IPv4 가 아님(IPv6·호스트 이름 포함). {ip} = 입력값",
        "IPv4 주소(예 192.168.0.10) 안내",
        "IPv4 형식이 올바르지 않습니다: {ip}");
    public static readonly ErrorCode NetPortInvalid = new ErrorCode("NET_PORT_INVALID", Net,
        "Port 가 숫자가 아니거나 0·65535 초과. {port} = 입력값",
        "1~65535 숫자 입력 안내(기본 7777)",
        "Port는 1~65535 범위의 숫자여야 합니다: {port}");
    public static readonly ErrorCode NetProviderMissing = new ErrorCode("NET_PROVIDER_MISSING", Net,
        "NetworkSessionLauncher.TryGetProvider — 고른 연결 방식의 프로바이더가 없음(UnityTransport 없음). {mode} = 연결 방식",
        "NetworkManager 프리팹의 UnityTransport 확인(개발 문제)",
        "{mode} 연결 방식의 프로바이더가 등록되지 않았습니다.");
    public static readonly ErrorCode NetStartCanceled = new ErrorCode("NET_START_CANCELED", Net,
        "세션 준비 중 취소 토큰(OperationCanceledException)",
        "다시 시도",
        "세션 시작이 취소되었습니다.");
    public static readonly ErrorCode NetPrepareFailed = new ErrorCode("NET_PREPARE_FAILED", Net,
        "NetworkSessionLauncher.Prepare*Async — 프로바이더 준비 중 예상 못 한 예외(상세 = 예외 메시지, network.log)",
        "network.log 의 예외 확인",
        "세션 연결 준비에 실패했습니다.");
    public static readonly ErrorCode NetStartException = new ErrorCode("NET_START_EXCEPTION", Net,
        "NetworkSessionLauncher.CompleteSessionStartAsync — 세션 시작 중 예외(상세 = 예외 메시지)",
        "Console·network.log 의 예외 확인",
        "세션 시작 중 예외가 발생했습니다.");
    public static readonly ErrorCode NetStartFailed = new ErrorCode("NET_START_FAILED", Net,
        "SessionStartResult.Failed 에 코드 없이 실패가 들어옴(코드 누락 — 개발 문제)",
        "network.log 확인",
        "세션 시작에 실패했습니다.");
    public static readonly ErrorCode NetHostStartFailed = new ErrorCode("NET_HOST_START_FAILED", Net,
        "NetworkManager.StartHost() 가 false — 대개 포트 점유",
        "같은 포트를 쓰는 다른 실행(다른 게임 창) 종료 후 재시도",
        "Host 시작에 실패했습니다. 포트가 이미 사용 중인지 확인하세요.");
    public static readonly ErrorCode NetClientStartFailed = new ErrorCode("NET_CLIENT_START_FAILED", Net,
        "NetworkManager.StartClient() 가 false",
        "게임 재시작 후 재시도",
        "Client 시작에 실패했습니다.");

    // 접속 중·접속 뒤 — LobbySceneManager 콜백
    public static readonly ErrorCode NetConnectFailed = new ErrorCode("NET_CONNECT_FAILED", Net,
        "클라 접속 시도 중 끊김(호스트 거절 아님) — 주소 틀림·호스트 없음·방화벽",
        "호스트 IP/Port·호스트가 방을 열었는지·방화벽 확인",
        "서버 접속에 실패했습니다. IP/Port와 Host 상태를 확인하세요.");
    public static readonly ErrorCode NetRejectedByHost = new ErrorCode("NET_REJECTED_BY_HOST", Net,
        "호스트가 끊음('disconnected by server') — 대개 NetworkConfig 해시 불일치(빌드 버전 다름). {cfg} = 내 설정 해시",
        "양쪽 화면의 cfg 값 비교 — 다르면 같은 빌드로 맞춘다",
        "호스트가 접속을 거절했습니다. 양쪽 빌드가 같은 버전인지 확인하세요. (내 {cfg})");
    public static readonly ErrorCode NetDisconnected = new ErrorCode("NET_DISCONNECTED", Net,
        "접속된 뒤 클라 자신의 연결이 끊김",
        "네트워크 상태 확인 후 다시 참가",
        "서버와의 연결이 끊어졌습니다.");
    public static readonly ErrorCode NetPlayerDisconnected = new ErrorCode("NET_PLAYER_DISCONNECTED", Net,
        "호스트 화면 — 다른 플레이어의 연결이 끊김. {clientId} = NGO ClientId",
        "끊긴 플레이어에게 다시 참가 안내",
        "플레이어(ClientId={clientId})의 연결이 끊어졌습니다.");
    public static readonly ErrorCode NetTransportFailure = new ErrorCode("NET_TRANSPORT_FAILURE", Net,
        "NetworkManager.OnTransportFailure — 전송 계층 오류",
        "network.log 확인 후 재시도",
        "네트워크 전송 오류가 발생했습니다.");

    // Relay — RelayConnectionProvider
    public static readonly ErrorCode RelayTransportMissing = new ErrorCode("RELAY_TRANSPORT_MISSING", Relay,
        "RelayConnectionProvider.IsAvailable — UnityTransport 가 없음",
        "NetworkManager 프리팹의 UnityTransport 확인(개발 문제)",
        "Relay 연결에 필요한 UnityTransport를 찾을 수 없습니다.");
    public static readonly ErrorCode RelayJoinCodeEmpty = new ErrorCode("RELAY_JOIN_CODE_EMPTY", Relay,
        "Relay 참가 — 조인코드 입력칸이 빔",
        "호스트 화면의 조인코드 입력 안내",
        "Relay 조인코드를 입력하세요.");
    public static readonly ErrorCode RelayJoinCodeInvalid = new ErrorCode("RELAY_JOIN_CODE_INVALID", Relay,
        "RelayServiceException JoinCodeNotFound·AllocationNotFound·EntityNotFound",
        "호스트가 방을 다시 만들었는지 확인 — 최신 조인코드로 재시도",
        "Relay 조인코드가 잘못되었거나 만료되었습니다.");
    public static readonly ErrorCode RelayInactiveProject = new ErrorCode("RELAY_INACTIVE_PROJECT", Relay,
        "RelayServiceException InactiveProject — 대시보드에서 Relay 꺼짐",
        "Unity Dashboard 에서 Relay 서비스 활성화(개발 문제)",
        "Unity Dashboard에서 이 프로젝트의 Relay 서비스를 활성화하세요.");
    public static readonly ErrorCode RelayUnauthorized = new ErrorCode("RELAY_UNAUTHORIZED", Relay,
        "RelayServiceException Unauthorized·Forbidden",
        "Unity Services 로그인·프로젝트 연결 확인",
        "Relay 인증 또는 프로젝트 접근 권한이 없습니다.");
    public static readonly ErrorCode RelayPaymentRequired = new ErrorCode("RELAY_PAYMENT_REQUIRED", Relay,
        "RelayServiceException PaymentRequired — 사용 한도·결제",
        "Unity Dashboard 결제·한도 확인(개발 문제)",
        "Relay 사용 한도 또는 결제 설정을 확인하세요.");
    public static readonly ErrorCode RelayRateLimited = new ErrorCode("RELAY_RATE_LIMITED", Relay,
        "RelayServiceException RateLimited",
        "잠시 뒤 재시도",
        "Relay 요청 한도를 초과했습니다. 잠시 후 다시 시도하세요.");
    public static readonly ErrorCode RelayUnavailable = new ErrorCode("RELAY_UNAVAILABLE", Relay,
        "RelayServiceException NoSuitableRelay·ServiceUnavailable·GatewayTimeout",
        "잠시 뒤 재시도. 계속되면 Unity 서비스 상태 확인",
        "사용 가능한 Relay 서버가 없습니다. 잠시 후 다시 시도하세요.");
    public static readonly ErrorCode RelayJoinRejected = new ErrorCode("RELAY_JOIN_REJECTED", Relay,
        "참가 중 RelayServiceException InvalidRequest·Conflict — 방 정원 초과 등(상세 = 예외 메시지)",
        "조인코드와 방 인원(호스트 포함 3명) 확인",
        "Relay 방에 참가할 수 없습니다. 조인코드와 방 정원을 확인하세요.");
    public static readonly ErrorCode RelayHostFailed = new ErrorCode("RELAY_HOST_FAILED", Relay,
        "방 생성 중 위에 없는 RelayServiceException(상세 = reason·예외 메시지)",
        "network.log 의 RelayServiceException 확인",
        "Relay 방 생성에 실패했습니다.");
    public static readonly ErrorCode RelayJoinFailed = new ErrorCode("RELAY_JOIN_FAILED", Relay,
        "참가 중 위에 없는 RelayServiceException(상세 = reason·예외 메시지)",
        "network.log 의 RelayServiceException 확인",
        "Relay 방 참가에 실패했습니다.");

    // Unity Services — UnityServicesBootstrap
    public static readonly ErrorCode UgsProjectNotLinked = new ErrorCode("UGS_PROJECT_NOT_LINKED", Ugs,
        "Application.cloudProjectId 가 빔 — 프로젝트가 Unity Services 에 연결 안 됨",
        "Project Settings › Services 에서 프로젝트 연결(개발 문제)",
        "Unity Services 프로젝트 연결(cloudProjectId)이 설정되지 않았습니다.");
    public static readonly ErrorCode UgsInitFailed = new ErrorCode("UGS_INIT_FAILED", Ugs,
        "UnityServicesBootstrap.InitializeAsync — 초기화·익명 로그인 예외(상세 = 예외 메시지)",
        "인터넷 연결 확인 후 게임 재시작",
        "Unity Services 초기화 또는 익명 로그인에 실패했습니다.");
    public static readonly ErrorCode UgsNotInitialized = new ErrorCode("UGS_NOT_INITIALIZED", Ugs,
        "UnityServices.State == Uninitialized",
        "게임 재시작",
        "Unity Services가 초기화되지 않았습니다.");
    public static readonly ErrorCode UgsInitializing = new ErrorCode("UGS_INITIALIZING", Ugs,
        "Unity Services 초기화가 아직 끝나지 않음",
        "몇 초 뒤 재시도",
        "Unity Services 초기화 중입니다. 잠시 후 다시 시도하세요.");
    public static readonly ErrorCode UgsNotSignedIn = new ErrorCode("UGS_NOT_SIGNED_IN", Ugs,
        "초기화는 됐지만 익명 로그인이 안 됨",
        "인터넷 연결 확인 후 게임 재시작",
        "Unity Services 익명 로그인이 완료되지 않았습니다.");

    public static readonly IReadOnlyList<ErrorCode> All = new[]
    {
        LobbyLauncherMissing, LobbyUiMissing, LobbyNotAllReady,
        NetIpEmpty, NetIpInvalid, NetIpv4Invalid, NetPortInvalid, NetProviderMissing,
        NetStartCanceled, NetPrepareFailed, NetStartException, NetStartFailed, NetHostStartFailed, NetClientStartFailed,
        NetConnectFailed, NetRejectedByHost, NetDisconnected, NetPlayerDisconnected, NetTransportFailure,
        RelayTransportMissing, RelayJoinCodeEmpty, RelayJoinCodeInvalid, RelayInactiveProject, RelayUnauthorized,
        RelayPaymentRequired, RelayRateLimited, RelayUnavailable, RelayJoinRejected, RelayHostFailed, RelayJoinFailed,
        UgsProjectNotLinked, UgsInitFailed, UgsNotInitialized, UgsInitializing, UgsNotSignedIn,
    };
}
