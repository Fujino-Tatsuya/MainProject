using System.Collections.Generic;
using System.Net;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbySceneManager : NemoSceneManager
{
    private const string DiagTag = "LobbySceneManager";
    private const int CharacterButtonCount = 3;

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

    [Header("Character Select")]
    [SerializeField] private Button[] characterButtons = new Button[CharacterButtonCount];
    [SerializeField] private Color characterSelectedColor = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color characterIdleColor = Color.white;
    [SerializeField] private Color characterReadyColor = new Color(0.25f, 0.85f, 0.45f, 1f);
    [Header("Messages")]
    [SerializeField] private TMP_Text errorText;

    // 직전 판 결과 — 4.Map 이 끝나고 로비에 들어왔을 때만 자동으로 연다(PLAN-48 C7b, Docs/tech/lobby-result-panel.md).
    // 첫 부팅·재접속은 SessionResult 가 비어 있어 열리지 않는다(비우기 = SessionStatsTracker.Awake · DemoSessionDirector.CleanupAfterDrop).
    [Header("Result")]
    [SerializeField] private LobbyResultPanel resultPanel;
    [SerializeField] private Button showLastResultButton;

    [Header("Connection Defaults")]
    [SerializeField] private string defaultIp = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;

    // Unity Relay 는 뺐다(PLAN-48 Q6 — 유선 IPv4 만). 씬의 Relay 패널·모드 전환 버튼은 숨기기만 하고 삭제는 C9.
    [Header("Relay Connection (숨김 전용)")]
    [SerializeField] private GameObject relayPanel;
    [SerializeField] private GameObject directPanel;
    [SerializeField] private Button modeToggleButton;

    private NetworkManager _networkManager;
    private NetworkSessionLauncher _sessionLauncher;
    private LobbyUIController _lobbyUIController;
    private bool _connectionDataApplied;
    private bool _clientConnectPending;
    private bool _networkCallbacksRegistered;

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

        // 빌드에서만 나는 문제는 이름 기반 해석이 실패한 경우가 많다 — 어떤 참조가 비었는지
        // 한 줄로 남겨 둔다. (ResolveSceneReferences 의 WarnIfMissing 은 콘솔로만 간다.)
        NetworkDiagnosticsLog.Log(
            $"{DiagTag}.Awake",
            $"networkManager={_networkManager != null} sessionLauncher={_sessionLauncher != null} " +
            $"relayPanel={relayPanel != null} directPanel={directPanel != null} " +
            $"modeToggleButton={modeToggleButton != null} errorText={errorText != null} " +
            $"demoFlow={DemoSessionDirector.IsDemoFlowActive}");
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] LobbySceneManager.Start");
        PlayEnterFade();
        ApplyDefaultInputValues();
        SetErrorMessage(string.Empty);
        RegisterLobbyUiEvents();
        SelectDirectMode();
        if (DemoSessionDirector.IsDemoFlowActive)
        {
            // 부스 데모는 설정 파일로 자동 접속한다 — 옛 IP/Port·StartHost/StartClient 패널은 쓰지 않는다.
            SetSessionConnectPanel(false);
        }

        ApplyRoleUi();
        ShowLastResultOnEnter();

        // BGM 재생
        AudioManager.Instance.PlayBGM("BGM_Lobby");
    }

    private void OnDestroy()
    {
        UnregisterNetworkCallbacks();
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
            ShowError(ErrorCatalog.LobbyLauncherMissing);
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
            ShowError(ErrorCatalog.NetHostStartFailed);
        }
    }

    public void StartClient()
    {
        Debug.Log($"[SceneFlow] LobbySceneManager.StartClient hasSessionLauncher={_sessionLauncher != null}");
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartClient", $"버튼 입력 hasSessionLauncher={_sessionLauncher != null}");
        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            ShowError(ErrorCatalog.LobbyLauncherMissing);
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
            ShowError(ErrorCatalog.NetClientStartFailed);
        }
    }

    /// <summary>연결은 DirectIPv4 하나뿐이다 — Relay 패널·모드 전환 버튼을 숨기고 IPv4 패널만 보인다.</summary>
    public void SelectDirectMode()
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.SelectDirectMode", $"hasSessionLauncher={_sessionLauncher != null}");

        if (_sessionLauncher != null)
        {
            _sessionLauncher.Mode = SessionConnectionMode.DirectIPv4;
        }

        SetActiveIfPresent(relayPanel, false);
        SetActiveIfPresent(modeToggleButton != null ? modeToggleButton.gameObject : null, false);
        SetActiveIfPresent(directPanel, true);
        SetErrorMessage(string.Empty);
    }

    public void ToggleReady()
    {
        var controller = ResolveLobbyUIController();
        Debug.Log($"[SceneFlow] LobbySceneManager.ToggleReady hasLobbyUIController={controller != null}");
        NetworkDiagnosticsLog.Log($"{DiagTag}.ToggleReady", $"버튼 입력 hasLobbyUIController={controller != null}");
        if (controller == null)
        {
            WarnMissingReference(nameof(LobbyUIController));
            ShowError(ErrorCatalog.LobbyUiMissing);
            return;
        }

        controller.ToggleLocalReady();
    }

    /// <summary>
    /// 옛 GameStart 버튼 — 게임 시작은 LobbySession 카운트다운(전원 준비 → 5초)으로 바뀌었다(PLAN-48 C8). 버튼은 숨기고 호출되면 진단 로그만 남긴다(C9 에서 씬째 삭제).
    /// </summary>
    public void StartGameLoading()
    {
        NetworkDiagnosticsLog.Log($"{DiagTag}.StartGameLoading", "옛 GameStart 입력 — 카운트다운으로 대체되어 무시");
    }

    /// <summary>다시 보기 버튼 — 결과는 로비에 있는 동안 지우지 않으므로 몇 번이든 다시 연다.</summary>
    public void ShowLastResult()
    {
        if (resultPanel != null)
        {
            resultPanel.Show();
        }
    }

    // 클라는 결과 페이로드(MapSceneManager.HandleGoToResultMessage → SessionResult.Capture)가 로비 로드보다 먼저 와 있다.
    // 결과는 로비에 있는 동안 바뀌지 않으므로(다음 판 Map 진입·끊김 정리 때만 비움) 버튼 표시는 진입 때 한 번 정한다.
    private void ShowLastResultOnEnter()
    {
        var hasResult = resultPanel != null && resultPanel.HasResult;
        Debug.Log($"[SceneFlow] LobbySceneManager.ShowLastResultOnEnter hasPanel={resultPanel != null} hasResult={hasResult}");

        if (showLastResultButton != null)
        {
            showLastResultButton.gameObject.SetActive(hasResult);
            var label = showLastResultButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = UiStrings.LobbyShowLastResult.Text;
            }
        }

        if (hasResult)
        {
            resultPanel.Show();
        }
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
            ShowError(ErrorCatalog.NetIpEmpty);
            return false;
        }

        if (!IPAddress.TryParse(ipText, out _))
        {
            ShowError(ErrorCatalog.NetIpInvalid, ErrorDisplay.Arg("ip", ipText));
            return false;
        }

        if (!ushort.TryParse(portText, out var port) || port == 0)
        {
            ShowError(ErrorCatalog.NetPortInvalid, ErrorDisplay.Arg("port", portText));
            return false;
        }

        if (_sessionLauncher == null)
        {
            WarnMissingReference(nameof(NetworkSessionLauncher));
            ShowError(ErrorCatalog.LobbyLauncherMissing);
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

    // 호스트·클라 모두 Ready 만 노출한다 — GameStart 는 LobbySession 카운트다운으로 대체돼 숨긴다(PLAN-48 C8). 네트워크 시작 전에는 패널이 화면을 덮는다.
    private void ApplyRoleUi()
    {
        var listening = _networkManager != null && _networkManager.IsListening;
        var controller = ResolveLobbyUIController();

        if (gameStartButton != null)
        {
            gameStartButton.gameObject.SetActive(false);
        }

        if (readyButton != null)
        {
            readyButton.gameObject.SetActive(true);
        }

        ApplyCharacterSelectUi(listening, controller);
    }

    // 선택 버튼 강조, Ready = 선택 버튼 초록. Ready·카운트다운 중 변경 불가(서버도 거부) — 호스트 예외 없음.
    private void ApplyCharacterSelectUi(bool listening, LobbyUIController controller)
    {
        var roster = controller != null ? controller.Roster : null;
        var locked = controller != null && controller.IsLocalCharacterSelectionLocked();
        var selectedId = controller != null ? controller.GetLocalCharacterSelectionId() : -1;

        for (var i = 0; i < characterButtons.Length; i++)
        {
            var button = characterButtons[i];
            if (button == null)
            {
                continue;
            }

            // Ready 잠금은 interactable 로 하지 않는다 — 비활성 틴트가 Ready 초록을 흐리게 덮는다. 클릭은 SelectCharacter 가 무시.
            var available = roster != null && roster.TryGetAvailableCharacter(i, out _);
            button.interactable = listening && available;

            if (button.targetGraphic != null)
            {
                var selectedColor = locked ? characterReadyColor : characterSelectedColor;
                button.targetGraphic.color = i == selectedId ? selectedColor : characterIdleColor;
            }
        }
    }

    private void SelectCharacter(int characterId)
    {
        var controller = ResolveLobbyUIController();
        if (controller == null)
        {
            WarnMissingReference(nameof(LobbyUIController));
            return;
        }

        if (controller.IsLocalCharacterSelectionLocked())
        {
            return;
        }

        controller.RequestLocalCharacterSelection(characterId);
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
                ShowError(ErrorCatalog.NetPlayerDisconnected, ErrorDisplay.Arg("clientId", clientId.ToString()));
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
            ShowError(new SessionError(
                ErrorCatalog.NetRejectedByHost,
                ErrorDisplay.Arg("cfg", DescribeConfigHash()),
                _networkManager.DisconnectReason));
        }
        else
        {
            ShowError(new SessionError(
                _clientConnectPending ? ErrorCatalog.NetConnectFailed : ErrorCatalog.NetDisconnected,
                detail: _networkManager.DisconnectReason));
        }
        _clientConnectPending = false;
        SetSessionConnectPanel(true);
        SetConnectControlsInteractable(true);
        ApplyRoleUi();
    }

    private void HandleTransportFailure()
    {
        Debug.LogError("[SceneFlow] LobbySceneManager.HandleTransportFailure");
        NetworkDiagnosticsLog.LogError(
            $"{DiagTag}.HandleTransportFailure",
            $"connectPending={_clientConnectPending}");
        ShowError(ErrorCatalog.NetTransportFailure);
        _clientConnectPending = false;
        SetSessionConnectPanel(true);
        SetConnectControlsInteractable(true);
        ApplyRoleUi();
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

        // Relay 패널·모드 전환 버튼은 숨기려고만 찾는다 — 이 씬은 인스펙터 배선을 쓰지 않고 이름으로 찾는다
        // (프리팹의 참조가 전부 fileID 0 인 이유). FindInActiveScene 은 트랜스폼 계층을 순회하므로
        // 꺼져 있는 패널도 찾는다.
        relayPanel ??= FindInActiveScene("Panel_Relay");
        directPanel ??= FindInActiveScene("Panel_Direct");
        modeToggleButton ??= FindButton("Button_ModeToggle");

        showLastResultButton ??= FindButton("Button_ShowLastResult");
        resultPanel ??= FindFirstObjectByType<LobbyResultPanel>();

        if (characterButtons == null || characterButtons.Length != CharacterButtonCount)
        {
            characterButtons = new Button[CharacterButtonCount];
        }

        for (var i = 0; i < characterButtons.Length; i++)
        {
            characterButtons[i] ??= FindButton($"Button_Character{i}");
            WarnIfMissing(characterButtons[i], $"characterButtons[{i}]");
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
        WarnIfMissing(resultPanel, nameof(resultPanel));
        WarnIfMissing(showLastResultButton, nameof(showLastResultButton));

        // relayPanel·directPanel·modeToggleButton 은 숨김 전용이라 없어도 경고하지 않는다.

        WarnIfMissing(_lobbyUIController, nameof(LobbyUIController));
    }

    private void BindButtons()
    {
        BindButton(startHostButton, StartHost);
        BindButton(startClientButton, StartClient);
        BindButton(setConnectionDataButton, ApplyConnectionData);
        BindButton(gameStartButton, StartGameLoading);
        BindButton(readyButton, ToggleReady);
        BindButton(showLastResultButton, ShowLastResult);

        for (var i = 0; i < characterButtons.Length; i++)
        {
            var characterId = i;
            BindButton(characterButtons[i], () => SelectCharacter(characterId));
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

    private static void SetActiveIfPresent(GameObject target, bool active)
    {
        if (target != null)
        {
            target.SetActive(active);
        }
    }

    private void SetSessionConnectPanel(bool active)
    {
        // 부스 데모 흐름에서는 끊겨도 패널을 다시 띄우지 않는다 — DemoSessionDirector 가 대기 씬으로 보내 재접속한다.
        if (DemoSessionDirector.IsDemoFlowActive)
        {
            active = false;
        }

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

    /// <summary>오류 — 화면은 현재 언어 문구 + 작은 코드 표기, 로그는 코드 + 인자 + 상세(ErrorCodes 시트를 찾는 열쇠).</summary>
    private void ShowError(SessionError error)
    {
        if (errorText != null)
        {
            errorText.text = error.DisplayText;
        }

        Debug.Log($"[SceneFlow] LobbySceneManager.ShowError {error.LogText}");
        NetworkDiagnosticsLog.Log($"{DiagTag}.ShowError", error.LogText);
    }

    private void ShowError(ErrorCode code, IReadOnlyDictionary<string, string> args = null)
    {
        ShowError(new SessionError(code, args));
    }

    private void WarnIfMissing(Object reference, string referenceName)
    {
        if (reference == null)
        {
            WarnMissingReference(referenceName);
        }
    }
}
