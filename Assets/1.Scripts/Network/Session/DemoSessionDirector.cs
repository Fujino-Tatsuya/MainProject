using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 부스 데모 자동 접속·재접속 — PLAN-48 §4.2. NetworkManager.prefab 의 NetworkSessionLauncher 옆에 붙는다(DDOL).
/// 판단은 <see cref="ReconnectLoop"/>(순수)가 하고, 여기서는 NGO 이벤트를 루프에 넣고 루프의 명령을 실행만 한다.
/// <para>
/// 부팅: 설정 파일 로드(<see cref="DemoConnectionConfig.Load"/>, MPPM 태그 포함) → 타이틀 자동 로드 억제 →
/// UnityTransport 타임아웃 덮어쓰기 → 대기 씬(6.DemoConnectScene) 로컬 로드 → 루프 시작.
/// 호스트는 StartHost 성공 시 NGO LoadScene(3.LobbyScene, Single), 클라는 hostAddress:port 로 접속하고 NGO 동기화로 로비에 들어간다.
/// </para>
/// 켜지는 조건은 <see cref="DemoSessionRules.ShouldRunDemoFlow"/> — 0.BootStrapScene 에서 시작한 메인 흐름일 때만.
/// </summary>
[DisallowMultipleComponent]
public sealed class DemoSessionDirector : MonoBehaviour
{
    private const string DiagTag = "DemoSessionDirector";

    [Tooltip("끄면 부스 데모 흐름(설정 파일 자동 접속)을 쓰지 않고 종전처럼 타이틀 → 로비 수동 접속으로 간다.")]
    [SerializeField] private bool runDemoFlow = true;
    [SerializeField] private string lobbySceneName = "3.LobbyScene";
    [SerializeField] private string waitSceneName = "6.DemoConnectScene";
    [Tooltip("판 도중 재합류 오버레이(\"판에 합류하는 중…\")를 띄울 맵 씬 이름.")]
    [SerializeField] private string mapSceneName = "4.MapScene";

    /// <summary>이번 실행에서 데모 흐름이 켜졌는가. 로비의 옛 연결 패널을 숨기는 판단에 쓴다.</summary>
    public static bool IsDemoFlowActive { get; private set; }

    /// <summary>접속 승인(<see cref="DemoJoinRule"/>)이 "로비인가" 를 판단할 씬 이름.</summary>
    public string LobbySceneName => lobbySceneName;

    NetworkManager _networkManager;
    NetworkSessionLauncher _launcher;
    DemoConnectionLoadResult _loadResult;
    DemoConnectionConfig _config;
    ReconnectLoop _loop;
    DemoConnectionOverlay _overlay;

    float _bootTime;
    bool _waitingForWaitScene;
    bool _lobbyLoadPending;
    bool _callbacksRegistered;
    string _lastConfigHash;
    // 지금 떠 있는 맵에서 내 플레이어가 한 번이라도 있었는가 — 맵이 새로 로드되면 false(재합류 오버레이 판단).
    bool _hadLocalPlayerInMap;

    double Now => Time.realtimeSinceStartupAsDouble;

    private void Awake()
    {
        var activeScene = SceneManager.GetActiveScene().name;
        var isVirtualPlayer = false;
        var hasClientTag = false;
#if UNITY_EDITOR
        isVirtualPlayer = !Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
        hasClientTag = DemoConnectionConfig.HasClientTag(Unity.Multiplayer.PlayMode.CurrentPlayer.Tags);
#endif
        IsDemoFlowActive = DemoSessionRules.ShouldRunDemoFlow(runDemoFlow, activeScene, isVirtualPlayer, hasClientTag);
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.Awake",
            $"active={IsDemoFlowActive} toggle={runDemoFlow} activeScene={activeScene} " +
            $"virtualPlayer={isVirtualPlayer} clientTag={hasClientTag}");
        if (!IsDemoFlowActive)
        {
            enabled = false;
            return;
        }

        _networkManager = GetComponent<NetworkManager>();
        _launcher = GetComponent<NetworkSessionLauncher>();
        if (_networkManager == null || _launcher == null)
        {
            Debug.LogError($"[DemoSession] NetworkManager·NetworkSessionLauncher 가 같은 오브젝트에 없다 — 데모 흐름을 끈다. " +
                           $"networkManager={_networkManager != null} launcher={_launcher != null}", this);
            IsDemoFlowActive = false;
            enabled = false;
            return;
        }

        // GameManager.Start 의 타이틀 로드보다 앞서야 한다(DevSceneBooter 와 같은 이유로 Find 를 쓴다 — Awake 순서 무관).
        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
            gameManager.SuppressStartupSceneLoad();
        else
            Debug.LogWarning("[DemoSession] 씬에 GameManager 가 없다 — 타이틀 억제를 건너뛴다.", this);

        _loadResult = DemoConnectionConfig.Load();
        _config = _loadResult.Config;
        LogConfig();

