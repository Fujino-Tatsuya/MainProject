using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkSessionLauncher : MonoBehaviour
{
    NetworkManager _networkManager;
    NetworkLoadingFlowController _loadingFlowController;
    DirectIPv4ConnectionProvider _directIPv4Provider;
    [SerializeField] private GameObject defaultPlayerPrefab;

    public SessionConnectionMode Mode { get; set; } = SessionConnectionMode.DirectIPv4;

    public event Action<SessionStartResult> SessionStartCompleted;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        _loadingFlowController = GetComponent<NetworkLoadingFlowController>();
        var unityTransport = GetComponent<UnityTransport>();
        if (unityTransport != null)
        {
            _directIPv4Provider = new DirectIPv4ConnectionProvider(unityTransport);
        }

        _loadingFlowController?.SetDefaultPlayerPrefab(defaultPlayerPrefab);
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.Awake hasNetworkManager={_networkManager != null} hasLoadingFlow={_loadingFlowController != null}");
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
        if (!TryGetProvider(out var provider, out var failureResult))
        {
            return failureResult;
        }

        var prepareResult = await PrepareHostAsync(provider, cancellationToken);
        if (!prepareResult.Success)
        {
            return prepareResult;
        }

        return StartHostCore()
            ? SessionStartResult.Succeeded(prepareResult.ShareCode)
            : SessionStartResult.Failed("Host 시작에 실패했습니다. 포트가 이미 사용 중인지 확인하세요.");
    }

    public async Task<SessionStartResult> StartClientAsync(
        string joinInput,
        CancellationToken cancellationToken)
    {
        if (!TryGetProvider(out var provider, out var failureResult))
        {
            return failureResult;
        }

        var prepareResult = await PrepareClientAsync(provider, joinInput, cancellationToken);
        if (!prepareResult.Success)
        {
            return prepareResult;
        }

        return StartClientCore()
            ? SessionStartResult.Succeeded(prepareResult.ShareCode)
            : SessionStartResult.Failed("Client 시작에 실패했습니다.");
    }

    public void BeginHost()
    {
        CompleteSessionStartAsync(StartHostAsync(CancellationToken.None));
    }

    public void BeginClient(string joinInput)
    {
        CompleteSessionStartAsync(StartClientAsync(joinInput, CancellationToken.None));
    }

    private bool StartHostCore()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartHost before listening={_networkManager.IsListening}");
        if (_networkManager.StartHost())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartHost success localClientId={_networkManager.LocalClientId}");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartHost failed");
        return false;
    }

    private bool StartClientCore()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient before listening={_networkManager.IsListening}");
        if (_networkManager.StartClient())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartClient success localClientId={_networkManager.LocalClientId}");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartClient failed");
        return false;
    }

    private bool StartServerCore()
    {
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.StartServer before listening={_networkManager.IsListening}");
        if (_networkManager.StartServer())
        {
            RegisterLoadingFlowCallbacks();
            Debug.Log("[SceneFlow] NetworkSessionLauncher.StartServer success");
            return true;
        }

        Debug.Log("[SceneFlow] NetworkSessionLauncher.StartServer failed");
        return false;
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
        provider = Mode == SessionConnectionMode.DirectIPv4 ? _directIPv4Provider : null;
        if (provider == null)
        {
            failureResult = SessionStartResult.Failed(
                $"{Mode} 연결 방식의 프로바이더가 등록되지 않았습니다.");
            return false;
        }

        if (!provider.IsAvailable(out var unavailableReason))
        {
            failureResult = SessionStartResult.Failed(unavailableReason);
            return false;
        }

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
            return SessionStartResult.Failed("세션 시작이 취소되었습니다.");
        }
        catch (Exception exception)
        {
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
            return SessionStartResult.Failed("세션 시작이 취소되었습니다.");
        }
        catch (Exception exception)
        {
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

        _loadingFlowController?.SetDefaultPlayerPrefab(defaultPlayerPrefab);
        _loadingFlowController?.RegisterNetworkCallbacks();
        Debug.Log($"[SceneFlow] NetworkSessionLauncher.RegisterLoadingFlowCallbacks done hasFlow={_loadingFlowController != null}");
    }

    private void OnApplicationQuit()
    {
        Debug.Log("[SceneFlow] NetworkSessionLauncher.OnApplicationQuit");
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }
}
