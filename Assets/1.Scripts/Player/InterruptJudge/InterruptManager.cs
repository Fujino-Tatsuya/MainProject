using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 간파의 단일 진입점(할 일 191, PLAN §3.1) — 모든 피어에 하나, <c>NetworkManager.prefab</c> 에 붙는다.
/// <b>1~2단계(지금)</b>: 판정 데이터를 모으고 예측·판정 함수(<see cref="Predict"/>·<see cref="Judge(in InterruptJudgeAttempt)"/> = <see cref="InterruptJudgeCalculator"/>)를 둔다 —
/// 아직 스킬에 연결하지 않아 기존 예측·판정 경로(DashInterruptPredictor·InterruptWindowReplicaTracker·서버 ResolveHit)는 그대로다.
/// <list type="bullet">
/// <item><b>세계 스냅샷</b>: 등록된 플레이어(<see cref="RegisterPlayer"/> — Player 스폰)와 간파 대상(<see cref="InterruptTargetRegistry"/> 그대로)을
///       매 프레임 LateUpdate 에서 샘플해 약 1초 이력(<see cref="InterruptSampleHistory{T}"/>)에 쌓는다. 클라는 보는 값, 서버는 실제 값.</item>
/// <item><b>창 시간표</b>: 보스·허수아비는 창 열림(닫힘 예정 길이·공격 시작·지목자)·닫힘만 <see cref="ServerReportWindow"/> 로 알린다.
///       서버가 기록(<see cref="InterruptWindowSchedule"/>)을 만들고 named message 로 모든 클라에 복제한다 — 열림·조기 닫힘·늦은 닫힘(갱신) 모두.</item>
/// <item><b>시각</b>: 전부 서버 시각(<see cref="NetworkClock.GameNow"/> 도메인). 로컬 게임초와는 <see cref="InterruptServerClock"/> 로 바꾼다.</item>
/// </list>
/// NetworkObject 가 없는 프리팹이라 NetworkBehaviour·RPC 대신 <see cref="GlobalTimeScale"/> 와 같은 named message 를 쓴다.
/// 늦은 합류 클라에는 접속 때 열린 창만 한 번 보낸다. 문서: Docs/tech/interrupt-client-judge.md.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public sealed class InterruptManager : MonoBehaviour, IInterruptJudgeWorld
{
    public static InterruptManager Instance { get; private set; }

    /// <summary>이력 보관 길이(초).</summary>
    public const double HistorySeconds = 1.0;

    // 60fps 1초 + 여유. 넘치면 오래된 것부터 밀린다.
    const int HistoryCapacity = 96;

    const string WindowMessageName = "InterruptManager.Window";

    [SerializeField]
    [Tooltip("창 시간표 열림·닫힘을 콘솔에 남긴다(에디터 전용 로그). 할 일 191 Play 확인용.")]
    bool logWindows;

    // ---- 플레이어 등록(정적 — 매니저보다 플레이어가 먼저 스폰될 수 있다) ----

    static readonly List<Player> s_players = new List<Player>();

    /// <summary>[모든 피어] 플레이어 스폰 때 부른다(Player.OnNetworkSpawn).</summary>
    public static void RegisterPlayer(Player player)
    {
        if (player != null && !s_players.Contains(player))
            s_players.Add(player);
    }

    /// <summary>[모든 피어] 플레이어 디스폰 때 부른다(Player.OnNetworkDespawn).</summary>
    public static void UnregisterPlayer(Player player) => s_players.Remove(player);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_players.Clear();

    // ---- 상태 ----

    sealed class ActorTrack
    {
        public Player Player;
        public PlayerStateController States;
        public PlayerLifeCycleController Life;
        public PlayerSkillController Skills;
        public PlayerMotor Motor;
        public readonly InterruptSampleHistory<InterruptActorSample> History =
            new InterruptSampleHistory<InterruptActorSample>(HistoryCapacity, HistorySeconds);
        public bool Seen;
    }

    sealed class TargetTrack
    {
        public Unit Unit;
        public IInterruptJudgeTarget State;
        public readonly InterruptSampleHistory<InterruptTargetSample> History =
            new InterruptSampleHistory<InterruptTargetSample>(HistoryCapacity, HistorySeconds);
        public bool Seen;
    }

    readonly InterruptServerClock _clock = new InterruptServerClock();
    readonly InterruptWindowSchedule _schedule = new InterruptWindowSchedule();
    readonly Dictionary<ulong, ActorTrack> _actors = new Dictionary<ulong, ActorTrack>();
    readonly Dictionary<ulong, TargetTrack> _targets = new Dictionary<ulong, TargetTrack>();
    readonly List<InterruptTargetRegistry.Entry> _targetBuffer = new List<InterruptTargetRegistry.Entry>();
    readonly List<ulong> _removeBuffer = new List<ulong>();
    readonly List<ulong> _targetIds = new List<ulong>();
    readonly Collider[] _overlapBuffer = new Collider[16];
    readonly List<InterruptWindowRecord> _recordBuffer = new List<InterruptWindowRecord>();

    NetworkManager _networkManager;
    NetworkClock _networkClock;
    CustomMessagingManager _registeredMessaging;   // NGO 는 세션마다 새로 만든다 — 등록한 객체를 기억한다(GlobalTimeScale 과 같은 이유)
    bool _clientConnectedHooked;
    bool _hasState;

    // ---- 읽기 API(2단계 예측·판정이 쓴다) ----

    bool IsSessionRunning => _networkManager != null && _networkManager.IsListening;
    bool IsServerActive => IsSessionRunning && _networkManager.IsServer;

    /// <summary>지금 서버 시각.</summary>
    public double ServerNow => _clock.ToServer(Time.timeAsDouble);

    /// <summary>로컬 게임초(Time.time 계열) → 서버 시각.</summary>
    public double ToServerTime(double localTime) => _clock.ToServer(localTime);

    /// <summary>서버 시각 → 로컬 게임초.</summary>
    public double ToLocalTime(double serverTime) => _clock.ToLocal(serverTime);

    /// <summary>플레이어(NetworkObjectId)의 <paramref name="serverTime"/> 표본 — 이력 범위 밖이면 false.</summary>
    public bool TryGetActorAt(ulong networkObjectId, double serverTime, out InterruptActorSample sample)
    {
        sample = default;
        return _actors.TryGetValue(networkObjectId, out ActorTrack track) && track.History.TryGetAt(serverTime, out sample);
    }

    /// <summary>간파 대상(NetworkObjectId)의 <paramref name="serverTime"/> 표본 — 이력 범위 밖이면 false.</summary>
    public bool TryGetTargetAt(ulong networkObjectId, double serverTime, out InterruptTargetSample sample)
    {
        sample = default;
        return _targets.TryGetValue(networkObjectId, out TargetTrack track) && track.History.TryGetAt(serverTime, out sample);
    }

    /// <summary>대상의 <paramref name="serverTime"/> 에 열려 있던 창.</summary>
    public bool TryGetWindowAt(ulong targetId, double serverTime, out InterruptWindowRecord record) =>
        _schedule.TryGetWindowAt(targetId, serverTime, out record);

    /// <summary>대상의 가장 최근 창.</summary>
    public bool TryGetLatestWindow(ulong targetId, out InterruptWindowRecord record) =>
        _schedule.TryGetLatest(targetId, out record);

    // ---- 예측·판정(2단계 — 아직 스킬에 연결하지 않는다. 연결 3·4단계) ----

    /// <summary>
    /// [각 피어] 누르는 순간 예측 — <see cref="InterruptJudgeCalculator.Predict"/>(같은 순수 계산). 클라는 허용 오차 0.
    /// 결과의 <see cref="InterruptJudgeResult.Forecast"/> = 대상 없음/약/강.
    /// </summary>
    public InterruptJudgeResult Predict(in InterruptCastRequest cast) =>
        InterruptJudgeCalculator.Predict(this, cast, InterruptJudgeTolerance.Zero);

    /// <summary>[각 피어] Hit 타이밍 판정 — <see cref="InterruptJudgeCalculator.Judge"/>. 서버 검증(3단계)은 허용 오차를 넣은 오버로드로 서버 이력에서 다시 돌린다.</summary>
    public InterruptJudgeResult Judge(in InterruptJudgeAttempt attempt) =>
        InterruptJudgeCalculator.Judge(this, attempt, InterruptJudgeTolerance.Zero);

    public InterruptJudgeResult Judge(in InterruptJudgeAttempt attempt, in InterruptJudgeTolerance tolerance) =>
        InterruptJudgeCalculator.Judge(this, attempt, tolerance);

    /// <summary>
    /// 시전자·근접/대쉬·조준 방향·누른 로컬 게임초로 예측 입력을 만든다 — Hit 지연 = 우클릭 간파 스킬의 <see cref="PlayerInterruptSkillBase.TryGetJudgeHitDelay"/>.
    /// 우클릭이 간파 스킬이 아니거나 데이터가 없으면 false.
    /// </summary>
    public bool TryBuildCastRequest(Player caster, bool viaDash, Vector3 attackDirection, float pressLocalTime, out InterruptCastRequest cast)
    {
        cast = default;
        PlayerSkillController skills = caster != null ? caster.GetComponent<PlayerSkillController>() : null;
        if (skills == null || !(skills.GetSkill(PlayerSkillSlot.Interrupt) is PlayerInterruptSkillBase skill) ||
            !skill.TryGetJudgeHitDelay(out float hitDelay))
            return false;

        cast = new InterruptCastRequest
        {
            ActorId = caster.NetworkObjectId,
            ViaDash = viaDash,
            PressTime = ToServerTime(pressLocalTime),
            AttackDirection = attackDirection,
            HitDelay = hitDelay,
        };
        return true;
    }

    // ---- IInterruptJudgeWorld ----

    int IInterruptJudgeWorld.TargetCount => _targetIds.Count;

    ulong IInterruptJudgeWorld.GetTargetId(int index) => _targetIds[index];

    bool IInterruptJudgeWorld.TryGetLatestWindowOpenedBy(ulong targetId, double time, out InterruptWindowRecord record) =>
        _schedule.TryGetLatestOpenedBy(targetId, time, out record);

    // 대상의 지금 콜라이더로 질의한다 — 대상이 targetPosition 에 있었다고 보고 상자를 (지금 위치 − 그 위치)만큼 옮긴다(수평 회전 대칭 몸통 가정).
    // 걸린 콜라이더의 피해 수신 Unit(ResolveHitUnit — 기존 판정과 같은 해석)이 그 대상이면 닿은 것.
    bool IInterruptJudgeWorld.ShapeHitsTarget(ulong targetId, in HitboxAnchorShape shape, Vector3 targetPosition, int layers)
    {
        if (!_targets.TryGetValue(targetId, out TargetTrack track) || track.Unit == null)
            return false;

        int count = shape.Offset(track.Unit.transform.position - targetPosition).Overlap(_overlapBuffer, layers);
        for (int i = 0; i < count; i++)
        {
            Collider hit = _overlapBuffer[i];
            if (hit != null && PlayerSkillBase.ResolveHitUnit(hit, out _) == track.Unit)
                return true;
        }

        return false;
    }

    // 대시 경로 = 시전자 모터의 PredictDashPath(실제 대시 이동과 같은 스윕·유닛 막힘 검사). 모터가 없으면 직선.
    PlayerDashPathResult IInterruptJudgeWorld.SimulateDash(ulong actorId, Vector3 start, Vector3 direction, float speed, float remaining)
    {
        if (_actors.TryGetValue(actorId, out ActorTrack track) && track.Motor != null)
            return track.Motor.PredictDashPath(start, direction, speed, remaining);

        return new PlayerDashPathResult
        {
            End = start + InterruptSampleMath.Planar(direction) * (Mathf.Max(0f, speed) * Mathf.Max(0f, remaining)),
            Duration = Mathf.Max(0f, remaining),
        };
    }

    // ---- 서버 알림(보스·허수아비) ----

    /// <summary>
    /// [서버] 간파 창 열림·닫힘 알림. 서버가 아니거나 매니저·세션이 없으면 아무것도 안 한다(기존 동작 무영향).
    /// <paramref name="scheduledSeconds"/> = 열 때 닫힘 예정까지 길이(초, 로컬 게임초). 0 이하·무한 = 길이 모름(닫힘 배포 전까지 열림).
    /// <paramref name="attackStartedAt"/> = 공격(패턴) 시작 로컬 게임초(Time.time). 음수 = 열림 시각과 같음.
    /// 같은 대상에 열린 창이 있는데 다시 열면 앞 창을 닫고 새 창 번호를 준다. 닫힌 상태에서 닫기는 무시된다(멱등).
    /// </summary>
    public static void ServerReportWindow(GameObject owner, bool open, float scheduledSeconds = -1f,
        ulong designatedClientId = InterruptWindowRecord.NoDesignee, float attackStartedAt = -1f)
    {
        InterruptManager manager = Instance;
        if (manager == null || owner == null || !manager.IsServerActive)
            return;

        manager.ServerApplyWindow(owner, open, scheduledSeconds, designatedClientId, attackStartedAt);
    }

    void ServerApplyWindow(GameObject owner, bool open, float scheduledSeconds, ulong designatedClientId, float attackStartedAt)
    {
        ulong targetId = InterruptWindowReplicaTracker.TargetIdOf(owner);
        if (targetId == 0)
            return;

        SampleClock();
        double now = ServerNow;
        _hasState = true;

        if (!open)
        {
            if (_schedule.ServerClose(targetId, now, out InterruptWindowRecord closed))
                Publish(closed, owner);
            return;
        }

        double planned = scheduledSeconds > 0f && !float.IsInfinity(scheduledSeconds) ? now + scheduledSeconds : double.PositiveInfinity;
        double attackStart = attackStartedAt >= 0f ? ToServerTime(attackStartedAt) : now;
        InterruptWindowRecord record = _schedule.ServerOpen(targetId, attackStart, now, planned, designatedClientId,
            out bool closedPrevious, out InterruptWindowRecord previous);
        if (closedPrevious)
            Publish(previous, owner);
        Publish(record, owner);
    }

    void Publish(in InterruptWindowRecord record, Object context)
    {
        if (logWindows)
            Edit.Log($"[InterruptManager] 창 #{record.WindowId} {Describe(record)}", context);
        Broadcast(record);
    }

    static string Describe(in InterruptWindowRecord r)
    {
        string designee = r.DesignatedClientId == InterruptWindowRecord.NoDesignee ? string.Empty : $" · 지목 client {r.DesignatedClientId}";
        string planned = double.IsPositiveInfinity(r.PlannedCloseTime) ? "길이 모름" : $"예정 {r.PlannedCloseTime - r.OpenTime:0.###}s";
        return r.IsClosed
            ? $"닫힘 {r.CloseKind} — 대상 {r.TargetId} · 열린 {r.CloseTime - r.OpenTime:0.###}s · {planned}{designee}"
            : $"열림 — 대상 {r.TargetId} · 공격 시작 뒤 {r.OpenTime - r.AttackStartTime:0.###}s · {planned}{designee}";
    }

    // ---- 수명 ----

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        _networkManager = GetComponent<NetworkManager>();
        _networkClock = GetComponent<NetworkClock>();
    }

    void OnDisable()
    {
        UnregisterHandlers();
        ResetState();
    }

    void OnDestroy()
    {
        UnregisterHandlers();
        ResetState();
        if (Instance == this)
            Instance = null;
    }

    void LateUpdate()
    {
        if (!IsSessionRunning || _networkManager.CustomMessagingManager == null)
        {
            if (_registeredMessaging != null || _hasState)
            {
                UnregisterHandlers();
                ResetState();
            }
            return;
        }

        EnsureHandlersRegistered();
        SampleClock();
        _hasState = true;

        double now = ServerNow;
        SamplePlayers(now);
        SampleTargets(now);
    }

    void SampleClock()
    {
        double local = Time.timeAsDouble;
        bool running = _networkClock != null && _networkClock.IsRunning;
        _clock.Sample(local, running ? _networkClock.GameNow : local);
    }

    void ResetState()
    {
        _clock.Reset();
        _schedule.Clear();
        _actors.Clear();
        _targets.Clear();
        _targetIds.Clear();
        _hasState = false;
    }

    // ---- 샘플 ----

    void SamplePlayers(double now)
    {
        foreach (ActorTrack track in _actors.Values)
            track.Seen = false;

        for (int i = s_players.Count - 1; i >= 0; i--)
        {
            Player player = s_players[i];
            if (player == null)
            {
                s_players.RemoveAt(i);
                continue;
            }

            if (!player.IsSpawned)
                continue;

            ulong id = player.NetworkObjectId;
            if (!_actors.TryGetValue(id, out ActorTrack track) || track.Player != player)
            {
                track = new ActorTrack
                {
                    Player = player,
                    States = player.GetComponent<PlayerStateController>(),
                    Life = player.GetComponent<PlayerLifeCycleController>(),
                    Skills = player.GetComponent<PlayerSkillController>(),
                    Motor = player.GetComponent<PlayerMotor>(),
                };
                _actors[id] = track;
            }

            track.Seen = true;
            track.History.Add(now, SamplePlayer(track, now));
        }

        PruneUnseen(_actors);
    }

    InterruptActorSample SamplePlayer(ActorTrack track, double now)
    {
        Transform t = track.Player.transform;
        PlayerActionState state = track.Player.CurrentState;
        var s = new InterruptActorSample
        {
            Position = t.position,
            Forward = InterruptSampleMath.Planar(t.forward),
            Alive = track.Life == null || track.Life.State == PlayerLifeState.Alive,
            CrowdControlled = state == PlayerActionState.Knockback || state == PlayerActionState.Restrained,
            ClientId = track.Player.OwnerClientId,
        };

        // 간파 상자 = 우클릭 간파 스킬의 판정 앵커(InterruptAttack) 지금 형상 그대로 — 기존 예측·서버 판정이 읽는 같은 값(HitboxAnchorShape.TryRead).
        if (track.Skills != null && track.Skills.GetSkill(PlayerSkillSlot.Interrupt) is PlayerInterruptSkillBase skill &&
            skill.HitboxAnchor != null && HitboxAnchorShape.TryRead(skill.HitboxAnchor, out HitboxAnchorShape hitbox))
        {
            s.HasHitbox = true;
            s.Hitbox = hitbox.Offset(-s.Position);
            s.HitboxForward = InterruptSampleMath.Planar(skill.HitboxAnchor.transform.forward);
            s.HitLayers = skill.Data != null ? skill.Data.HittableLayers.value : 0;
        }

        PlayerDashState dash = track.States != null ? track.States.ActiveDash : null;
        if (dash != null)
        {
            s.Dashing = true;
            s.DashStartTime = now - dash.Elapsed;
            s.DashDirection = dash.Direction;
            s.DashSpeed = dash.Speed;
            s.DashRemaining = dash.Remaining;
        }

        return s;
    }

    void SampleTargets(double now)
    {
        foreach (TargetTrack track in _targets.Values)
            track.Seen = false;

        InterruptTargetRegistry.CopyLiveTo(_targetBuffer);
        for (int i = 0; i < _targetBuffer.Count; i++)
        {
            Unit unit = _targetBuffer[i].Unit;
            ulong id = InterruptWindowReplicaTracker.TargetIdOf(unit.gameObject);
            if (id == 0)
                continue;

            if (!_targets.TryGetValue(id, out TargetTrack track) || track.Unit != unit)
            {
                track = new TargetTrack
                {
                    Unit = unit,
                    State = unit as IInterruptJudgeTarget ?? unit.GetComponent<IInterruptJudgeTarget>(),
                };
                _targets[id] = track;
            }

            track.Seen = true;
            Transform t = unit.transform;
            var s = new InterruptTargetSample
            {
                Position = t.position,
                Forward = InterruptSampleMath.Planar(t.forward),
                BlockedByState = track.State != null && track.State.InterruptBlockedByState,
                DesignatedClientId = _schedule.TryGetWindowAt(id, now, out InterruptWindowRecord window)
                    ? window.DesignatedClientId
                    : InterruptWindowRecord.NoDesignee,
                FrontAngle = _targetBuffer[i].FrontAngle,
            };
            track.History.Add(now, s);
        }

        _targetBuffer.Clear();

        // 빠진 대상(디스폰·사망·씬 언로드)은 이력과 창 시간표를 함께 지운다.
        _removeBuffer.Clear();
        foreach (KeyValuePair<ulong, TargetTrack> pair in _targets)
            if (!pair.Value.Seen)
                _removeBuffer.Add(pair.Key);
        for (int i = 0; i < _removeBuffer.Count; i++)
        {
            _targets.Remove(_removeBuffer[i]);
            _schedule.Forget(_removeBuffer[i]);
        }
        _removeBuffer.Clear();

        _targetIds.Clear();
        _targetIds.AddRange(_targets.Keys);
    }

    void PruneUnseen(Dictionary<ulong, ActorTrack> tracks)
    {
        _removeBuffer.Clear();
        foreach (KeyValuePair<ulong, ActorTrack> pair in tracks)
            if (!pair.Value.Seen)
                _removeBuffer.Add(pair.Key);
        for (int i = 0; i < _removeBuffer.Count; i++)
            tracks.Remove(_removeBuffer[i]);
        _removeBuffer.Clear();
    }

    // ---- CustomMessaging: 서버 → 클라 창 시간표 ----

    void EnsureHandlersRegistered()
    {
        CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
        if (_registeredMessaging == messaging)
            return;

        UnregisterHandlers();
        messaging.RegisterNamedMessageHandler(WindowMessageName, HandleWindowMessage);
        _registeredMessaging = messaging;

        if (_networkManager.IsServer)
        {
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _clientConnectedHooked = true;
        }
    }

    void UnregisterHandlers()
    {
        if (_registeredMessaging != null)
        {
            _registeredMessaging.UnregisterNamedMessageHandler(WindowMessageName);
            _registeredMessaging = null;
        }

        if (_clientConnectedHooked && _networkManager != null)
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _clientConnectedHooked = false;
    }

    // 늦은 합류 — 지금 열린 창만 보낸다(닫힌 창은 1초 이력 밖이 곧 된다).
    void HandleClientConnected(ulong clientId)
    {
        if (!IsServerActive || clientId == _networkManager.LocalClientId)
            return;

        _schedule.CopyOpenTo(_recordBuffer);
        for (int i = 0; i < _recordBuffer.Count; i++)
            Send(_recordBuffer[i], clientId);
        _recordBuffer.Clear();
    }

    void Broadcast(in InterruptWindowRecord record)
    {
        if (_networkManager.CustomMessagingManager == null)
            return;

        IReadOnlyList<ulong> clients = _networkManager.ConnectedClientsIds;
        for (int i = 0; i < clients.Count; i++)
        {
            if (clients[i] == _networkManager.LocalClientId)
                continue;   // 호스트 자신은 이미 기록했다
            Send(record, clients[i]);
        }
    }

    void Send(in InterruptWindowRecord record, ulong clientId)
    {
        CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
            return;

        using (var writer = new FastBufferWriter(InterruptWindowRecord.SerializedSize, Allocator.Temp))
        {
            record.Write(writer);
            messaging.SendNamedMessage(WindowMessageName, clientId, writer);
        }
    }

    void HandleWindowMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId || _networkManager.IsServer)
            return;

        InterruptWindowRecord record = InterruptWindowRecord.Read(reader);
        _schedule.Apply(record);
        _hasState = true;
        if (logWindows)
            Edit.Log($"[InterruptManager] 창 #{record.WindowId} 수신 {Describe(record)}", this);
    }
}
