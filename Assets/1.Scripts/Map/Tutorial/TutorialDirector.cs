using System;
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
///  · S2 구간 감지 → <see cref="SetStepServer"/> · 존 첫 진입 이벤트(별도 컴포넌트에서 이 API 호출)
///  · S3 배리어 → <see cref="GateOpened"/> 구독 + 늦은 클라는 <see cref="IsGateOpen"/> 으로 재적용
///  · S4 존 장부 → 존2 전멸 <see cref="TryOpenGateServer"/>(G3), 처치 수 <see cref="AddCounterServer"/>(Zone2Kills)
///  · S5 타이머 → G8 열림에서 타이머 시작, 판별은 <see cref="TutorialStageMarker.IsActive"/>
///  · S7 존1 → <see cref="ReportCheckServer"/>(Dash·AttackHit — 서버 관측) / <see cref="ReportLocalCheck"/>(Move·PassiveTooltip — 오너 보고)
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
    [SerializeField, Min(1)] private int zone2KillTarget = 9;
    [SerializeField, Min(1)] private int zone3InterruptTarget = 3;
    [SerializeField, Min(1)] private int zone4PanelTarget = 4;

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

    // 서버 전용
    private TutorialFlowModel _model;
    private bool _pendingActivate;
    private NetworkManager _subscribedNm;

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

    public int GetCounterTarget(TutorialCounter c) => c switch
    {
        TutorialCounter.Zone2Kills => zone2KillTarget,
        TutorialCounter.Zone3Interrupts => zone3InterruptTarget,
        _ => zone4PanelTarget,
    };

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

        _model = new TutorialFlowModel(deadlockSeconds, zone2KillTarget, zone3InterruptTarget, zone4PanelTarget);
        _model.GateOpened += HandleModelGateOpened;
        _model.StepChanged += HandleModelStepChanged;

        _subscribedNm = NetworkManager;
        foreach (ulong id in _subscribedNm.ConnectedClientsIds)
            AddPlayer(id);
        _subscribedNm.OnClientConnectedCallback += HandleClientConnected;
        _subscribedNm.OnClientDisconnectCallback += HandleClientDisconnected;
        PlayerLifeCycleController.ServerLifeStateChanged += HandleServerLifeStateChanged;

        _counters.Clear();
        for (int i = 0; i < TutorialFlowModel.CounterCount; i++) _counters.Add(0);

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
        if (_model != null)
        {
            _model.GateOpened -= HandleModelGateOpened;
            _model.StepChanged -= HandleModelStepChanged;
        }
    }

    private void Update()
    {
        if (_model == null || !IsServer || !IsSpawned) return;
        Commit();   // 교착 방지 시계 — 매 프레임 재판정(바뀐 값만 복제)
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
        // TODO(S7): 이동 거리 타당성(서버가 본 위치 변화) 등 추가 검증.

        if (_model.ReportCheck(sender, check)) Commit();
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
