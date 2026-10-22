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
    [SerializeField] private string lobbySceneName = "3.LobbyScene";
    [SerializeField] private string resultSceneName = "ResultScene";
    [SerializeField] private string loadingSceneName = "2.LoadingScene";
    [SerializeField] private string mainGameSceneName = "4.MapScene";

    /// <summary>현재 게임 상태. 씬 전환 시점에 갱신된다.</summary>
    public GameState CurrentState { get; private set; } = GameState.Title;

    /// <summary>
    /// Additive 로딩과 플레이어 준비가 모두 끝나 MainGame을 시작할 수 있을 때 발행된다.
    /// 늦게 활성화되는 소비자는 구독 전에 <see cref="IsMainGameReady"/>를 먼저 확인해야 한다.
    /// </summary>
    public event Action OnMainGameReady;

    public bool IsMainGameReady { get; private set; }

    private bool _suppressStartupSceneLoad;

    /// <summary>
    /// <see cref="Start"/>의 타이틀 씬 자동 로드를 막는다. 개발용 단독 부팅(<c>DevSceneBooter</c>)이
    /// 원하는 씬으로 직행할 때만 쓴다. 호출하지 않으면 종전과 동일하게 타이틀로 진입한다.
    /// <b>반드시 Awake에서 호출할 것</b> — Start보다 앞서야 효과가 있다.
    /// </summary>
    public void SuppressStartupSceneLoad()
    {
        _suppressStartupSceneLoad = true;
    }

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
        if (_suppressStartupSceneLoad)
        {
            Debug.Log("[SceneFlow] GameManager.Start 타이틀 자동 로드 억제됨(개발용 단독 부팅)");
            return;
        }

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
    /// 예) GameManager.SubscribeMainGameStart(OnReady); / 해제: GameManager.UnsubscribeMainGameStart(OnReady);
    /// </summary>
    public static void SubscribeMainGameStart(Action callback)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[GameManager] Instance가 아직 없어 MainGame 시작 구독 실패. BootStrap 이후 호출할 것.");
            return;
        }

        Instance.SubscribeMainGameReady(callback);
    }

    /// <summary><see cref="SubscribeMainGameStart"/>로 등록한 콜백을 해제한다.</summary>
    public static void UnsubscribeMainGameStart(Action callback)
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
        Debug.Log($"[SceneFlow] GameManager.GoToLobby LoadScene lobbySceneName={lobbySceneName}");
        SceneManager.LoadScene(lobbySceneName);
    }

    private bool _leavingToTitle;

    /// <summary>
    /// ESC 그만두기 → "타이틀"(로비·인게임, PLAN-settings-ddol). 데모 재접속 루프를 먼저 Idle 로 돌리고(끊김으로 처리되지 않게) NGO 를 Shutdown,
    /// 정리가 끝나면 타이틀을 로컬 Single 로드한다. 호스트가 누르면 클라 전원은 끊김 경로("호스트 대기 중")로 간다.
    /// 🔴 0.BootStrapScene 은 다시 로드하지 않는다 — NetworkManager 가 둘이 된다.
    /// </summary>
    public void LeaveSessionToTitle()
    {
        if (_leavingToTitle)
        {
            return;
        }

        StartCoroutine(LeaveSessionToTitleRoutine());
    }

    private System.Collections.IEnumerator LeaveSessionToTitleRoutine()
    {
        _leavingToTitle = true;
        Debug.Log("[SceneFlow] GameManager.LeaveSessionToTitle");
        DemoSessionDirector.PrepareLeaveToTitle();

        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && (networkManager.IsListening || networkManager.ShutdownInProgress))
        {
            networkManager.Shutdown();
            while (networkManager != null && (networkManager.IsListening || networkManager.ShutdownInProgress))
            {
                yield return null;
            }
        }

        Time.timeScale = 1f;
        SessionResult.Clear();
        SetState(GameState.Title);
        SceneManager.LoadScene(titleSceneName);
        _leavingToTitle = false;
    }

    /// <summary>ESC 그만두기 → "바탕화면" — 에디터는 Play 정지, 빌드는 종료. 세션 정리는 각 OnApplicationQuit 가 한다.</summary>
    public static void QuitGame()
    {
        Debug.Log("[SceneFlow] GameManager.QuitGame");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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

    /// <summary>
    /// 판 종료 전환(PLAN-48 C7) — 5.ResultScene 대신 로비로 간다. 결과는 <see cref="SessionResult"/> 에 남는다
    /// (C7b 에서 Work1 LobbyResultPanel 연결 예정). 서버: NGO Single 로 로비 로드(동적 객체 디스폰 포함, 클라는 NGO 가 따라온다).
    /// 클라: 아무것도 안 한다 — 서버의 전환을 기다린다. 오프라인: 로컬 Single 로드.
    /// </summary>
    public void GoToResult()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            if (!networkManager.IsServer)
            {
                Debug.Log("[SceneFlow] GameManager.GoToResult client — 서버의 로비 NGO 전환을 기다린다");
                return;
            }

            var loadingFlow = networkManager.GetComponent<NetworkLoadingFlowController>();
            string reason = "NetworkLoadingFlowController 없음";
            if (loadingFlow != null && loadingFlow.TryLoadSessionSceneSingle(lobbySceneName, out reason))
            {
                Debug.Log($"[SceneFlow] GameManager.GoToResult server NGO Single lobbySceneName={lobbySceneName}");
                return;
            }

            Debug.LogError($"[SceneFlow] GameManager.GoToResult 로비 NGO 전환 거부 — {reason}");
            return;
        }

        Debug.Log($"[SceneFlow] GameManager.GoToResult offline LoadScene lobbySceneName={lobbySceneName}");
        SceneManager.LoadScene(lobbySceneName);
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

        // NGO 로드(Single·Additive)도 sceneLoaded를 발생시키므로 상태는 전부 씬 이름으로 정한다.
        if (scene.name == titleSceneName)
        {
            SetState(GameState.Title);
        }
        else if (scene.name == lobbySceneName)
        {
            SetState(GameState.Lobby);
        }
        else if (scene.name == resultSceneName)
        {
            SetState(GameState.Result);
        }
        else if (scene.name == loadingSceneName)
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
    }
}