        ApplyTransportTimeouts();
        _launcher.Mode = SessionConnectionMode.DirectIPv4;
        // 재접속해도 같은 키로 이어지게 설정 파일의 playerId 를 ConnectionData 로 보낸다(Q4, C6).
        _launcher.LocalPlayerId = _config.PlayerId;
        _loop = ReconnectLoop.FromConfig(_config);
        _overlay = DemoConnectionOverlay.Create();
        _bootTime = Time.realtimeSinceStartup;
    }

    private void Start()
    {
        // 첫 시도도 대기 씬에서 한다 — 부트스트랩 씬엔 카메라·배경이 없다. 호스트는 곧바로 NGO 로 로비를 Single 로드한다.
        _waitingForWaitScene = true;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Player.LocalPlayerChanged += HandleLocalPlayerChanged;
        RegisterNetworkCallbacks();
        SceneManager.LoadScene(waitSceneName, LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Player.LocalPlayerChanged -= HandleLocalPlayerChanged;
        UnregisterNetworkCallbacks();
        if (_overlay != null)
            Destroy(_overlay.gameObject);
        IsDemoFlowActive = false;
    }

    private void Update()
    {
        if (_loop == null)
            return;

        if (_networkManager.IsListening && _networkManager.NetworkConfig != null)
        {
            // GetConfig(false) — 기본값 true 는 해시를 캐시해 실제 접속에 영향을 준다.
            _lastConfigHash = _networkManager.NetworkConfig.GetConfig(false).ToString();
        }

        if (_loop.State == ReconnectState.ShuttingDown && !_networkManager.IsListening && !_networkManager.ShutdownInProgress)
            Execute(_loop.OnShutdownComplete(Now));

        if (!_waitingForWaitScene)
            Execute(_loop.Tick(Now));

        if (_lobbyLoadPending)
            TryLoadLobbyScene();

        RefreshOverlay();
    }

    private void HandleLocalPlayerChanged(Player player)
    {
        // 재합류 스폰이 끝나 내 플레이어가 생기면 "판에 합류하는 중…" 을 닫는다(다음 RefreshOverlay).
        if (player != null && IsMapActive())
            _hadLocalPlayerInMap = true;
    }

    private bool IsMapActive()
    {
        return SceneManager.GetActiveScene().name == mapSceneName;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == mapSceneName)
            _hadLocalPlayerInMap = false;

        if (!_waitingForWaitScene || scene.name != waitSceneName)
            return;

        _waitingForWaitScene = false;
        NetworkDiagnosticsLog.Log($"{DiagTag}.HandleSceneLoaded", $"대기 씬 로드 완료 state={_loop.State}");
        Execute(_loop.State == ReconnectState.Dropped ? _loop.OnDropCleanupComplete(Now) : _loop.Start(Now));
    }

    // ---- 명령 실행 ----

    private void Execute(ReconnectCommand command)
    {
        // 명령 실행이 곧바로 다음 이벤트를 낳는 경우(StartHost 결과 → 로비 로드 등)를 같은 프레임에 이어서 처리한다.
        while (command != ReconnectCommand.None)
        {
            NetworkDiagnosticsLog.Log(
                $"{DiagTag}.Execute",
                $"command={command} state={_loop.State} attempt={_loop.AttemptCount} reason={_loop.LastReason}");
            command = ExecuteOne(command);
        }
    }

    private ReconnectCommand ExecuteOne(ReconnectCommand command)
    {
        switch (command)
        {
            case ReconnectCommand.StartHost:
                _launcher.Mode = SessionConnectionMode.DirectIPv4;
                _launcher.OnSetConnectionData(DemoConnectionConfig.DefaultHostAddress, _config.Port);
                return _loop.OnHostStartResult(_launcher.StartHost(), Now);

            case ReconnectCommand.StartClient:
                _launcher.Mode = SessionConnectionMode.DirectIPv4;
                _launcher.OnSetConnectionData(_config.HostAddress, _config.Port);
                // 시작조차 못 했으면(이전 Shutdown 이 덜 끝남 등) 끊긴 것으로 넣어 Shutdown → Backoff 를 탄다.
                return _launcher.StartClient() ? ReconnectCommand.None : _loop.OnStopped(DemoJoinRejectReason.Unknown, Now);

            case ReconnectCommand.Shutdown:
                _lobbyLoadPending = false;
                _networkManager.Shutdown();
                return ReconnectCommand.None;

            case ReconnectCommand.CleanupAndLoadWaitScene:
                CleanupAfterDrop();
                return ReconnectCommand.None;

            case ReconnectCommand.LoadLobbyScene:
                _lobbyLoadPending = true;
                TryLoadLobbyScene();
                return ReconnectCommand.None;

            default:
                return ReconnectCommand.None;
        }
    }

    private void TryLoadLobbyScene()
    {
        var sceneManager = _networkManager.SceneManager;
        if (!_networkManager.IsServer || sceneManager == null)
            return;

        var status = sceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        if (status == SceneEventProgressStatus.SceneEventInProgress)
            return; // 다음 프레임에 다시

        _lobbyLoadPending = false;
        if (status != SceneEventProgressStatus.Started)
            Debug.LogError($"[DemoSession] 로비 NGO 로드 실패 scene={lobbySceneName} status={status}", this);
        NetworkDiagnosticsLog.Log($"{DiagTag}.TryLoadLobbyScene", $"scene={lobbySceneName} status={status}");
    }

    /// <summary>Dropped 정리 — 슬로우 배율 1 복구·직전 판 결과 비우기 → 대기 씬 로컬 Single 로드. 끝나면 HandleSceneLoaded.</summary>
    private void CleanupAfterDrop()
    {
        Time.timeScale = 1f;
        SessionResult.Clear();
        _waitingForWaitScene = true;
        SceneManager.LoadScene(waitSceneName, LoadSceneMode.Single);
    }

    // ---- NGO 이벤트 → 루프 ----

    private void RegisterNetworkCallbacks()
    {
        if (_callbacksRegistered)
            return;

        _networkManager.OnClientConnectedCallback += HandleClientConnected;
        if (_config.IsHost)
            _networkManager.OnServerStopped += HandleStopped;
        else
            _networkManager.OnClientStopped += HandleStopped;
        _callbacksRegistered = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (!_callbacksRegistered || _networkManager == null)
            return;

        _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _networkManager.OnServerStopped -= HandleStopped;
        _networkManager.OnClientStopped -= HandleStopped;
        _callbacksRegistered = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (_config.IsHost || clientId != _networkManager.LocalClientId)
            return;
        Execute(_loop.OnClientConnected(Now));
    }

    private void HandleStopped(bool wasHost)
    {
        var disconnectReason = _networkManager.DisconnectReason;
        var reason = DemoSessionRules.ResolveStopReason(_loop.State, disconnectReason);
        NetworkDiagnosticsLog.LogWarning(
            $"{DiagTag}.HandleStopped",
            $"wasHost={wasHost} state={_loop.State} reason={reason} disconnectReason='{disconnectReason}'");
        Execute(_loop.OnStopped(reason, Now));
    }

    // ---- 설정 ----

    private void ApplyTransportTimeouts()
    {
        var transport = GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogWarning("[DemoSession] UnityTransport 가 없다 — 타임아웃 덮어쓰기를 건너뛴다.", this);
            return;
        }

        var timeouts = DemoSessionRules.ComputeTransportTimeouts(_config.ConnectTimeoutSeconds, _config.DisconnectTimeoutSeconds);
        transport.ConnectTimeoutMS = timeouts.ConnectTimeoutMS;
        transport.MaxConnectAttempts = timeouts.MaxConnectAttempts;
        transport.DisconnectTimeoutMS = timeouts.DisconnectTimeoutMS;
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.ApplyTransportTimeouts",
            $"connect={timeouts.ConnectTimeoutMS}ms x{timeouts.MaxConnectAttempts} disconnect={timeouts.DisconnectTimeoutMS}ms");
    }

    private void LogConfig()
    {
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.LoadConfig",
            $"path={DemoConnectionConfig.ResolvePath(Application.dataPath)} missing={_loadResult.IsFileMissing} " +
            $"role={_config.Role} host={_config.HostAddress}:{_config.Port} playerId={_config.PlayerId} " +
            $"retry={_config.RetryIntervalSeconds}s connect={_config.ConnectTimeoutSeconds}s disconnect={_config.DisconnectTimeoutSeconds}s");
        foreach (var issue in _loadResult.Issues)
        {
            Debug.LogWarning($"[DemoSession] 설정 파일: {issue}", this);
            NetworkDiagnosticsLog.LogWarning($"{DiagTag}.LoadConfig", issue);
        }
    }

    // ---- 오버레이 ----

    private void RefreshOverlay()
    {
        if (_overlay == null)
            return;

        var isMapActive = IsMapActive();
        var hasLocalPlayer = Player.LocalPlayer != null;
        if (isMapActive && hasLocalPlayer)
            _hadLocalPlayerInMap = true; // 맵 활성 전에 스폰된 경우(정상 진입)도 잡는다

        var showStatus = DemoConnectionOverlayText.ShouldShowStatus(_loop.State);
        var showJoining = DemoConnectionOverlayText.ShouldShowJoiningMatch(_loop.State, isMapActive, hasLocalPlayer, _hadLocalPlayerInMap);
        if (showJoining)
            _overlay.SetStatus(true, DemoConnectUiStrings.JoiningMatch.Text);
        else
            _overlay.SetStatus(showStatus, showStatus ? DemoConnectionOverlayText.BuildStatus(BuildOverlayModel()) : string.Empty);

        var showNotice = DemoConnectionOverlayText.ShouldShowConfigMissing(_loadResult.IsFileMissing, Time.realtimeSinceStartup - _bootTime);
        _overlay.SetNotice(showNotice, showNotice ? DemoConnectUiStrings.ConfigMissing.Text : string.Empty);
    }

    private DemoConnectionOverlayModel BuildOverlayModel()
    {
        return new DemoConnectionOverlayModel(
            _config.Role,
            _loop.State,
            _config.IsHost ? _config.ListenAddress : _config.HostAddress,
            _config.Port,
            _loop.AttemptCount,
            _loop.SecondsUntilRetry(Now),
            _loop.LastReason,
            Application.version,
            _lastConfigHash);
    }
}
