using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MapSceneManager : NemoSceneManager
{
    // 호스트→클라 전환 신호. NetworkLoadingFlowController와 동일한 CustomMessaging 방식.
    private const string GoToResultMessageName = "MapScene.GoToResult";

    [Header("Buttons")]
    [SerializeField] private Button resultButton; // ExitButton — 호스트/오프라인만 GoToResult 개시
    [SerializeField] private Button optionButton; // Option_Button — 옵션 패널 열기

    [Header("Option")]
    [SerializeField] private GameObject optionPanel;

    [Header("Client Exit Warning")]
    [SerializeField] private GameObject warningPanel;     // WarningMessage_Panel — 클라 전용 경고창
    [SerializeField] private Button warningConfirmButton; // ConfirmButton(Yes) — 게임 종료
    [SerializeField] private Button warningCancelButton;  // CancelButton — 경고창 닫기

    private GameManager _gameManager;
    private NetworkManager _networkManager;
    private bool _messageHandlerRegistered;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("[SceneFlow] MapSceneManager.Awake");
        _gameManager = GetGameManager();
        _networkManager = NetworkManager.Singleton;
        ResolveSceneReferences();
        SetOptionPanel(false);
        BindButtons();
        RegisterGoToResultHandler();
    }

    private void Update()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            ToggleOptionPanel();
        }
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] MapSceneManager.Start");
        SetWarningPanel(false); // 경고창은 기본 숨김 — 클라가 Exit를 누를 때만 표시
        PlayEnterFade();

        // 인게임 BGM: 씬 로드 시점이 아니라 "본게임 준비 완료(로딩+플레이어 스폰 동기화)" 시점에 재생.
        // 이미 준비된 뒤 구독하면 즉시 1회 실행되고, 아직이면 준비되는 순간 호출된다.
        if (_gameManager != null)
        {
            _gameManager.SubscribeMainGameReady(PlayInGameBgm);
        }
    }

    // 본게임 준비 완료 시 호출되는 콜백. 인게임 BGM을 재생한다.
    private void PlayInGameBgm()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBGM(AudioManager.Instance.Catalog.InGameBGM);
        }
    }

    private void OnDestroy()
    {
        if (_gameManager != null)
        {
            _gameManager.UnsubscribeMainGameReady(PlayInGameBgm);
        }
        UnregisterGoToResultHandler();
    }

    // ExitButton 진입점. TempGameManager.GoToResultButton 브리지가 호출한다.
    // 호스트/오프라인: 전 클라 ResultScene 전환. 클라: 경고창 표시.
    public void GoToResult()
    {
        Debug.Log($"[SceneFlow] MapSceneManager.GoToResult transitioning={IsTransitioning} client={IsNetworkClientOnly()}");
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        if (IsNetworkClientOnly())
        {
            SetWarningPanel(true);
            return;
        }

        BroadcastGoToResultToClients();
        PerformGoToResult();
    }

    // 클라 경고창 Yes — 게임 종료.
    public void ConfirmClientExit()
    {
        Debug.Log("[SceneFlow] MapSceneManager.ConfirmClientExit quit");
        SetButtonsInteractable(false, warningConfirmButton, warningCancelButton);
        QuitApplication();
    }

    // 클라 경고창 Cancel — 경고창 닫기.
    public void CancelClientExit()
    {
        Debug.Log("[SceneFlow] MapSceneManager.CancelClientExit close warning");
        SetWarningPanel(false);
    }

    public void OpenOptionPanel()
    {
        SetOptionPanel(true);
    }

    public void CloseOptionPanel()
    {
        SetOptionPanel(false);
    }

    // ESC / 톱니 토글 — 열려 있으면 닫고, 닫혀 있으면 연다.
    public void ToggleOptionPanel()
    {
        SetOptionPanel(optionPanel == null || !optionPanel.activeSelf);
    }

    private void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // 실제 로컬 전환. GameManager.GoToResult가 ResultScene을 단일 모드로 로드 → MapScene 자동 언로드.
    private void PerformGoToResult()
    {
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        SetButtonsInteractable(false, resultButton);
        StartCoroutine(FadeThenInvoke(_gameManager.GoToResult));
    }

    private void SetWarningPanel(bool active)
    {
        if (warningPanel != null)
        {
            warningPanel.SetActive(active);
        }
    }

    private void SetOptionPanel(bool active)
    {
        if (optionPanel != null)
        {
            optionPanel.SetActive(active);
        }
    }

    private bool IsNetworkClientOnly()
    {
        return _networkManager != null &&
               _networkManager.IsListening &&
               !_networkManager.IsServer;
    }

    private void RegisterGoToResultHandler()
    {
        if (_messageHandlerRegistered ||
            _networkManager == null ||
            _networkManager.CustomMessagingManager == null)
        {
            return;
        }

        _networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            GoToResultMessageName, HandleGoToResultMessage);
        _messageHandlerRegistered = true;
        Debug.Log("[SceneFlow] MapSceneManager.RegisterGoToResultHandler registered");
    }

    private void UnregisterGoToResultHandler()
    {
        if (!_messageHandlerRegistered ||
            _networkManager == null ||
            _networkManager.CustomMessagingManager == null)
        {
            return;
        }

        _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(GoToResultMessageName);
        _messageHandlerRegistered = false;
        Debug.Log("[SceneFlow] MapSceneManager.UnregisterGoToResultHandler unregistered");
    }

    // 서버(호스트)만 호출. 자기 자신을 제외한 전 클라에게 전환 신호를 보낸다 (BroadcastState 패턴).
    private void BroadcastGoToResultToClients()
    {
        if (_networkManager == null ||
            !_networkManager.IsServer ||
            _networkManager.CustomMessagingManager == null)
        {
            return;
        }

        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            if (clientId == _networkManager.LocalClientId)
            {
                continue; // 호스트 자신은 로컬에서 직접 전환
            }

            using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
            writer.WriteValueSafe((byte)1);
            _networkManager.CustomMessagingManager.SendNamedMessage(GoToResultMessageName, clientId, writer);
        }

        Debug.Log($"[SceneFlow] MapSceneManager.BroadcastGoToResultToClients clients={_networkManager.ConnectedClientsIds.Count}");
    }

    // 클라 수신: 서버가 보낸 것만 신뢰하고 로컬 전환한다.
    // ⚠️ 이 핸들러는 Awake에서 호스트/클라 구분 없이 등록된다(RegisterGoToResultHandler). 송신자 검증이
    //    없으면 클라가 서버로 같은 이름의 메시지를 보내 호스트를 결과 화면으로 끌고 갈 수 있었다.
    //    LobbyUIController·NetworkLoadingFlowController·NetworkClock이 쓰는 것과 동일한 가드다.
    private void HandleGoToResultMessage(ulong senderClientId, FastBufferReader reader)
    {
        // 서버는 BroadcastGoToResultToClients에서 자신을 제외하고 로컬에서 직접 전환한다.
        // 따라서 서버가 이 메시지를 받았다면 송신자는 클라이언트다 — 처리하지 않는다.
        if (_networkManager != null && _networkManager.IsServer)
        {
            Debug.LogWarning($"[SceneFlow] GoToResult 메시지를 서버가 수신해 무시합니다. sender={senderClientId} " +
                             "(서버는 로컬에서 직접 전환하므로 이 경로로 들어올 이유가 없습니다.)");
            return;
        }

        if (senderClientId != NetworkManager.ServerClientId)
        {
            Debug.LogWarning($"[SceneFlow] GoToResult 메시지의 송신자가 서버가 아니라 무시합니다. sender={senderClientId}");
            return;
        }

        Debug.Log($"[SceneFlow] MapSceneManager.HandleGoToResultMessage sender={senderClientId}");
        PerformGoToResult();
    }

    private void ResolveSceneReferences()
    {
        resultButton ??= FindButton("ExitButton");
        optionButton ??= FindButton("Option_Button");
        warningConfirmButton ??= FindButton("ConfirmButton");
        warningCancelButton ??= FindButton("CancelButton");
        if (optionPanel == null)
        {
            optionPanel = FindInActiveScene("OptionPanel");
        }

        if (warningPanel == null)
        {
            warningPanel = FindInActiveScene("WarningMessage_Panel");
        }

        if (resultButton == null)
        {
            WarnMissingReference(nameof(resultButton));
        }

        if (optionButton == null)
        {
            WarnMissingReference(nameof(optionButton));
        }

        if (optionPanel == null)
        {
            WarnMissingReference(nameof(optionPanel));
        }

        if (warningPanel == null)
        {
            WarnMissingReference(nameof(warningPanel));
        }

        if (warningConfirmButton == null)
        {
            WarnMissingReference(nameof(warningConfirmButton));
        }

        if (warningCancelButton == null)
        {
            WarnMissingReference(nameof(warningCancelButton));
        }
    }

    private void BindButtons()
    {
        // resultButton(ExitButton)은 씬에서 GameManager.GoToResultButton 퍼시스턴트 이벤트로 이미 연결됨 —
        // 코드 리스너를 추가하면 이중 호출되므로 여기서는 바인딩하지 않는다. (interactable 제어용으로만 참조 보유)
        if (optionButton != null)
        {
            optionButton.onClick.RemoveListener(OpenOptionPanel);
            optionButton.onClick.AddListener(OpenOptionPanel);
        }

        if (warningConfirmButton != null)
        {
            warningConfirmButton.onClick.RemoveListener(ConfirmClientExit);
            warningConfirmButton.onClick.AddListener(ConfirmClientExit);
        }

        if (warningCancelButton != null)
        {
            warningCancelButton.onClick.RemoveListener(CancelClientExit);
            warningCancelButton.onClick.AddListener(CancelClientExit);
        }
    }
}
