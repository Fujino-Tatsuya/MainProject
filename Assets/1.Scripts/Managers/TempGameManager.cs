using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    /// <summary>클라이언트 로컬 게임 상태(현재 씬 단계). 서버 SessionPhase와는 별개.</summary>
    public enum GameState
    {
        Title,
        Lobby,
        Loading,
        MainGame,
        Result,
    }

    [SerializeField] private string titleSceneName = "TitleScene";
    [SerializeField] private string lobbySceneName = "Temp_LobbyScene";
    [SerializeField] private string resultSceneName = "ResultScene";
    [SerializeField] private string loadingSceneName = "2.LoadingScene";
    [SerializeField] private string mainGameSceneName = "4.MapScene";
    [SerializeField] private string sessionConnectPanelName = "Pannel_SessionConnect";

    /// <summary>현재 게임 상태. 씬 전환 시점에 갱신된다.</summary>
    public GameState CurrentState { get; private set; } = GameState.Title;

    /// <summary>
    /// Additive 로딩과 플레이어 준비가 모두 끝나 MainGame을 시작할 수 있을 때 발행된다.
    /// 늦게 활성화되는 소비자는 구독 전에 <see cref="IsMainGameReady"/>를 먼저 확인해야 한다.
    /// </summary>
    public event Action OnMainGameReady;

    public bool IsMainGameReady { get; private set; }

    private bool _hideSessionConnectPanelOnLobbyLoad;

    private void Awake()
    {
        Debug.Log("[SceneFlow] GameManager.Awake");
        if (Instance != null && Instance != this)
        {
            Debug.Log("[SceneFlow] GameManager.Awake duplicate destroyed");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        Debug.Log($"[SceneFlow] GameManager.Start LoadScene titleSceneName={titleSceneName}");
        SetState(GameState.Title);
        SceneManager.LoadScene(titleSceneName);
    }

    private void SetState(GameState next)
    {
        if (CurrentState == next)
        {
            return;
        }

        if (next != GameState.MainGame)
        {
            IsMainGameReady = false;
        }

        Debug.Log($"[SceneFlow] GameManager.SetState {CurrentState} -> {next}");
        CurrentState = next;
    }

    /// <summary>
    /// 현재 피어의 MainGame 로딩 완료를 알린다. 같은 세션에서는 한 번만 발행된다.
    /// </summary>
    public void NotifyMainGameReady()
    {
        if (IsMainGameReady)
        {
            return;
        }

        IsMainGameReady = true;
        OnMainGameReady?.Invoke();
    }

    /// <summary>
    /// OnMainGameReady 구독 파사드. 이벤트를 몰라도 이 한 줄로 안전하게 구독한다.
    /// - 이미 준비된 상태면 콜백을 <b>즉시 1회</b> 실행한다(늦게 붙은 구독자 보호).
    /// - 아직이면 준비되는 순간 자동 호출되도록 등록한다.
    /// - 같은 세션에서 중복 실행되지 않는다(발행이 멱등).
    /// 반드시 짝이 되는 <see cref="UnsubscribeMainGameReady"/>로 해제할 것(GameManager는 계속 살아있음).
    /// </summary>
    public void SubscribeMainGameReady(Action callback)
    {
        if (callback == null)
        {
            return;
        }

        OnMainGameReady += callback;   // 이후(다음 세션 재진입 포함) 발행 대비
        if (IsMainGameReady)
        {
            callback();                // 이미 지나갔으면 지금 1회
        }
    }

    /// <summary><see cref="SubscribeMainGameReady"/>로 등록한 콜백을 해제한다.</summary>
    public void UnsubscribeMainGameReady(Action callback)
    {
        if (callback == null)
        {
            return;
        }

        OnMainGameReady -= callback;
    }

    /// <summary>
    /// Instance null 방어까지 포함한 정적 구독 파사드. 구독자는 GameManager 참조 없이 호출 가능.
    /// 예) GameManager.SubscribeReady(OnReady); / 해제: GameManager.UnsubscribeReady(OnReady);
    /// </summary>
    public static void SubscribeReady(Action callback)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[GameManager] Instance가 아직 없어 MainGameReady 구독 실패. BootStrap 이후 호출할 것.");
            return;
        }

        Instance.SubscribeMainGameReady(callback);
    }

    /// <summary><see cref="SubscribeReady"/>로 등록한 콜백을 해제한다.</summary>
    public static void UnsubscribeReady(Action callback)
    {
        Instance?.UnsubscribeMainGameReady(callback);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Instance = null;
        }
    }

    public void GoToLobby()
    {
        _hideSessionConnectPanelOnLobbyLoad = IsInNetworkSession();
        Debug.Log($"[SceneFlow] GameManager.GoToLobby LoadScene lobbySceneName={lobbySceneName} hideSessionPanel={_hideSessionConnectPanelOnLobbyLoad}");
        SetState(GameState.Lobby);
        SceneManager.LoadScene(lobbySceneName);
    }

    public static void GoToLobbyButton()
    {
        Debug.Log("[SceneFlow] GameManager.GoToLobbyButton");
        var resultSceneManager = FindFirstObjectByType<ResultSceneManager>();
        if (resultSceneManager != null)
        {
            resultSceneManager.GoToLobby();
            return;
        }

        if (Instance == null)
        {
            Debug.LogWarning($"{nameof(GameManager)} is missing from the scene.");
            return;
        }

        Instance.GoToLobby();
    }

    public void GoToResult()
    {
        Debug.Log($"[SceneFlow] GameManager.GoToResult LoadScene resultSceneName={resultSceneName}");
        SetState(GameState.Result);
        SceneManager.LoadScene(resultSceneName);
    }

    public static void GoToResultButton()
    {
        Debug.Log("[SceneFlow] GameManager.GoToResultButton");
        var mapSceneManager = FindFirstObjectByType<MapSceneManager>();
        if (mapSceneManager != null)
        {
            mapSceneManager.GoToResult();
            return;
        }

        if (Instance == null)
        {
            Debug.LogWarning($"{nameof(GameManager)} is missing from the scene.");
            return;
        }

        Instance.GoToResult();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[SceneFlow] GameManager.HandleSceneLoaded scene={scene.name} mode={mode} activeScene={SceneManager.GetActiveScene().name}");

        // NGO 추가 로드도 sceneLoaded를 발생시키므로 Loading/MainGame 진입을 여기서 감지한다.
        if (scene.name == loadingSceneName)
        {
            SetState(GameState.Loading);
        }
        else if (scene.name == mainGameSceneName)
        {
            SetState(GameState.MainGame);
            // 공유 시계에 MainGame 시작을 스탬프(서버만 실제 반영, 클라 호출은 무시됨).
            if (NetworkClock.Instance != null)
            {
                NetworkClock.Instance.MarkMainGameStart();
            }
        }

        if (scene.name != lobbySceneName)
        {
            return;
        }

        ApplyLobbySessionConnectPanelState();
    }

    private void ApplyLobbySessionConnectPanelState()
    {
        var shouldHide = _hideSessionConnectPanelOnLobbyLoad || IsInNetworkSession();
        _hideSessionConnectPanelOnLobbyLoad = IsInNetworkSession();
        Debug.Log($"[SceneFlow] GameManager.ApplyLobbySessionConnectPanelState shouldHide={shouldHide} inNetwork={_hideSessionConnectPanelOnLobbyLoad}");

        if (!shouldHide)
        {
            return;
        }

        var sessionConnectPanel = FindInActiveScene(sessionConnectPanelName);
        if (sessionConnectPanel != null)
        {
            sessionConnectPanel.SetActive(false);
        }
    }

    private static bool IsInNetworkSession()
    {
        var networkManager = NetworkManager.Singleton;
        return networkManager != null &&
               networkManager.IsListening &&
               (networkManager.IsHost ||
                networkManager.IsServer ||
                networkManager.IsClient ||
                networkManager.IsConnectedClient);
    }

    private static GameObject FindInActiveScene(string objectName)
    {
        var activeScene = SceneManager.GetActiveScene();
        var rootObjects = activeScene.GetRootGameObjects();

        foreach (var rootObject in rootObjects)
        {
            var target = FindInChildren(rootObject.transform, objectName);
            if (target != null)
            {
                return target.gameObject;
            }
        }

        return null;
    }

    private static Transform FindInChildren(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        for (var i = 0; i < parent.childCount; i++)
        {
            var target = FindInChildren(parent.GetChild(i), objectName);
            if (target != null)
            {
                return target;
            }
        }

        return null;
    }
}
