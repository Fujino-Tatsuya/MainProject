using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MapSceneManager : NemoSceneManager
{
    // 호스트→클라 전환 신호. NetworkLoadingFlowController와 동일한 CustomMessaging 방식.
    private const string GoToResultMessageName = "MapScene.GoToResult";

    // 수신 페이로드 행 버퍼(SessionResult.Capture 가 복사해 가므로 재사용한다).
    private static readonly List<PlayerSessionStats> ReceivedPlayerRows = new List<PlayerSessionStats>();

    [Header("Buttons")]
    [SerializeField] private Button optionButton; // Option_Button — ESC 메뉴 열기(GameMenuController)

    // ESC 메뉴·설정창·종료는 DDOL GameMenuController 가 한다(PLAN-settings-ddol) — 씬의 OptionPanel·클라 경고창·ExitButton 은 지웠다.
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
            AudioManager.Instance.PlayBGM("BGM_InGame");
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

    // 판 끝 전환 진입점 — 서버 경로(PartyWipeWatcher 전멸 · BossEncounterDirector 처치)와 GameManager.GoToResultButton 이 호출한다.
    // 호스트/오프라인: 결과 확정·페이로드 송신 후 로비로 전환(PLAN-48 C7 — 서버 NGO Single). 클라: 무시(서버의 전환을 따라간다).
    public void GoToResult()
    {
        Debug.Log($"[SceneFlow] MapSceneManager.GoToResult transitioning={IsTransitioning} client={IsNetworkClientOnly()}");
        if (IsTransitioning || _gameManager == null || IsNetworkClientOnly())
        {
            return;
        }

        // 중도 종료도 누른 시점까지의 집계값으로 확정한다. 전멸·클리어가 먼저 확정했으면
        // 첫 확정이 이기므로 그 결과가 그대로 간다.
        SessionStatsTracker.Active?.Capture(SessionOutcome.Aborted);

        BroadcastGoToResultToClients();
        PerformGoToResult();
    }

    // 톱니(Option_Button) — ESC 메뉴 열기.
    public void OpenOptionPanel()
    {
        if (GameMenuController.Instance != null)
        {
            GameMenuController.Instance.OpenMenu();
        }
    }

    // 페이드 후 GameManager.GoToResult — 서버는 NGO 로 로비를 Single 로드(동적 객체 디스폰 포함), 오프라인은 로컬 Single 로드,
    // 클라는 페이드만 하고 서버의 NGO 전환을 기다린다(로컬 씬 로드 없음).
    private void PerformGoToResult()
    {
        if (IsTransitioning || _gameManager == null)
        {
            return;
        }

        GameMenuController.Instance?.Close();
        StartCoroutine(FadeThenInvoke(_gameManager.GoToResult));
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

    // 서버(호스트)만 호출. 자기 자신을 제외한 전 클라에게 결과 페이로드를 보낸다 (BroadcastState 패턴). 씬 전환은 NGO 가 한다.
    // 집계는 서버에서만 하므로 확정된 SessionResult를 페이로드로 실어 클라 결과 화면도 같은 값을 보이게 한다.
    // GoToResult 가 이 호출 전에 SessionStatsTracker.Capture 를 끝낸다(전멸·클리어는 그 호출자가 먼저 확정).
    // 페이로드는 플레이어 수에 맞춘 가변 크기다(SessionResultPayload — 상한 초과 시 경고 후 자름).
    private void BroadcastGoToResultToClients()
    {
        if (_networkManager == null ||
            !_networkManager.IsServer ||
            _networkManager.CustomMessagingManager == null)
        {
            return;
        }

        int size = SessionResultPayload.GetSize(SessionResult.Players.Count);
        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            if (clientId == _networkManager.LocalClientId)
            {
                continue; // 호스트 자신은 로컬에서 직접 전환
            }

            using var writer = new FastBufferWriter(size, Allocator.Temp);
            var payloadWriter = writer;
            SessionResultPayload.Write(ref payloadWriter, SessionResult.HasValue, SessionResult.Outcome,
                SessionResult.SurvivalSeconds, SessionResult.Players);
            _networkManager.CustomMessagingManager.SendNamedMessage(GoToResultMessageName, clientId, writer);
        }

        Debug.Log($"[SceneFlow] MapSceneManager.BroadcastGoToResultToClients clients={_networkManager.ConnectedClientsIds.Count}");
    }

    // 클라 수신: 서버가 보낸 것만 신뢰하고 결과를 확정한 뒤 페이드한다. 씬은 서버의 NGO LoadScene(로비, Single)이 옮긴다 —
    // 서버는 이 메시지를 보낸 뒤 페이드 + 한 프레임 뒤에 LoadScene 을 부르고, 둘 다 같은 ReliableSequenced 파이프라인이라 이 메시지가 먼저 온다.
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

        // 형식이 깨졌으면 결과 없이(대시 표기) 전환만 한다 — 결과 화면에 못 가는 것보다 낫다.
        if (!SessionResultPayload.TryRead(ref reader, out bool hasValue, out SessionOutcome outcome,
                out float survivalSeconds, ReceivedPlayerRows))
        {
            Debug.LogWarning($"[SceneFlow] GoToResult 페이로드 형식이 올바르지 않아 결과 없이 전환합니다. sender={senderClientId}");
        }

        // 서버에 SessionStatsTracker 가 없었다면 hasValue=false — 클라도 대시 표기로 둔다.
        if (hasValue)
        {
            SessionResult.Capture(outcome, survivalSeconds, ReceivedPlayerRows);
        }

        Debug.Log($"[SceneFlow] MapSceneManager.HandleGoToResultMessage sender={senderClientId} hasValue={hasValue}");
        PerformGoToResult();
    }

    private void ResolveSceneReferences()
    {
        optionButton ??= FindButton("Option_Button");
        if (optionButton == null)
        {
            WarnMissingReference(nameof(optionButton));
        }
    }

    private void BindButtons()
    {
        if (optionButton != null)
        {
            optionButton.onClick.RemoveListener(OpenOptionPanel);
            optionButton.onClick.AddListener(OpenOptionPanel);
        }
    }
}
