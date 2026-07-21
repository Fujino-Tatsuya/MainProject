using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MapSceneManager : NemoSceneManager
{
    // 호스트→클라 전환 신호. NetworkLoadingFlowController와 동일한 CustomMessaging 방식.
    private const string GoToResultMessageName = "MapScene.GoToResult";

    [Header("Buttons")]
    [SerializeField] private Button resultButton; // ExitButton — 호스트/오프라인만 GoToResult 개시

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
        BindButtons();
        RegisterGoToResultHandler();
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] MapSceneManager.Start");
        SetWarningPanel(false); // 경고창은 기본 숨김 — 클라가 Exit를 누를 때만 표시
        PlayEnterFade();
    }

    private void OnDestroy()
    {
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

    // 클라 수신: 서버만 이 메시지를 보내므로 신뢰하고 즉시 로컬 전환. 재전송/호스트가드 없음.
    private void HandleGoToResultMessage(ulong senderClientId, FastBufferReader reader)
    {
        Debug.Log($"[SceneFlow] MapSceneManager.HandleGoToResultMessage sender={senderClientId}");
        PerformGoToResult();
    }

    private void ResolveSceneReferences()
    {
        resultButton ??= FindButton("ExitButton");
        warningConfirmButton ??= FindButton("ConfirmButton");
        warningCancelButton ??= FindButton("CancelButton");
        if (warningPanel == null)
        {
            warningPanel = FindInActiveScene("WarningMessage_Panel");
        }

        if (resultButton == null)
        {
            WarnMissingReference(nameof(resultButton));
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
