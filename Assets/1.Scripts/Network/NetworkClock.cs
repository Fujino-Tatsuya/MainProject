using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 세션 전역 공유 시계. NetworkManager.prefab에 부착한다.
/// - <see cref="ServerNow"/>   : NGO ServerTime raw (피어 간 시계차 대응용)
/// - <see cref="GameNow"/>      : 일시정지 제외 게임시간 (솔로 host 일시정지 반영)
/// - <see cref="MainGameElapsed"/> : MainGame 시작 이후 경과 (결정론 모션의 시간 기준)
///
/// 타임스탬프(세션 구성/ MainGame 시작 시각)는 서버가 정하고 CustomMessaging으로 전원에 배포한다.
/// late joiner/재접속은 없음(v1) → 최초 배포 + 접속 시 1회 전송으로 충분.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class NetworkClock : MonoBehaviour
{
    public static NetworkClock Instance { get; private set; }

    private const string TimestampMessageName = "NetworkClock.Timestamps";
    private const int TimestampMessageSize = sizeof(double) * 2;

    private NetworkManager _networkManager;
    private bool _handlerRegistered;
    private bool _clientConnectedHooked;

    // 일시정지 (솔로 host 전용). 멀티에서는 사용하지 않는다.
    private bool _paused;
    private double _pauseStartedServerTime;
    private double _pausedAccum;

    // 서버 권위 타임스탬프 (GameNow 도메인). NaN = 미설정.
    private double _sessionFormedAt = double.NaN;
    private double _mainGameStartedAt = double.NaN;

    /// <summary>NGO ServerTime raw. 세션 미가동 시 0.</summary>
    public double ServerNow =>
        _networkManager != null && _networkManager.IsListening ? _networkManager.ServerTime.Time : 0.0;

    /// <summary>일시정지 시간을 제외한 게임시간.</summary>
    public double GameNow => ServerNow - CurrentPausedAccum();

    /// <summary>이 클라이언트의 NGO LocalTime(서버보다 편도지연만큼 앞섬). 대시 RTT 보정 요청에 사용. (PLAN §9)</summary>
    public double LocalNow =>
        _networkManager != null && _networkManager.IsListening ? _networkManager.LocalTime.Time : 0.0;

    /// <summary>일시정지를 제외한 클라이언트 로컬 게임시간.</summary>
    public double GameLocalNow => LocalNow - CurrentPausedAccum();

    public bool HasSessionFormed => !double.IsNaN(_sessionFormedAt);
    public bool HasMainGameStarted => !double.IsNaN(_mainGameStartedAt);

    /// <summary>세션(파티) 구성 시각 (GameNow 도메인).</summary>
    public double SessionFormedAt => _sessionFormedAt;
    /// <summary>MainGame 시작 시각 (GameNow 도메인).</summary>
    public double MainGameStartedAt => _mainGameStartedAt;

    /// <summary>MainGame 시작 이후 경과 시간. 미시작 시 0. (결정론 모션이 읽는 기준)</summary>
    public double MainGameElapsed =>
        HasMainGameStarted ? System.Math.Max(0.0, GameNow - _mainGameStartedAt) : 0.0;

    public bool IsPaused => _paused;

    private bool IsServerActive =>
        _networkManager != null && _networkManager.IsListening && _networkManager.IsServer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        _networkManager = GetComponent<NetworkManager>();
    }

    private void OnDestroy()
    {
        UnregisterHandler();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (_networkManager == null)
        {
            return;
        }

        if (_networkManager.IsListening && _networkManager.CustomMessagingManager != null)
        {
            EnsureHandlerRegistered();

            // 서버: 세션 구성 시각을 최초 1회 스탬프하고 배포.
            if (IsServerActive && !HasSessionFormed)
            {
                _sessionFormedAt = GameNow;
                Edit.Log($"[NetworkClock] 세션 구성 스탬프 = {_sessionFormedAt:F3}", this);
                BroadcastTimestamps();
            }
        }
        else if (_handlerRegistered)
        {
            // 세션 종료 → 핸들러 해제 + 상태 리셋.
            UnregisterHandler();
            ResetState();
        }
    }

    /// <summary>MainGame 진입 시 호출. 서버만 스탬프하고 전원에 배포한다(클라 호출은 무시됨).</summary>
    public void MarkMainGameStart()
    {
        if (!IsServerActive)
        {
            return;
        }

        _mainGameStartedAt = GameNow;
        Edit.Log($"[NetworkClock] MainGame 시작 스탬프 = {_mainGameStartedAt:F3}", this);
        BroadcastTimestamps();
    }

    /// <summary>솔로(host) 일시정지. 멀티에서는 호출하지 않는다.</summary>
    public void Pause()
    {
        if (_paused)
        {
            return;
        }

        _paused = true;
        _pauseStartedServerTime = ServerNow;
    }

    /// <summary>솔로(host) 일시정지 해제.</summary>
    public void Resume()
    {
        if (!_paused)
        {
            return;
        }

        _pausedAccum += ServerNow - _pauseStartedServerTime;
        _paused = false;
    }

    private double CurrentPausedAccum() =>
        _paused ? _pausedAccum + (ServerNow - _pauseStartedServerTime) : _pausedAccum;

    private void ResetState()
    {
        _paused = false;
        _pausedAccum = 0.0;
        _sessionFormedAt = double.NaN;
        _mainGameStartedAt = double.NaN;
    }

    // ---- CustomMessaging: 서버 → 클라 타임스탬프 배포 ----

    private void EnsureHandlerRegistered()
    {
        if (_handlerRegistered)
        {
            return;
        }

        _networkManager.CustomMessagingManager.RegisterNamedMessageHandler(TimestampMessageName, HandleTimestampMessage);
        _handlerRegistered = true;

        // 서버는 신규 접속 클라에 현재 타임스탬프를 1회 전송(접속 순서 보정).
        if (IsServerActive && !_clientConnectedHooked)
        {
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _clientConnectedHooked = true;
        }
    }

    private void UnregisterHandler()
    {
        if (_networkManager == null)
        {
            return;
        }

        if (_handlerRegistered && _networkManager.CustomMessagingManager != null)
        {
            _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(TimestampMessageName);
        }

        if (_clientConnectedHooked)
        {
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _clientConnectedHooked = false;
        }

        _handlerRegistered = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServerActive)
        {
            return;
        }

        if (_networkManager.IsHost && clientId == _networkManager.LocalClientId)
        {
            return;
        }

        SendTimestamps(clientId);
    }

    private void BroadcastTimestamps()
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

            SendTimestamps(clientId);
        }
    }

    private void SendTimestamps(ulong clientId)
    {
        var messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        using (var writer = new FastBufferWriter(TimestampMessageSize, Allocator.Temp))
        {
            writer.WriteValueSafe(_sessionFormedAt);
            writer.WriteValueSafe(_mainGameStartedAt);
            messaging.SendNamedMessage(TimestampMessageName, clientId, writer);
        }
    }

    private void HandleTimestampMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        reader.ReadValueSafe(out double sessionFormedAt);
        reader.ReadValueSafe(out double mainGameStartedAt);
        _sessionFormedAt = sessionFormedAt;
        _mainGameStartedAt = mainGameStartedAt;
    }
}
