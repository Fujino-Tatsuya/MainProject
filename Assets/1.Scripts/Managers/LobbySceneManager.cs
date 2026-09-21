using System.Net;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbySceneManager : NemoSceneManager
{
    private const string DiagTag = "LobbySceneManager";

    [Header("Connection Inputs")]
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private TMP_InputField portInputField;
    [SerializeField] private Button setConnectionDataButton;

    [Header("Buttons")]
    [SerializeField] private Button startHostButton;
    [SerializeField] private Button startClientButton;
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button readyButton;

    [Header("Panels")]
    [SerializeField] private GameObject sessionConnectPanel;

    [Header("Messages")]
    [SerializeField] private TMP_Text errorText;

    [Header("Connection Defaults")]
    [SerializeField] private string defaultIp = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;

    [Header("Relay Connection")]
    [SerializeField] private GameObject relayPanel;
    [SerializeField] private GameObject directPanel;
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private TMP_Text joinCodeDisplayText;
    [SerializeField] private Button relayHostButton;
    [SerializeField] private Button relayJoinButton;
    [SerializeField] private Button modeToggleButton;

    private NetworkManager _networkManager;
    private NetworkSessionLauncher _sessionLauncher;
    private LobbyUIController _lobbyUIController;
    private bool _connectionDataApplied;
    private bool _clientConnectPending;
    private bool _networkCallbacksRegistered;
    private bool _relayStartPending;
    private bool _relayHostPending;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] LobbySceneManager.Awake");
        _networkManager = GetNetworkManager();
        _sessionLauncher = _networkManager != null ? _networkManager.GetComponent<NetworkSessionLauncher>() : null;

        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
        }

        ResolveSceneReferences();
        BindButtons();
        RegisterNetworkCallbacks();
        RegisterSessionLauncherEvents();

        // 빌드에서만 나는 문제는 이름 기반 해석이 실패한 경우가 많다 — 어떤 참조가 비었는지
        // 한 줄로 남겨 둔다. (ResolveSceneReferences 의 WarnIfMissing 은 콘솔로만 간다.)
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.Awake",
            $"networkManager={_networkManager != null} sessionLauncher={_sessionLauncher != null} " +
            $"relayPanel={relayPanel != null} directPanel={directPanel != null} " +
            $"relayHostButton={relayHostButton != null} relayJoinButton={relayJoinButton != null} " +
            $"modeToggleButton={modeToggleButton != null} joinCodeInput={joinCodeInputField != null} " +
            $"joinCodeDisplay={joinCodeDisplayText != null} errorText={errorText != null}");
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] LobbySceneManager.Start");
        PlayEnterFade();
        ApplyDefaultInputValues();
        SetErrorMessage(string.Empty);
        RegisterLobbyUiEvents();
        SelectDirectMode();
        ApplyRoleUi();

        // BGM 재생
        AudioManager.Instance.PlayBGM(AudioManager.Instance.Catalog.LobbyBGM);
    }

    private void OnDestroy()
    {
        UnregisterNetworkCallbacks();
        UnregisterSessionLauncherEvents();
        UnregisterLobbyUiEvents();
    }

    public void ApplyConnectionData()
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.ApplyConnectionData", "버튼 입력");
        TryApplyConnectionData();
    }

    public void StartHost()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.StartHost hasSessionLauncher={_sessionLauncher != null}");
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartHost", $"버튼 입력 hasSessionLauncher={_sessionLauncher != null}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            SetErrorMessage("네트워크 세션 런처를 찾을 수 없습니다.");
            return;
        }

        if (!EnsureConnectionData())
        {
            return;
        }

        if (_sessionLauncher.StartHost())
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartHost succeeded");
            SetErrorMessage($"Host 시작됨 ({DescribeConfigHash()})");
            SetSessionConnectPanel(false);
            ApplyRoleUi();
        }
        else
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartHost failed");
            SetErrorMessage("Host 시작에 실패했습니다. 포트가 이미 사용 중인지 확인하세요.");
        }
    }

    public void StartClient()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.StartClient hasSessionLauncher={_sessionLauncher != null}");
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartClient", $"버튼 입력 hasSessionLauncher={_sessionLauncher != null}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            SetErrorMessage("네트워크 세션 런처를 찾을 수 없습니다.");
            return;
        }

        if (!EnsureConnectionData())
        {
            return;
        }

        if (_sessionLauncher.StartClient())
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartClient connecting");
            _clientConnectPending = true;
            SetErrorMessage($"서버 접속 시도 중... ({DescribeConfigHash()})");
            SetConnectControlsInteractable(false);
        }
        else
        {
            Debug.Log("[SceneFlow] LobbySceneManager.StartClient failed");
            SetErrorMessage("Client 시작에 실패했습니다.");
        }
    }

    public void SelectDirectMode()
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.SelectDirectMode", $"hasSessionLauncher={_sessionLauncher != null}");

        if (_sessionLauncher != null)
        {
            _sessionLauncher.Mode = SessionConnectionMode.DirectIPv4;
        }

        SetConnectionModePanels(false);
        SetErrorMessage(string.Empty);
    }

    public void SelectRelayMode()
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.SelectRelayMode", $"hasSessionLauncher={_sessionLauncher != null}");

        if (_sessionLauncher != null)
        {
            _sessionLauncher.Mode = SessionConnectionMode.UnityRelay;
        }

        SetConnectionModePanels(true);
        SetErrorMessage(string.Empty);
    }

    public void StartRelayHost()
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartRelayHost", "버튼 입력");

        if (!TryBeginRelayStart(true))
        {
            return;
        }

        if (joinCodeDisplayText != null)
        {
            joinCodeDisplayText.text = string.Empty;
        }

        SetErrorMessage("Relay 방 생성 중...");
        _sessionLauncher.BeginHost();
    }

    public void StartRelayJoin()
    {
        var joinCode = joinCodeInputField != null ? joinCodeInputField.text : string.Empty;
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.StartRelayJoin",
            $"버튼 입력 hasInputField={joinCodeInputField != null} joinCode='{joinCode}'");

        if (string.IsNullOrWhiteSpace(joinCode))
        {
            SetErrorMessage("Relay 조인코드를 입력하세요.");
            return;
        }

        if (!TryBeginRelayStart(false))
        {
            return;
        }

        SetErrorMessage("Relay 방 참가 준비 중...");
        _sessionLauncher.BeginClient(joinCode);
    }

    public void ToggleReady()
    {
        var controller = ResolveLobbyUIController();
        Debug.Log($"[SceneFlow] LobbySceneManager.ToggleReady hasLobbyUIController={controller != null}");
        NetworkDiagnosticsLog.Log($"{DiagTag}.ToggleReady", $"버튼 입력 hasLobbyUIController={controller != null}");
        if (controller == null)
        {
            WarnMissingReference(nameof(LobbyUIController));
            SetErrorMessage("로비 UI 컨트롤러를 찾을 수 없습니다.");
            return;
        }

        controller.ToggleLocalReady();
    }

    public void StartGameLoading()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.StartGameLoading hasSessionLauncher={_sessionLauncher != null}");
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.StartGameLoading",
            $"버튼 입력 hasSessionLauncher={_sessionLauncher != null} " +
            $"isHost={_networkManager != null && _networkManager.IsHost} " +
            $"connectedCount={(_networkManager != null && _networkManager.IsServer ? _networkManager.ConnectedClientsIds.Count : -1)}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            SetErrorMessage("네트워크 세션 런처를 찾을 수 없습니다.");
            return;
        }

        var controller = ResolveLobbyUIController();
        if (controller != null && !controller.CanStartGame)
        {
            SetErrorMessage("모든 플레이어가 준비되지 않았습니다.");
            return;
        }

        SetErrorMessage(string.Empty);
        SetButtonsInteractable(false, gameStartButton);
        _sessionLauncher.StartGameLoading();
    }

    private bool EnsureConnectionData()
    {
        if (_connectionDataApplied)
        {
            return true;
        }

        return TryApplyConnectionData();
    }

    private bool TryApplyConnectionData()
    {
        var ipText = ipInputField != null ? ipInputField.text.Trim() : defaultIp;
        var portText = portInputField != null ? portInputField.text.Trim() : defaultPort.ToString();

        if (string.IsNullOrEmpty(ipText))
        {
            SetErrorMessage("IP를 입력하세요.");
            return false;
        }

        if (!IPAddress.TryParse(ipText, out _))
        {
            SetErrorMessage($"IP 형식이 올바르지 않습니다: {ipText}");
            return false;
        }

        if (!ushort.TryParse(portText, out var port) || port == 0)
        {
            SetErrorMessage($"Port는 1~65535 범위의 숫자여야 합니다: {portText}");
            return false;
        }

        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            SetErrorMessage("네트워크 세션 런처를 찾을 수 없습니다.");
            return false;
        }

        _sessionLauncher.OnSetConnectionData(ipText, port);
        _connectionDataApplied = true;
        NetworkDiagnosticsLog.Log($"{DiagTag}.TryApplyConnectionData", $"적용 {ipText}:{port}");
        SetErrorMessage($"연결 대상 설정: {ipText}:{port}");
        return true;
    }

    private void ApplyDefaultInputValues()
    {
        if (ipInputField != null && string.IsNullOrWhiteSpace(ipInputField.text))
        {
            ipInputField.text = defaultIp;
        }

        if (portInputField != null && string.IsNullOrWhiteSpace(portInputField.text))
        {
            portInputField.text = defaultPort.ToString();
        }
    }

    // Host는 GameStart만, Client는 Ready만 노출한다. 네트워크 시작 전에는 패널이 화면을 덮는다.
    private void ApplyRoleUi()
    {
        var listening = _networkManager != null && _networkManager.IsListening;
        var isHost = listening && _networkManager.IsHost;
        var controller = ResolveLobbyUIController();

        if (gameStartButton != null)
        {
            gameStartButton.gameObject.SetActive(!listening || isHost);
            gameStartButton.interactable = isHost && controller != null && controller.CanStartGame;
        }

        if (readyButton != null)
        {
            readyButton.gameObject.SetActive(!listening || !isHost);
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.HandleClientConnected",
            $"clientId={clientId} localClientId={(_networkManager != null ? _networkManager.LocalClientId : 0)} " +
            $"isHost={_networkManager != null && _networkManager.IsHost} " +
            $"isServer={_networkManager != null && _networkManager.IsServer} " +
            $"connectedCount={(_networkManager != null && _networkManager.IsServer ? _networkManager.ConnectedClientsIds.Count : -1)}");

        if (_networkManager == null || clientId != _networkManager.LocalClientId)
        {
            return;
        }

        Debug.Log($"[SceneFlow] LobbySceneManager.HandleClientConnected localClientId={clientId}");
        _clientConnectPending = false;
        SetErrorMessage(string.Empty);
        SetSessionConnectPanel(false);
        SetConnectControlsInteractable(true);
        SetRelayControlsInteractable(true);
        ApplyRoleUi();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (_networkManager == null)
        {
            return;
        }

        Debug.Log($"[SceneFlow] LobbySceneManager.HandleClientDisconnected clientId={clientId} isServer={_networkManager.IsServer} reason='{_networkManager.DisconnectReason}'");

        // 🔴 여기가 지금 문제의 착지점이다. reason 이 "Client-N disconnected by server." 면
        // 릴레이는 뚫린 것이고 호스트가 거절한 것이다 — NGO 는 NetworkConfig 해시가 어긋나면
        // 이 문구로 끊는다(ConnectionRequestMessage.Deserialize → CompareConfig).
        // 호스트 쪽 network.log 의 "NetworkConfig mismatch" 경고와 configHash 블록을 대조할 것.
        NetworkDiagnosticsLog.LogWarning(
            $"{DiagTag}.HandleClientDisconnected",
            $"clientId={clientId} localClientId={_networkManager.LocalClientId} " +
            $"isServer={_networkManager.IsServer} connectPending={_clientConnectPending} " +
            $"reason='{_networkManager.DisconnectReason}'");

        if (_networkManager.IsServer)
        {
            if (clientId != _networkManager.LocalClientId)
            {
                SetErrorMessage($"플레이어(ClientId={clientId})의 연결이 끊어졌습니다.");
            }

            return;
        }

        if (clientId != _networkManager.LocalClientId)
        {
            return;
        }

        // 호스트가 명시적으로 끊은 경우는 "접속이 안 된다"와 원인이 전혀 다르다.
        // 여기까지 왔다는 건 릴레이·방화벽은 뚫렸고 호스트가 거절했다는 뜻이라,
        // IP/Port 를 확인하라는 기존 안내는 오히려 사람을 엉뚱한 데로 보낸다.
        var rejectedByHost = _networkManager.DisconnectReason != null &&
                             _networkManager.DisconnectReason.Contains("disconnected by server");

        if (rejectedByHost)
        {
            SetErrorMessage(
                $"호스트가 접속을 거절했습니다. 양쪽 빌드가 같은 버전인지 확인하세요. (내 {DescribeConfigHash()})");
        }
        else
        {
            SetErrorMessage(_clientConnectPending
                ? "서버 접속에 실패했습니다. IP/Port와 Host 상태를 확인하세요."
                : "서버와의 연결이 끊어졌습니다.");
        }
        _clientConnectPending = false;
        SetSessionConnectPanel(true);
        SetConnectControlsInteractable(true);
        SetRelayControlsInteractable(true);
        ApplyRoleUi();
    }

    private void HandleTransportFailure()
    {
        Debug.LogError("[SceneFlow] LobbySceneManager.HandleTransportFailure");
        NetworkDiagnosticsLog.LogError(
            $"{DiagTag}.HandleTransportFailure",
            $"connectPending={_clientConnectPending} relayPending={_relayStartPending}");
        SetErrorMessage("네트워크 전송 오류가 발생했습니다.");
        _clientConnectPending = false;
        SetSessionConnectPanel(true);
        SetConnectControlsInteractable(true);
        SetRelayControlsInteractable(true);
        ApplyRoleUi();
    }

    private bool TryBeginRelayStart(bool hosting)
    {
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            SetErrorMessage("네트워크 세션 런처를 찾을 수 없습니다.");
            return false;
        }

        if (_relayStartPending)
        {
            NetworkDiagnosticsLog.LogWarning(
                $"{DiagTag}.TryBeginRelayStart", $"이미 진행 중이라 무시 hosting={hosting}");
            return false;
        }

        _sessionLauncher.Mode = SessionConnectionMode.UnityRelay;
        _relayStartPending = true;
        _relayHostPending = hosting;
        SetConnectControlsInteractable(false);
        SetRelayControlsInteractable(false);
        NetworkDiagnosticsLog.Log($"{DiagTag}.TryBeginRelayStart", $"시작 hosting={hosting} mode={_sessionLauncher.Mode}");
        return true;
    }

    /// <summary>
    /// 접속 거절을 <b>화면에서 바로</b> 대조할 수 있게 만드는 짧은 식별자.
    ///
    /// 왜 화면에 띄우나 — 호스트와 클라가 서로 다른 PC 에 있으면 한쪽 network.log 만으로는
    /// 아무것도 못 가린다. 파일을 주고받는 대신 두 사람이 이 숫자만 맞춰 보면 된다.
    /// NGO 는 이 값이 다르면 설명 없이 "Client-N disconnected by server." 로 끊는다.
    ///
    /// ⚠️ <c>NetworkManager</c> 초기화 뒤(= <c>Start*</c> 이후)에만 의미가 있다.
    /// 그 전에는 프리팹 링크가 비어 있어 실제 접속에 쓰일 값과 다르다.
    /// ⚠️ <c>GetConfig(false)</c> — 기본값 <c>true</c> 는 해시를 캐시해 실제 접속에 영향을 준다.
    /// </summary>
    private string DescribeConfigHash()
    {
        if (_networkManager == null || _networkManager.NetworkConfig == null)
        {
            return "cfg=?";
        }

        return $"cfg={_networkManager.NetworkConfig.GetConfig(false)}";
    }

    private void HandleSessionStartCompleted(SessionStartResult result)
    {
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.HandleSessionStartCompleted",
            $"success={result.Success} shareCode='{result.ShareCode}' reason='{result.FailureReason}' " +
            $"relayPending={_relayStartPending} hostPending={_relayHostPending}");

        if (!_relayStartPending)
        {
            return;
        }

        var wasHosting = _relayHostPending;
        _relayStartPending = false;
        _relayHostPending = false;

        if (!result.Success)
        {
            SetErrorMessage(result.FailureReason);
            SetConnectControlsInteractable(true);
            SetRelayControlsInteractable(true);
            return;
        }

        if (wasHosting)
        {
            if (joinCodeDisplayText != null)
            {
                joinCodeDisplayText.text = result.ShareCode;
            }

            // 조인코드 옆에 설정 해시를 같이 띄운다 — 상대와 이 숫자가 다르면 무조건 못 붙는다.
            SetErrorMessage(string.IsNullOrEmpty(result.ShareCode)
                ? $"Relay Host가 시작되었습니다. ({DescribeConfigHash()})"
                : $"Relay 조인코드: {result.ShareCode} ({DescribeConfigHash()})");
            SetConnectControlsInteractable(false);
            SetRelayControlsInteractable(false);
            ApplyRoleUi();
            return;
        }

        _clientConnectPending = true;
        SetErrorMessage($"서버 접속 시도 중... ({DescribeConfigHash()})");
    }

    private void RegisterSessionLauncherEvents()
    {
        if (_sessionLauncher != null)
        {
            _sessionLauncher.SessionStartCompleted += HandleSessionStartCompleted;
        }
    }

    private void UnregisterSessionLauncherEvents()
    {
        if (_sessionLauncher != null)
        {
            _sessionLauncher.SessionStartCompleted -= HandleSessionStartCompleted;
        }
    }

    private void HandleLobbyStateChanged()
    {
        ApplyRoleUi();
    }

    private void RegisterNetworkCallbacks()
    {
        if (_networkManager == null || _networkCallbacksRegistered)
        {
            return;
        }

        _networkManager.OnClientConnectedCallback += HandleClientConnected;
        _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        _networkManager.OnTransportFailure += HandleTransportFailure;
        _networkCallbacksRegistered = true;
        NetworkDiagnosticsLog.Log($"{DiagTag}.RegisterNetworkCallbacks", "등록 완료");
    }

    private void UnregisterNetworkCallbacks()
    {
        if (_networkManager == null || !_networkCallbacksRegistered)
        {
            return;
        }

        _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        _networkManager.OnTransportFailure -= HandleTransportFailure;
        _networkCallbacksRegistered = false;
    }

    private void RegisterLobbyUiEvents()
    {
        var controller = ResolveLobbyUIController();
        if (controller != null)
        {
            controller.StateChanged += HandleLobbyStateChanged;
        }
    }

    private void UnregisterLobbyUiEvents()
    {
        if (_lobbyUIController != null)
        {
            _lobbyUIController.StateChanged -= HandleLobbyStateChanged;
        }
    }

    private LobbyUIController ResolveLobbyUIController()
    {
        if (_lobbyUIController == null)
        {
            _lobbyUIController = LobbyUIController.Active;
        }

        return _lobbyUIController;
    }

    private void ResolveSceneReferences()
    {
        startHostButton ??= FindButton("StartHost");
        startClientButton ??= FindButton("StartClient");
        setConnectionDataButton ??= FindButton("SetConnectionData");
        gameStartButton ??= FindButton("GameStart");
        readyButton ??= FindButton("Ready");

        if (ipInputField == null)
        {
            var target = FindInActiveScene("InputField_IP");
            ipInputField = target != null ? target.GetComponent<TMP_InputField>() : null;
        }

        if (portInputField == null)
        {
            var target = FindInActiveScene("InputField_Port");
            portInputField = target != null ? target.GetComponent<TMP_InputField>() : null;
        }

        if (errorText == null)
        {
            var target = FindInActiveScene("Text_ErrorMessage");
            errorText = target != null ? target.GetComponent<TMP_Text>() : null;
        }

        if (sessionConnectPanel == null)
        {
            sessionConnectPanel = FindInActiveScene("Pannel_SessionConnect");
        }

        // Relay 통로도 같은 규약을 따른다 — 이 씬은 인스펙터 배선을 쓰지 않고 이름으로 찾는다
        // (프리팹의 참조가 전부 fileID 0 인 이유). FindInActiveScene 은 트랜스폼 계층을 순회하므로
        // 모드 전환으로 꺼져 있는 패널도 찾는다.
        relayPanel ??= FindInActiveScene("Panel_Relay");
        directPanel ??= FindInActiveScene("Panel_Direct");
        relayHostButton ??= FindButton("Button_RelayHost");
        relayJoinButton ??= FindButton("Button_RelayJoin");
        modeToggleButton ??= FindButton("Button_ModeToggle");

        if (joinCodeInputField == null)
        {
            var target = FindInActiveScene("InputField_JoinCode");
            joinCodeInputField = target != null ? target.GetComponent<TMP_InputField>() : null;
        }

        if (joinCodeDisplayText == null)
        {
            var target = FindInActiveScene("Text_JoinCode");
            joinCodeDisplayText = target != null ? target.GetComponent<TMP_Text>() : null;
        }

        _lobbyUIController ??= FindFirstObjectByType<LobbyUIController>();

        WarnIfMissing(startHostButton, nameof(startHostButton));
        WarnIfMissing(startClientButton, nameof(startClientButton));
        WarnIfMissing(setConnectionDataButton, nameof(setConnectionDataButton));
        WarnIfMissing(gameStartButton, nameof(gameStartButton));
        WarnIfMissing(readyButton, nameof(readyButton));
        WarnIfMissing(ipInputField, nameof(ipInputField));
        WarnIfMissing(portInputField, nameof(portInputField));
        WarnIfMissing(errorText, nameof(errorText));
        WarnIfMissing(sessionConnectPanel, nameof(sessionConnectPanel));

        // 이름 기반 해석이라 오브젝트 이름이 바뀌면 조용히 null 이 된다 —
        // 그러면 모드 전환이 안 되거나 조인코드를 못 읽는데 로그가 0줄이다. 반드시 짚고 넘어간다.
        WarnIfMissing(relayPanel, nameof(relayPanel));
        WarnIfMissing(directPanel, nameof(directPanel));
        WarnIfMissing(relayHostButton, nameof(relayHostButton));
        WarnIfMissing(relayJoinButton, nameof(relayJoinButton));
        // modeToggleButton 은 선택 사항이라 경고하지 않는다 — 없으면 두 패널을 함께 보여준다
        // (SetConnectionModePanels 주석 참조).
        WarnIfMissing(joinCodeInputField, nameof(joinCodeInputField));
        WarnIfMissing(joinCodeDisplayText, nameof(joinCodeDisplayText));

        WarnIfMissing(_lobbyUIController, nameof(LobbyUIController));
    }

    private void BindButtons()
    {
        BindButton(startHostButton, StartHost);
        BindButton(startClientButton, StartClient);
        BindButton(setConnectionDataButton, ApplyConnectionData);
        BindButton(gameStartButton, StartGameLoading);
        BindButton(readyButton, ToggleReady);
        BindButton(relayHostButton, StartRelayHost);
        BindButton(relayJoinButton, StartRelayJoin);
        BindButton(modeToggleButton, ToggleConnectionMode);
    }

    private void ToggleConnectionMode()
    {
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.ToggleConnectionMode",
            $"현재 mode={(_sessionLauncher != null ? _sessionLauncher.Mode.ToString() : "(런처 없음)")}");

        if (_sessionLauncher != null && _sessionLauncher.Mode == SessionConnectionMode.UnityRelay)
        {
            SelectDirectMode();
        }
        else
        {
            SelectRelayMode();
        }
    }

    private void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void SetConnectControlsInteractable(bool interactable)
    {
        SetButtonsInteractable(interactable, startHostButton, startClientButton, setConnectionDataButton);

        if (ipInputField != null)
        {
            ipInputField.interactable = interactable;
        }

        if (portInputField != null)
        {
            portInputField.interactable = interactable;
        }
    }

    private void SetRelayControlsInteractable(bool interactable)
    {
        SetButtonsInteractable(interactable, relayHostButton, relayJoinButton, modeToggleButton);

        if (joinCodeInputField != null)
        {
            joinCodeInputField.interactable = interactable;
        }
    }

    /// <summary>
    /// 전환 버튼이 씬에 있을 때만 패널을 배타적으로 토글한다.
    ///
    /// ⚠️ 버튼이 없는데 토글하면 한쪽 패널이 숨겨진 뒤 되살릴 방법이 없어 그 통로가 통째로
    /// 막힌다. 전환 버튼은 기능상 필수가 아니다 — IPv4 버튼은 동기 레거시 경로라 Mode 와
    /// 무관하고, Relay 버튼은 TryBeginRelayStart 가 Mode 를 직접 UnityRelay 로 세팅한다.
    /// 그래서 버튼이 없으면 두 패널을 저작된 그대로 함께 보여주는 것이 맞다.
    /// </summary>
    private void SetConnectionModePanels(bool relaySelected)
    {
        if (modeToggleButton == null)
        {
            return;
        }

        if (relayPanel != null)
        {
            relayPanel.SetActive(relaySelected);
        }

        if (directPanel != null)
        {
            directPanel.SetActive(!relaySelected);
        }
    }

    private void SetSessionConnectPanel(bool active)
    {
        if (sessionConnectPanel != null)
        {
            sessionConnectPanel.SetActive(active);
        }

        Debug.Log($"[SceneFlow] LobbySceneManager.SetSessionConnectPanel active={active} hasPanel={sessionConnectPanel != null}");
    }

    private void SetErrorMessage(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
        }

        if (!string.IsNullOrEmpty(message))
        {
            Debug.Log($"[SceneFlow] LobbySceneManager.SetErrorMessage message={message}");
        }
    }

    private void WarnIfMissing(Object reference, string referenceName)
    {
        if (reference == null)
        {
            WarnMissingReference(referenceName);
        }
    }
}
