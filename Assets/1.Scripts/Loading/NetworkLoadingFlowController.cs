using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkManager))]
public class NetworkLoadingFlowController : MonoBehaviour
{
    private const string ProgressMessageName = "LoadingScene.Progress";
    private const string StateMessageName = "LoadingScene.State";
    private const int ProgressMessageSize = sizeof(uint) + sizeof(float);
    // flowId · phase · 평균 진행률 · 소스가 NGO 씬인가(클라가 로컬 언로드를 건너뛸지).
    private const int StateMessageSize = sizeof(uint) + sizeof(byte) + sizeof(float) + sizeof(byte);

    [SerializeField] private string loadingSceneName = "LoadingScene";
    [SerializeField] private string targetSceneName = "MapScene"; // Temp_inGameScene에서 변경.
    [SerializeField] private float minimumVisibleSeconds = 5f;
    [SerializeField] private float readyMessageSeconds = 2.5f;
    [SerializeField] private bool requireLobbyReadyToStart = true;
    [SerializeField] private GameObject defaultPlayerPrefab;
    [SerializeField] private CharacterRoster characterRoster;
    [SerializeField] private bool debugLogging;
    [SerializeField] private Vector3 spawn_Offset = new Vector3(0, 10.0f, 0);

    private readonly HashSet<ulong> _trackedClients = new HashSet<ulong>();
    private readonly HashSet<ulong> _timedOutClients = new HashSet<ulong>();
    private readonly HashSet<string> _completedUnloads = new HashSet<string>();
    private readonly Dictionary<ulong, float> _clientProgress = new Dictionary<ulong, float>();

    private NetworkManager _networkManager;
    private NetworkLoadingScreenView _view;
    private NetworkLoadingPhase _phase = NetworkLoadingPhase.Idle;
    private AsyncOperation _localLoadOperation;
    private Coroutine _localProgressRoutine;
    private Coroutine _targetLoadRoutine;
    private Coroutine _completionRoutine;
    private Coroutine _registerCallbacksRoutine;
    private bool _callbacksRegistered;

    // 구독한 상대. NGO 가 세션마다 갈아치우므로 "등록 여부"가 아니라 "등록 대상"을 들고 있어야 한다.
    // 자세한 이유는 IsRegistrationCurrent 주석 참조.
    private NetworkSceneManager _registeredSceneManager;
    private CustomMessagingManager _registeredMessagingManager;
    private bool _playersSpawnedForCurrentTargetScene;
    // 타깃 씬 LoadEventCompleted 를 받았는가 — 완료 루틴이 로딩 씬 언로드 전에 기다린다(CompleteAfterMinimumVisibleTime 주석).
    private bool _targetLoadEventCompleted;
    private uint _flowId;
    private float _averageProgress;
    private float _minimumTimerStartedAt;
    private string _sourceSceneName;
    // 판 재시작(TryRestartTargetScene) — 로딩 씬이 뜬 뒤 소스(로컬) 씬 대신 타깃 씬을 네트워크 언로드한다.
    private bool _restartingTargetScene;
    // 소스 씬이 NGO 가 로드한 씬인가 — 서버가 StartGameLoading 에서 정해 상태 메시지로 클라에 알린다(SourceSceneUnloadRule).
    private bool _sourceIsNetworkScene;
    // [서버] 마지막 타깃 씬 진입 종류(할 일 100 C3) — 진입 함수가 정하고 ResetFlow 는 지우지 않는다(로딩이 끝난 뒤 맵 스폰이 읽는다).
    // 새 세션·판 종료 Single 전환에서 Direct 로 돌린다.
    private MapEntryKind _entryKind = MapEntryKind.Direct;
    // [서버] NGO LoadScene 으로 들어와 아직 떠 있는 씬 이름. 서버 자신의 LoadComplete 로 넣고 Unity sceneUnloaded 로 뺀다.
    // NGO 의 ScenesLoaded 는 internal 이고, StartHost 때 이미 떠 있던 로컬 씬(지금의 로비)까지 담아 판별에 못 쓴다.
    private readonly HashSet<string> _networkLoadedScenes = new HashSet<string>();
    private readonly List<NetworkObject> _despawnBuffer = new List<NetworkObject>();
    // [서버] 판 종료 Single 전환(TryLoadSessionSceneSingle) 루틴.
    private Coroutine _sessionSceneLoadRoutine;

    // [서버] 판 도중 재합류 대기열(C14 R2c) — Map 이 떠 있을 때 SynchronizeComplete 를 마친 명단 안 클라.
    // 매 프레임 RejoinSpawnGate 로 판정해 Ready 면 첫 스폰 지점에 세운다. 대기 중 퇴장하면 뺀다.
    private sealed class PendingRejoin
    {
        public ulong ClientId;
        public string PlayerId;
        public float EnqueuedAt;
        public bool HasVerdict;
        public RejoinSpawnVerdict LastVerdict;
        public bool LongHoldWarned;
    }

    private const float RejoinLongHoldWarningSeconds = 10f;
    private readonly List<PendingRejoin> _pendingRejoin = new List<PendingRejoin>();
    private readonly List<PendingRejoin> _finishedRejoin = new List<PendingRejoin>();
    private readonly List<Vector3> _rejoinOtherPlayerPositions = new List<Vector3>();
    private readonly List<Vector3> _rejoinBossRoomCandidates = new List<Vector3>();
    private readonly List<Vector3> _rejoinBossPositions = new List<Vector3>();
    private readonly List<Pose> _rejoinBossRoomCorners = new List<Pose>();
    private PartyWipeWatcher _partyWipeWatcher;

    private bool IsServerActive => _networkManager != null && _networkManager.IsListening && _networkManager.IsServer;

    /// <summary>로딩 흐름(첫 진입·판 재시작)이 진행 중인가 — 끝나기 전엔 재시작을 받지 않는다.</summary>
    public bool IsFlowInProgress =>
        (_phase != NetworkLoadingPhase.Idle && _phase != NetworkLoadingPhase.Completed) ||
        _targetLoadRoutine != null || _completionRoutine != null || _restartingTargetScene;

    /// <summary>[서버] 지금(마지막) 타깃 씬 진입 종류 — 로비 포드 연출을 거쳐 왔으면 <see cref="MapEntryKind.Lobby"/>. 클라에선 의미 없다(늘 Direct).</summary>
    public MapEntryKind EntryKind => _entryKind;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.Awake hasNetworkManager={_networkManager != null}");

        // 서버가 켜지자마자 씬 이벤트를 구독한다 — StartHost 직후 NGO 로 로드하는 씬(로비)도 _networkLoadedScenes 에 잡히게.
        if (_networkManager != null)
        {
            _networkManager.OnServerStarted += RegisterNetworkCallbacks;
        }

