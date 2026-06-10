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
    private const int StateMessageSize = sizeof(uint) + sizeof(byte) + sizeof(float);

    [SerializeField] private string loadingSceneName = "LoadingScene";
    [SerializeField] private string targetSceneName = "Temp_inGameScene";
    [SerializeField] private float minimumVisibleSeconds = 5f;
    [SerializeField] private float readyMessageSeconds = 2.5f;
    [SerializeField] private float progressReportInterval = 0.1f;
    [SerializeField, Min(0.01f)] private float sceneLoadWeight = 1f;
    [SerializeField] private bool requireLobbyReadyToStart = true;
    [SerializeField] private bool debugLogging;

    private readonly HashSet<ulong> _trackedClients = new HashSet<ulong>();
    private readonly HashSet<ulong> _timedOutClients = new HashSet<ulong>();
    private readonly Dictionary<ulong, float> _clientProgress = new Dictionary<ulong, float>();
    private readonly List<NetworkLoadingTask> _localLoadingTasks = new List<NetworkLoadingTask>();
    private readonly List<Coroutine> _localTaskRoutines = new List<Coroutine>();

    private NetworkManager _networkManager;
    private NetworkLoadingScreenView _view;
    private NetworkLoadingPhase _phase = NetworkLoadingPhase.Idle;
    private AsyncOperation _localLoadOperation;
    private Coroutine _localProgressRoutine;
    private Coroutine _targetLoadRoutine;
    private Coroutine _completionRoutine;
    private Coroutine _registerCallbacksRoutine;
    private bool _callbacksRegistered;
    private uint _flowId;
    private float _averageProgress;
    private float _minimumTimerStartedAt;

    private bool IsServerActive => _networkManager != null && _networkManager.IsListening && _networkManager.IsServer;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
    }

    private void OnDestroy()
    {
        if (_registerCallbacksRoutine != null)
        {
            StopCoroutine(_registerCallbacksRoutine);
            _registerCallbacksRoutine = null;
        }

        UnregisterNetworkCallbacks();
    }

    public void RegisterNetworkCallbacks()
    {
        if (_callbacksRegistered)
        {
            return;
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

        _networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
        _networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ProgressMessageName, HandleProgressMessage);
        _networkManager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessageName, HandleStateMessage);
        _networkManager.OnClientConnectedCallback += HandleClientConnected;
        _callbacksRegistered = true;

        LogDebug("Network callbacks registered.");
    }

    private IEnumerator RegisterNetworkCallbacksWhenReady()
    {
        while (!_callbacksRegistered)
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

    public void StartGameLoading()
    {
        RegisterNetworkCallbacks();

        if (!IsServerActive)
        {
            Debug.LogWarning("Only the server or host can start the loading flow.");
            return;
        }

        if (requireLobbyReadyToStart && !CanStartFromLobby())
        {
            Debug.LogWarning("Cannot start game loading until all connected clients are ready.");
            return;
        }

        ResetFlow();
        _flowId++;
        _phase = NetworkLoadingPhase.LoadingScene;
        _minimumTimerStartedAt = Time.unscaledTime;

        LogDebug($"StartGameLoading flowId={_flowId}, loadingScene={loadingSceneName}, targetScene={targetSceneName}.");
        ApplyViewState();
        BroadcastState();

        var status = _networkManager.SceneManager.LoadScene(loadingSceneName, LoadSceneMode.Single);
        LogDebug($"Requested loading scene load. status={status}.");
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogWarning($"Failed to load {loadingSceneName} with {nameof(SceneEventProgressStatus)}.{status}.");
        }
    }

    private bool CanStartFromLobby()
    {
        var lobby = LobbyUIController.Active;
        if (lobby == null)
        {
            lobby = FindFirstObjectByType<LobbyUIController>();
        }

        return lobby == null || lobby.CanStartGame;
    }

    public void RegisterView(NetworkLoadingScreenView view)
    {
        _view = view;
        ApplyViewState();
    }

    private void UnregisterNetworkCallbacks()
    {
        if (!_callbacksRegistered || _networkManager == null)
        {
            return;
        }

        if (_networkManager.SceneManager != null)
        {
            _networkManager.SceneManager.OnSceneEvent -= HandleSceneEvent;
        }

        if (_networkManager.CustomMessagingManager != null)
        {
            _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ProgressMessageName);
            _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessageName);
        }

        _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _callbacksRegistered = false;

        LogDebug("Network callbacks unregistered.");
    }

    private void HandleClientConnected(ulong clientId)
    {
        LogDebug($"Client connected. clientId={clientId}, phase={_phase}.");

        if (!IsServerActive || _phase == NetworkLoadingPhase.Idle || _phase == NetworkLoadingPhase.Completed)
        {
            return;
        }

        SendState(clientId);
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        RegisterNetworkCallbacks();
        LogDebug(
            $"SceneEvent type={sceneEvent.SceneEventType}, scene={sceneEvent.SceneName}, clientId={sceneEvent.ClientId}, " +
            $"phase={_phase}, flowId={_flowId}.");

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                HandleLoadStarted(sceneEvent);
                break;
            case SceneEventType.LoadComplete:
                HandleLoadComplete(sceneEvent);
                break;
            case SceneEventType.LoadEventCompleted:
                HandleLoadEventCompleted(sceneEvent);
                break;
        }
    }

    private void HandleLoadStarted(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneName != targetSceneName)
        {
            return;
        }

        _phase = NetworkLoadingPhase.LoadingGame;
        _localLoadOperation = sceneEvent.AsyncOperation;

        LogDebug($"Target load started. clientId={sceneEvent.ClientId}, asyncOperation={_localLoadOperation != null}.");
        _minimumTimerStartedAt = Time.unscaledTime;

        if (IsServerActive)
        {
            CaptureTrackedClients();
            BroadcastState();
        }

        StartLocalLoadingTasks();
        StartLocalProgressReporting();
        ApplyViewState();
    }

    private void HandleLoadComplete(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneName != targetSceneName)
        {
            return;
        }

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
            if (IsServerActive && _targetLoadRoutine == null)
            {
                _targetLoadRoutine = StartCoroutine(StartTargetLoadAfterSceneEvent());
            }

            return;
        }

        if (sceneEvent.SceneName != targetSceneName)
        {
            return;
        }

        StopLocalProgressReporting();
        _localLoadOperation = null;

        if (!IsServerActive)
        {
            SubmitLocalProgress(1f);
            return;
        }

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

        _averageProgress = CalculateAverageProgress();
        LogDebug($"Target scene load event completed. average={_averageProgress:P0}. Waiting for completion routine.");
        ApplyViewState();
        BroadcastAverageProgress();
    }

    private IEnumerator StartTargetLoadAfterSceneEvent()
    {
        yield return null;

        _phase = NetworkLoadingPhase.LoadingGame;
        CaptureTrackedClients();
        LogDebug($"Starting target scene load. trackedClients={_trackedClients.Count}.");
        ApplyViewState();
        BroadcastState();

        var status = _networkManager.SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
        LogDebug($"Requested target scene load. status={status}.");
        var retries = 0;
        while (status == SceneEventProgressStatus.SceneEventInProgress && retries < 30)
        {
            retries++;
            yield return null;
            status = _networkManager.SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
            LogDebug($"Retry target scene load. retry={retries}, status={status}.");
        }

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogWarning($"Failed to load {targetSceneName} with {nameof(SceneEventProgressStatus)}.{status}.");
        }

        _targetLoadRoutine = null;
    }

    private IEnumerator CompleteAfterMinimumVisibleTime()
    {
        _phase = NetworkLoadingPhase.WaitingForPlayers;
        LogDebug("All tracked clients reached 100%. Waiting for minimum visible time.");
        ApplyViewState();
        BroadcastState();

        var elapsed = Time.unscaledTime - _minimumTimerStartedAt;
        var remaining = minimumVisibleSeconds - elapsed;
        if (remaining > 0f)
        {
            yield return new WaitForSecondsRealtime(remaining);
        }

        _phase = NetworkLoadingPhase.Ready;
        LogDebug("Minimum visible time satisfied. Showing ready message.");
        ApplyViewState();
        BroadcastState();

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, readyMessageSeconds));

        _phase = NetworkLoadingPhase.Activating;
        LogDebug("Ready message finished. Completing loading flow.");
        ApplyViewState();
        BroadcastState();

        yield return null;

        _phase = NetworkLoadingPhase.Completed;
        _averageProgress = 1f;
        ApplyViewState();
        BroadcastState();
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
        var wait = new WaitForSecondsRealtime(Mathf.Max(0.02f, progressReportInterval));
        while (!IsLocalLoadingReady())
        {
            SubmitLocalProgress(CalculateLocalLoadingProgress());
            yield return wait;
        }

        SubmitLocalProgress(1f);
    }

    private void StartLocalLoadingTasks()
    {
        StopLocalLoadingTasks();
        _localLoadingTasks.Clear();

        var registry = NetworkLoadingTaskRegistry.Active;
        if (registry == null)
        {
            return;
        }

        foreach (var task in registry.Tasks)
        {
            if (task == null)
            {
                continue;
            }

            _localLoadingTasks.Add(task);
            _localTaskRoutines.Add(StartCoroutine(task.Run()));
        }

        LogDebug($"Started local loading tasks. count={_localLoadingTasks.Count}.");
    }

    private void StopLocalLoadingTasks()
    {
        foreach (var routine in _localTaskRoutines)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
            }
        }

        _localTaskRoutines.Clear();
    }

    private bool IsLocalLoadingReady()
    {
        var sceneReady = _localLoadOperation == null || _localLoadOperation.progress >= 0.9f;
        if (!sceneReady)
        {
            return false;
        }

        foreach (var task in _localLoadingTasks)
        {
            if (task != null && !task.IsDone)
            {
                return false;
            }
        }

        return true;
    }

    private float CalculateLocalLoadingProgress()
    {
        var totalWeight = Mathf.Max(0.01f, sceneLoadWeight);
        var weightedProgress = GetLocalSceneLoadProgress() * sceneLoadWeight;

        foreach (var task in _localLoadingTasks)
        {
            if (task == null)
            {
                continue;
            }

            var taskWeight = Mathf.Max(0.01f, task.Weight);
            totalWeight += taskWeight;
            weightedProgress += task.Progress * taskWeight;
        }

        return Mathf.Clamp01(weightedProgress / totalWeight);
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

        if (flowId < _flowId)
        {
            return;
        }

        _flowId = flowId;
        _phase = (NetworkLoadingPhase)phaseValue;
        _averageProgress = Mathf.Clamp01(averageProgress);

        LogDebug($"Received state. sender={senderClientId}, phase={_phase}, average={_averageProgress:P0}, flowId={_flowId}.");

        ApplyViewState();
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

        Debug.Log($"[NetworkLoadingFlowController][{role}:{localClientId}] {message}", this);
    }

    private void ResetFlow()
    {
        StopLocalProgressReporting();
        StopLocalLoadingTasks();

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
        _localLoadingTasks.Clear();
        _trackedClients.Clear();
        _timedOutClients.Clear();
        _clientProgress.Clear();
        _averageProgress = 0f;

        if (_phase != NetworkLoadingPhase.Completed)
        {
            _phase = NetworkLoadingPhase.Idle;
        }
    }

    public void SetEditorDefaults(string loadingScene, string targetScene, float minimumSeconds, float readySeconds)
    {
        loadingSceneName = loadingScene;
        targetSceneName = targetScene;
        minimumVisibleSeconds = minimumSeconds;
        readyMessageSeconds = readySeconds;
    }
}
