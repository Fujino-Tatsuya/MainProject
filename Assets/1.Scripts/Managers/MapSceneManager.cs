using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MapSceneManager : NemoSceneManager
{
    // 호스트→클라 전환 신호. NetworkLoadingFlowController와 동일한 CustomMessaging 방식.
    private const string GoToResultMessageName = "MapScene.GoToResult";

    [Header("Buttons")]
    [SerializeField] private Button resultButton;

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
        ApplyEndButtonVisibility();
        RegisterGoToResultHandler();
    }

    private void Start()
    {
        Debug.Log("[SceneFlow] MapSceneManager.Start");
        PlayEnterFade();
    }

    private void OnDestroy()
    {
        UnregisterGoToResultHandler();
    }

    // 호스트 버튼 진입점. TempGameManager.GoToResultButton 브리지와 버튼 리스너가 호출한다.
    public void GoToResult()
    {
        Debug.Log($"[SceneFlow] MapSceneManager.GoToResult transitioning={IsTransitioning} hasGameManager={_gameManager != null}");
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        // 전환 개시는 오프라인/호스트만. 클라 버튼은 숨겨져 있으나 이중 방어.
        if (IsNetworkClientOnly())
        {
            Debug.Log("[SceneFlow] MapSceneManager.GoToResult ignored on client");
            return;
        }

        BroadcastGoToResultToClients();
        PerformGoToResult();
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

    // 세션에 접속한 순수 클라이언트에게는 End 버튼을 숨긴다. 오프라인/호스트/서버는 표시.
    private void ApplyEndButtonVisibility()
    {
        if (resultButton == null)
        {
            return;
        }

        bool hide = IsNetworkClientOnly();
        resultButton.gameObject.SetActive(!hide);
        Debug.Log($"[SceneFlow] MapSceneManager.ApplyEndButtonVisibility hideForClient={hide}");
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
        resultButton ??= FindButton("Button_GotoResult");

        if (resultButton == null)
        {
            WarnMissingReference(nameof(resultButton));
        }
    }

    private void BindButtons()
    {
        if (resultButton != null)
        {
            resultButton.onClick.RemoveListener(GoToResult);
            resultButton.onClick.AddListener(GoToResult);
        }
    }
}
