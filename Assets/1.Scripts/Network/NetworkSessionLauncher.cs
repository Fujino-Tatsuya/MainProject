using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkSessionLauncher : MonoBehaviour
{
    private const string DiagTag = "NetworkSessionLauncher";

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
    DirectIPv4ConnectionProvider _directIPv4Provider;
    RelayConnectionProvider _relayProvider;
    UnityTransport _transport;

    public SessionConnectionMode Mode { get; set; } = SessionConnectionMode.DirectIPv4;

    public event Action<SessionStartResult> SessionStartCompleted;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        _loadingFlowController = GetComponent<NetworkLoadingFlowController>();
        if (verboseNetcodeLogging && _networkManager != null && _networkManager.LogLevel > LogLevel.Developer)
        {
            _networkManager.LogLevel = LogLevel.Developer;
        }

        _transport = GetComponent<UnityTransport>();
        if (_transport != null)
        {
            _directIPv4Provider = new DirectIPv4ConnectionProvider(_transport);
            _relayProvider = new RelayConnectionProvider(_transport);
        }

        Debug.Log($"[SceneFlow] NetworkSessionLauncher.Awake hasNetworkManager={_networkManager != null} hasLoadingFlow={_loadingFlowController != null}");
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.Awake",
            $"hasNetworkManager={_networkManager != null} hasTransport={_transport != null} " +
            $"hasDirectProvider={_directIPv4Provider != null} hasRelayProvider={_relayProvider != null} mode={Mode} " +
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
                $"{DiagTag}.StartHostAsync", $"프로바이더 없음 reason='{failureResult.FailureReason}'");
            return failureResult;
        }

        var prepareResult = await PrepareHostAsync(provider, cancellationToken);
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.StartHostAsync",
            $"prepare success={prepareResult.Success} shareCode='{prepareResult.ShareCode}' " +
            $"reason='{prepareResult.FailureReason}'");
        if (!prepareResult.Success)
        {
            return prepareResult;
        }

        var started = StartHostCore();
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartHostAsync", $"end started={started}");
        return started
            ? SessionStartResult.Succeeded(prepareResult.ShareCode)
            : SessionStartResult.Failed("Host 시작에 실패했습니다. 포트가 이미 사용 중인지 확인하세요.");
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
                $"{DiagTag}.StartClientAsync", $"프로바이더 없음 reason='{failureResult.FailureReason}'");
            return failureResult;
        }

        var prepareResult = await PrepareClientAsync(provider, joinInput, cancellationToken);
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.StartClientAsync",
            $"prepare success={prepareResult.Success} reason='{prepareResult.FailureReason}'");
        if (!prepareResult.Success)
        {
            return prepareResult;
        }

        var started = StartClientCore();
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartClientAsync", $"end started={started}");
        return started
            ? SessionStartResult.Succeeded(prepareResult.ShareCode)
            : SessionStartResult.Failed("Client 시작에 실패했습니다.");
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
        provider = Mode switch
        {
            SessionConnectionMode.DirectIPv4 => _directIPv4Provider,
            SessionConnectionMode.UnityRelay => _relayProvider,
            _ => null
        };
        if (provider == null)
        {
            failureResult = SessionStartResult.Failed(
                $"{Mode} 연결 방식의 프로바이더가 등록되지 않았습니다.");
            return false;
        }

        if (!provider.IsAvailable(out var unavailableReason))
        {
            NetworkDiagnosticsLog.LogWarning(
                $"{DiagTag}.TryGetProvider",
                $"mode={Mode} 사용 불가 reason='{unavailableReason}'");
            failureResult = SessionStartResult.Failed(unavailableReason);
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
            return SessionStartResult.Failed("세션 시작이 취소되었습니다.");
        }
        catch (Exception exception)
        {
            NetworkDiagnosticsLog.LogError($"{DiagTag}.PrepareHostAsync", $"예외 {exception}");
            return SessionStartResult.Failed($"세션 연결 준비에 실패했습니다: {exception.Message}");
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
            return SessionStartResult.Failed("세션 시작이 취소되었습니다.");
        }
        catch (Exception exception)
        {
            NetworkDiagnosticsLog.LogError($"{DiagTag}.PrepareClientAsync", $"예외 {exception}");
            return SessionStartResult.Failed($"세션 연결 준비에 실패했습니다: {exception.Message}");
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
            result = SessionStartResult.Failed($"세션 시작 중 예외가 발생했습니다: {exception.Message}");
        }

        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.CompleteSessionStartAsync",
            $"success={result.Success} shareCode='{result.ShareCode}' reason='{result.FailureReason}'");

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
}