        SceneManager.sceneUnloaded += HandleUnitySceneUnloaded;
    }

    private void OnDestroy()
    {
        if (_registerCallbacksRoutine != null)
        {
            StopCoroutine(_registerCallbacksRoutine);
            _registerCallbacksRoutine = null;
        }

        if (_networkManager != null)
        {
            _networkManager.OnServerStarted -= RegisterNetworkCallbacks;
        }

        SceneManager.sceneUnloaded -= HandleUnitySceneUnloaded;
        UnregisterNetworkCallbacks();
    }

    // NGO 언로드든 로컬 언로드·Single 교체든 씬이 내려가면 "NGO 씬" 기록에서 뺀다.
    private void HandleUnitySceneUnloaded(Scene scene)
    {
        _networkLoadedScenes.Remove(scene.name);
    }

    public void RegisterNetworkCallbacks()
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.RegisterNetworkCallbacks requested registered={_callbacksRegistered} current={IsRegistrationCurrent()} canRegister={CanRegisterNetworkCallbacks()}");
        if (IsRegistrationCurrent())
        {
            return;
        }

        // 여기까지 왔는데 등록 상태라면, 구독해 둔 상대가 이미 죽은 객체다. 떼고 다시 붙인다.
        if (_callbacksRegistered)
        {
            UnregisterNetworkCallbacks();
        }

        if (_networkManager == null)
        {
            _networkManager = GetComponent<NetworkManager>();
        }

        if (!CanRegisterNetworkCallbacks())
        {
            if (_registerCallbacksRoutine == null)
            {
                _registerCallbacksRoutine = StartCoroutine(RegisterNetworkCallbacksWhenReady());
            }

            return;
        }

        // 구독한 "대상"을 기억해 둔다 — 아래 IsRegistrationCurrent 가 이걸로 세대를 판별한다.
        _registeredSceneManager = _networkManager.SceneManager;
        _registeredMessagingManager = _networkManager.CustomMessagingManager;
        _networkLoadedScenes.Clear(); // 새 세션 — 지난 세션의 NGO 씬 기록은 이 SceneManager 와 무관하다
        _pendingRejoin.Clear();
        _entryKind = MapEntryKind.Direct; // 새 세션(Dev Boot 포함) — 지난 세션의 로비 진입을 끌고 오지 않는다
        if (!_networkManager.IsServer)
        {
            ResetClientFlowForNewSession();
        }

        _registeredSceneManager.OnSceneEvent += HandleSceneEvent;
        _registeredMessagingManager.RegisterNamedMessageHandler(ProgressMessageName, HandleProgressMessage);
        _registeredMessagingManager.RegisterNamedMessageHandler(StateMessageName, HandleStateMessage);
        _networkManager.OnClientConnectedCallback += HandleClientConnected;
        _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        _callbacksRegistered = true;

        Debug.Log("[SceneFlow] NetworkLoadingFlowController.RegisterNetworkCallbacks registered");
        LogDebug("Network callbacks registered.");
    }

    /// <summary>
    /// 지금 구독이 <b>현재 세션의</b> 객체에 붙어 있는가.
    ///
    /// 🔴 NGO 는 <c>StartHost/StartClient</c> 마다 <see cref="NetworkManager.SceneManager"/> 와
    /// <see cref="NetworkManager.CustomMessagingManager"/> 를 <b>새로 만들고</b>, Shutdown 때 null 로 만든다
    /// (NetworkManager.cs 의 Initialize / ShutdownInternal). 그래서 "등록했는가"(bool) 로 판단하면
    /// 두 번째 세션부터 죽은 객체에 붙은 구독만 남고 새 객체에는 영영 안 붙는다.
    ///
    /// 2026-09-18 빌드에서 이것 때문에 로딩이 0% 에서 멈췄다 — 접속에 실패한 뒤 같은 실행에서
    /// 호스트를 켜면 <c>OnSceneEvent</c> 가 한 번도 안 와서 <c>StartTargetLoadAfterSceneEvent</c> 가
    /// 돌지 않고, 타깃 씬 로드가 시작조차 되지 않았다(로그에 HandleSceneEvent 가 0줄).
    /// 그래서 "누구에게 등록했는가"를 본다.
    /// </summary>
    private bool IsRegistrationCurrent()
    {
        return _callbacksRegistered &&
               _networkManager != null &&
               _registeredSceneManager != null &&
               ReferenceEquals(_registeredSceneManager, _networkManager.SceneManager) &&
               ReferenceEquals(_registeredMessagingManager, _networkManager.CustomMessagingManager);
    }

    private IEnumerator RegisterNetworkCallbacksWhenReady()
    {
        while (!IsRegistrationCurrent())
        {
            if (_networkManager == null)
            {
                _networkManager = GetComponent<NetworkManager>();
            }

            if (CanRegisterNetworkCallbacks())
            {
                _registerCallbacksRoutine = null;
                RegisterNetworkCallbacks();
                yield break;
            }

            yield return null;
        }

        _registerCallbacksRoutine = null;
    }

    private bool CanRegisterNetworkCallbacks()
    {
        return _networkManager != null &&
               _networkManager.IsListening &&
               _networkManager.SceneManager != null &&
               _networkManager.CustomMessagingManager != null;
    }

    public void StartGameLoading() => StartGameLoading(MapEntryKind.Direct);

    /// <summary>[서버] 로비 → 맵 로딩. <paramref name="entryKind"/> = 맵 스폰·맵 포드 착지 연출이 읽을 진입 종류(로비 포드 연출 끝이면 Lobby).</summary>
    public void StartGameLoading(MapEntryKind entryKind)
    {
        Debug.Log(
            $"[SceneFlow] NetworkLoadingFlowController.StartGameLoading begin phase={_phase} entryKind={entryKind} " +
            $"listening={_networkManager != null && _networkManager.IsListening} server={IsServerActive}");
        LogLoadedScenes("StartGameLoading.begin");
        RegisterNetworkCallbacks();

        if (!IsServerActive)
        {
            Debug.Log("[SceneFlow] NetworkLoadingFlowController.StartGameLoading blocked not server");
            Edit.LogWarning("[Loading] Only the server or host can start the loading flow.");
            return;
        }

        if (requireLobbyReadyToStart && !CanStartFromLobby())
        {
            Debug.Log("[SceneFlow] NetworkLoadingFlowController.StartGameLoading blocked lobby not ready");
            Edit.LogWarning("[Loading] Cannot start game loading until all connected clients are ready.");
            return;
        }

        BeginMatchRoster();
        ResetFlow();
        _entryKind = entryKind;
        _flowId++;
        _phase = NetworkLoadingPhase.LoadingScene;
        _minimumTimerStartedAt = Time.unscaledTime;
        _sourceSceneName = SceneManager.GetActiveScene().name;
        _sourceIsNetworkScene = _networkLoadedScenes.Contains(_sourceSceneName);

        Debug.Log(
            $"[SceneFlow] NetworkLoadingFlowController.StartGameLoading loadingScene={loadingSceneName} " +
            $"targetScene={targetSceneName} sourceScene={_sourceSceneName} sourceIsNetworkScene={_sourceIsNetworkScene} flowId={_flowId}");
        LogDebug($"StartGameLoading flowId={_flowId}, loadingScene={loadingSceneName}, targetScene={targetSceneName}.");
        ApplyViewState();
        BroadcastState();

        var status = _networkManager.SceneManager.LoadScene(loadingSceneName, LoadSceneMode.Additive);
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.StartGameLoading LoadScene loading status={status}");
        LogDebug($"Requested loading scene load. status={status}.");
        if (status != SceneEventProgressStatus.Started)
        {
            Edit.LogWarning($"[Loading] Failed to load {loadingSceneName} with {nameof(SceneEventProgressStatus)}.{status}.");
        }
    }

    /// <summary>
    /// [서버] 판 재시작 — 타깃 씬(4.MapScene)을 처음 로드한 것처럼 다시 불러온다(호출자: 튜토리얼 구간 스킵 Alt+0, 할 일 34 후속 3).
    /// 정식 진입과 같은 경로를 탄다: 로딩 씬 Additive → (소스 씬 언로드 대신) 동적 NetworkObject·플레이어 디스폰 + 타깃 씬
    /// 네트워크 언로드 → 타깃 씬 Additive 로드 → <see cref="SpawnAllPlayers"/>(10m 낙하 포함) → MainGameReady → 로딩 씬 언로드.
    /// 씬 배치 객체(디렉터·게이트·몬스터·보스 타이머·통계)는 씬과 함께 새로 생기고, 캐릭터 선택(<see cref="ServerCharacterSelectionStore"/>)은
    /// 세션 정적 저장소라 그대로 남는다. 로딩 흐름이 진행 중이거나 타깃 씬이 안 떠 있으면 거부(false + 이유).
    /// 원본: Docs/tech/tutorial-director.md §10.5 · Docs/tech/scene-flow-audit.md.
    /// </summary>
    public bool TryRestartTargetScene(out string reason)
    {
        RegisterNetworkCallbacks();

        if (!IsServerActive)
        {
            reason = "서버가 아니다";
            return false;
        }

        if (IsFlowInProgress)
        {
            reason = $"로딩 흐름이 진행 중이다(phase={_phase})";
            return false;
        }

        if (!IsRegistrationCurrent())
        {
            reason = "씬 이벤트 구독이 아직 준비되지 않았다";
            return false;
        }

        var targetScene = SceneManager.GetSceneByName(targetSceneName);
        if (!targetScene.IsValid() || !targetScene.isLoaded)
        {
            reason = $"타깃 씬 {targetSceneName} 이 로드돼 있지 않다";
            return false;
        }

        ResetFlow();
        MapEntryKind previousEntryKind = _entryKind;
        _entryKind = MapEntryKind.Restart;
        _flowId++;
        _phase = NetworkLoadingPhase.LoadingScene;
        _minimumTimerStartedAt = Time.unscaledTime;
        _sourceSceneName = targetSceneName; // 소스 = 타깃 → 로컬 소스 언로드는 건너뛰고 아래 재시작 분기가 언로드한다
        _restartingTargetScene = true;

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.TryRestartTargetScene loadingScene={loadingSceneName} targetScene={targetSceneName} flowId={_flowId}");
        ApplyViewState();
        BroadcastState();

        var status = _networkManager.SceneManager.LoadScene(loadingSceneName, LoadSceneMode.Additive);
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.TryRestartTargetScene LoadScene loading status={status}");
        if (status != SceneEventProgressStatus.Started)
        {
            // 아무것도 안 바뀌었다 — 판은 그대로 이어진다. 클라가 받은 LoadingScene 상태를 Completed 로 되돌린다.
            _restartingTargetScene = false;
            _entryKind = previousEntryKind;
            _sourceSceneName = string.Empty;
            _phase = NetworkLoadingPhase.Completed;
            BroadcastState();
            reason = $"{loadingSceneName} 로드 요청 실패({status})";
            return false;
        }

        // 판 재시작은 처음 로드한 것처럼 전부 초기화한다 — 부재자 생명 기록도 버린다(돌아오면 Alive·만피). 특성 기록은 특성 디렉터가 새로 스폰될 때 버린다.
        int clearedLives = RejoinMemory.Server.ClearLives();
        if (clearedLives > 0)
        {
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.RejoinMemory clear lives context=restart count={clearedLives}");
        }

        reason = null;
        return true;
    }

    // [서버] 판 재시작 — 타깃 씬을 비운다. 동적 스폰 NetworkObject(플레이어·투사체·장판·스폰 몹 등)는 Spawn() 기본값이
    // DestroyWithScene=false 라 씬 언로드 때 NGO 가 DDOL → 활성 씬으로 옮겨 살려 둔다. 그래서 언로드 전에 전부 직접 디스폰한다.
    // 씬 배치 객체(IsSceneObject)는 언로드가 정리한다. false = 언로드 실패.
    private IEnumerator UnloadTargetSceneForRestart()
    {
        int despawned = DespawnDynamicNetworkObjects();
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadTargetSceneForRestart despawned={despawned}");

        var scene = SceneManager.GetSceneByName(targetSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            MoveActiveSceneBeforeLocalUnload(scene);
        }

        // 로딩 씬 LoadEventCompleted 직후라 NGO 씬 이벤트가 아직 닫히지 않았을 수 있다 — 타깃 로드와 같은 재시도.
        _completedUnloads.Remove(targetSceneName);
        var status = scene.IsValid() && scene.isLoaded
            ? _networkManager.SceneManager.UnloadScene(scene)
            : SceneEventProgressStatus.SceneNotLoaded;
        var retries = 0;
        while (status == SceneEventProgressStatus.SceneEventInProgress && retries < 30)
        {
            retries++;
            yield return null;
            status = _networkManager.SceneManager.UnloadScene(scene);
        }

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadTargetSceneForRestart UnloadScene target={targetSceneName} status={status} retries={retries}");
        if (status == SceneEventProgressStatus.Started)
        {
            while (!_completedUnloads.Contains(targetSceneName))
            {
                yield return null;
            }
        }
        else if (status != SceneEventProgressStatus.SceneNotLoaded)
        {
            Debug.LogError($"[Loading] 판 재시작 — {targetSceneName} 언로드 실패({status}). 같은 씬이 두 번 뜨지 않게 다시 로드하지 않는다.");
        }

        LogLoadedScenes("UnloadTargetSceneForRestart.after");
    }

    private int DespawnDynamicNetworkObjects()
    {
        _despawnBuffer.Clear();
        _despawnBuffer.AddRange(_networkManager.SpawnManager.SpawnedObjectsList);

        var count = 0;
        foreach (var networkObject in _despawnBuffer)
        {
            // 씬 배치(true)는 언로드가 정리한다. 부모가 먼저 디스폰하며 같이 사라진 자식은 IsSpawned=false.
            if (networkObject == null || !networkObject.IsSpawned || networkObject.IsSceneObject != false)
            {
                continue;
            }

            networkObject.Despawn(true);
            count++;
        }

        _despawnBuffer.Clear();
        return count;
    }

    // [서버] 씬 전환 직전 정리(PLAN-48 §4.3·R1) — 동적 NetworkObject(PlayerObject 포함, IsSceneObject=false)를 전부 디스폰하고
    // 남은 스폰 개수(씬 배치 객체 — 언로드가 정리)를 남긴다. 전환마다 남은 수가 불어나면 누적 누수다.
    private void DespawnDynamicNetworkObjectsBeforeTransition(string context)
    {
        int despawned = DespawnDynamicNetworkObjects();
        int remaining = _networkManager.SpawnManager.SpawnedObjectsList.Count;
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.DespawnBeforeTransition context={context} despawned={despawned} remaining={remaining}");
    }

    /// <summary>
    /// [서버] 세션 유지형 Single 전환 — 판이 끝나면 4.MapScene 에서 로비로 바로 간다(PLAN-48 C7, 5.ResultScene 경유 없음).
    /// 한 프레임 뒤에 동적 NetworkObject·플레이어를 디스폰하고(남은 개수 로그, R1) NGO <c>LoadScene(Single)</c> 을 부른다 — 클라는 NGO 가 따라 옮긴다.
    /// 한 프레임 미루는 건 호출 전에 보낸 결과 페이로드(MapScene.GoToResult named message)를 씬 이벤트보다 앞선 프레임에 송신하기 위해서다
    /// (둘 다 UnityTransport 의 같은 ReliableSequenced 파이프라인이라 같은 프레임이어도 순서는 지켜진다).
    /// 로딩 흐름이 진행 중이면 끝날 때까지 기다렸다 전환한다. 서버가 아니거나 이미 전환 중이면 거부(false + 이유).
    /// </summary>
    public bool TryLoadSessionSceneSingle(string sceneName, out string reason)
    {
        RegisterNetworkCallbacks();

        if (!IsServerActive)
        {
            reason = "서버가 아니다";
            return false;
        }

        if (_sessionSceneLoadRoutine != null)
        {
            reason = "이미 Single 전환 중이다";
            return false;
        }

        // 판 끝 — 여기부터 명단 안 사람도 재합류가 아니라 로비 입장으로 다시 받는다(C14).
        EndMatchRoster($"single={sceneName}");
        _entryKind = MapEntryKind.Direct;
        _sessionSceneLoadRoutine = StartCoroutine(LoadSessionSceneSingle(sceneName));
        reason = null;
        return true;
    }

    /// <summary>[서버] 씬을 바꾸는 중 — 로딩 흐름(첫 진입·판 재시작) 또는 맵 → 로비 Single 전환 대기·진행. 재합류 승인을 미룬다(C14).</summary>
    public bool IsSceneTransitionInProgress => IsFlowInProgress || _sessionSceneLoadRoutine != null;

    // [서버] 판 명단 고정(C14) — 로비 → 맵 래치를 통과한 순간 접속해 있던 playerId. 데모 흐름 밖에선 명단 기능 전체를 끈다
    // (playerId 가 없거나 겹친 client-·# 키가 명단에 들어가지 않게).
    private void BeginMatchRoster()
    {
        var roster = MatchRoster.Server;
        if (!DemoSessionDirector.IsDemoFlowActive)
        {
            EndMatchRoster("begin.notDemo");
            return;
        }

        var registry = PlayerIdRegistry.Server;
        var playerIds = new List<string>();
        var clientIds = new List<ulong>();
        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            if (registry.TryGetPlayerId(clientId, out var playerId))
            {
                playerIds.Add(playerId);
                clientIds.Add(clientId);
            }
        }

        // 판 재시작 경로(TryRestartTargetScene)는 이 함수를 거치지 않는다 — 같은 판이라 명단을 그대로 둔다.
        var previous = roster.IsActive ? roster.Count : 0;
        ClearRejoinMemory("begin");
        roster.Begin(playerIds);
        var players = new List<string>(playerIds.Count);
        for (var i = 0; i < playerIds.Count; i++)
        {
            roster.BindClient(playerIds[i], clientIds[i], out _, out _);
            players.Add($"{playerIds[i]}:{clientIds[i]}");
        }

        Debug.Log(
            $"[SceneFlow] NetworkLoadingFlowController.MatchRoster begin count={roster.Count} bound={roster.BoundCount} " +
            $"connected={_networkManager.ConnectedClientsIds.Count} previousActive={previous} players=[{string.Join(",", players)}]");
    }

    /// <summary>[서버] 판 명단을 끝낸다(C14). 활성이었으면 크기를 남긴다 — 판마다 begin/end 가 짝을 이루지 않으면 누적이다(R1).</summary>
    public static void EndMatchRoster(string context)
    {
        // 보관소는 명단이 비활성이어도 비운다 — 판 경계마다 0 으로 돌아가야 누적이 없다(R1).
        ClearRejoinMemory(context);
        var roster = MatchRoster.Server;
        if (!roster.IsActive)
        {
            return;
        }

        int bound = roster.BoundCount;
        int count = roster.End();
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.MatchRoster end context={context} count={count} bound={bound}");
    }

    // [서버] 재합류 보관소(RejoinMemory) 비우기 — 판 명단과 같은 경계. 비운 게 있을 때만 크기를 남긴다.
    private static void ClearRejoinMemory(string context)
    {
        int cleared = RejoinMemory.Server.Clear();
        if (cleared > 0)
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.RejoinMemory clear context={context} count={cleared}");
    }

    private IEnumerator LoadSessionSceneSingle(string sceneName)
    {
        yield return null;
        while (IsServerActive && IsFlowInProgress)
        {
            yield return null;
        }

        if (!IsServerActive)
        {
            Debug.LogWarning($"[SceneFlow] NetworkLoadingFlowController.LoadSessionSceneSingle 서버가 멈춰 전환을 취소한다 scene={sceneName}");
            _sessionSceneLoadRoutine = null;
            yield break;
        }

        DespawnDynamicNetworkObjectsBeforeTransition($"single={sceneName}");

        var status = _networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        var retries = 0;
        while (status == SceneEventProgressStatus.SceneEventInProgress && retries < 30)
        {
            retries++;
            yield return null;
            status = _networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.LoadSessionSceneSingle LoadScene scene={sceneName} status={status} retries={retries}");
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[Loading] {sceneName} Single 로드 실패({status}) — 판 종료 전환이 멈췄다.");
        }

        _sessionSceneLoadRoutine = null;
    }

    // 로비 시작 조건 = 출격 집결 확정으로 로비 훈련 진행기가 메인 진입 래치를 걸었다(할 일 86 T2b — 옛 LobbySession 준비 카운트다운 대체).
    // 진행기가 없는 경로(Dev 부팅 등)는 막지 않는다.
    private bool CanStartFromLobby()
    {
        var training = LobbyTrainingDirector.Active;
        return training == null || training.ServerHasLaunched;
    }

    public void RegisterView(NetworkLoadingScreenView view)
    {
        _view = view;
        ApplyViewState();
    }

    /// <summary>
    /// 스폰 프리팹을 런타임에 덮어쓴다. **개발용 오버라이드 전용**이다(<see cref="DevSceneBooter"/>).
    /// 평소 값은 인스펙터의 <c>defaultPlayerPrefab</c> 하나뿐이다 —
    /// 2026-09-15 이전에는 <c>NetworkSessionLauncher</c> 에도 같은 이름의 필드가 있어
    /// Awake 에서 이 값을 덮어썼다. 그래서 **인스펙터에 보이는 프리팹과 실제로 스폰되는 프리팹이
    /// 달랐다**(이 컴포넌트=Paladin, 실제=Player). 필드를 이쪽 하나로 합쳐 그 괴리를 없앴다.
    /// null 은 무시한다 — 오버라이드를 안 건 씬에서 기본값이 지워지지 않게 하기 위함이다.
    /// </summary>
    public void SetDefaultPlayerPrefab(GameObject playerPrefab)
    {
        if (playerPrefab != null)
        {
            defaultPlayerPrefab = playerPrefab;
        }
    }

    private void UnregisterNetworkCallbacks()
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnregisterNetworkCallbacks registered={_callbacksRegistered}");
        if (!_callbacksRegistered)
        {
            return;
        }

        // ⚠️ 현재의 NetworkManager.SceneManager 가 아니라 **구독할 때 기억해 둔 객체**에서 뗀다.
        // 세션이 바뀌었으면 그 프로퍼티는 이미 다른 객체(또는 null)라, 현재 것을 쓰면
        // 엉뚱한 대상에서 떼거나 조용히 아무것도 안 한 채 낡은 구독이 남는다.
        if (_registeredSceneManager != null)
        {
            _registeredSceneManager.OnSceneEvent -= HandleSceneEvent;
        }

        if (_registeredMessagingManager != null)
        {
            _registeredMessagingManager.UnregisterNamedMessageHandler(ProgressMessageName);
            _registeredMessagingManager.UnregisterNamedMessageHandler(StateMessageName);
        }

        if (_networkManager != null)
        {
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        _registeredSceneManager = null;
        _registeredMessagingManager = null;
        _callbacksRegistered = false;

        LogDebug("Network callbacks unregistered.");
    }

    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.HandleClientConnected clientId={clientId} phase={_phase}");
        LogDebug($"Client connected. clientId={clientId}, phase={_phase}.");

        if (!IsServerActive || _phase == NetworkLoadingPhase.Idle || _phase == NetworkLoadingPhase.Completed)
        {
            return;
        }

        SendState(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        CommitRejoinLife(clientId);

        for (var i = _pendingRejoin.Count - 1; i >= 0; i--)
        {
            var entry = _pendingRejoin[i];
            if (entry.ClientId != clientId)
            {
                continue;
            }

            _pendingRejoin.RemoveAt(i);
            Debug.Log(
                $"[Rejoin] 대기 중 퇴장 — 대기열에서 뺀다 clientId={clientId} playerId={entry.PlayerId} " +
                $"waited={Time.unscaledTime - entry.EnqueuedAt:F1}s pending={_pendingRejoin.Count}");
        }
    }

    // [서버] 퇴장 확정(C14 R5c) — PlayerObject 디스폰 때 남긴 생명 메모를 playerId 키 기록으로 옮긴다(NGO 는 이 콜백보다 디스폰이 먼저다).
    // 메모가 없으면(스폰 보류 중 다시 나감) 이전 기록을 그대로 둔다.
    private void CommitRejoinLife(ulong clientId)
    {
        if (!IsServerActive || !MatchRoster.Server.IsActive)
        {
            return;
        }

        var memory = RejoinMemory.Server;
        if (!MatchRoster.Server.TryGetPlayerIdByLastClient(clientId, out var playerId))
        {
            return;
        }

        if (memory.CommitLifeOnDisconnect(clientId, playerId, out var record))
        {
            Debug.Log($"[Rejoin] 퇴장 — 재합류용 생명 보관 clientId={clientId} playerId={playerId} life={record} (보관 {memory.Count}건)");
        }
        else
        {
            Debug.Log($"[Rejoin] 퇴장 — 생명 메모 없음(PlayerObject 없이 나감), 이전 기록 유지 clientId={clientId} playerId={playerId}");
        }
    }

    // ── 판 도중 재합류(C14 R2c) ─────────────────────────────────────────────────

    // [서버] 늦게 붙은 클라가 씬 동기화를 마쳤다. 판 진행 중(명단 활성·Map 로드됨)이고 명단 안 사람이면
    // Completed 상태를 보내(클라 NotifyMainGameReady → BGM·AudioListener) 재합류 대기열에 넣는다.
    // 로딩 흐름은 Map 이 뜬 뒤 _phase=Completed 로 남아 HandleClientConnected 가 상태를 보내지 않는다 — 그래서 여기서 보낸다.
    private void HandleClientSynchronized(ulong clientId)
    {
        if (!IsServerActive || clientId == _networkManager.LocalClientId)
        {
            return;
        }

        var roster = MatchRoster.Server;
        if (!roster.IsActive)
        {
            return; // 로비·데모 흐름 밖 — 로비 스포너(LobbyPlayerSpawner)나 기존 경로 몫
        }

        if (!PlayerIdRegistry.Server.TryGetPlayerId(clientId, out var playerId) || !roster.Contains(playerId))
        {
            Debug.LogWarning($"[Rejoin] 동기화 완료 클라가 판 명단 밖이다 — 재합류 스폰 안 함 clientId={clientId} playerId='{playerId}'");
            return;
        }

        if (!IsTargetSceneLoaded())
        {
            Debug.LogWarning($"[Rejoin] 동기화 완료인데 {targetSceneName} 이 떠 있지 않다 — 재합류 스폰 안 함 clientId={clientId} playerId={playerId} phase={_phase}");
            return;
        }

        // 판 재시작 흐름 도중이면 그 흐름의 BroadcastState 가 이 클라에게도 간다 — Completed 일 때만 직접 보낸다.
        var sentState = _phase == NetworkLoadingPhase.Completed;
        if (sentState)
        {
            SendState(clientId);
        }

        foreach (var existing in _pendingRejoin)
        {
            if (existing.ClientId == clientId)
            {
                Debug.Log($"[Rejoin] 이미 대기열에 있다 clientId={clientId} playerId={playerId}");
                return;
            }
        }

        _pendingRejoin.Add(new PendingRejoin
        {
            ClientId = clientId,
            PlayerId = playerId,
            EnqueuedAt = Time.unscaledTime,
        });
        var lastClientText = roster.TryGetLastClientId(playerId, out var lastClientId) ? lastClientId.ToString() : "-";
        Debug.Log(
            $"[Rejoin] 대기열 추가 clientId={clientId} playerId={playerId} lastClientId={lastClientText} " +
            $"phase={_phase} flowId={_flowId} sentCompleted={sentState} pending={_pendingRejoin.Count}");
    }

    private void Update()
    {
        if (_pendingRejoin.Count == 0)
        {
            return;
        }

        if (!IsServerActive)
        {
            _pendingRejoin.Clear();
            return;
        }

        TickPendingRejoin();
    }

    // [서버] 대기열 앞에서부터 판정한다 — 같은 프레임에 둘이 Ready 면 앞사람이 먼저 서고, 뒷사람의 빈 칸 계산에 앞사람 위치가 들어간다.
    private void TickPendingRejoin()
    {
        var mapReady = _phase == NetworkLoadingPhase.Completed &&
                       !IsSceneTransitionInProgress &&
                       IsTargetSceneLoaded() &&
                       (GameManager.Instance == null || GameManager.Instance.IsMainGameReady);

        var boss = BossEncounterDirector.Instance;
        var resultReserved = !MatchRoster.Server.IsActive ||
                             _sessionSceneLoadRoutine != null ||
                             (boss != null && boss.IsBossDefeatedServer);

        var teleport = BossTeleportManager.Instance;
        var elevatorOrTeleportBusy = teleport != null &&
                                     (teleport.IsElevatorRunning || teleport.IsEncounterBusy || teleport.IsCountdownActive);

        var bossCinematic = boss != null && IsBossCinematicPhase(boss.Phase);
        var slowMotion = GlobalTimeScale.Instance != null && GlobalTimeScale.Instance.IsActive;

        if (_partyWipeWatcher == null)
        {
            _partyWipeWatcher = FindFirstObjectByType<PartyWipeWatcher>();
        }

        var wipePending = _partyWipeWatcher != null && _partyWipeWatcher.IsWipeGracePending;

        _finishedRejoin.Clear();
        // 스폰이 구독자를 거쳐 대기열을 건드려도 안전하게 복사본을 돈다.
        var snapshotList = _pendingRejoin.ToArray();
        foreach (var entry in snapshotList)
        {
            var connected = _networkManager.ConnectedClients.TryGetValue(entry.ClientId, out var client);
            var verdict = RejoinSpawnGate.Evaluate(new RejoinSpawnSnapshot(
                connected,
                clientSynchronized: true, // SynchronizeComplete 를 받은 클라만 대기열에 들어온다
                mapReady,
                resultReserved,
                connected && client.PlayerObject != null,
                elevatorOrTeleportBusy,
                bossCinematic,
                slowMotion,
                wipePending));

            var waited = Time.unscaledTime - entry.EnqueuedAt;
            if (!entry.HasVerdict || !entry.LastVerdict.Equals(verdict))
            {
                Debug.Log(
                    $"[Rejoin] 판정 clientId={entry.ClientId} playerId={entry.PlayerId} verdict={verdict} " +
                    $"before={(entry.HasVerdict ? entry.LastVerdict.ToString() : "-")} waited={waited:F1}s");
                entry.HasVerdict = true;
                entry.LastVerdict = verdict;
            }

            switch (verdict.Decision)
            {
                case RejoinSpawnDecision.Hold:
                    if (!entry.LongHoldWarned && waited > RejoinLongHoldWarningSeconds)
                    {
                        entry.LongHoldWarned = true;
                        Debug.LogWarning(
                            $"[Rejoin] {RejoinLongHoldWarningSeconds:F0}초 넘게 스폰 보류 중 clientId={entry.ClientId} playerId={entry.PlayerId} " +
                            $"reason={verdict.Reason} waited={waited:F1}s");
                    }

                    break;

                case RejoinSpawnDecision.Drop:
                    if (verdict.Reason == RejoinSpawnReason.AlreadyHasPlayerObject)
                    {
                        // 판 재시작 스폰 등이 먼저 세웠다 — 스폰은 안 하지만 명단 묶기·재합류 발행은 한다(옛 clientId 키가 남지 않게).
                        BindRejoinedClient(entry, "existingPlayerObject");
                    }

                    _finishedRejoin.Add(entry);
                    break;

                case RejoinSpawnDecision.Ready:
                    SpawnRejoiner(entry);
                    _finishedRejoin.Add(entry);
                    break;
            }
        }

        foreach (var entry in _finishedRejoin)
        {
            _pendingRejoin.Remove(entry);
        }

        _finishedRejoin.Clear();
    }

    // [서버] 맵 첫 스폰 지점(Q-A) — 처음 스폰과 같은 공식(RejoinSpawnPicker.SlotPose)의 빈 칸. 보스 룸이면 모서리 4지점 중 가장 먼 곳(R3c).
    // 프리팹은 선택 저장소(playerId 로 승인 때 복원).
    private void SpawnRejoiner(PendingRejoin entry)
    {
        var baseSpawnPoint = ResolveBasePlayerSpawnPoint();
        _rejoinOtherPlayerPositions.Clear();
        _rejoinBossRoomCandidates.Clear();
        foreach (var other in _networkManager.ConnectedClientsList)
        {
            if (other.ClientId != entry.ClientId && other.PlayerObject != null)
            {
                var position = other.PlayerObject.transform.position;
                _rejoinOtherPlayerPositions.Add(position);

                // 보스 룸 판정은 영구 사망자를 빼고 본다(메모 "생존·Soul" — 사망 연출 중도 아직 아레나 안이라 넣는다).
                var lifeCycle = other.PlayerObject.GetComponent<PlayerLifeCycleController>();
                if (lifeCycle == null || lifeCycle.State != PlayerLifeState.PermanentDead)
                {
                    _rejoinBossRoomCandidates.Add(position);
                }
            }
        }

        var boss = BossEncounterDirector.Instance;
        var bossPhase = boss != null ? boss.Phase : BossEncounterPhase.Idle;
        var inBossRoom = BossRoomRejoin.IsInBossRoom(bossPhase, BossRoomRejoin.AnyInsideBossArea(_rejoinBossRoomCandidates));

        _rejoinBossPositions.Clear();
        var cornersFromPrefab = false;
        if (inBossRoom)
        {
            BossRoomRejoin.TryCollectCorners(_rejoinBossRoomCorners, out cornersFromPrefab);
            // 보스 쪽 파일을 고치지 않으려고 Director 의 private 참조 대신 씬에서 찾는다(재합류 순간 1회).
            foreach (var bossUnit in FindObjectsByType<TwentyThreeBoss>(FindObjectsSortMode.None))
            {
                if (bossUnit.isActiveAndEnabled)
                {
                    _rejoinBossPositions.Add(bossUnit.transform.position);
                }
            }
        }
        else
        {
            _rejoinBossRoomCorners.Clear();
        }

        var pick = RejoinSpawnPicker.Pick(new RejoinSpawnContext(
            inBossRoom,
            baseSpawnPoint != null,
            baseSpawnPoint != null ? baseSpawnPoint.position : Vector3.zero,
            baseSpawnPoint != null ? baseSpawnPoint.rotation : Quaternion.identity,
            spawn_Offset,
            _rejoinOtherPlayerPositions,
            _rejoinBossRoomCorners,
            _rejoinBossPositions));

        var result = PlayerSpawnService.SpawnForClient(
            _networkManager, entry.ClientId, characterRoster, defaultPlayerPrefab, pick.Pose,
            SceneManager.GetSceneByName(targetSceneName));
        if (!result.Succeeded)
        {
            Debug.LogError(
                $"[Rejoin] 재합류 스폰 실패 clientId={entry.ClientId} playerId={entry.PlayerId} failure={result.Failure} — 대기열에서 뺀다.");
            return;
        }

        Debug.Log(
            $"[Rejoin] 재합류 스폰 clientId={entry.ClientId} playerId={entry.PlayerId} prefab={result.Prefab.name} " +
            $"slot={pick.SlotIndex} others={_rejoinOtherPlayerPositions.Count} " +
            $"bossRoom={inBossRoom}(phase={bossPhase}) corner={(pick.IsBossRoomCorner ? (cornersFromPrefab ? "prefab" : "fallback") : "-")} bosses={_rejoinBossPositions.Count} " +
            $"baseSpawnPoint={(baseSpawnPoint != null ? baseSpawnPoint.position.ToString() : "null(폴백)")} " +
            $"position={pick.Pose.position} waited={Time.unscaledTime - entry.EnqueuedAt:F1}s");
        BindRejoinedClient(entry, "spawn");
    }

    // [서버] 스폰 직후 — 명단에 새 clientId 를 묶고, 이 판에서 다른 clientId 를 썼으면 PlayerRejoined 를 발행한다(구독은 R4c·R5c).
    private static void BindRejoinedClient(PendingRejoin entry, string context)
    {
        if (!MatchRoster.Server.BindClient(entry.PlayerId, entry.ClientId, out var rejoined, out var oldClientId))
        {
            Debug.LogWarning($"[Rejoin] 명단 묶기 실패(명단 비활성·밖) clientId={entry.ClientId} playerId={entry.PlayerId} context={context}");
            return;
        }

        if (!rejoined)
        {
            Debug.Log($"[Rejoin] 명단 묶기 — 같은 clientId 라 재합류 발행 없음 clientId={entry.ClientId} playerId={entry.PlayerId} context={context}");
            return;
        }

        // 구독자(특성 복원 R4c·튜토리얼 RebindPlayer)가 먼저 돈다. 생명 상태 복원(R5c)은 구독으로 붙이지 말고 이 발행이 끝난 뒤
        // 여기서 직접 부른다 — 특성 효과(영구 상태이상)가 사망·연출 잠금에서 거부되지 않게 "생명 상태는 마지막에"(설계 메모 §3.3).
        var subscribers = MatchRoster.RaisePlayerRejoined(entry.PlayerId, oldClientId, entry.ClientId);
        Debug.Log(
            $"[Rejoin] PlayerRejoined playerId={entry.PlayerId} oldClientId={oldClientId} newClientId={entry.ClientId} " +
            $"subscribers={subscribers} bound={MatchRoster.Server.BoundCount}/{MatchRoster.Server.Count} context={context}");

        RestoreRejoinedLife(entry, restore: context == "spawn");
    }

    // [서버] 생명·체력 복원(C14 R5c) — PlayerRejoined 구독자 뒤. 기록이 없으면 새 PlayerObject 의 Alive·만피 그대로.
    // 판 재시작 스폰이 먼저 세운 오브젝트(existingPlayerObject)는 "처음 로드한 것처럼" 초기화된 판이라 기록을 버린다.
    private static void RestoreRejoinedLife(PendingRejoin entry, bool restore)
    {
        if (!RejoinMemory.Server.TryTakeLife(entry.PlayerId, out var record))
        {
            Debug.Log($"[Rejoin] 생명 기록 없음 — Alive·만피 그대로 clientId={entry.ClientId} playerId={entry.PlayerId}");
            return;
        }

        if (!restore)
        {
            Debug.Log($"[Rejoin] 생명 기록 버림(판 재시작 스폰) clientId={entry.ClientId} playerId={entry.PlayerId} life={record}");
            return;
        }

        var networkManager = NetworkManager.Singleton;
        var lifeCycle = networkManager != null && networkManager.ConnectedClients.TryGetValue(entry.ClientId, out var client) &&
                        client.PlayerObject != null
            ? client.PlayerObject.GetComponent<PlayerLifeCycleController>()
            : null;
        if (lifeCycle == null || !lifeCycle.RestoreForRejoinServer(record.State, record.HpRatio))
        {
            Debug.LogWarning(
                $"[Rejoin] 생명 복원 실패(PlayerLifeCycleController 없음·연출 잠금) — Alive·만피 그대로 " +
                $"clientId={entry.ClientId} playerId={entry.PlayerId} life={record}");
            return;
        }

        Debug.Log($"[Rejoin] 생명 복원 clientId={entry.ClientId} playerId={entry.PlayerId} life={record} 보관 {RejoinMemory.Server.Count}건");
    }

    // BossEncounterDirector.IsCinematicPhase(private)와 같은 값 — 보스 쪽 파일을 고치지 않으려고 공개 Phase 로 판정한다.
    // (R3c 판단: 사본 유지. Director 에 getter 를 열면 경석 영역 파일 수정이 생기고, 4줄 사본은 enum 이 바뀔 때만 어긋난다.)
    private static bool IsBossCinematicPhase(BossEncounterPhase phase)
    {
        return phase == BossEncounterPhase.Preparing ||
               phase == BossEncounterPhase.Descending ||
               phase == BossEncounterPhase.Impact ||
               phase == BossEncounterPhase.Dialogue;
    }

    private bool IsTargetSceneLoaded()
    {
        var scene = SceneManager.GetSceneByName(targetSceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        RegisterNetworkCallbacks();
        CaptureSourceSceneIfNeeded(sceneEvent);
        Debug.Log(
            $"[SceneFlow] NetworkLoadingFlowController.HandleSceneEvent type={sceneEvent.SceneEventType} " +
            $"scene={sceneEvent.SceneName} clientId={sceneEvent.ClientId} phase={_phase} flowId={_flowId}");
        LogLoadedScenes($"SceneEvent.{sceneEvent.SceneEventType}.{sceneEvent.SceneName}");
        LogDebug(
            $"SceneEvent type={sceneEvent.SceneEventType}, scene={sceneEvent.SceneName}, clientId={sceneEvent.ClientId}, " +
            $"phase={_phase}, flowId={_flowId}.");

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                HandleLoadStarted(sceneEvent);
                break;
            case SceneEventType.LoadComplete:
                TrackNetworkLoadedScene(sceneEvent);
                HandleLoadComplete(sceneEvent);
                break;
            case SceneEventType.LoadEventCompleted:
                HandleLoadEventCompleted(sceneEvent);
                break;
            case SceneEventType.UnloadEventCompleted:
                HandleUnloadEventCompleted(sceneEvent);
                break;
            case SceneEventType.SynchronizeComplete:
                HandleClientSynchronized(sceneEvent.ClientId);
                break;
        }
    }

    // [서버] 서버 자신의 LoadComplete = 서버가 NGO LoadScene 으로 올린 씬이다(서버는 동기화 로드를 받지 않는다).
    // 클라 쪽은 기록하지 않는다 — 클라는 접속 동기화 때 이미 떠 있던 로컬 로비에도 LoadComplete 를 받아 구분이 안 된다.
    private void TrackNetworkLoadedScene(SceneEvent sceneEvent)
    {
        if (!IsServerActive || sceneEvent.ClientId != _networkManager.LocalClientId)
        {
            return;
        }

        _networkLoadedScenes.Add(sceneEvent.SceneName);
    }

    private void HandleUnloadEventCompleted(SceneEvent sceneEvent)
    {
        _completedUnloads.Add(sceneEvent.SceneName);
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.HandleUnloadEventCompleted scene={sceneEvent.SceneName}");
        LogDebug($"UnloadEventCompleted. scene={sceneEvent.SceneName}.");
    }

    private void HandleLoadStarted(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneName != targetSceneName)
        {
            return;
        }

        _phase = NetworkLoadingPhase.LoadingGame;
        _localLoadOperation = sceneEvent.AsyncOperation;

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.HandleLoadStarted target scene={sceneEvent.SceneName} clientId={sceneEvent.ClientId}");
        LogDebug($"Target load started. clientId={sceneEvent.ClientId}, asyncOperation={_localLoadOperation != null}.");
        _minimumTimerStartedAt = Time.unscaledTime;

        if (IsServerActive)
        {
            CaptureTrackedClients();
            BroadcastState();
        }

        StartLocalProgressReporting();
        ApplyViewState();

        // 할 일 57 R 예열 — 로딩 화면이 떠 있는 동안(최소 표시 시간 타이머는 위에서 막 시작) 적 예고 C3 의 첫 호출 비용을 치른다.
        // 호스트·클라 모두 여기 온다. Play 당 1회(두 번째부터 아무것도 안 함). 상세: handoff-enemy-indicator §9.4.
        EnemyC3Visuals.Prewarm();
    }

    private void HandleLoadComplete(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneName != targetSceneName)
        {
            return;
        }

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.HandleLoadComplete target scene={sceneEvent.SceneName} clientId={sceneEvent.ClientId}");
        if (sceneEvent.ClientId == _networkManager.LocalClientId)
        {
            LogDebug("Local target scene load complete. Submitting 100% progress.");
            SubmitLocalProgress(1f);
            StopLocalProgressReporting();
        }

        if (IsServerActive)
        {
            _trackedClients.Add(sceneEvent.ClientId);
            _clientProgress[sceneEvent.ClientId] = 1f;
            LogDebug($"Server recorded LoadComplete. clientId={sceneEvent.ClientId}.");
            BroadcastAverageProgress();
        }
    }

    private void HandleLoadEventCompleted(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneName == loadingSceneName)
        {
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.HandleLoadEventCompleted loading scene={sceneEvent.SceneName} isServer={IsServerActive} hasTargetRoutine={_targetLoadRoutine != null}");
            if (IsServerActive && _targetLoadRoutine == null)
            {
                _targetLoadRoutine = StartCoroutine(StartTargetLoadAfterSceneEvent());
            }
            else if (!IsServerActive)
            {
                // 로컬 소스(지금의 Lobby/Result)는 NGO가 언로드하지 않는다.
                // 서버는 StartTargetLoadAfterSceneEvent에서 로컬 소스 씬을 언로드하므로
                // 클라이언트도 대칭적으로 자기 로컬 소스(Lobby) 씬을 여기서 언로드한다.
                // NGO 소스면(서버 상태 메시지의 플래그) 서버의 NGO 언로드가 클라까지 내리므로 여기선 건너뛴다(SourceSceneUnloadRule).
                StartCoroutine(UnloadSourceSceneIfNeeded());
            }

            return;
        }

        if (sceneEvent.SceneName != targetSceneName)
        {
            return;
        }

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.HandleLoadEventCompleted target scene={sceneEvent.SceneName} completed={sceneEvent.ClientsThatCompleted.Count} timedOut={sceneEvent.ClientsThatTimedOut.Count}");
        StopLocalProgressReporting();
        _localLoadOperation = null;

        if (!IsServerActive)
        {
            SubmitLocalProgress(1f);
            return;
        }

        _targetLoadEventCompleted = true;

        foreach (var clientId in sceneEvent.ClientsThatCompleted)
        {
            _trackedClients.Add(clientId);
            _clientProgress[clientId] = 1f;
            LogDebug($"LoadEventCompleted client completed. clientId={clientId}.");
        }

        foreach (var clientId in sceneEvent.ClientsThatTimedOut)
        {
            _timedOutClients.Add(clientId);
            _trackedClients.Remove(clientId);
            _clientProgress.Remove(clientId);
            LogDebug($"LoadEventCompleted client timed out. clientId={clientId}.");
        }

        foreach (var clientId in _trackedClients)
        {
            if (!_timedOutClients.Contains(clientId))
            {
                _clientProgress[clientId] = 1f;
            }
        }

        SpawnAllPlayersOnce();

        _averageProgress = CalculateAverageProgress();
        LogDebug($"Target scene load event completed. average={_averageProgress:P0}. Waiting for completion routine.");
        ApplyViewState();
        BroadcastAverageProgress();
    }

    public void SpawnAllPlayers()
    {
        if (!IsServerActive)
        {
            Edit.LogWarning("[Loading] Only the server or host can spawn player objects.");
            return;
        }

        var baseSpawnPoint = ResolveBasePlayerSpawnPoint();

        var spawnIndex = 0;
        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            SpawnPlayerForClient(clientId, spawnIndex, baseSpawnPoint);
            spawnIndex++;
        }
    }

    private void SpawnAllPlayersOnce()
    {
        if (_playersSpawnedForCurrentTargetScene)
        {
            return;
        }

        _playersSpawnedForCurrentTargetScene = true;
        SpawnAllPlayers();
    }

    // 위치 계산은 여기, 프리팹 결정·생성·SpawnAsPlayerObject 는 PlayerSpawnService(로비와 공용).
    private void SpawnPlayerForClient(ulong clientId, int spawnIndex, Transform baseSpawnPoint)
    {
        var spawnPose = ResolvePlayerSpawnPose(spawnIndex, baseSpawnPoint);
        var result = PlayerSpawnService.SpawnForClient(
            _networkManager, clientId, characterRoster, defaultPlayerPrefab, spawnPose,
            SceneManager.GetSceneByName(targetSceneName));
        if (!result.Succeeded)
        {
            return;
        }

        // 스폰 좌표는 "이 클라만 엉뚱한 위치" 류 문제의 1차 증거다. debugLogging 설정과 무관하게 남긴다.
        Edit.Log(
            $"[Loading] Spawned player. clientId={clientId}, prefab={result.Prefab.name}, " +
            $"spawnIndex={spawnIndex}, baseSpawnPoint={(baseSpawnPoint != null ? baseSpawnPoint.position.ToString() : "null(폴백)")}, " +
            $"position={spawnPose.position}.");
    }

    /// <summary>스폰에 쓰는 캐릭터 로스터(튜토리얼 캐릭터 판별이 PrefabIdHash 역조회에 쓴다).</summary>
    public CharacterRoster CharacterRoster => characterRoster;

    // 판 도중 재합류(SpawnRejoiner)와 같은 칸 공식이다 — RejoinSpawnPicker.SlotPose 하나로 맞춘다.
    // 예외: 로비 포드 연출을 거쳐 왔고 맵 포드 착지 연출이 켜져 있으면 포드 착지점 좌석(낙하 없이, 할 일 100 C3). 재합류는 이 함수를 안 거친다.
    private Pose ResolvePlayerSpawnPose(int spawnIndex, Transform baseSpawnPoint)
    {
        if (_entryKind == MapEntryKind.Lobby && baseSpawnPoint != null &&
            MapPodArrival.Active != null && MapPodArrival.Active.TryGetSeatPoseServer(spawnIndex, baseSpawnPoint, out Pose seat))
        {
            return seat;
        }

        return baseSpawnPoint != null
            ? RejoinSpawnPicker.SlotPose(true, baseSpawnPoint.position, baseSpawnPoint.rotation, spawn_Offset, spawnIndex)
            : RejoinSpawnPicker.SlotPose(false, Vector3.zero, Quaternion.identity, spawn_Offset, spawnIndex);
    }

    private Transform ResolveBasePlayerSpawnPoint()
    {
        var mapGenerator = FindFirstObjectByType<MapGenerator>();
        if (mapGenerator == null)
        {
            return null; // ResolvePlayerSpawnPose가 null이면 기본 위치로 폴백
        }

        // v2: 플레이어 스폰 구역 = PlayerSpawn 역할이 배정된 ZoneSlot.
        // 슬롯 앵커 transform 위치에 스폰 존 ZoneLayout 프리팹이 배치된다.
        var spawnSlot = mapGenerator.GetRoleSlot(ZoneRole.PlayerSpawn);
        return spawnSlot != null ? spawnSlot.transform : null;
    }

    private IEnumerator StartTargetLoadAfterSceneEvent()
    {
        Debug.Log("[SceneFlow] NetworkLoadingFlowController.StartTargetLoadAfterSceneEvent begin");
        LogLoadedScenes("StartTargetLoadAfterSceneEvent.beforeSourceUnload");
        yield return null;
        if (_restartingTargetScene)
        {
            yield return UnloadTargetSceneForRestart();
            _restartingTargetScene = false;

            var stale = SceneManager.GetSceneByName(targetSceneName);
            if (stale.IsValid() && stale.isLoaded)
            {
                // 언로드 실패 복구 — 남은 판에 플레이어만 다시 세우고 로딩을 닫는다(같은 씬을 겹쳐 올리지 않는다).
                // 타깃 로드 이벤트가 없는 경로라 완료 루틴이 기다리지 않게 표시한다.
                _targetLoadEventCompleted = true;
                SpawnAllPlayersOnce();
                _targetLoadRoutine = null;
                _completionRoutine = StartCoroutine(CompleteAfterMinimumVisibleTime());
                yield break;
            }
        }
        else
        {
            yield return UnloadSourceSceneIfNeeded();
        }
        LogLoadedScenes("StartTargetLoadAfterSceneEvent.afterSourceUnload");

        _phase = NetworkLoadingPhase.LoadingGame;
        CaptureTrackedClients();
        LogDebug($"Starting target scene load. trackedClients={_trackedClients.Count}.");
        ApplyViewState();
        BroadcastState();

        var status = _networkManager.SceneManager.LoadScene(targetSceneName, LoadSceneMode.Additive);
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.StartTargetLoadAfterSceneEvent LoadScene target={targetSceneName} status={status}");
        LogDebug($"Requested target scene load. status={status}.");
        var retries = 0;
        while (status == SceneEventProgressStatus.SceneEventInProgress && retries < 30)
        {
            retries++;
            yield return null;
            status = _networkManager.SceneManager.LoadScene(targetSceneName, LoadSceneMode.Additive);
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.StartTargetLoadAfterSceneEvent retry={retries} status={status}");
            LogDebug($"Retry target scene load. retry={retries}, status={status}.");
        }

        if (status != SceneEventProgressStatus.Started)
        {
            Edit.LogWarning($"[Loading] Failed to load {targetSceneName} with {nameof(SceneEventProgressStatus)}.{status}.");
        }

        _targetLoadRoutine = null;
        Debug.Log("[SceneFlow] NetworkLoadingFlowController.StartTargetLoadAfterSceneEvent end");
    }

    private IEnumerator CompleteAfterMinimumVisibleTime()
    {
        _phase = NetworkLoadingPhase.WaitingForPlayers;
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.CompleteAfterMinimumVisibleTime begin phase={_phase} average={_averageProgress}");
        LogLoadedScenes("CompleteAfterMinimumVisibleTime.begin");
        LogDebug("All tracked clients reached 100%. Waiting for minimum visible time.");
        ApplyViewState();
        BroadcastState();

        // 🔴 이 루틴은 로컬 진행률 0.9(= AsyncOperation 이 활성화 직전) 에서 시작된다. 표시 시간이 0 이면
        // (DevSceneBooter.SetEditorDefaults(...,0,0) — Dev 부팅의 판 재시작) 타깃 씬 NGO 로드 이벤트가 닫히기 전에
        // 로딩 씬 언로드를 요청해 NGO 가 SceneEventInProgress 로 거절했다 → 로딩 씬이 활성 씬인 채 남고,
        // 그 사이 Instantiate 된 몬스터도 로딩 씬에 남았다(할 일 34 후속 4). 로드 이벤트 완료를 먼저 기다린다.
        while (!_targetLoadEventCompleted)
        {
            yield return null;
        }

        var elapsed = Time.unscaledTime - _minimumTimerStartedAt;
        var remaining = minimumVisibleSeconds - elapsed;
        if (remaining > 0f)
        {
            yield return new WaitForSecondsRealtime(remaining);
        }

        _phase = NetworkLoadingPhase.Ready;
        Debug.Log("[SceneFlow] NetworkLoadingFlowController.CompleteAfterMinimumVisibleTime phase=Ready");
        LogDebug("Minimum visible time satisfied. Showing ready message.");
        ApplyViewState();
        BroadcastState();

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, readyMessageSeconds));

        _phase = NetworkLoadingPhase.Activating;
        Debug.Log("[SceneFlow] NetworkLoadingFlowController.CompleteAfterMinimumVisibleTime phase=Activating");
        LogDebug("Ready message finished. Completing loading flow.");
        ApplyViewState();
        BroadcastState();

        _phase = NetworkLoadingPhase.Completed;
        _averageProgress = 1f;
        Debug.Log("[SceneFlow] NetworkLoadingFlowController.CompleteAfterMinimumVisibleTime phase=Completed");
        ApplyViewState();
        GameManager.Instance?.NotifyMainGameReady();
        BroadcastState();

        yield return UnloadLoadingScene();
        LogLoadedScenes("CompleteAfterMinimumVisibleTime.afterLoadingUnload");
        ResetFlow();
    }

    private void CaptureTrackedClients()
    {
        _trackedClients.Clear();
        _timedOutClients.Clear();
        _clientProgress.Clear();

        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            _trackedClients.Add(clientId);
            _clientProgress[clientId] = 0f;
        }

        if (_networkManager.IsHost)
        {
            _trackedClients.Add(_networkManager.LocalClientId);
            _clientProgress[_networkManager.LocalClientId] = 0f;
        }

        LogDebug($"Captured tracked clients. count={_trackedClients.Count}, ids={string.Join(", ", _trackedClients)}.");
    }

    private void StartLocalProgressReporting()
    {
        StopLocalProgressReporting();
        _localProgressRoutine = StartCoroutine(ReportLocalProgress());
    }

    private void StopLocalProgressReporting()
    {
        if (_localProgressRoutine != null)
        {
            StopCoroutine(_localProgressRoutine);
            _localProgressRoutine = null;
        }
    }

    private IEnumerator ReportLocalProgress()
    {
        while (!IsLocalLoadingReady())
        {
            SubmitLocalProgress(CalculateLocalLoadingProgress());
            yield return null;
        }

        SubmitLocalProgress(1f);
    }

    private bool IsLocalLoadingReady()
    {
        return _localLoadOperation == null || _localLoadOperation.progress >= 0.9f;
    }

    private float CalculateLocalLoadingProgress()
    {
        return GetLocalSceneLoadProgress();
    }

    private float GetLocalSceneLoadProgress()
    {
        if (_localLoadOperation == null)
        {
            return 1f;
        }

        return Mathf.Clamp01(_localLoadOperation.progress / 0.9f);
    }

    private void SubmitLocalProgress(float progress)
    {
        progress = Mathf.Clamp01(progress);

        if (IsServerActive)
        {
            _trackedClients.Add(_networkManager.LocalClientId);
            _clientProgress[_networkManager.LocalClientId] = progress;
            LogDebug($"Local server progress updated. progress={progress:P0}.");
            BroadcastAverageProgress();
            return;
        }

        var messagingManager = _networkManager.CustomMessagingManager;
        if (messagingManager == null)
        {
            return;
        }

        using (var writer = new FastBufferWriter(ProgressMessageSize, Allocator.Temp))
        {
            writer.WriteValueSafe(_flowId);
            writer.WriteValueSafe(progress);
            messagingManager.SendNamedMessage(ProgressMessageName, NetworkManager.ServerClientId, writer);
        }

        LogDebug($"Client sent progress. progress={progress:P0}, flowId={_flowId}.");
    }

    private void HandleProgressMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsServerActive)
        {
            return;
        }

        reader.ReadValueSafe(out uint flowId);
        reader.ReadValueSafe(out float progress);

        if ((flowId != _flowId && flowId != 0) || _timedOutClients.Contains(senderClientId))
        {
            LogDebug($"Ignored progress message. sender={senderClientId}, messageFlowId={flowId}, currentFlowId={_flowId}, timedOut={_timedOutClients.Contains(senderClientId)}.");
            return;
        }

        _trackedClients.Add(senderClientId);
        _clientProgress[senderClientId] = Mathf.Clamp01(progress);
        LogDebug($"Received progress message. sender={senderClientId}, progress={progress:P0}, flowId={flowId}.");
        BroadcastAverageProgress();
    }

    private void BroadcastAverageProgress()
    {
        if (!IsServerActive)
        {
            return;
        }

        _averageProgress = CalculateAverageProgress();
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.BroadcastAverageProgress phase={_phase} average={_averageProgress} tracked={_trackedClients.Count} completionRoutine={_completionRoutine != null}");
        LogDebug($"Average progress updated. average={_averageProgress:P0}, trackedClients={_trackedClients.Count}.");
        ApplyViewState();
        BroadcastState();

        if (_phase == NetworkLoadingPhase.LoadingGame && _averageProgress >= 1f && _completionRoutine == null)
        {
            _completionRoutine = StartCoroutine(CompleteAfterMinimumVisibleTime());
        }
    }

    private float CalculateAverageProgress()
    {
        if (_trackedClients.Count == 0)
        {
            return 0f;
        }

        var sum = 0f;
        var count = 0;

        foreach (var clientId in _trackedClients)
        {
            if (_timedOutClients.Contains(clientId))
            {
                continue;
            }

            count++;
            if (_clientProgress.TryGetValue(clientId, out var progress))
            {
                sum += progress;
            }
        }

        return count == 0 ? 1f : Mathf.Clamp01(sum / count);
    }

    private void BroadcastState()
    {
        if (!IsServerActive || _networkManager.CustomMessagingManager == null)
        {
            return;
        }

        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            if (_networkManager.IsHost && clientId == _networkManager.LocalClientId)
            {
                continue;
            }

            SendState(clientId);
        }

        LogDebug($"Broadcast state. phase={_phase}, average={_averageProgress:P0}, flowId={_flowId}.");
    }

    private void SendState(ulong clientId)
    {
        var messagingManager = _networkManager.CustomMessagingManager;
        if (messagingManager == null)
        {
            return;
        }

        using (var writer = new FastBufferWriter(StateMessageSize, Allocator.Temp))
        {
            writer.WriteValueSafe(_flowId);
            writer.WriteValueSafe((byte)_phase);
            writer.WriteValueSafe(_averageProgress);
            writer.WriteValueSafe(_sourceIsNetworkScene);
            messagingManager.SendNamedMessage(StateMessageName, clientId, writer);
        }

        LogDebug($"Sent state. clientId={clientId}, phase={_phase}, average={_averageProgress:P0}, flowId={_flowId}.");
    }

    private void HandleStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        reader.ReadValueSafe(out uint flowId);
        reader.ReadValueSafe(out byte phaseValue);
        reader.ReadValueSafe(out float averageProgress);
        reader.ReadValueSafe(out bool sourceIsNetworkScene);

        if (LoadingStateFlowRule.IsStale(flowId, _flowId))
        {
            Debug.LogWarning($"[SceneFlow] NetworkLoadingFlowController.HandleStateMessage 지난 흐름 상태를 버린다 messageFlowId={flowId} currentFlowId={_flowId} phase={(NetworkLoadingPhase)phaseValue}");
            return;
        }

        if (flowId > _flowId && _phase == NetworkLoadingPhase.Completed)
        {
            // 끝난 흐름 뒤의 새 흐름(판 재시작) — 지난 흐름의 소스 씬(로비) 이름을 버린다. 소스 = 맵이라 클라가 로컬로 내릴 씬은 없다.
            // 첫 진입(_phase=Idle)은 건드리지 않는다 — 로딩 씬 이벤트가 먼저 와서 로비를 잡아 뒀을 수 있다.
            _sourceSceneName = string.Empty;
        }

        _flowId = flowId;
        _phase = (NetworkLoadingPhase)phaseValue;
        _averageProgress = Mathf.Clamp01(averageProgress);
        // 서버는 로딩 씬 LoadScene 전에 상태를 먼저 보내므로, 클라가 로딩 씬 LoadEventCompleted 를 받을 땐 이미 와 있다.
        _sourceIsNetworkScene = sourceIsNetworkScene;

        LogDebug($"Received state. sender={senderClientId}, phase={_phase}, average={_averageProgress:P0}, flowId={_flowId}.");

        ApplyViewState();
        if (_phase == NetworkLoadingPhase.Completed)
        {
            GameManager.Instance?.NotifyMainGameReady();
        }
    }

    private void ApplyViewState()
    {
        if (_view == null)
        {
            return;
        }

        _view.SetProgress(_averageProgress);
        _view.SetPhase(_phase);

        if (_phase == NetworkLoadingPhase.Completed)
        {
            _view.CompleteAndDestroy();
            _view = null;
        }
    }

    private void LogDebug(string message)
    {
        if (!debugLogging)
        {
            return;
        }

        var role = "Offline";
        var localClientId = 0UL;
        if (_networkManager != null)
        {
            localClientId = _networkManager.LocalClientId;
            if (_networkManager.IsHost)
            {
                role = "Host";
            }
            else if (_networkManager.IsServer)
            {
                role = "Server";
            }
            else if (_networkManager.IsClient)
            {
                role = "Client";
            }
        }

        Edit.Log($"[NetworkLoadingFlowController][{role}:{localClientId}] {message}", this);
    }

    private void ResetFlow()
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.ResetFlow phaseBefore={_phase} flowId={_flowId}");
        StopLocalProgressReporting();

        if (_targetLoadRoutine != null)
        {
            StopCoroutine(_targetLoadRoutine);
            _targetLoadRoutine = null;
        }

        if (_completionRoutine != null)
        {
            StopCoroutine(_completionRoutine);
            _completionRoutine = null;
        }

        _localLoadOperation = null;
        _trackedClients.Clear();
        _timedOutClients.Clear();
        _clientProgress.Clear();
        _playersSpawnedForCurrentTargetScene = false;
        _targetLoadEventCompleted = false;
        _averageProgress = 0f;
        _sourceSceneName = string.Empty;
        _sourceIsNetworkScene = false;
        _restartingTargetScene = false;

        if (_phase != NetworkLoadingPhase.Completed)
        {
            _phase = NetworkLoadingPhase.Idle;
        }
        LogLoadedScenes("ResetFlow.after");
    }

    // [클라] 새 세션(StartClient) — 지난 세션의 흐름 번호·단계를 버린다(LoadingStateFlowRule). 서버 번호는 세션마다 1부터일 수 있다
    // (호스트 exe 재시작). 판 도중 끊겼다 다시 붙은 클라도 Completed 를 "처음 받는" 상태가 된다.
    private void ResetClientFlowForNewSession()
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.ResetClientFlowForNewSession flowIdBefore={_flowId} phaseBefore={_phase}");
        _phase = NetworkLoadingPhase.Idle;
        ResetFlow();
        _flowId = LoadingStateFlowRule.SessionStartFlowId;
    }

    public void SetEditorDefaults(string loadingScene, string targetScene, float minimumSeconds, float readySeconds)
    {
        loadingSceneName = loadingScene;
        targetSceneName = targetScene;
        minimumVisibleSeconds = minimumSeconds;
        readyMessageSeconds = readySeconds;
    }

    private IEnumerator UnloadSourceSceneIfNeeded()
    {
        var mode = SourceSceneUnloadRule.Decide(
            _sourceSceneName, loadingSceneName, targetSceneName, _sourceIsNetworkScene, IsServerActive);
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadSourceSceneIfNeeded source={_sourceSceneName} loading={loadingSceneName} target={targetSceneName} mode={mode}");
        switch (mode)
        {
            case SourceSceneUnloadMode.Local:
                yield return UnloadLocalScene(_sourceSceneName);
                break;
            case SourceSceneUnloadMode.Network:
                yield return UnloadNetworkSourceScene(_sourceSceneName);
                break;
        }
    }

    // [서버] NGO 소스 씬(로비·결과) — 동적 객체·플레이어를 먼저 디스폰(DestroyWithScene=false 라 언로드로는 안 사라지고,
    // 남은 PlayerObject 는 타깃 씬 SpawnAllPlayersOnce 를 막는다) → 활성 씬을 로딩 씬으로 옮기고(R2) → NGO 언로드. 클라는 NGO 가 따라 내린다.
    private IEnumerator UnloadNetworkSourceScene(string sceneName)
    {
        DespawnDynamicNetworkObjectsBeforeTransition($"source={sceneName}");

        var scene = SceneManager.GetSceneByName(sceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            MoveActiveSceneBeforeLocalUnload(scene);
        }

        // 로딩 씬 LoadEventCompleted 직후라 NGO 씬 이벤트가 아직 닫히지 않았을 수 있다 — 판 재시작 언로드와 같은 재시도.
        yield return UnloadNetworkScene(sceneName, inProgressRetries: 30);
    }

    private IEnumerator UnloadLoadingScene()
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadLoadingScene loading={loadingSceneName}");
        yield return UnloadNetworkScene(loadingSceneName);
    }

    // inProgressRetries — SceneEventInProgress 로 거절되면 한 프레임씩 이만큼 다시 요청한다(0 = 재시도 없음).
    private IEnumerator UnloadNetworkScene(string sceneName, int inProgressRetries = 0)
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadNetworkScene begin scene={sceneName} isServer={IsServerActive}");
        if (!IsServerActive)
        {
            yield break;
        }

        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadNetworkScene skipped not loaded scene={sceneName}");
            LogDebug($"Scene is not loaded. scene={sceneName}.");
            yield break;
        }

        _completedUnloads.Remove(sceneName);
        var status = _networkManager.SceneManager.UnloadScene(scene);
        var retries = 0;
        while (status == SceneEventProgressStatus.SceneEventInProgress && retries < inProgressRetries)
        {
            retries++;
            yield return null;
            status = _networkManager.SceneManager.UnloadScene(scene);
        }

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadNetworkScene requested scene={sceneName} status={status} retries={retries}");
        LogDebug($"Requested scene unload. scene={sceneName}, status={status}.");
        if (status != SceneEventProgressStatus.Started)
        {
            Edit.LogWarning($"[Loading] Failed to unload {sceneName} with {nameof(SceneEventProgressStatus)}.{status}.");
            yield break;
        }

        while (!_completedUnloads.Contains(sceneName))
        {
            yield return null;
        }
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadNetworkScene completed scene={sceneName}");
        LogLoadedScenes($"UnloadNetworkScene.completed.{sceneName}");
    }

    private IEnumerator UnloadLocalScene(string sceneName)
    {
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadLocalScene begin scene={sceneName}");

        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadLocalScene skipped not loaded scene={sceneName}");
            yield break;
        }

        MoveActiveSceneBeforeLocalUnload(scene);

        var operation = SceneManager.UnloadSceneAsync(scene);
        if (operation == null)
        {
            Debug.LogWarning($"Failed to locally unload scene {sceneName}.");
            yield break;
        }

        while (!operation.isDone)
        {
            yield return null;
        }

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.UnloadLocalScene completed scene={sceneName}");
        LogLoadedScenes($"UnloadLocalScene.completed.{sceneName}");
    }

    private void MoveActiveSceneBeforeLocalUnload(Scene sceneToUnload)
    {
        if (SceneManager.GetActiveScene() != sceneToUnload)
        {
            return;
        }

        var loadingScene = SceneManager.GetSceneByName(loadingSceneName);
        if (loadingScene.IsValid() && loadingScene.isLoaded)
        {
            SceneManager.SetActiveScene(loadingScene);
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.MoveActiveSceneBeforeLocalUnload active={loadingScene.name}");
            return;
        }

        var targetScene = SceneManager.GetSceneByName(targetSceneName);
        if (targetScene.IsValid() && targetScene.isLoaded)
        {
            SceneManager.SetActiveScene(targetScene);
            Debug.Log($"[SceneFlow] NetworkLoadingFlowController.MoveActiveSceneBeforeLocalUnload active={targetScene.name}");
        }
    }

    private void CaptureSourceSceneIfNeeded(SceneEvent sceneEvent)
    {
        if (!string.IsNullOrEmpty(_sourceSceneName) || sceneEvent.SceneName != loadingSceneName)
        {
            return;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() ||
            activeScene.name == loadingSceneName ||
            activeScene.name == targetSceneName)
        {
            return;
        }

        _sourceSceneName = activeScene.name;
        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.CaptureSourceSceneIfNeeded source={_sourceSceneName}");
    }

    private void LogLoadedScenes(string context)
    {
        var sceneNames = new List<string>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            sceneNames.Add($"{scene.name}(loaded={scene.isLoaded}, active={scene == SceneManager.GetActiveScene()})");
        }

        Debug.Log($"[SceneFlow] NetworkLoadingFlowController.LoadedScenes context={context} scenes={string.Join(", ", sceneNames)}");
    }
}
