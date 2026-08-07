using System.Net;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class BeaverLobbySceneManager : NemoSceneManager
{
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
        Debug.Log("[SceneFlow] BeaverLobbySceneManager.Awake");
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
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] BeaverLobbySceneManager.Start");
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
        TryApplyConnectionData();
    }

    public void StartHost()
    {
        Debug.Log($"[SceneFlow] BeaverLobbySceneManager.StartHost hasSessionLauncher={_sessionLauncher != null}");
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
            Debug.Log("[SceneFlow] BeaverLobbySceneManager.StartHost succeeded");
            SetErrorMessage(string.Empty);
            SetSessionConnectPanel(false);
            ApplyRoleUi();
        }
        else
        {
            Debug.Log("[SceneFlow] BeaverLobbySceneManager.StartHost failed");
            SetErrorMessage("Host 시작에 실패했습니다. 포트가 이미 사용 중인지 확인하세요.");
        }
    }

    public void StartClient()
    {
        Debug.Log($"[SceneFlow] BeaverLobbySceneManager.StartClient hasSessionLauncher={_sessionLauncher != null}");
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
            Debug.Log("[SceneFlow] BeaverLobbySceneManager.StartClient connecting");
            _clientConnectPending = true;
            SetErrorMessage("서버 접속 시도 중...");
            SetConnectControlsInteractable(false);
        }
        else
        {
            Debug.Log("[SceneFlow] BeaverLobbySceneManager.StartClient failed");
            SetErrorMessage("Client 시작에 실패했습니다.");
        }
    }

    public void SelectDirectMode()
    {
        if (_sessionLauncher != null)
        {
            _sessionLauncher.Mode = SessionConnectionMode.DirectIPv4;
        }

        SetConnectionModePanels(false);
        SetErrorMessage(string.Empty);
    }

    public void SelectRelayMode()
    {
        if (_sessionLauncher != null)
        {
            _sessionLauncher.Mode = SessionConnectionMode.UnityRelay;
        }

        SetConnectionModePanels(true);
        SetErrorMessage(string.Empty);
    }

    public void StartRelayHost()
    {
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
        Debug.Log($"[SceneFlow] BeaverLobbySceneManager.ToggleReady hasLobbyUIController={controller != null}");
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
        Debug.Log($"[SceneFlow] BeaverLobbySceneManager.StartGameLoading hasSessionLauncher={_sessionLauncher != null}");
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
        if (_networkManager == null || clientId != _networkManager.LocalClientId)
        {
            return;
        }

        Debug.Log($"[SceneFlow] BeaverLobbySceneManager.HandleClientConnected localClientId={clientId}");
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

        Debug.Log($"[SceneFlow] BeaverLobbySceneManager.HandleClientDisconnected clientId={clientId} isServer={_networkManager.IsServer} reason='{_networkManager.DisconnectReason}'");
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

        SetErrorMessage(_clientConnectPending
            ? "서버 접속에 실패했습니다. IP/Port와 Host 상태를 확인하세요."
            : "서버와의 연결이 끊어졌습니다.");
        _clientConnectPending = false;
        SetSessionConnectPanel(true);
        SetConnectControlsInteractable(true);
        SetRelayControlsInteractable(true);
        ApplyRoleUi();
    }

    private void HandleTransportFailure()
    {
        Debug.LogError("[SceneFlow] BeaverLobbySceneManager.HandleTransportFailure");
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
            return false;
        }

        _sessionLauncher.Mode = SessionConnectionMode.UnityRelay;
        _relayStartPending = true;
        _relayHostPending = hosting;
        SetConnectControlsInteractable(false);
        SetRelayControlsInteractable(false);
        return true;
    }

    private void HandleSessionStartCompleted(SessionStartResult result)
    {
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

            SetErrorMessage(string.IsNullOrEmpty(result.ShareCode)
                ? "Relay Host가 시작되었습니다."
                : $"Relay 조인코드: {result.ShareCode}");
            SetConnectControlsInteractable(false);
            SetRelayControlsInteractable(false);
            ApplyRoleUi();
            return;
        }

        _clientConnectPending = true;
        SetErrorMessage("서버 접속 시도 중...");
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

    private void SetConnectionModePanels(bool relaySelected)
    {
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

        Debug.Log($"[SceneFlow] BeaverLobbySceneManager.SetSessionConnectPanel active={active} hasPanel={sessionConnectPanel != null}");
    }

    private void SetErrorMessage(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
        }

        if (!string.IsNullOrEmpty(message))
        {
            Debug.Log($"[SceneFlow] BeaverLobbySceneManager.SetErrorMessage message={message}");
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
