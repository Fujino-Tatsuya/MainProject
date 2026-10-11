using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 진행 상태의 <b>소유자</b>(씬 상주 NetworkObject, 서버 권한 — 클라는 읽기만).
/// 규칙은 순수 <see cref="TutorialFlowModel"/> 이 갖고, 이 컴포넌트는 서버에서 모델을 돌려 결과를 복제한다.
/// 원본: Docs/design/tutorial-scenario.md §2.1·§4 · PLAN-tutorial.md S1 · Docs/tech/tutorial-director.md.
///
/// 활성 조건: 서버가 맵 생성(<see cref="MapGenerator.OnGenerated"/>)을 받은 시점에 <see cref="TutorialStageMarker.IsActive"/>.
/// 아니면(절차 맵 등) 아무것도 하지 않는다 — 복제 값도 기본값(<see cref="IsTutorialActive"/> = false) 그대로.
///
/// 늦은 클라: 이벤트를 재생하지 않는다. 복제 값(NetworkVariable·NetworkList)만으로 지금 상태를 다시 그릴 수 있고,
/// 스폰 직후 <see cref="StateChanged"/> 가 한 번 나간다.
///
/// 시간: 교착 방지는 <b>서버 실제 시간</b>(<c>ServerTime</c>) — 간파 슬로우모션·솔로 일시정지 영향 없음(§2.1).
///
/// S2 이후가 붙일 자리:
///  · S2 구간 감지 ✅ → <see cref="AreaFirstEntered"/>(파티 첫 진입)·<see cref="CorridorMidReached"/>(개인 복도 중간)·
///    <see cref="GetPlayerAreaServer"/>. 단계 갱신(<see cref="SetStepServer"/>)은 아직 안 한다(S6 에서 게이트와 함께)
///  · S3 배리어 ✅ → <see cref="TutorialBarriers"/>(모든 피어)가 <see cref="StateChanged"/>·<see cref="GateOpened"/> 를 받아 <see cref="IsGateOpen"/> 으로 적용
///  · S4 존 장부 ✅ → <see cref="TutorialMonsterZones"/>: 활성 때 존2~4 휴면, 존 클리어 → <see cref="RequestGateServer"/>(G3·G6·G9, 차례 전이면 보류),
///    존2 처치 → Zone2Kills(목표 = 장부 수). 기상 = <see cref="SetZoneDormantServer"/>(S8·S9·S10 이 부른다)
///  · S5 타이머·보스 길 ✅ → <b>G8 열림 = 타이머 시작</b>(<see cref="StartBossTimerServer"/>, 420초 — 단일 기준 <see cref="IsBossTimerStarted"/>),
///    타이머 HUD 표시(<see cref="ShowTimerHudServer"/> — S10 Z4-0 이 부른다), G10 → 패드·엘리베이터 문(<see cref="TutorialBossPath"/>·<see cref="TutorialElevatorDoor"/>),
///    보스방 도착(패드·만료 강제 이동 모두) → 단계 Finished + <see cref="BossRoomArrived"/>(S12 안내 종료)
///  · S10 존4 → Z4-0 <see cref="ShowTimerHudServer"/> · 설정 자막 끝 <see cref="StartBossTimerServer"/>(= G8) · 만료 문구는 BossTimerManager.State == Expired
///  · S11 사망 규칙 → 목숨 차감 기준 = <see cref="IsBossTimerStarted"/>(G8 열림, 모든 피어)
///  · S7 존1 ✅ → TutorialZone1Flow: <see cref="ReportCheckServer"/>(Dash·AttackHit — 서버 관측) / <see cref="ReportLocalMoveDistance"/>(Move — 오너 누적 거리, 서버 상한 검증) /
///    <see cref="ReportLocalCheck"/>(PassiveTooltip — 오너 보고)
///  · 서버 → 클라 자막 → <see cref="ShowSubtitleServer"/>(id, 키·값 인자, 대상 null = 전원). 강조·목표 패널은 흐름 파일이 클라에서 직접
///  · S9 존3 → <see cref="AddCounterServer"/>(Zone3Interrupts) — 목표 도달 시 G5 자동 개방
///  · S12 특성 → 개인 게이트가 <see cref="TutorialGateOpenReason.Timeout"/> 으로 열리면 미선택자 자동 획득
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialDirector : NetworkBehaviour
{
    /// <summary>복제되는 개인 진행.</summary>
    public struct PlayerState : INetworkSerializable, IEquatable<PlayerState>
    {
        public ulong ClientId;
        public int CheckMask;
        public byte AttackHits;
        public bool Connected;
        public bool PermanentDead;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref CheckMask);
            s.SerializeValue(ref AttackHits);
            s.SerializeValue(ref Connected);
            s.SerializeValue(ref PermanentDead);
        }

        public bool Equals(PlayerState o)
            => ClientId == o.ClientId && CheckMask == o.CheckMask && AttackHits == o.AttackHits
               && Connected == o.Connected && PermanentDead == o.PermanentDead;
    }

    /// <summary>복제되는 대기 표시 — 지금 열 차례인 개인 게이트(없으면 Gate = <see cref="NoGate"/>).</summary>
    public struct WaitState : INetworkSerializable, IEquatable<WaitState>
    {
        public byte Gate;
        public byte Done;
        public byte Total;
        /// <summary>교착 방지 자동 진행 시각(ServerTime). 첫 완료자 전이면 -1.</summary>
        public double AutoAdvanceAtServerTime;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Gate);
            s.SerializeValue(ref Done);
            s.SerializeValue(ref Total);
            s.SerializeValue(ref AutoAdvanceAtServerTime);
        }

        public bool Equals(WaitState o)
            => Gate == o.Gate && Done == o.Done && Total == o.Total && AutoAdvanceAtServerTime.Equals(o.AutoAdvanceAtServerTime);
    }

    public const byte NoGate = byte.MaxValue;

    [Header("교착 방지 (D9)")]
    [Tooltip("개인 체크리스트 게이트(G1·G2·G4·G7·G10)가 첫 완료자 뒤 자동으로 열리기까지의 시간(서버 실제 시간 초).")]
    [SerializeField, Min(1f)] private float deadlockSeconds = (float)TutorialFlowModel.DefaultDeadlockSeconds;

    [Header("파티 카운터 목표")]
    [Tooltip("존2 처치 목표 — 존 장부(S4)가 존2 몬스터를 1마리 이상 찾으면 그 수가 우선한다. 이 값은 장부가 비었을 때만 쓴다.")]
    [SerializeField, Min(1)] private int zone2KillTarget = 9;
    [SerializeField, Min(1)] private int zone3InterruptTarget = 3;
    [SerializeField, Min(1)] private int zone4PanelTarget = 4;

    [Header("보스 타이머 (S5)")]
    [Tooltip("튜토리얼 제한시간(게임 시간 초, D6). G8 에서 BossTimerManager 를 이 값으로 시작한다 — 씬의 BossTimerManager 값(비튜토리얼)은 그대로.")]
    [SerializeField, Min(5f)] private float bossTimerLimitSeconds = TutorialBossPathRules.DefaultTutorialTimerLimitSeconds;

    [Header("구간 감지 (S2)")]
    [Tooltip("플레이어 위치로 구간을 판정하는 간격(초, 서버). 매 프레임일 필요는 없다.")]
    [SerializeField, Range(0.02f, 1f)] private float areaPollInterval = 0.15f;
    [Tooltip("새 구간에 들어갈 때 경계에서 안쪽으로 이만큼 더 들어와야 한다(m). 원격 위치는 NetworkTransform 복제 지연·보간으로 흔들린다.")]
    [SerializeField, Min(0f)] private float areaEnterMargin = TutorialAreaTracker.DefaultEnterMargin;
    [Tooltip("지금 구간을 떠날 때 경계 바깥으로 이만큼 더 나가야 한다(m). 들어갈 때 여유보다 크게 — 경계 왕복 깜빡임 방지.")]
    [SerializeField, Min(0f)] private float areaExitMargin = TutorialAreaTracker.DefaultExitMargin;

    // 🔴 선언 순서 = 같은 틱 안의 클라 반영 순서로 기대한다 — 교착 방지 마스크를 열림 마스크보다 먼저 둬서
    //    클라 GateOpened 가 이유(Timeout)를 읽을 수 있게 한다(보장은 아님 — 이유는 표시 보조용).
    private readonly NetworkVariable<bool> _active = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<TutorialStep> _step = new NetworkVariable<TutorialStep>(
        TutorialStep.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _timeoutGates = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _openedGates = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<WaitState> _wait = new NetworkVariable<WaitState>(
        new WaitState { Gate = NoGate, AutoAdvanceAtServerTime = -1d },
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkList<PlayerState> _players = new NetworkList<PlayerState>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkList<int> _counters = new NetworkList<int>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 타이머 HUD 표시(S5) — 튜토리얼에서 Idle 동안 숨긴 HUD 를 켠다(Z4-0). 타이머가 시작되면(G8) 이 값과 무관하게 보인다.
    private readonly NetworkVariable<bool> _timerHudShown = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static TutorialDirector Instance { get; private set; }

    /// <summary>단계 변경(이전, 새). 서버 = 모델에서 즉시, 클라 = 복제 반영 때. 스폰 직후 초기값으로는 안 나간다.</summary>
    public static event Action<TutorialStep, TutorialStep> StepChanged;

    /// <summary>
    /// 게이트 열림. 서버 = 실제 이유. 클라 = 복제 마스크 차이로 — 이유는 Timeout 이 아니면 Manual 로 보인다(AllDone·CounterReached 는 복제하지 않음).
    /// 스폰 직후 이미 열린 게이트로는 안 나간다 — 늦은 클라는 <see cref="IsGateOpen"/> 으로 다시 그린다.
    /// </summary>
    public static event Action<TutorialGate, TutorialGateOpenReason> GateOpened;

    /// <summary>복제 상태 중 무엇이든 바뀌었다(스폰 직후 1회 포함) — UI 를 통째로 다시 그리는 신호.</summary>
    public static event Action StateChanged;

    /// <summary>
    /// [서버] 파티 첫 진입(구간, 처음 들어온 client) — 존1~4·보스 진입 각각 판 전체 1번. 살아 있는 플레이어만 센다.
    /// 붙일 자리: S8 존2 안내 → 기상 · S9 존3 연습 모드 시작 · S10 존4 설정 자막(Z4-0) · S6 단계/안내 갱신.
    /// </summary>
    public static event Action<TutorialArea, ulong> AreaFirstEntered;

    /// <summary>
    /// [서버] 개인 첫 복도 중간 지점(client, 복도 번호 1~4) — 플레이어마다 복도마다 1번, 왕복해도 다시 안 낸다. 살아 있는 플레이어만.
    /// 붙일 자리: S8/S12 특성 선택 창(OpenSelection(clientId, corridor, showIntro: corridor == 1)) — 게이트가 이미 열렸거나
    /// 자동 획득을 받은 플레이어는 창을 띄우지 않는 판단(§3.2)은 소비처 몫.
    /// </summary>
    public static event Action<ulong, int> CorridorMidReached;

    /// <summary>
    /// [서버] 보스방 도착(도착한 clientId 목록) — 패드든 타이머 만료 강제 이동이든 <see cref="BossTeleportManager.AlivePlayersArrived"/> 한 곳. 판 전체 1번.
    /// 이때 단계가 <see cref="TutorialStep.Finished"/> 로 바뀐다(복제 — 클라·늦은 클라는 <see cref="IsFinished"/>·StepChanged 로 본다).
    /// 붙일 자리: S12 안내 종료(목표 패널 정리), D19 23호 정면 간파 팁.
    /// </summary>
    public static event Action<IReadOnlyList<ulong>> BossRoomArrived;

    // 서버 전용
    private TutorialFlowModel _model;
    private TutorialAreaTracker _areas;
    private TutorialMonsterZones _monsterZones;
    private float _areaPollTimer;
    private bool _pendingActivate;
    private NetworkManager _subscribedNm;
    private BossTeleportManager _teleport;
    private bool _bossArrived;
    private double _activatedServerTime = -1d;

    // ─── 읽기(모든 피어) ─────────────────────────────────────────────────

    public bool IsTutorialActive => IsSpawned && _active.Value;
    public TutorialStep Step => IsSpawned ? _step.Value : TutorialStep.None;
    public bool IsGateOpen(TutorialGate g) => IsSpawned && (_openedGates.Value & (1 << (int)g)) != 0;
    public bool WasGateAutoOpened(TutorialGate g) => IsSpawned && (_timeoutGates.Value & (1 << (int)g)) != 0;

    public int GetCounter(TutorialCounter c)
    {
        int i = (int)c;
        return IsSpawned && i < _counters.Count ? _counters[i] : 0;
    }

    /// <summary>카운터 목표. 활성 뒤에는 복제값(존2 처치 = 존 장부의 실제 수), 그 전에는 인스펙터 값.</summary>
    public int GetCounterTarget(TutorialCounter c)
    {
        int i = TutorialFlowModel.CounterCount + (int)c;
        if (IsSpawned && i < _counters.Count) return _counters[i];
        return c switch
        {
            TutorialCounter.Zone2Kills => zone2KillTarget,
            TutorialCounter.Zone3Interrupts => zone3InterruptTarget,
            _ => zone4PanelTarget,
        };
    }

    public bool TryGetPlayerProgress(ulong clientId, out TutorialPlayerProgress progress)
    {
        progress = default;
        if (!IsSpawned) return false;

        for (int i = 0; i < _players.Count; i++)
        {
            PlayerState s = _players[i];
            if (s.ClientId != clientId) continue;
            progress = new TutorialPlayerProgress
            {
                ClientId = s.ClientId, CheckMask = s.CheckMask, AttackHits = s.AttackHits,
                Connected = s.Connected, PermanentDead = s.PermanentDead,
            };
            return true;
        }
        return false;
    }

    /// <summary>
    /// "동료 대기 중 (done/total) · 자동 진행 mm:ss" 표시용. 열 차례인 개인 게이트가 없으면 false.
    /// <paramref name="autoAdvanceRemaining"/> = 자동 진행까지 남은 초(첫 완료자 전이면 -1).
    /// </summary>
    public bool TryGetWait(out TutorialGate gate, out int done, out int total, out float autoAdvanceRemaining)
    {
        gate = default;
        done = total = 0;
        autoAdvanceRemaining = -1f;
        if (!IsSpawned || _wait.Value.Gate == NoGate) return false;

        WaitState w = _wait.Value;
        gate = (TutorialGate)w.Gate;
        done = w.Done;
        total = w.Total;
        if (w.AutoAdvanceAtServerTime >= 0d)
            autoAdvanceRemaining = Mathf.Max(0f, (float)(w.AutoAdvanceAtServerTime - NetworkManager.ServerTime.Time));
        return true;
    }

    /// <summary>타이머 HUD 표시가 켜졌는가(Z4-0 · G8). 튜토리얼 Idle 동안 HUD 숨김을 푼다(<see cref="TutorialBossPath.ShouldHideTimerHud"/>).</summary>
    public bool IsTimerHudShown => IsSpawned && _timerHudShown.Value;

    /// <summary>
    /// 보스 타이머가 시작됐는가 = <b>G8 열림</b>(모든 피어). 튜토리얼 '타이머 전/후' 판단(S11 목숨 차감·존4 기상)은 이 값 하나만 본다 —
    /// BossTimerManager.State 는 도착(Stopped)·만료로 바뀌므로 기준으로 쓰지 않는다.
    /// </summary>
    public bool IsBossTimerStarted => IsGateOpen(TutorialGate.G8_TimerStart);

    /// <summary>보스방에 도착해 튜토리얼 안내가 끝났는가(단계 Finished, 모든 피어).</summary>
    public bool IsFinished => Step == TutorialStep.Finished;

    public float BossTimerLimitSeconds => bossTimerLimitSeconds;

    public float AreaEnterMargin => areaEnterMargin;
    public float AreaExitMargin => areaExitMargin;

    /// <summary>
    /// [서버] 플레이어의 지금 구간(히스테리시스 적용). 튜토리얼 비활성·모르는 id·살아 있지 않음(영혼·사망 연출·완전 사망) = None.
    /// 판정 주기(<c>areaPollInterval</c>)만큼 늦을 수 있다.
    /// </summary>
    public TutorialArea GetPlayerAreaServer(ulong clientId) =>
        _areas != null ? _areas.GetCurrentArea(clientId) : TutorialArea.None;

    /// <summary>[서버] 파티가 이 존에 들어간 적이 있는가.</summary>
    public bool HasPartyEnteredServer(TutorialArea area) => _areas != null && _areas.HasPartyEntered(area);

    /// <summary>[서버] 이 플레이어가 복도 n(1~4) 중간 지점에 닿은 적이 있는가.</summary>
    public bool HasReachedCorridorMidServer(ulong clientId, int corridor) =>
        _areas != null && _areas.HasReachedCorridorMid(clientId, corridor);

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        // 구독은 스폰 전에 건다 — 늦게 걸면 생성 이벤트 자체를 놓친다(BossTimerManager 와 같은 이유).
        MapGenerator.OnGenerated += HandleMapGenerated;
    }

    private void OnDisable()
    {
        MapGenerator.OnGenerated -= HandleMapGenerated;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _active.OnValueChanged += HandleActiveChanged;
        _step.OnValueChanged += HandleStepChanged;
        _openedGates.OnValueChanged += HandleOpenedGatesChanged;
        _timeoutGates.OnValueChanged += HandleIntChanged;
        _wait.OnValueChanged += HandleWaitChanged;
        _players.OnListChanged += HandlePlayersChanged;
        _counters.OnListChanged += HandleCountersChanged;
        _timerHudShown.OnValueChanged += HandleBoolChanged;

        // 🔴 스폰 순서가 보장되지 않는다 — 맵 생성(MapNetworkSync.OnNetworkSpawn 이 시작)이 이 스폰보다 먼저일 수 있다.
        //    그때 받은 생성 이벤트는 보류해 뒀다가 여기서 한 번만 적용한다(스폰 전 NetworkList 쓰기는 조용히 버려진다).
        if (IsServer) ApplyPendingActivate();

        Edit.Log($"[Tutorial] 디렉터 스폰 (서버:{IsServer}) — 활성:{_active.Value} 단계:{_step.Value} 열린 게이트 0x{_openedGates.Value:X}.", this);
        StateChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _active.OnValueChanged -= HandleActiveChanged;
        _step.OnValueChanged -= HandleStepChanged;
        _openedGates.OnValueChanged -= HandleOpenedGatesChanged;
        _timeoutGates.OnValueChanged -= HandleIntChanged;
        _wait.OnValueChanged -= HandleWaitChanged;
        _players.OnListChanged -= HandlePlayersChanged;
        _counters.OnListChanged -= HandleCountersChanged;
        _timerHudShown.OnValueChanged -= HandleBoolChanged;
        UnsubscribeServer();
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// 🔴 <see cref="MapGenerator.OnGenerated"/> 는 서버·클라 양쪽에서 발생한다 — 서버만 판정한다.
    /// 스폰 전에는 <c>NetworkManager</c>(NetworkBehaviour 프로퍼티)가 null 이라 싱글턴으로 본다(BossTimerManager 의 2026-09-20 함정).
    /// </summary>
    private void HandleMapGenerated(MapGenerator generator)
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool online = nm != null && nm.IsListening;
        if (online && !nm.IsServer) return;

        if (!TutorialStageMarker.IsActive)
        {
            Edit.Log("[Tutorial] 맵 생성 — 튜토리얼 스테이지가 아니다(TutorialStageMarker 없음) → 디렉터 비활성.", this);
            return;
        }

        if (!TutorialStageMarker.OwnsGeneratedMap(generator))
            Edit.LogWarning($"[Tutorial] 마커('{TutorialStageMarker.Active.name}')는 켜져 있는데 생성된 슬롯이 그 스테이지 소속이 아니다 — " +
                            "스테이지가 둘 켜졌는지 확인. 마커 기준으로 튜토리얼을 켠다.", this);

        if (!online)
        {
            // 서버 권한 상태라 네트워크 없이는 복제·판정이 없다(BossTimerManager 와 같다). 확인은 호스트로.
            Edit.LogWarning("[Tutorial] 네트워크 없이 맵이 생성됐다 — 디렉터가 스폰되지 않아 튜토리얼 진행이 돌지 않는다(호스트로 실행).", this);
            return;
        }

        _pendingActivate = true;
        if (IsSpawned && IsServer) ApplyPendingActivate();
        else Edit.Log("[Tutorial] 맵 생성이 디렉터 스폰보다 먼저 왔다 — 스폰 직후 활성화한다.", this);
    }

    private void ApplyPendingActivate()
    {
        if (!IsServer || !IsSpawned || !_pendingActivate) return;
        _pendingActivate = false;

        if (_model != null)
        {
            Edit.LogWarning("[Tutorial] 이미 활성화됐다 — 맵 생성 이벤트가 두 번 왔다. 무시한다.", this);
            return;
        }

        // 구간 → 존 장부 → 모델 순서: 존2 처치 목표를 장부의 실제 수로 맞춘다(S4).
        BuildAreas();
        int zone2Target = BuildMonsterZones();

        _model = new TutorialFlowModel(deadlockSeconds, zone2Target, zone3InterruptTarget, zone4PanelTarget);
        _model.GateOpened += HandleModelGateOpened;
        _model.StepChanged += HandleModelStepChanged;

        _subscribedNm = NetworkManager;
        foreach (ulong id in _subscribedNm.ConnectedClientsIds)
            AddPlayer(id);
        _subscribedNm.OnClientConnectedCallback += HandleClientConnected;
        _subscribedNm.OnClientDisconnectCallback += HandleClientDisconnected;
        PlayerLifeCycleController.ServerLifeStateChanged += HandleServerLifeStateChanged;
        SubscribeBossPath();

        // [0, CounterCount) = 값, [CounterCount, 2·CounterCount) = 목표(존2 목표가 장부 수라 클라도 알아야 한다).
        _counters.Clear();
        for (int i = 0; i < TutorialFlowModel.CounterCount; i++) _counters.Add(0);
        for (int i = 0; i < TutorialFlowModel.CounterCount; i++) _counters.Add(_model.GetCounterTarget((TutorialCounter)i));

        _activatedServerTime = NetworkManager.ServerTime.Time;   // S7 이동 보고 상한 검증의 기준
        _active.Value = true;
        _model.SetStep(TutorialStep.Zone1);
        Commit();

        Edit.Log($"[Tutorial] 튜토리얼 시작 — 참가자 {_model.ParticipantCount}명, 교착 방지 {deadlockSeconds:0}초.", this);
    }

    private void UnsubscribeServer()
    {
        if (_subscribedNm != null)
        {
            _subscribedNm.OnClientConnectedCallback -= HandleClientConnected;
            _subscribedNm.OnClientDisconnectCallback -= HandleClientDisconnected;
            _subscribedNm = null;
        }
        PlayerLifeCycleController.ServerLifeStateChanged -= HandleServerLifeStateChanged;
        if (_teleport != null)
        {
            _teleport.AlivePlayersArrived -= HandleBossRoomArrived;
            _teleport = null;
        }
        if (_model != null)
        {
            _model.GateOpened -= HandleModelGateOpened;
            _model.StepChanged -= HandleModelStepChanged;
        }
        if (_areas != null)
        {
            _areas.PartyFirstEntered -= HandleAreaFirstEntered;
            _areas.CorridorMidReached -= HandleCorridorMidReached;
        }
        if (_monsterZones != null)
        {
            _monsterZones.ZoneCleared -= HandleZoneCleared;
            _monsterZones.MemberDied -= HandleZoneMemberDied;
            _monsterZones.Dispose();
        }
    }

    private void Update()
    {
        if (_model == null || !IsServer || !IsSpawned) return;
        Commit();   // 교착 방지 시계 — 매 프레임 재판정(바뀐 값만 복제)
        PollAreas();
    }

    // ─── 구간 감지(서버, S2) ──────────────────────────────────────────────
    // 규칙은 순수 TutorialAreaTracker, 모양은 TutorialAreaLayout(StageTutorial 슬롯·복도 바닥). 이벤트만 낸다 —
    // 게이트 개방·안내·특성 창은 이후 단계가 AreaFirstEntered / CorridorMidReached 를 구독해 붙인다.
    // 위치 = 서버가 보는 PlayerObject.transform(원격 = 오너 권한 NetworkTransform 복제값, 지연 있음 → 여유 경계).

    private void BuildAreas()
    {
        TutorialStageMarker stage = TutorialStageMarker.Active;
        _areas = new TutorialAreaTracker(areaEnterMargin, areaExitMargin);
        if (!TutorialAreaLayout.TryBuild(stage != null ? stage.transform : null, _areas, out string error))
            Edit.LogWarning($"[Tutorial] 구간 계산 실패 — {error}. 계산된 구간만 판정한다.", this);
        else if (error != null)
            Edit.LogWarning($"[Tutorial] 구간 계산 경고 — {error}.", this);

        _areas.PartyFirstEntered += HandleAreaFirstEntered;
        _areas.CorridorMidReached += HandleCorridorMidReached;
        _areaPollTimer = 0f;
    }

    private void PollAreas()
    {
        if (_areas == null) return;
        _areaPollTimer -= Time.unscaledDeltaTime;   // 슬로우모션 무관(§2.1)
        if (_areaPollTimer > 0f) return;
        _areaPollTimer = areaPollInterval;

        foreach (NetworkClient client in _subscribedNm.ConnectedClientsList)
        {
            NetworkObject po = client.PlayerObject;
            if (po == null) continue;
            var life = po.GetComponent<PlayerLifeCycleController>();
            PlayerLifeState state = life != null ? life.State : PlayerLifeState.Alive;
            Vector3 p = po.transform.position;
            _areas.UpdatePlayer(client.ClientId, p.x, p.z, state);
        }
    }

    private void HandleAreaFirstEntered(TutorialArea area, ulong clientId)
    {
        Edit.Log($"[Tutorial] 파티 첫 진입 {area} — client {clientId}.", this);
        // TODO(S6): 단계 갱신(SetStepServer)·안내 — 단계 전환 규칙은 게이트와 함께 정한다.
        AreaFirstEntered?.Invoke(area, clientId);
    }

    private void HandleCorridorMidReached(ulong clientId, int corridor)
    {
        Edit.Log($"[Tutorial] client {clientId} 복도{corridor} 중간 지점 도착(개인 1회).", this);
        // TODO(S8/S12): 특성 선택 창 열기 → 선택 완료 시 ReportCheckServer(clientId, TraitCorridor{n}).
        CorridorMidReached?.Invoke(clientId, corridor);
    }

    // ─── 존 장부 · 몬스터 휴면(서버, S4) ───────────────────────────────────
    // 규칙은 순수 TutorialMonsterLedger, 배선은 TutorialMonsterZones. 몬스터 쪽 훅은 MonsterBase.SetDormantServer 하나
    // (Docs/tech/handoff-tutorial.md). 활성 = 존2·존3·존4 전부 휴면(존1 은 몬스터 0 — 있으면 깨워 둔 채로 둔다).
    // 실제 기상 호출은 S8(존2)·S9(존3 — 스피너봇 레인과 정리)·S10(존4 일반 → 다리 개통 때 중간보스).

    /// <returns>존2 처치 목표(장부 수, 비었으면 인스펙터 값).</returns>
    private int BuildMonsterZones()
    {
        _monsterZones = new TutorialMonsterZones();
        _monsterZones.Build(_areas);
        _monsterZones.ZoneCleared += HandleZoneCleared;
        _monsterZones.MemberDied += HandleZoneMemberDied;

        int slept = 0;
        slept += _monsterZones.SetZoneDormant(TutorialArea.Zone2, true, TutorialMonsterFilter.All);
        slept += _monsterZones.SetZoneDormant(TutorialArea.Zone3, true, TutorialMonsterFilter.All);
        slept += _monsterZones.SetZoneDormant(TutorialArea.Zone4, true, TutorialMonsterFilter.All);

        int zone2 = _monsterZones.Ledger.Count(TutorialArea.Zone2);
        Edit.Log($"[Tutorial] 존 장부 — {_monsterZones.Describe()} · 휴면 시작 {slept}마리.", this);
        if (_monsterZones.Ledger.Count(TutorialArea.Zone1) > 0)
            Edit.LogWarning("[Tutorial] 존 장부: 존1 에 몬스터가 있다(시나리오는 0) — 휴면시키지 않는다.", this);
        if (zone2 == 0)
            Edit.LogWarning($"[Tutorial] 존 장부: 존2 몬스터 0 — 존2 클리어(G3) 판정이 없다. 처치 목표는 인스펙터 값 {zone2KillTarget}.", this);
        if (_monsterZones.Ledger.Count(TutorialArea.Zone3, TutorialMonsterFilter.KeyOnly) == 0
            || _monsterZones.Ledger.Count(TutorialArea.Zone4, TutorialMonsterFilter.KeyOnly) == 0)
            Edit.LogWarning("[Tutorial] 존 장부: 존3/존4 핵심 몬스터(중간보스급)가 없다 — 그 존은 전멸로 클리어를 판정한다.", this);

        return zone2 > 0 ? zone2 : zone2KillTarget;
    }

    private void HandleZoneMemberDied(ulong id, TutorialArea zone, bool key)
    {
        if (zone == TutorialArea.Zone2 && _model != null && _model.AddCounter(TutorialCounter.Zone2Kills)) Commit();
    }

    private void HandleZoneCleared(TutorialArea zone)
    {
        TutorialGate gate;
        switch (zone)
        {
            case TutorialArea.Zone2: gate = TutorialGate.G3_Corridor2Entry; break;
            case TutorialArea.Zone3: gate = TutorialGate.G6_Corridor3Entry; break;
            case TutorialArea.Zone4: gate = TutorialGate.G9_Corridor4Entry; break;
            default: return;
        }

        Edit.Log($"[Tutorial] {zone} 클리어 → {gate}.", this);
        ZoneCleared?.Invoke(zone);
        RequestGateServer(gate);
    }

    /// <summary>
    /// [서버] 존 클리어(존마다 1번) — 존2 전멸 / 존3 스피너봇 처치 / 존4 중간보스 처치. 게이트(G3·G6·G9)는 디렉터가 이미 연다(차례가 아니면 보류).
    /// 붙일 자리: S6 안내(`TUT_ZONE_CLEAR`·`TUT_Z4_CLEAR`).
    /// </summary>
    public static event Action<TutorialArea> ZoneCleared;

    /// <summary>
    /// [서버] 게이트를 열거나, 앞 게이트가 닫혀 있으면 <b>보류</b>했다가 차례가 오면 연다(<see cref="TutorialFlowModel.RequestGate"/>).
    /// 존 클리어처럼 "조건은 이미 충족, 순서만 기다림"인 경우에 쓴다. 지금 열었으면 true.
    /// </summary>
    public bool RequestGateServer(TutorialGate gate)
    {
        if (!RequireServerActive(nameof(RequestGateServer))) return false;
        if (_model.IsGateOpen(gate)) return false;

        bool opened = _model.RequestGate(gate);
        if (!opened)
            Edit.Log($"[Tutorial] {gate} 보류 — 앞 게이트가 아직 닫혀 있다. 차례가 오면 연다.", this);
        Commit();
        return opened;
    }

    /// <summary>
    /// [서버] 존 몬스터(살아 있는 것) 휴면/기상. 바뀐 수. 존4 기상은 <see cref="TutorialMonsterFilter.NormalOnly"/>(중간보스 제외),
    /// 다리 개통 때 <see cref="TutorialMonsterFilter.KeyOnly"/> 로 중간보스만.
    /// </summary>
    public int SetZoneDormantServer(TutorialArea zone, bool dormant, TutorialMonsterFilter filter = TutorialMonsterFilter.All)
    {
        if (!RequireServerActive(nameof(SetZoneDormantServer)) || _monsterZones == null) return 0;
        int n = _monsterZones.SetZoneDormant(zone, dormant, filter);
        Edit.Log($"[Tutorial] {zone} {filter} {(dormant ? "휴면" : "기상")} {n}마리 — {_monsterZones.Describe()}", this);
        return n;
    }

    public int WakeZoneServer(TutorialArea zone, TutorialMonsterFilter filter = TutorialMonsterFilter.All) =>
        SetZoneDormantServer(zone, false, filter);

    /// <summary>[서버] 몬스터 하나 휴면/기상(장부 밖이어도 된다). 바뀌었으면 true.</summary>
    public bool SetMonsterDormantServer(MonsterBase monster, bool dormant)
    {
        if (!RequireServerActive(nameof(SetMonsterDormantServer)) || monster == null || monster.IsDormant == dormant) return false;
        monster.SetDormantServer(dormant);
        return true;
    }

    /// <summary>[서버] 존의 살아 있는 몬스터(등록 순서). 장부가 없으면 비운다.</summary>
    public void GetZoneMonstersServer(TutorialArea zone, TutorialMonsterFilter filter, List<MonsterBase> result)
    {
        if (_monsterZones == null) { result.Clear(); return; }
        _monsterZones.GetAliveMonsters(zone, filter, result);
    }

    /// <summary>[서버] 몬스터 수(전체/생존). 장부가 없으면 false.</summary>
    public bool TryGetZoneMonsterCountServer(TutorialArea zone, TutorialMonsterFilter filter, out int total, out int alive)
    {
        total = alive = 0;
        if (_monsterZones == null) return false;
        total = _monsterZones.Ledger.Count(zone, filter);
        alive = _monsterZones.Ledger.Count(zone, filter, true);
        return true;
    }

    public bool IsZoneClearedServer(TutorialArea zone) => _monsterZones != null && _monsterZones.Ledger.IsZoneCleared(zone);

    /// <summary>[서버] 장부 요약 문자열(디버그).</summary>
    public string DescribeMonsterZonesServer() => _monsterZones != null ? _monsterZones.Describe() : "(장부 없음 — 튜토리얼 비활성)";

    // ─── 보스 타이머 · 보스 길(서버, S5) ─────────────────────────────────────
    // 타이머 자동 시작 끔·HUD 숨김·패드 잠금은 경석 코드가 TutorialBossPath 로 묻는다(Docs/tech/handoff-tutorial.md §3).
    // 여기서는 G8 = 타이머 시작, HUD 표시, 보스방 도착만 배선한다. 만료 강제 이동은 BossTimerManager 그대로(게이트·배리어·패드 무관).

    private void SubscribeBossPath()
    {
        BossTimerManager timer = BossTimerManager.Instance;
        if (timer == null)
            Edit.LogError("[Tutorial] BossTimerManager 가 없다 — G8 에서 타이머를 시작할 수 없다.", this);
        else if (timer.State != BossTimerManager.TimerState.Idle)
            Edit.LogError($"[Tutorial] 활성화 때 보스 타이머가 이미 {timer.State} — 자동 시작이 걸렸다(TutorialBossPath 판별 확인).", this);

        _teleport = BossTeleportManager.Instance;
        if (_teleport != null) _teleport.AlivePlayersArrived += HandleBossRoomArrived;
        else Edit.LogError("[Tutorial] BossTeleportManager 가 없다 — 보스방 도착(안내 종료)을 받을 수 없다.", this);
    }

    /// <summary>
    /// [서버] 타이머 HUD 를 보이게 한다(S10 Z4-0 — 첫 플레이어 존4 진입). 아직 Idle 이면 빈 게이지로 보인다. 복제 — 늦은 클라도 보인다.
    /// 펄스 강조는 S6/S10 몫.
    /// </summary>
    public void ShowTimerHudServer()
    {
        if (!RequireServerActive(nameof(ShowTimerHudServer)) || _timerHudShown.Value) return;
        _timerHudShown.Value = true;
        Edit.Log("[Tutorial] 타이머 HUD 표시.", this);
    }

    /// <summary>
    /// [서버] 보스 타이머 시작 = <b>G8 열기</b>(단일 기준 — <see cref="IsBossTimerStarted"/>). S10 이 존4 설정 자막이 끝날 때 부른다.
    /// 앞 게이트(G7)가 닫혀 있으면 거부(false). 실제 시작은 G8 열림 처리(<see cref="HandleModelGateOpened"/>)에서 — 다른 경로로 G8 이 열려도 같다.
    /// </summary>
    public bool StartBossTimerServer() => TryOpenGateServer(TutorialGate.G8_TimerStart);

    // G8 이 어떤 경로로 열리든(TryOpenGate·보류 RequestGate·디버그) 여기서 한 번 타이머를 시작한다.
    private void OnTimerGateOpened()
    {
        if (!_timerHudShown.Value) _timerHudShown.Value = true;

        BossTimerManager timer = BossTimerManager.Instance;
        if (timer == null)
        {
            Edit.LogError("[Tutorial] G8 열림 — BossTimerManager 가 없어 타이머를 시작하지 못했다.", this);
            return;
        }
        if (timer.StartTimerServer(bossTimerLimitSeconds))
            Edit.Log($"[Tutorial] G8 → 보스 타이머 시작 {bossTimerLimitSeconds:0}초.", this);
    }

    private void HandleBossRoomArrived(IReadOnlyList<ulong> arrived)
    {
        if (_model == null || !IsServer || !IsSpawned || _bossArrived) return;
        _bossArrived = true;

        Edit.Log($"[Tutorial] 보스방 도착 {arrived.Count}명 — 튜토리얼 안내 종료(단계 Finished).", this);
        // TODO(S12): 안내 종료(목표 패널 정리)·D19 23호 정면 간파 팁 — BossRoomArrived / IsFinished 를 구독.
        _model.SetStep(TutorialStep.Finished);
        Commit();
        BossRoomArrived?.Invoke(arrived);
    }

    // ─── 인원(서버) ──────────────────────────────────────────────────────

    private void AddPlayer(ulong clientId)
    {
        if (!_model.AddPlayer(clientId)) return;

        // 이미 스폰된 플레이어면 지금 생명주기를 반영한다(이후 변화는 ServerLifeStateChanged 로).
        if (_subscribedNm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
        {
            var life = client.PlayerObject.GetComponent<PlayerLifeCycleController>();
            if (life != null) _model.SetPermanentDead(clientId, life.State == PlayerLifeState.PermanentDead);
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        // 게임 중 난입은 없다(networking.md) — 접속 순서가 맵 생성과 엇갈린 경우만 여기로 온다.
        if (_model == null) return;
        AddPlayer(clientId);
        Commit();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (_model == null || !IsSpawned) return;
        if (_model.MarkDisconnected(clientId))
            Edit.Log($"[Tutorial] client {clientId} 이탈 — 남은 참가자 {_model.ParticipantCount}명 기준으로 재판정.", this);
        _areas?.RemovePlayer(clientId);
        Commit();
    }

    private void HandleServerLifeStateChanged(ulong clientId, PlayerLifeState from, PlayerLifeState to)
    {
        if (_model == null || !IsSpawned) return;
        if (_model.SetPermanentDead(clientId, to == PlayerLifeState.PermanentDead))
            Edit.Log($"[Tutorial] client {clientId} {(to == PlayerLifeState.PermanentDead ? "완전 사망 — 전원 판정에서 뺀다" : "완전 사망 해제")} " +
                     $"(참가자 {_model.ParticipantCount}명).", this);
        Commit();
    }

    // ─── 서버 API ────────────────────────────────────────────────────────

    /// <summary>
    /// 개인 체크 완료 보고(서버 관측분 — Dash·AttackHit·SkillQ·SkillE·Trait*). 반영되면 true.
    /// 중복·모르는 client·비활성은 false.
    /// </summary>
    public bool ReportCheckServer(ulong clientId, TutorialCheck check)
    {
        if (!RequireServerActive(nameof(ReportCheckServer))) return false;
        bool changed = _model.ReportCheck(clientId, check);
        if (changed) Commit();
        return changed;
    }

    /// <summary>파티 카운터 증가(목표에서 멈춤). Zone3Interrupts 가 목표에 닿으면 G5 가 자동으로 열린다.</summary>
    public bool AddCounterServer(TutorialCounter counter, int amount = 1)
    {
        if (!RequireServerActive(nameof(AddCounterServer))) return false;
        bool changed = _model.AddCounter(counter, amount);
        if (changed) Commit();
        return changed;
    }

    /// <summary>게이트를 연다(G3·G6·G8·G9 등 서버 판정). 앞 게이트가 닫혀 있으면 거부(false + 경고).</summary>
    public bool TryOpenGateServer(TutorialGate gate, TutorialGateOpenReason reason = TutorialGateOpenReason.Manual)
    {
        if (!RequireServerActive(nameof(TryOpenGateServer))) return false;
        if (_model.IsGateOpen(gate)) return false;

        if (!_model.TryOpenGate(gate, reason))
        {
            Edit.LogWarning($"[Tutorial] {gate} 열기 거부 — 앞 게이트가 아직 닫혀 있다(순서 규칙).", this);
            return false;
        }
        Commit();
        return true;
    }

    /// <summary>단계 변경 — 앞으로만.</summary>
    public bool SetStepServer(TutorialStep step)
    {
        if (!RequireServerActive(nameof(SetStepServer))) return false;
        bool changed = _model.SetStep(step);
        if (changed) Commit();
        return changed;
    }

    /// <summary>
    /// [서버] 자막 한 줄을 클라들에 띄운다 — 각 클라가 <see cref="TutorialGuideUI.ShowSubtitle"/> 로 자기 캐릭터 기준 문구를 고른다.
    /// <paramref name="args"/> = 자리표시자 키·값 교대 배열(<c>{ "n", "3", "name", "…" }</c>, 홀수 길이면 마지막 키는 버린다).
    /// <paramref name="targets"/> = 받을 clientId(null = 전원). 복제 상태가 아니라 늦은 클라는 받지 않는다.
    /// 강조·목표 패널은 여기로 보내지 않는다 — 흐름 파일이 클라에서 복제 상태를 보고 직접 건다.
    /// </summary>
    public void ShowSubtitleServer(string id, string[] args = null, ulong[] targets = null)
    {
        if (!RequireServerActive(nameof(ShowSubtitleServer)) || string.IsNullOrEmpty(id)) return;
        var payload = new SubtitleArgs { Values = args ?? Array.Empty<string>() };
        if (targets == null)
            ShowSubtitleClientRpc(id, payload);
        else if (targets.Length > 0)
            ShowSubtitleClientRpc(id, payload, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } });
    }

    [ClientRpc]
    private void ShowSubtitleClientRpc(string id, SubtitleArgs args, ClientRpcParams p = default)
    {
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null)
        {
            Edit.LogWarning($"[Tutorial] 자막 {id} — TutorialGuideUI 가 없어 생략.", this);
            return;
        }

        Dictionary<string, string> dict = null;
        string[] v = args.Values;
        if (v != null && v.Length >= 2)
        {
            dict = new Dictionary<string, string>();
            for (int i = 0; i + 1 < v.Length; i += 2)
                if (!string.IsNullOrEmpty(v[i])) dict[v[i]] = v[i + 1];
        }
        ui.ShowSubtitle(id, dict);
    }

    /// <summary>자막 인자 배열 — RPC 배열 직렬화 지원에 기대지 않고 길이 + 문자열로 직접 쓴다.</summary>
    private struct SubtitleArgs : INetworkSerializable
    {
        public string[] Values;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            int n = Values?.Length ?? 0;
            s.SerializeValue(ref n);
            if (s.IsReader) Values = new string[n];
            for (int i = 0; i < n; i++)
            {
                string value = Values[i] ?? string.Empty;
                s.SerializeValue(ref value);
                if (s.IsReader) Values[i] = value;
            }
        }
    }

    private bool RequireServerActive(string api)
    {
        if (!IsServer || !IsSpawned)
        {
            Edit.LogWarning($"[Tutorial] {api}: 서버에서 스폰된 뒤에만 쓸 수 있다(서버:{IsServer} 스폰:{IsSpawned}).", this);
            return false;
        }
        if (_model == null)
        {
            // 튜토리얼이 아닌 스테이지에서는 정상 — 소비처가 먼저 TutorialStageMarker.IsActive 로 거르는 것이 원칙.
            Edit.Log($"[Tutorial] {api}: 튜토리얼 비활성 — 무시.", this);
            return false;
        }
        return true;
    }

    // ─── 오너 → 서버 보고(초안 — S7 에서 사용) ───────────────────────────────

    /// <summary>
    /// 로컬(오너) 클라가 자기 체크를 보고한다 — 서버가 볼 수 없는 것만(Move: 이동은 오너 권한, PassiveTooltip: 클라 UI).
    /// 이미 완료면 보내지 않는다.
    /// </summary>
    public void ReportLocalCheck(TutorialCheck check)
    {
        if (!IsSpawned || !_active.Value) return;
        if (!IsOwnerReportable(check))
        {
            Edit.LogWarning($"[Tutorial] {check} 는 서버가 직접 관측하는 항목이다 — 오너 보고 경로를 쓰지 않는다.", this);
            return;
        }
        if (TryGetPlayerProgress(NetworkManager.LocalClientId, out TutorialPlayerProgress p) && p.Has(check)) return;
        ReportOwnerCheckServerRpc(check);
    }

    public static bool IsOwnerReportable(TutorialCheck check) =>
        check == TutorialCheck.Move || check == TutorialCheck.PassiveTooltip;

    [ServerRpc(RequireOwnership = false)]
    private void ReportOwnerCheckServerRpc(TutorialCheck check, ServerRpcParams p = default)
    {
        ulong sender = p.Receive.SenderClientId;
        if (_model == null) return;

        // 서버 재검증 — 클라 검사만 믿지 않는다.
        if (!IsOwnerReportable(check))
        {
            Edit.LogWarning($"[Tutorial] 서버 거부: client {sender} 가 오너 보고 불가 항목 {check} 를 보냈다.", this);
            return;
        }
        // 존1 항목이라 G1 이 열린 뒤 온 보고는 늦은 것이다(게이트에 영향 없음).
        if (_model.IsGateOpen(TutorialGate.G1_Corridor1Entry))
        {
            Edit.Log($"[Tutorial] 서버 무시: client {sender} {check} — G1 이미 열림.", this);
            return;
        }
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) || client.PlayerObject == null)
        {
            Edit.LogWarning($"[Tutorial] 서버 거부: client {sender} 의 PlayerObject 가 없다.", this);
            return;
        }
        var life = client.PlayerObject.GetComponent<PlayerLifeCycleController>();
        if (check == TutorialCheck.Move && life != null && life.State != PlayerLifeState.Alive)
        {
            Edit.Log($"[Tutorial] 서버 무시: client {sender} 이동 보고 — 상태 {life.State}(Alive 만).", this);
            return;
        }
        // S7: 이동은 거리가 실린 보고(ReportOwnerMoveServerRpc)로만 받는다 — 여기엔 상한을 검증할 값이 없다.
        if (check == TutorialCheck.Move)
        {
            Edit.LogWarning($"[Tutorial] 서버 거부: client {sender} 이동 보고에 거리가 없다 — ReportLocalMoveDistance 를 쓴다.", this);
            return;
        }

        if (_model.ReportCheck(sender, check)) Commit();
    }

    /// <summary>
    /// [오너 클라] 존1 이동 누적 거리(m)를 보고한다(S7 Z1-1). 서버가 상한을 검증한다
    /// (<see cref="TutorialZone1Rules.IsMoveReportPlausible"/> — 목표 이상 · 튜토리얼 시작 뒤 최대 속도 × 경과 + 여유 이하).
    /// 이미 완료면 보내지 않는다. 거부되면 체크가 안 켜지니 오너가 나중에 다시 보내도 된다.
    /// </summary>
    public void ReportLocalMoveDistance(float meters)
    {
        if (!IsSpawned || !_active.Value) return;
        if (TryGetPlayerProgress(NetworkManager.LocalClientId, out TutorialPlayerProgress p) && p.Has(TutorialCheck.Move)) return;
        ReportOwnerMoveServerRpc(meters);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportOwnerMoveServerRpc(float meters, ServerRpcParams p = default)
    {
        ulong sender = p.Receive.SenderClientId;
        if (_model == null) return;

        if (_model.IsGateOpen(TutorialGate.G1_Corridor1Entry))
        {
            Edit.Log($"[Tutorial] 서버 무시: client {sender} 이동 {meters:0.0}m — G1 이미 열림.", this);
            return;
        }
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) || client.PlayerObject == null)
        {
            Edit.LogWarning($"[Tutorial] 서버 거부: client {sender} 의 PlayerObject 가 없다.", this);
            return;
        }
        var life = client.PlayerObject.GetComponent<PlayerLifeCycleController>();
        if (life != null && life.State != PlayerLifeState.Alive)
        {
            Edit.Log($"[Tutorial] 서버 무시: client {sender} 이동 보고 — 상태 {life.State}(Alive 만).", this);
            return;
        }

        double elapsed = NetworkManager.ServerTime.Time - _activatedServerTime;
        if (!TutorialZone1Rules.IsMoveReportPlausible(meters, elapsed))
        {
            Edit.LogWarning($"[Tutorial] 서버 거부: client {sender} 이동 {meters:0.0}m — 시작 뒤 {elapsed:0.0}초로는 불가능하거나 목표 미만.", this);
            return;
        }

        if (_model.ReportCheck(sender, TutorialCheck.Move)) Commit();
    }

    // ─── 모델 → 복제 ──────────────────────────────────────────────────────

    /// <summary>재판정 후 바뀐 값만 복제 변수에 쓴다(서버).</summary>
    private void Commit()
    {
        if (_model == null || !IsServer || !IsSpawned) return;

        _model.Evaluate(NetworkManager.ServerTime.Time);

        if (_step.Value != _model.Step) _step.Value = _model.Step;
        if (_timeoutGates.Value != _model.TimeoutGateMask) _timeoutGates.Value = _model.TimeoutGateMask;
        if (_openedGates.Value != _model.OpenedGateMask) _openedGates.Value = _model.OpenedGateMask;

        for (int i = 0; i < TutorialFlowModel.CounterCount && i < _counters.Count; i++)
        {
            int v = _model.GetCounter((TutorialCounter)i);
            if (_counters[i] != v) _counters[i] = v;
        }

        // 등록 순서가 고정(추가만)이라 인덱스로 맞춘다.
        for (int i = 0; i < _model.PlayerIds.Count; i++)
        {
            _model.TryGetPlayer(_model.PlayerIds[i], out TutorialPlayerProgress m);
            var s = new PlayerState
            {
                ClientId = m.ClientId, CheckMask = m.CheckMask, AttackHits = (byte)Math.Min(255, m.AttackHits),
                Connected = m.Connected, PermanentDead = m.PermanentDead,
            };
            if (i < _players.Count) { if (!_players[i].Equals(s)) _players[i] = s; }
            else _players.Add(s);
        }

        var w = new WaitState { Gate = NoGate, AutoAdvanceAtServerTime = -1d };
        if (_model.TryGetPendingPersonalGate(out TutorialGate g, out int done, out int total, out double autoAt))
            w = new WaitState { Gate = (byte)g, Done = (byte)done, Total = (byte)total, AutoAdvanceAtServerTime = autoAt };
        if (!_wait.Value.Equals(w)) _wait.Value = w;
    }

    private void HandleModelGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        Edit.Log($"[Tutorial] 게이트 {gate} 열림 — {reason}.", this);
        if (gate == TutorialGate.G8_TimerStart) OnTimerGateOpened();
        // G10(패드·엘리베이터 문)은 각 피어가 IsGateOpen 으로 본다 — TutorialBossPath·TutorialElevatorDoor.
        // TODO(S6): G10 열림 → TUT_B_PAD_WARN 파티 전원(D17).
        GateOpened?.Invoke(gate, reason);
    }

    private void HandleModelStepChanged(TutorialStep from, TutorialStep to)
    {
        Edit.Log($"[Tutorial] 단계 {from} → {to}.", this);
        StepChanged?.Invoke(from, to);
    }

    // ─── 복제 반영(클라 이벤트) ───────────────────────────────────────────
    // 서버는 모델 이벤트로 이미 알렸으니 GateOpened·StepChanged 는 클라에서만 낸다. StateChanged 는 양쪽.

    private void HandleActiveChanged(bool _, bool __) => StateChanged?.Invoke();
    private void HandleBoolChanged(bool _, bool __) => StateChanged?.Invoke();
    private void HandleIntChanged(int _, int __) => StateChanged?.Invoke();
    private void HandleWaitChanged(WaitState _, WaitState __) => StateChanged?.Invoke();
    private void HandlePlayersChanged(NetworkListEvent<PlayerState> _) => StateChanged?.Invoke();
    private void HandleCountersChanged(NetworkListEvent<int> _) => StateChanged?.Invoke();

    private void HandleStepChanged(TutorialStep from, TutorialStep to)
    {
        if (!IsServer) StepChanged?.Invoke(from, to);
        StateChanged?.Invoke();
    }

    private void HandleOpenedGatesChanged(int from, int to)
    {
        if (!IsServer)
        {
            int newly = to & ~from;
            for (int i = 0; i < TutorialFlowModel.GateCount; i++)
            {
                if ((newly & (1 << i)) == 0) continue;
                var reason = (_timeoutGates.Value & (1 << i)) != 0 ? TutorialGateOpenReason.Timeout : TutorialGateOpenReason.Manual;
                GateOpened?.Invoke((TutorialGate)i, reason);
            }
        }
        StateChanged?.Invoke();
    }
}
