using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 세션 전역 시간 레이어 — 서버가 슬로우 모션을 발동하면 전 피어가 같은 타임라인으로 <see cref="Time.timeScale"/> 을 정한다.
/// NetworkManager.prefab 에 <see cref="NetworkClock"/> 과 함께 붙는다. (PLAN-interrupt-slowmo S2)
///
/// <list type="bullet">
/// <item>시간 도메인 t = <see cref="NetworkClock.UnslowedGameNow"/>(ServerTime − 일시정지 누적). 일시정지 중엔 t 가 멈춰
///       슬로우도 그 자리에서 멈췄다가 재개 시 이어진다(D12 — 따로 저장할 것이 없다).</item>
/// <item>발동·실패·리셋 판정은 서버만 한다. 서버는 자기 타임라인에 즉시 적용하고 named message 로 전원에 배포,
///       클라는 받은 이벤트를 같은 규칙(<see cref="SlowMotionSession"/>)으로 넣는다(D8). 프로필 값을 실어 보낸다.</item>
/// <item>배율은 매 프레임 게임플레이보다 먼저 정한다. <see cref="Time.fixedDeltaTime"/> 은 건드리지 않는다(R3).
///       슬로우가 없을 땐 <see cref="Time.timeScale"/> 에 쓰지 않는다 — 이 레이어가 올린 값을 1 로 돌려놓을 때만 쓴다.</item>
/// <item>컷신 잠금·씬 전환 중엔 발동을 거부하고, 진행 중이면 즉시 1.0(D11).</item>
/// <item>세션 종료·비활성·파괴·앱 종료 시 1.0 과 타임라인 초기화(R6).</item>
/// </list>
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(NetworkManager))]
public sealed class GlobalTimeScale : MonoBehaviour
{
    public static GlobalTimeScale Instance { get; private set; }

    private const string EventMessageName = "GlobalTimeScale.Event";

    [Tooltip("인터럽트 유효타 슬로우 모양. 수치 원본은 이후 GameData.xlsx(S6)")]
    [SerializeField] private SlowMotionProfile interruptProfile = new SlowMotionProfile();

    private readonly SlowMotionSession _session = new SlowMotionSession();

    private NetworkManager _networkManager;
    private NetworkClock _clock;

    // NGO 는 세션마다 CustomMessagingManager·SceneManager 를 새로 만든다 — "누구에게 등록했는가"로 판별한다
    // (NetworkLoadingFlowController.IsRegistrationCurrent 와 같은 이유).
    private CustomMessagingManager _registeredMessaging;
    private NetworkSceneManager _registeredSceneManager;

    private uint _lastIssuedTriggerId;  // 서버 발동 번호 발급기
    private bool _sceneTransitioning;    // 서버: Load/Unload 시작 ~ 완료
    private bool _ownsTimeScale;         // 이 레이어가 Time.timeScale 을 1 이 아닌 값으로 올려 둔 상태

    /// <summary>인터럽트 슬로우 프로필(서버가 발동할 때 넘긴다).</summary>
    public SlowMotionProfile InterruptProfile => interruptProfile;

    /// <summary>슬로우 시작(전 피어). (발동 번호, 발동자 clientId)</summary>
    public event Action<uint, ulong> Started;
    /// <summary>진행 중이던 슬로우가 실패 복귀로 넘어감(전 피어). (발동 번호)</summary>
    public event Action<uint> Failed;
    /// <summary>
    /// 진행 중이던 슬로우가 컷신·씬 전환·세션 종료로 즉시 1.0 이 됨(전 피어). (발동 번호)
    /// 이름이 Reset 이 아닌 이유: MonoBehaviour 의 Reset() 메시지와 헷갈린다.
    /// </summary>
    public event Action<uint> ForcedReset;

    /// <summary>이번 프레임 배율. 슬로우 없음 = 1.</summary>
    public float CurrentScale { get; private set; } = 1f;
    public SlowMotionPhase CurrentPhase { get; private set; } = SlowMotionPhase.None;
    public bool IsActive => CurrentPhase != SlowMotionPhase.None;
    /// <summary>마지막 슬로우의 발동 번호. 0 = 이번 세션에 없음.</summary>
    public uint CurrentTriggerId => _session.CurrentTriggerId;
    /// <summary>마지막 슬로우의 발동자 clientId. 진행 중인지는 <see cref="IsActive"/> 로 본다.</summary>
    public ulong CurrentTriggerClientId => _session.CurrentTriggerClientId;

