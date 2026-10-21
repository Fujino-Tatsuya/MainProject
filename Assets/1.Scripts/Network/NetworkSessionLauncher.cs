using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSessionLauncher : MonoBehaviour
{
    private const string DiagTag = "NetworkSessionLauncher";
    private const string DefaultLobbySceneName = "3.LobbyScene";

    /// <summary>
    /// NGO 의 내부 진단 로그를 Developer 수준까지 연다.
    ///
    /// 왜 필요한가 — 호스트가 클라를 끊는 두 경로 중 하나인
    /// "transport 연결은 붙었는데 connection request 메시지가 안 왔다"(<c>ClientConnectionBufferTimeout</c>)
    /// 경고는 <b>Developer 수준에서만</b> 찍힌다. 기본값 Normal 로는 호스트 로그가 한 줄도 없이
    /// 클라만 "Client-N disconnected by server." 를 받아 원인이 보이지 않는다.
    /// </summary>
    [Tooltip("NGO 내부 로그를 Developer 수준으로 올려 network.log 에 담는다. 접속 문제를 다 잡은 뒤 끄면 된다.")]
    [SerializeField] private bool verboseNetcodeLogging = true;

    NetworkManager _networkManager;
    NetworkLoadingFlowController _loadingFlowController;
    // Unity Relay 는 런타임 경로에서 뺐다(PLAN-48 Q6 — 유선 IPv4 만). RelayConnectionProvider.cs·패키지는 남겨 둔다.
    DirectIPv4ConnectionProvider _directIPv4Provider;
    UnityTransport _transport;

    public SessionConnectionMode Mode { get; set; } = SessionConnectionMode.DirectIPv4;

    string _localPlayerId;

    /// <summary>
    /// StartHost/StartClient 때 ConnectionData 로 보낼 playerId(PLAN-48 Q4). 기본은 PC 이름(MPPM 가상 플레이어는 인스턴스 접미사),
    /// 데모 흐름이면 DemoSessionDirector 가 설정 파일 값으로 바꾼다. 지연 초기화라 Awake 순서와 무관하다.
    /// </summary>
    public string LocalPlayerId
    {
        get => _localPlayerId ??= DemoConnectionConfig.ResolveLocalDefaultPlayerId();
        set => _localPlayerId = value;
    }

    /// <summary>
    /// 로비 카운트다운 중인가 — 접속 승인(<see cref="DemoJoinRule"/>)이 묻는다. <see cref="LobbySession"/> 이 스폰 때 넣고 디스폰 때 뺀다. 없으면 false.
    /// </summary>
    public static Func<bool> LobbyCountdownActiveProvider { get; set; }

    public event Action<SessionStartResult> SessionStartCompleted;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        if (_networkManager != null)
        {
            _networkManager.OnServerStopped += HandleSessionStopped;
            _networkManager.OnClientStopped += HandleSessionStopped;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            // 프리팹이 ConnectionApproval 을 켰으므로 콜백이 없으면 모든 접속이 승인 대기로 타임아웃된다 — 경로와 무관하게 여기서 건다.
            _networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        }

        _loadingFlowController = GetComponent<NetworkLoadingFlowController>();
        if (verboseNetcodeLogging && _networkManager != null && _networkManager.LogLevel > LogLevel.Developer)
        {
            _networkManager.LogLevel = LogLevel.Developer;
        }

        _transport = GetComponent<UnityTransport>();
        if (_transport != null)
        {
            _directIPv4Provider = new DirectIPv4ConnectionProvider(_transport);
        }

        Debug.Log($"[SceneFlow] NetworkSessionLauncher.Awake hasNetworkManager={_networkManager != null} hasLoadingFlow={_loadingFlowController != null}");
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.Awake",
            $"hasNetworkManager={_networkManager != null} hasTransport={_transport != null} " +
            $"hasDirectProvider={_directIPv4Provider != null} mode={Mode} " +
            $"ngoLogLevel={(_networkManager != null ? _networkManager.LogLevel.ToString() : "(없음)")}");
    }

    public bool StartHost()
    {
        return StartHostCore();
    }

    public bool StartClient()
    {
        return StartClientCore();
    }

    public bool StartServer()
    {
        return StartServerCore();
    }

    public void OnSetConnectionData(string ip)
    {
        SetDirectConnectionData(ip, 7777);
    }

    public void OnSetConnectionData(string ip, ushort port)
    {
        SetDirectConnectionData(ip, port);
    }

    public async Task<SessionStartResult> StartHostAsync(CancellationToken cancellationToken)
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartHostAsync", $"begin mode={Mode}");

        if (!TryGetProvider(out var provider, out var failureResult))
        {
            NetworkDiagnosticsLog.LogWarning(
                $"{DiagTag}.StartHostAsync", $"프로바이더 없음 error='{failureResult.Error.LogText}'");
            return failureResult;
        }

        var prepareResult = await PrepareHostAsync(provider, cancellationToken);
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.StartHostAsync",
            $"prepare success={prepareResult.Success} shareCode='{prepareResult.ShareCode}' " +
            $"error='{prepareResult.Error.LogText}'");
        if (!prepareResult.Success)
        {
            return prepareResult;
        }

        var started = StartHostCore();
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartHostAsync", $"end started={started}");
        return started
            ? SessionStartResult.Succeeded(prepareResult.ShareCode)
            : SessionStartResult.Failed(ErrorCatalog.NetHostStartFailed);
    }

    public async Task<SessionStartResult> StartClientAsync(
        string joinInput,
        CancellationToken cancellationToken)
    {
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.StartClientAsync", $"begin mode={Mode} joinInput='{joinInput}'");

        if (!TryGetProvider(out var provider, out var failureResult))
        {
            NetworkDiagnosticsLog.LogWarning(
                $"{DiagTag}.StartClientAsync", $"프로바이더 없음 error='{failureResult.Error.LogText}'");
            return failureResult;
        }

        var prepareResult = await PrepareClientAsync(provider, joinInput, cancellationToken);
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.StartClientAsync",
            $"prepare success={prepareResult.Success} error='{prepareResult.Error.LogText}'");
        if (!prepareResult.Success)
        {
            return prepareResult;
        }

        var started = StartClientCore();
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartClientAsync", $"end started={started}");
        return started
            ? SessionStartResult.Succeeded(prepareResult.ShareCode)
            : SessionStartResult.Failed(ErrorCatalog.NetClientStartFailed);
    }

    public void BeginHost()
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.BeginHost", $"mode={Mode}");
        CompleteSessionStartAsync(StartHostAsync(CancellationToken.None));
    }

    public void BeginClient(string joinInput)
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.BeginClient", $"mode={Mode} joinInput='{joinInput}'");
        CompleteSessionStartAsync(StartClientAsync(joinInput, CancellationToken.None));
    }

    private bool StartHostCore()
    {
        ServerCharacterSelectionStore.Clear();
        PrepareLocalConnectionData();
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartHost before listening={_networkManager.IsListening}");
        LogTransportSnapshot("StartHost.before");
        if (_networkManager.StartHost())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartHost success localClientId={_networkManager.LocalClientId}");
            LogNetworkConfigSnapshot("StartHost.after");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartHost failed");
        return false;
    }

    private bool StartClientCore()
    {
        ServerCharacterSelectionStore.Clear();
        PrepareLocalConnectionData();
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient before listening={_networkManager.IsListening}");
        LogTransportSnapshot("StartClient.before");
        if (_networkManager.StartClient())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient success localClientId={_networkManager.LocalClientId}");
            LogNetworkConfigSnapshot("StartClient.after");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartClient failed");
        return false;
    }

    private bool StartServerCore()
    {
        ServerCharacterSelectionStore.Clear();
        PlayerIdRegistry.Server.Clear();
        NetworkLoadingFlowController.EndMatchRoster("startServer");
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartServer before listening={_networkManager.IsListening}");
        LogTransportSnapshot("StartServer.before");
        if (_networkManager.StartServer())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log("[SceneFlow] NetworkSessionLauncher.StartServer success");
            LogNetworkConfigSnapshot("StartServer.after");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartServer failed");
        return false;
    }

    /// <summary>
    /// 새 세션 직전 — playerId 레지스트리를 비우고 내 playerId 를 ConnectionData 에 담는다.
    /// 호스트도 NGO 가 자기 ConnectionData 로 승인 콜백을 부르므로 같은 경로로 레지스트리에 들어간다.
    /// ConnectionData 는 NetworkConfig 해시 재료가 아니다.
    /// </summary>
    private void PrepareLocalConnectionData()
    {
        PlayerIdRegistry.Server.Clear();
        NetworkLoadingFlowController.EndMatchRoster("newSession");
        var payload = PlayerIdConnectionData.Encode(LocalPlayerId);
        _networkManager.NetworkConfig.ConnectionData = payload;
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.PrepareLocalConnectionData",
            $"playerId='{PlayerIdConnectionData.Normalize(LocalPlayerId)}' bytes={payload.Length}");
    }

    /// <summary>
    /// [서버] NGO 접속 승인 — PLAN-48 §4.8. 판단은 <see cref="DemoJoinRule.Evaluate"/> 가 한다.
    /// 데모 흐름이 아니면(Dev 부팅·태그 없는 MPPM·옛 수동 접속) 종전처럼 항상 승인하고 playerId 기록만 한다.
    /// NetworkConfig 해시가 다른 클라는 NGO 가 이 콜백 전에 사유 없이 끊는다(R6).
    /// </summary>
    private void HandleConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        var clientId = request.ClientNetworkId;
        var isHostSelf = clientId == NetworkManager.ServerClientId;
        var registry = PlayerIdRegistry.Server;
        var hasPlayerId = PlayerIdConnectionData.TryDecode(request.Payload, out var playerId);
        var isPlayerIdConnected = hasPlayerId && registry.IsPlayerConnected(playerId);
        var isInLobby = IsHostInLobby();
        var isCountdownActive = LobbyCountdownActiveProvider != null && LobbyCountdownActiveProvider();
        // 같은 프레임에 승인된 클라는 ConnectedClientsIds 에 아직 없다 — 승인 때 넣는 레지스트리 수와 큰 쪽을 쓴다.
        var connectedCount = Math.Max(registry.ConnectedCount, _networkManager.ConnectedClientsIds.Count);
        // 판 도중 재합류(C14) — 명단은 데모 흐름에서만 활성이다.
        var roster = MatchRoster.Server;
        var isMatchActive = roster.IsActive;
        var isInMatchRoster = hasPlayerId && roster.Contains(playerId);
        var isSceneTransition = _loadingFlowController != null && _loadingFlowController.IsSceneTransitionInProgress;

        var reason = DemoJoinRule.Evaluate(new DemoJoinRequest(
            isHostSelf,
            DemoSessionDirector.IsDemoFlowActive,
            isInLobby,
            isCountdownActive,
            connectedCount,
            DemoJoinRule.MaxPlayers,
            hasPlayerId,
            isPlayerIdConnected,
            isMatchActive,
            isInMatchRoster,
            isSceneTransition));

        response.Approved = reason == DemoJoinRejectReason.None;
        response.Reason = DemoJoinRule.ToApprovalReason(reason);
        // 콜백이 없던 때와 같게 — NGO 는 PlayerPrefab 이 있을 때만 플레이어 오브젝트를 만들었다(지금 프리팹은 비어 있다).
        response.CreatePlayerObject = _networkManager.NetworkConfig.PlayerPrefab != null;
        response.Pending = false;

        if (response.Approved)
        {
            // 데모 흐름 밖에선 playerId 가 없거나 겹쳐도 받으므로 clientId 로 구분되는 키를 쓴다.
            var registryKey = !hasPlayerId ? $"client-{clientId}"
                : isPlayerIdConnected ? $"{playerId}#{clientId}"
                : playerId;
            registry.Register(clientId, registryKey);
            // 재접속이면 그 playerId 의 마지막 캐릭터를 새 clientId 에 되살린다 — LobbySession 이 이 값으로 자리를 시드한다(Q4).
            ServerCharacterSelectionStore.BindPlayer(clientId, registryKey);
        }

        // 명단 묶기(MatchRoster.BindClient)와 재키잉 이벤트는 맵 재스폰 직후에 한다(R2c) — 승인만으로는 Map 에 캐릭터가 생기지 않는다.
        var isRejoin = response.Approved && !isHostSelf && isMatchActive && isInMatchRoster;
        var lastClientText = isRejoin && roster.TryGetLastClientId(playerId, out var lastClientId) ? lastClientId.ToString() : "-";

        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.HandleConnectionApproval",
            $"clientId={clientId} host={isHostSelf} demo={DemoSessionDirector.IsDemoFlowActive} playerId='{playerId}' " +
            $"validId={hasPlayerId} duplicate={isPlayerIdConnected} lobby={isInLobby} countdown={isCountdownActive} " +
            $"connected={connectedCount} match={isMatchActive} inRoster={isInMatchRoster} transition={isSceneTransition} " +
            $"rejoin={isRejoin} lastClientId={lastClientText} approved={response.Approved} reason={reason}");
    }

    /// <summary>호스트의 활성 씬이 로비이고 로딩 흐름(로비 → 맵 등)이 돌고 있지 않다.</summary>
    private bool IsHostInLobby()
    {
        var director = GetComponent<DemoSessionDirector>();
        var lobbySceneName = director != null ? director.LobbySceneName : DefaultLobbySceneName;
        if (!string.Equals(SceneManager.GetActiveScene().name, lobbySceneName, StringComparison.Ordinal))
            return false;
        return _loadingFlowController == null || !_loadingFlowController.IsFlowInProgress;
    }

    /// <summary>
    /// 전송 계층이 실제로 무엇을 들고 있는지 남긴다 — Relay 를 골랐는데
    /// <c>Protocol</c> 이 <c>UnityTransport</c> 면 <c>SetRelayServerData</c> 가 안 먹은 것이다.
    /// </summary>
    private void LogTransportSnapshot(string context)
    {
        if (_transport == null)
        {
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.{context}", "UnityTransport 가 없습니다.");
            return;
        }

        var connection = _transport.ConnectionData;
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.{context}",
            $"mode={Mode} protocol={_transport.Protocol} address={connection.Address}:{connection.Port} " +
            $"listen={connection.ServerListenAddress} webSockets={_transport.UseWebSockets} " +
            $"encryption={_transport.UseEncryption} maxPayload={_transport.MaxPayloadSize} " +
            $"listening={_networkManager != null && _networkManager.IsListening}");
    }

    /// <summary>
    /// 접속 거부의 결정적 증거. NGO 는 호스트와 클라의 <see cref="NetworkConfig"/> 해시가
    /// 다르면 아무 설명 없이 <c>"Client-N disconnected by server."</c> 로 끊는다
    /// (<c>ConnectionRequestMessage.Deserialize</c> → <c>CompareConfig</c>).
    /// 해시 재료(프리팹 GlobalObjectIdHash 목록·TickRate·플래그)를 통째로 남겨
    /// 양쪽 network.log 를 diff 하면 어느 항목이 어긋났는지 바로 드러나게 한다.
    ///
    /// ⚠️ <c>GetConfig(false)</c> 로 부른다 — 기본값 <c>true</c> 는 해시를 캐시해버려서
    /// 진단 호출이 실제 접속에 쓰일 해시를 고정시키는 부작용이 생긴다.
    /// </summary>
    private void LogNetworkConfigSnapshot(string context)
    {
        LogTransportSnapshot(context);

        if (_networkManager == null)
        {
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.{context}", "NetworkManager 가 없습니다.");
            return;
        }

        var config = _networkManager.NetworkConfig;
        if (config == null)
        {
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.{context}", "NetworkConfig 가 null 입니다.");
            return;
        }

        var lines = new List<string>
        {
            $"configHash          = {config.GetConfig(false)}",
            $"protocolVersion     = {config.ProtocolVersion}",
            $"tickRate            = {config.TickRate}",
            $"connectionApproval  = {config.ConnectionApproval}",
            $"forceSamePrefabs    = {config.ForceSamePrefabs}",
            $"enableSceneMgmt     = {config.EnableSceneManagement}",
            $"varLengthSafety     = {config.EnsureNetworkVariableLengthSafety}",
            $"rpcHashSize         = {config.RpcHashSize}",
            $"playerPrefab        = {(config.PlayerPrefab != null ? config.PlayerPrefab.name : "(없음)")}",
        };

        var prefabs = config.Prefabs;
        if (prefabs == null)
        {
            lines.Add("prefabs             = (null)");
        }
        else
        {
            var links = prefabs.NetworkPrefabOverrideLinks;
            lines.Add($"prefabLists         = {prefabs.NetworkPrefabsLists.Count}");
            lines.Add($"prefabOverrideLinks = {links.Count}");

            // 정렬 순서는 NetworkConfig.GetConfig 가 해시를 만들 때 쓰는 순서와 같다.
            foreach (var entry in links.OrderBy(pair => pair.Key))
            {
                var prefabName = entry.Value != null && entry.Value.Prefab != null
                    ? entry.Value.Prefab.name
                    : "(없음)";
                lines.Add($"    {entry.Key,12} {prefabName}");
            }
        }

        NetworkDiagnosticsLog.LogBlock($"{DiagTag}.{context} NetworkConfig", lines);
    }

    private void SetDirectConnectionData(string ip, ushort port)
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.OnSetConnectionData ip={ip} port={port} listen=0.0.0.0");

        // 비동기 경로는 TryGetProvider 가 프로바이더 부재를 사유와 함께 돌려주는데, 동기 레거시
        // 경로에는 그 가드가 없어 NRE 로 죽었다. 원인을 알 수 있게 사유를 남긴다.
        if (_directIPv4Provider == null)
        {
            Debug.LogError(
                "[SceneFlow] NetworkSessionLauncher: 같은 GameObject 에 UnityTransport 가 없어 연결 데이터를 " +
                "설정할 수 없습니다. NetworkManager 프리팹 배선을 확인하세요.", this);
            return;
        }

        _directIPv4Provider.SetConnectionData(ip, port);
    }

    private bool TryGetProvider(
        out ISessionConnectionProvider provider,
        out SessionStartResult failureResult)
    {
        // DirectIPv4 만 연결한다 — 그 밖의 모드(Relay·Steam)는 "프로바이더 없음" 으로 실패한다.
        provider = Mode == SessionConnectionMode.DirectIPv4 ? _directIPv4Provider : null;
        if (provider == null)
        {
            failureResult = SessionStartResult.Failed(
                ErrorCatalog.NetProviderMissing, ErrorDisplay.Arg("mode", Mode.ToString()));
            return false;
        }

        if (!provider.IsAvailable(out var unavailable))
        {
            NetworkDiagnosticsLog.LogWarning(
                $"{DiagTag}.TryGetProvider",
                $"mode={Mode} 사용 불가 error='{unavailable.LogText}'");
            failureResult = SessionStartResult.Failed(unavailable);
            return false;
        }

        NetworkDiagnosticsLog.Log($"{DiagTag}.TryGetProvider", $"mode={Mode} provider={provider.GetType().Name}");

        failureResult = default;
        return true;
    }

    private static async Task<SessionStartResult> PrepareHostAsync(
        ISessionConnectionProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.PrepareHostAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.PrepareHostAsync", "취소되었습니다.");
            return SessionStartResult.Failed(ErrorCatalog.NetStartCanceled);
        }
        catch (Exception exception)
        {
            NetworkDiagnosticsLog.LogError($"{DiagTag}.PrepareHostAsync", $"예외 {exception}");
            return SessionStartResult.Failed(ErrorCatalog.NetPrepareFailed, detail: exception.Message);
        }
    }

    private static async Task<SessionStartResult> PrepareClientAsync(
        ISessionConnectionProvider provider,
        string joinInput,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.PrepareClientAsync(joinInput, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.PrepareClientAsync", "취소되었습니다.");
            return SessionStartResult.Failed(ErrorCatalog.NetStartCanceled);
        }
        catch (Exception exception)
        {
            NetworkDiagnosticsLog.LogError($"{DiagTag}.PrepareClientAsync", $"예외 {exception}");
            return SessionStartResult.Failed(ErrorCatalog.NetPrepareFailed, detail: exception.Message);
        }
    }

    /// <summary>
    /// ⚠️ <c>async void</c> 는 예외를 호출자가 잡을 수 없다. 여기서 새는 예외는 아무 로그도 없이
    /// 사라져 "버튼을 눌렀는데 아무 일도 안 난다"가 된다. Prepare 단계는 이미 감싸져 있지만
    /// <c>NetworkManager.Start*()</c> 와 구독자 콜백은 밖에 있으므로 여기서 최종 방어한다.
    /// </summary>
    private async void CompleteSessionStartAsync(Task<SessionStartResult> startTask)
    {
        SessionStartResult result;
        try
        {
            result = await startTask;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SceneFlow] NetworkSessionLauncher 세션 시작 중 예외: {exception}", this);
            result = SessionStartResult.Failed(ErrorCatalog.NetStartException, detail: exception.Message);
        }

        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.CompleteSessionStartAsync",
            $"success={result.Success} shareCode='{result.ShareCode}' error='{result.Error.LogText}'");

        try
        {
            SessionStartCompleted?.Invoke(result);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SceneFlow] SessionStartCompleted 구독자에서 예외: {exception}", this);
        }
    }

    public void StartGameLoading()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartGameLoading hasFlow={_loadingFlowController != null}");
        RegisterLoadingFlowCallbacks();
        _loadingFlowController?.StartGameLoading();
    }

    private void RegisterLoadingFlowCallbacks()
    {
        if (_loadingFlowController == null)
        {
            _loadingFlowController = GetComponent<NetworkLoadingFlowController>();
        }

        if (_loadingFlowController == null)
        {
            _loadingFlowController = gameObject.AddComponent<NetworkLoadingFlowController>();
            Debug.Log("[SceneFlow] NetworkSessionLauncher.RegisterLoadingFlowCallbacks added NetworkLoadingFlowController");
        }

        _loadingFlowController?.RegisterNetworkCallbacks();
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.RegisterLoadingFlowCallbacks done hasFlow={_loadingFlowController != null}");
    }

    private void OnApplicationQuit()
    {
        Debug.Log("[SceneFlow] NetworkSessionLauncher.OnApplicationQuit");
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.OnApplicationQuit",
            $"listening={NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening}");

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // Shutdown 이 남기는 마지막 줄까지 담고 디스크에 밀어 넣는다.
        // 닫기는 프로세스 종료 시점의 NetworkDiagnosticsLog 가 알아서 한다.
        NetworkDiagnosticsLog.MarkSessionEnd();
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.OnServerStopped -= HandleSessionStopped;
            _networkManager.OnClientStopped -= HandleSessionStopped;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            if (_networkManager.ConnectionApprovalCallback == HandleConnectionApproval)
            {
                _networkManager.ConnectionApprovalCallback = null;
            }
        }
    }

    private void HandleSessionStopped(bool wasHost)
    {
        ServerCharacterSelectionStore.Clear();
        PlayerIdRegistry.Server.Clear();
        NetworkLoadingFlowController.EndMatchRoster("sessionStopped");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        // 판 명단 자리는 남기고 clientId 묶임만 푼다 — 같은 playerId 가 다시 오면 재합류로 받는다(C14).
        MatchRoster.Server.UnbindClient(clientId);
        // clientId 쪽 선택만 지운다 — playerId 의 마지막 선택은 남아 재접속 승인 때 되살아난다(Q4).
        ServerCharacterSelectionStore.Remove(clientId);
        // clientId 매핑만 지운다 — 재접속하면 새 clientId 로 같은 playerId 를 다시 묶는다(Q4).
        PlayerIdRegistry.Server.RemoveClient(clientId);
    }
}