    private bool IsSessionRunning =>
        _networkManager != null && _networkManager.IsListening && _clock != null && _clock.IsRunning;

    private bool IsServerActive => IsSessionRunning && _networkManager.IsServer;

    private double Now => _clock.UnslowedGameNow;

    /// <summary>
    /// t(<see cref="NetworkClock.UnslowedGameNow"/> 도메인)까지 슬로우로 덜 흐른 시간. 세션 미가동이면 0.
    /// <see cref="NetworkClock.GameNow"/> 가 읽는다.
    /// </summary>
    public double LostTimeUntil(double t) => IsSessionRunning ? _session.LostTimeUntil(t) : 0.0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        _networkManager = GetComponent<NetworkManager>();
        _clock = GetComponent<NetworkClock>();
    }

    private void OnDisable()
    {
        UnregisterHandlers();
        EndSession();
    }

    private void OnDestroy()
    {
        UnregisterHandlers();
        EndSession();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnApplicationQuit()
    {
        // 에디터 Play 종료도 여기를 지난다 — Time.timeScale 은 Play 가 끝나도 에디터에 남는다.
        EndSession();
    }

    private void Update()
    {
        if (!IsSessionRunning || _networkManager.CustomMessagingManager == null)
        {
            if (_registeredMessaging != null || _registeredSceneManager != null || _ownsTimeScale || _session.CurrentTriggerId != 0)
            {
                UnregisterHandlers();
                EndSession();
            }

            return;
        }

        EnsureHandlersRegistered();

        if (_networkManager.IsServer && IsActive && AnyPlayerCinematicLocked())
        {
            ServerForceReset();
        }

        ApplyTimeScale();
    }

    // ---- 서버 API ----

    /// <summary>
    /// 서버 전용. 슬로우를 발동하고 발동 번호를 돌려준다. 거부 시 0
    /// (서버 아님·세션 미가동·컷신 잠금·씬 전환 중). 진행 중이면 마지막 것이 이긴다(D6).
    /// </summary>
    public uint ServerStart(SlowMotionProfile profile, ulong triggerClientId)
    {
        if (profile == null || !IsServerActive)
        {
            return 0;
        }

        // 발동자만이 아니라 누구든 잠겨 있으면 거부한다 — 어차피 매 프레임 검사가 바로 끊는다(시작 직후 1.0 으로 튀는 깜빡임 방지).
        if (_sceneTransitioning || AnyPlayerCinematicLocked())
        {
            return 0;
        }

        uint triggerId = ++_lastIssuedTriggerId;
        if (triggerId == 0)
        {
            triggerId = ++_lastIssuedTriggerId; // uint 한 바퀴 — 0 은 "없음"이라 건너뛴다
        }

        ServerPublish(SlowMotionEvent.CreateStart(triggerId, Now, triggerClientId, profile));
        return triggerId;
    }

    /// <summary>
    /// 서버 전용. 빗나감 — <paramref name="triggerId"/> 가 현재 발동 번호와 같을 때만 실패 복귀.
    /// 옛 스킬의 늦은 Fail 은 무시된다.
    /// </summary>
    public void ServerFail(uint triggerId)
    {
        if (!IsServerActive || triggerId == 0 || triggerId != _session.CurrentTriggerId)
        {
            return;
        }

        ServerPublish(SlowMotionEvent.CreateFail(triggerId, Now));
    }

    /// <summary>서버 전용. 진행 중인 슬로우를 즉시 1.0 으로 끊는다(컷신·씬 전환). 진행 중이 아니면 아무것도 안 한다.</summary>
    public void ServerForceReset()
    {
        if (!IsServerActive || !_session.IsActiveAt(Now))
        {
            return;
        }

        ServerPublish(SlowMotionEvent.CreateReset(_session.CurrentTriggerId, Now));
    }

    private void ServerPublish(in SlowMotionEvent e)
    {
        ApplyEvent(e);
        Broadcast(e);
    }

    private bool AnyPlayerCinematicLocked()
    {
        foreach (var client in _networkManager.ConnectedClientsList)
        {
            var playerObject = client.PlayerObject;
            if (playerObject == null)
            {
                continue;
            }

            if (playerObject.TryGetComponent(out PlayerEncounterLock encounterLock) && encounterLock.IsCinematicLocked)
            {
                return true;
            }
        }

        return false;
    }

    // ---- 적용 ----

    private void ApplyEvent(in SlowMotionEvent e)
    {
        var result = _session.Apply(e);
        ApplyTimeScale();

        switch (result)
        {
            case SlowMotionApplyResult.Started:
                Started?.Invoke(e.TriggerId, e.TriggerClientId);
                break;
            case SlowMotionApplyResult.Failed:
                Failed?.Invoke(e.TriggerId);
                break;
            case SlowMotionApplyResult.Reset:
                ForcedReset?.Invoke(e.TriggerId);
                break;
        }
    }

    private void ApplyTimeScale()
    {
        double t = Now;
        CurrentPhase = _session.PhaseAt(t);
        CurrentScale = CurrentPhase == SlowMotionPhase.None ? 1f : _session.ScaleAt(t);

        if (CurrentPhase == SlowMotionPhase.None && !_ownsTimeScale)
        {
            return;
        }

        Time.timeScale = CurrentScale;
        _ownsTimeScale = CurrentPhase != SlowMotionPhase.None;
    }

    private void EndSession()
    {
        bool hadSlow = IsActive;
        uint triggerId = _session.CurrentTriggerId;

        _session.Clear();
        _lastIssuedTriggerId = 0;
        _sceneTransitioning = false;
        CurrentScale = 1f;
        CurrentPhase = SlowMotionPhase.None;

        if (_ownsTimeScale)
        {
            Time.timeScale = 1f;
            _ownsTimeScale = false;
        }

        if (hadSlow)
        {
            ForcedReset?.Invoke(triggerId);
        }
    }

    // ---- 씬 전환(서버) ----

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (!IsServerActive)
        {
            return;
        }

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
            case SceneEventType.Unload:
                _sceneTransitioning = true;
                ServerForceReset();
                break;
            case SceneEventType.LoadEventCompleted:
            case SceneEventType.UnloadEventCompleted:
                _sceneTransitioning = false;
                break;
        }
    }

    // ---- CustomMessaging: 서버 → 클라 ----

    private void EnsureHandlersRegistered()
    {
        var messaging = _networkManager.CustomMessagingManager;
        if (_registeredMessaging != messaging)
        {
            UnregisterHandlers();
            messaging.RegisterNamedMessageHandler(EventMessageName, HandleEventMessage);
            _registeredMessaging = messaging;
        }

        var sceneManager = _networkManager.SceneManager;
        if (_registeredSceneManager != sceneManager && sceneManager != null)
        {
            if (_registeredSceneManager != null)
            {
                _registeredSceneManager.OnSceneEvent -= HandleSceneEvent;
            }

            sceneManager.OnSceneEvent += HandleSceneEvent;
            _registeredSceneManager = sceneManager;
        }
    }

    private void UnregisterHandlers()
    {
        // 현재 프로퍼티가 아니라 등록할 때 기억해 둔 객체에서 뗀다.
        if (_registeredMessaging != null)
        {
            _registeredMessaging.UnregisterNamedMessageHandler(EventMessageName);
            _registeredMessaging = null;
        }

        if (_registeredSceneManager != null)
        {
            _registeredSceneManager.OnSceneEvent -= HandleSceneEvent;
            _registeredSceneManager = null;
        }
    }

    private void Broadcast(in SlowMotionEvent e)
    {
        var messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        foreach (var clientId in _networkManager.ConnectedClientsIds)
        {
            if (clientId == _networkManager.LocalClientId)
            {
                continue; // 호스트 자신은 이미 적용했다
            }

            using (var writer = new FastBufferWriter(SlowMotionEvent.SerializedSize, Allocator.Temp))
            {
                e.Write(writer);
                messaging.SendNamedMessage(EventMessageName, clientId, writer);
            }
        }
    }

    private void HandleEventMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId || _networkManager.IsServer)
        {
            return;
        }

        ApplyEvent(SlowMotionEvent.Read(reader));
    }
}
