using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 튜토리얼 진행기(할 일 86 → 할 일 157 커밋 ②, 3.LobbyScene 씬 상주 NetworkObject, 서버 권한).
/// 규칙은 <see cref="LobbyTutorialFlow"/>(행 번호 전이·revision·명단·라커 방문)·<see cref="LobbyTutorialRules"/>, 단계 정의는 단계표 SO(<see cref="LobbyRules.StepTable"/>).
/// 여기는 NGO 복제·집결 구역 켜고 끄기·확정 구독·임시 자동 통과·로그만 한다.
/// <list type="bullet">
/// <item>복제: 스냅샷 1개(<see cref="LobbyTutorialSnapshot"/> NetworkVariable — runId·revision·행 번호·시각·출격 확정) + 훈련 명단·클리어 플래그 목록.
/// 모든 피어가 스냅샷이 바뀔 때마다 <see cref="LobbyTutorialState.Publish"/>(158 파사드)와 <see cref="StepChanged"/> 를 낸다. 옛 stage/phase NetworkVariable 2개는 없앴다.</item>
/// <item>구역: 단계가 바뀔 때마다 지금 행의 집결 지점만 켜고 명단·유지 시간(행 holdSeconds)을 넣는다. 확정은 <see cref="PartyGatherZone.ServerConfirmed"/>.
/// 출격 = 접속자 전원(자동), 그 밖 = 진행기 참여자(명단 고정 전 = 클리어 플래그 끈 접속자, 뒤 = 고정 명단).</item>
/// <item>명단 고정 = 첫 집결 행(L10 중앙) 확정. 그 뒤 접속 = 로비 대기(늦은 참가자, 출격 집결 인원엔 포함).</item>
/// <item>단계 완료: 내용 컴포넌트가 <see cref="ServerReportActivityCompleted"/>(Activity, 시작 때 받은 revision), 브리핑은 <see cref="ServerReportBriefingCompleted"/>.
/// 지금 행·revision 이 아니면 무시. 한 프레임 한 전이(같은 프레임 두 번째는 모델이 다음 프레임으로 미룬다).</item>
/// <item>할 일 157 ② 임시 처리(내용 커밋 전까지 흐름이 막히지 않게): L00 라커 방문 = 캐릭터 선택 상호작용 반경 도착(라커 볼륨은 ③, 선택 오브젝트가 없으면 즉시 통과),
/// 대사 행(L11·L21) = 행 dialogueSeconds(지금 0) 뒤 자동 통과(대사 스케줄러는 ③), 브리핑(L31~L34) = 옛 <see cref="LobbyBriefing"/> 가 L31 에서 한 번에 돌고 L40 으로.</item>
/// <item>집결 확정 텔레포트(할 일 111 유지, 157 Q4 B·§10.3 안 A): L20 입구 확정 → 명단 전원 훈련장 안(Train01Start, 바로 L21),
/// T50 종료 확정 → 로비 중앙(Brief, L30 이 바로 센다). <see cref="LobbyTrainingEndPlacement"/> 자리로 <see cref="PlayerFallRecovery.RelocateServer"/>.</item>
/// <item>출격 확정(L40) = L41 로딩 + 출격 래치(<see cref="LobbyTutorialFlow.TryCommitLaunch"/>) → <see cref="NetworkSessionLauncher.StartGameLoading"/> 정확히 1회
/// (같은 오브젝트에 <see cref="LobbyPodLaunch"/> 가 있으면 포드 연출 끝에). 로딩 흐름의 로비 시작 조건 = <see cref="ServerHasLaunched"/>.
/// 시연 플래그 <see cref="LobbyRules.AllowInstantSortie"/>(기본 꺼짐)면 명단 고정 전에도 출격 구역이 켜지고 확정하면 훈련을 건너뛴다.</item>
/// <item>접속 승인: <see cref="NetworkSessionLauncher.LobbyCountdownActiveProvider"/> = 출격 집결 카운트다운 중 또는 출격 확정 뒤.</item>
/// <item>캐릭터 잠금: <see cref="CharacterChangeBlockFor"/> — 고정 명단 안이면 TrainingLocked(②는 옛 동작 유지, 해제는 커밋 ③ Q1).</item>
/// <item>훈련 보정: 행 <see cref="LobbyTutorialStep.trainingProfile"/> = Training 동안 고정 명단 체력 하한 1 — 단계 전이·0.25초 주기·캐릭터 교체 완료
/// (<see cref="LobbyPlayerSpawner.ServerCharacterSwapped"/>) 때 다시 맞추고 진행기 디스폰 때 0.</item>
/// <item>훈련 리셋(공용): <see cref="ServerResetTrainingRoster"/> — 명단 전원 <see cref="TrainingPlayerReset"/> + 진행 중 행동 예약 + 오너 대시 예측 짝(복제 일련번호).</item>
/// <item>튜토리얼 클리어 플래그(할 일 112, 로비 Alt+1): 켠 사람은 참여자·명단에서 빠진다. 훈련할 사람이 아무도 없으면 출격 집결(L40)로 건너뛴다.
/// 서버 세션 정적 장부로 회차를 넘겨 잇고 연결 종료·호스트 셧다운 때 지운다. 훈련 완료 자동 부여는 없앴다(할 일 157 Q3 — 매 회차 L00 부터).
/// 요청 RPC·멈춤 탈출·리로드는 DEV_HOTKEYS 게이트 partial(<c>LobbyTrainingDirector.Recovery.cs</c>).</item>
/// <item>판 끝 로비 복귀: 로비 씬이 다시 올라오면 새 진행기 = 새 회차(runId +1, L00 부터).</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed partial class LobbyTrainingDirector : NetworkBehaviour, ILobbyTutorialLiveSource
{
    private const string Tag = "[LobbyTraining]";

    [Tooltip("단계표(stepTable)·시연 즉시 출격 플래그를 읽는다. 비면 진행기가 멈춘다.")]
    [SerializeField] private LobbyRules rules;

    [Header("집결 구역")]
    [Tooltip("훈련장 입구(훈련실 문 앞 복도) — L20 집결. 확정 = 명단 전원 훈련장 안(Train01Start)으로 텔레포트.")]
    [SerializeField] private PartyGatherZone entranceZone;

    [Tooltip("출격(SortieStation 패드 위 — 임시 위치, 최종 배치는 사용자). L40.")]
    [SerializeField] private PartyGatherZone sortieZone;

    [Tooltip("훈련장 안 시작 위치 — L20 텔레포트 도착지.")]
    [SerializeField] private PartyGatherZone train01StartZone;
    [SerializeField] private PartyGatherZone train02StartZone;

    [Tooltip("세 번째 훈련 영역 — T30 집결.")]
    [SerializeField] private PartyGatherZone train03AreaZone;

    [Tooltip("훈련 종료 집결(훈련실 출구) — T50.")]
    [SerializeField] private PartyGatherZone trainEndZone;

    [Tooltip("로비 중앙 — L10·L30 집결.")]
    [SerializeField] private PartyGatherZone briefZone;

    // 🔴 NetworkVariable 구성은 빌드마다 같아야 한다(이 오브젝트 동기화) — 릴리스·DEV_HOTKEYS 빌드 모두 같은 칸.
    private readonly NetworkVariable<LobbyTutorialSnapshot> snapshot = new NetworkVariable<LobbyTutorialSnapshot>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 훈련 명단 복제 — 클라가 자기 캐릭터 잠금(명단 안 = 잠김, 늦은 참가자 = 허용)·늦은 참가자 여부를 본다.
    private readonly NetworkList<ulong> trainingRoster = new NetworkList<ulong>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 튜토리얼 클리어 플래그 복제 — 🔴 릴리스(DEV_HOTKEYS 없음)에도 둔다.
    private readonly NetworkList<ulong> trainingCleared = new NetworkList<ulong>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // [서버] 클리어 플래그 세션 장부 — 진행기는 로비 씬과 함께 새로 생기므로(판 끝 복귀·리로드) 플래그를 여기 남겨 다음 진행기에 넘긴다.
    private static readonly List<ulong> ServerClearedStore = new List<ulong>();

    // [서버] 회차 번호(서버 세션) — 진행기 스폰마다 +1. 0 은 '시작 전' 예약값.
    private static int s_runCounter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ServerClearedStore.Clear();
        s_runCounter = 0;
    }

    // 훈련 리셋 일련번호 — 오너가 바뀌는 것을 보고 대시 예측 충전을 서버 장부와 맞춘다(복도1 쿨 초기화와 같은 방식).
    private readonly NetworkVariable<int> trainingResetSerial = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private const float SupportTickInterval = 0.25f;

    // 집결 확정 텔레포트 높이 보정 — 스폰 지점·멈춤 탈출과 같은 값(바닥에 정확히 놓으면 콜라이더가 파고든 채 시작한다).
    private const float TeleportClearance = 0.5f;

    private static readonly LobbyGatherPoint[] Points =
    {
        LobbyGatherPoint.Entrance, LobbyGatherPoint.Sortie, LobbyGatherPoint.Train01Start, LobbyGatherPoint.Train02Start,
        LobbyGatherPoint.Train03Area, LobbyGatherPoint.TrainEnd, LobbyGatherPoint.Brief,
    };

    // 서버 전용
    private LobbyTutorialFlow _flow;
    private bool _zonesDirty;
    private bool _loggedWaitingZones;
    private int _timerRevision;
    private readonly HashSet<LobbyGatherPoint> _warnedNoTeleportZone = new HashSet<LobbyGatherPoint>();
    private Func<bool> _joinBlockedProvider;
    private readonly TrainingResetReservations _resetReservations = new TrainingResetReservations();
    private readonly List<ulong> _idBuffer = new List<ulong>();
    private readonly List<ulong> _participantBuffer = new List<ulong>();
    private float _supportTimer;

    // 전 피어(파사드)
    private bool _hasPublished;
    private int _publishedRunId;
    private int _publishedRevision;
    private int _progressKey = int.MinValue;

    /// <summary>스폰된 진행기(없으면 null).</summary>
    public static LobbyTrainingDirector Active { get; private set; }

    /// <summary>[전 피어] 단계(스냅샷 revision)가 바뀌었다(스폰 직후 1회 포함) — 목표 표시·카메라·핫키 부착이 붙는다. 값은 <see cref="CurrentView"/> 등에서 읽는다.</summary>
    public static event Action StepChanged;

    /// <summary>[서버] 단계가 바뀌었다(전이 확정 뒤 1회, 스폰 직후 1회) — 단계 내용(표적·몹·상대·브리핑) 시작·정리용.</summary>
    public event Action ServerFlowChanged;

    /// <summary>[서버] 클리어 플래그로 이 사람이 훈련 명단에서 빠졌다(연결 종료는 아니다 — 그건 각자 OnClientDisconnectCallback).</summary>
    public event Action<ulong> ServerRosterMemberRemoved;

    // ─── 읽기(전 피어 — 복제 스냅샷 + 단계표) ─────────────────────────────

    /// <summary>[전 피어] 단계표(LobbyRules.stepTable). 없으면 null.</summary>
    public LobbyTutorialStepTable Table => rules != null ? rules.StepTable : null;

    /// <summary>[전 피어] 복제 스냅샷. 스폰 전이면 default(RunId 0).</summary>
    public LobbyTutorialSnapshot Snapshot => IsSpawned ? snapshot.Value : default;

    public int RunId => Snapshot.RunId;
    public int Revision => Snapshot.Revision;
    public int CurrentStepIndex => Snapshot.StepIndex;

    /// <summary>[전 피어] 지금 행(없으면 null — 스폰 전·단계표 없음).</summary>
    public LobbyTutorialStep CurrentStep
    {
        get
        {
            var table = Table;
            return IsSpawned && Snapshot.IsStarted && table != null && table.TryGet(Snapshot.StepIndex, out var step) ? step : null;
        }
    }

    /// <summary>[전 피어] 지금 행 보기(없으면 IsValid false).</summary>
    public LobbyTutorialStepView CurrentView
    {
        get
        {
            var step = CurrentStep;
            return step != null ? new LobbyTutorialStepView(Snapshot.StepIndex, step) : default;
        }
    }

    /// <summary>[전 피어] 지금 일하는 훈련 내용(없으면 None).</summary>
    public LobbyTutorialActivity CurrentActivity => CurrentStep?.activity ?? LobbyTutorialActivity.None;

    /// <summary>[전 피어] 지금 켜진 행 집결 지점(집결 행이 아니면 None).</summary>
    public LobbyGatherPoint ActiveGatherPoint => CurrentView.GatherPointId;

    /// <summary>[전 피어] 명단 고정(첫 집결 행 확정) 뒤인가.</summary>
    public bool IsRosterLocked
    {
        get
        {
            var table = Table;
            var lockIndex = table != null ? LobbyTutorialRules.RosterLockIndex(table.Steps) : -1;
            return IsSpawned && Snapshot.IsStarted && lockIndex >= 0 && Snapshot.StepIndex > lockIndex;
        }
    }

    /// <summary>[전 피어] 출격 확정(메인 진입 시작) 또는 로딩 행.</summary>
    public bool IsLaunching
    {
        get
        {
            if (!IsSpawned)
                return false;
            var step = CurrentStep;
            return Snapshot.LaunchCommitted || (step != null && step.kind == LobbyTutorialStepKind.Loading);
        }
    }

    /// <summary>[전 피어] 출격 집결 구역이 켜져 있어야 하는 단계(L40, 또는 시연 즉시 출격 플래그로 명단 고정 전).</summary>
    public bool IsSortieGatherOpen =>
        !IsLaunching && (ActiveGatherPoint == LobbyGatherPoint.Sortie
                         || (rules != null && rules.AllowInstantSortie && IsSpawned && Snapshot.IsStarted && !IsRosterLocked));

    /// <summary>[전 피어] 이 사람이 훈련 명단(L10 확정 때 고정)에 있는가 — 복제 명단.</summary>
    public bool IsInTrainingRoster(ulong clientId) => IsSpawned && trainingRoster.Contains(clientId);

    /// <summary>[전 피어] 튜토리얼 클리어 플래그가 켜져 있는가 — 복제 목록.</summary>
    public bool IsTrainingCleared(ulong clientId) => IsSpawned && trainingCleared.Contains(clientId);

    /// <summary>[전 피어] 출격 집결 카운트다운 중 — 접속 승인·캐릭터 잠금이 본다(입력은 막지 않는다).</summary>
    public bool IsSortieCountdownActive => sortieZone != null && sortieZone.IsCountdownActive;

    /// <summary>[서버] 출격 확정으로 메인 진입을 내보냈다 — NetworkLoadingFlowController 의 로비 시작 조건.</summary>
    public bool ServerHasLaunched => _flow != null && _flow.LaunchCommitted;

    /// <summary>[전 피어] 이 사람의 캐릭터 변경을 막는 진행기 사유(없으면 None) — 서버 RPC 검증·클라 패널 잠금 공용.</summary>
    public LobbyCharacterChangeReject CharacterChangeBlockFor(ulong clientId)
    {
        return LobbyTutorialRules.CharacterChangeBlock(IsLaunching, IsRosterLocked && IsInTrainingRoster(clientId), IsSortieCountdownActive);
    }

    /// <summary>[서버] 훈련 참여 명단(L10 확정 때 고정). 서버가 아니면 빈 목록.</summary>
    public IReadOnlyList<ulong> ServerTrainingRoster => _flow != null ? _flow.Roster : Array.Empty<ulong>();

    /// <summary>[서버] 명단 고정 뒤 접속해 로비에서 기다리는 사람인가.</summary>
    public bool ServerIsWaitingLateJoiner(ulong clientId) => _flow != null && _flow.IsWaitingLateJoiner(clientId);

    /// <summary>[전 피어] 로그용 현재 단계 표기.</summary>
    public string DescribeStep()
    {
        var step = CurrentStep;
        return step != null ? $"{step.stateId}(#{Snapshot.StepIndex} rev {Snapshot.Revision})" : "(없음)";
    }

    // ─── 158 파사드 실시간 값(ILobbyTutorialLiveSource, 전 피어) ─────────

    bool ILobbyTutorialLiveSource.IsLocalWaitingLateJoiner
    {
        get
        {
            var networkManager = NetworkManager;
            return networkManager != null && IsRosterLocked && !IsInTrainingRoster(networkManager.LocalClientId)
                   && !LobbyTutorialRules.UsesConnectedClients(ActiveGatherPoint);
        }
    }

    bool ILobbyTutorialLiveSource.TryGetLocalChecklistMask(out int mask)
    {
        mask = 0;
        var check = LobbyTrainingWeaponCheck.Active;
        return check != null && check.IsRunning && NetworkManager != null && check.TryGetMask(NetworkManager.LocalClientId, out mask);
    }

    bool ILobbyTutorialLiveSource.TryGetPartyComplete(out int done, out int total)
    {
        done = 0;
        total = 0;
        var check = LobbyTrainingWeaponCheck.Active;
        if (check == null || !check.IsRunning)
            return false;

        check.GetPartyProgress(out done, out total);
        return true;
    }

    TrainingSparringBot ILobbyTutorialLiveSource.TrainingTarget => TrainingSparringBot.Current;

    // ─── 수명 ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Active = this;
        snapshot.OnValueChanged += HandleSnapshotChanged;
        trainingResetSerial.OnValueChanged += HandleTrainingResetSerialChanged;
        LobbyTutorialState.SetLiveSource(this);

        if (IsServer)
            SpawnServer();

        PublishLocal();
    }

    private void SpawnServer()
    {
        var table = Table;
        if (table == null || table.Count == 0)
        {
            Debug.LogError($"{Tag} 단계표가 없다(LobbyRules.stepTable) — 로비 튜토리얼이 시작되지 않는다(출격도 불가).", this);
            return;
        }

        var errors = new List<string>();
        if (!table.Validate(errors))
            Debug.LogError($"{Tag} 단계표 {table.name} 검증 실패 — {string.Join(" / ", errors)}", this);

        s_runCounter = s_runCounter >= int.MaxValue - 1 ? 1 : s_runCounter + 1;
        _flow = new LobbyTutorialFlow(table.Kinds(), s_runCounter, ServerNow());
        foreach (var clientId in NetworkManager.ConnectedClientsIds)
            _flow.AddClient(clientId);

        // 지난 진행기(판 끝 로비 복귀·리로드 전)가 남긴 클리어 플래그 — 아직 접속해 있는 사람만 잇는다(명단 고정 전이라 명단 변화 없음).
        foreach (var clientId in ServerClearedStore)
            _flow.SetTrainingCleared(clientId, true, out _);
        ServerClearedStore.Clear();
        ServerClearedStore.AddRange(_flow.TrainingCleared);

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        LobbyPlayerSpawner.ServerCharacterSwapped += HandleCharacterSwapped;
        ForEachZone((point, zone) => zone.ServerConfirmed += HandleZoneConfirmed);
        _joinBlockedProvider = () => _flow != null && LobbyTutorialRules.BlocksNewJoins(ServerIsLaunching(), IsSortieCountdownActive);
        NetworkSessionLauncher.LobbyCountdownActiveProvider = _joinBlockedProvider;
        WarnMissingZones();
        SyncReplicatedState();
        _zonesDirty = true;
        Debug.Log($"[SceneFlow] {Tag} 서버 스폰 — 새 회차 run {_flow.RunId}, {ServerDescribe()}, 단계표 {table.name} {table.Count}행, 접속자 {_flow.Connected.Count}명" +
                  (_flow.TrainingCleared.Count > 0 ? $", 튜토리얼 클리어 [{string.Join(",", _flow.TrainingCleared)}]" : "") +
                  (rules != null && rules.AllowInstantSortie ? ", 시연 즉시 출격 켜짐" : ""));
        ServerFlowChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        snapshot.OnValueChanged -= HandleSnapshotChanged;
        trainingResetSerial.OnValueChanged -= HandleTrainingResetSerialChanged;

        if (IsServer)
        {
            // 판 끝·씬 전환 — 훈련 보정이 남지 않게 접속자 전원 하한 해제.
            ClearHealthFloors();
            _resetReservations.Clear();

            // 호스트 셧다운 = 세션 끝 — 클리어 플래그도 버린다(씬 전환 디스폰이면 다음 진행기가 잇는다).
            if (NetworkManager == null || NetworkManager.ShutdownInProgress || !NetworkManager.IsListening)
                ServerClearedStore.Clear();

            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            LobbyPlayerSpawner.ServerCharacterSwapped -= HandleCharacterSwapped;
            ForEachZone((point, zone) => zone.ServerConfirmed -= HandleZoneConfirmed);
            if (NetworkSessionLauncher.LobbyCountdownActiveProvider == _joinBlockedProvider)
                NetworkSessionLauncher.LobbyCountdownActiveProvider = null;
            _joinBlockedProvider = null;
            _flow = null;
        }

        if (Active == this)
        {
            Active = null;
            LobbyTutorialState.Clear();
        }

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Active == this)
        {
            Active = null;
            LobbyTutorialState.Clear();
        }

        base.OnDestroy();
    }

    // ─── 단계 완료 보고(서버) ─────────────────────────────────────────────

    /// <summary>
    /// [서버] 훈련 내용 완료 보고 — 내용 컴포넌트가 시작 때 받은 <paramref name="revision"/> 을 싣는다. 지금 행이 그 Activity 의 Activity 행이고
    /// revision 이 같을 때만 다음 행으로(true). 다른 단계·지난 revision·중복 보고는 무시하고 false(경고 로그). 집결 행(T50)은 집결로만 끝난다.
    /// </summary>
    public bool ServerReportActivityCompleted(LobbyTutorialActivity completed, int revision)
    {
        if (!IsServer || _flow == null)
        {
            Debug.LogWarning($"{Tag} 단계 완료 보고 {completed} 무시 — 서버가 아니거나 스폰 전.", this);
            return false;
        }

        var step = ServerStep();
        if (step == null || step.kind != LobbyTutorialStepKind.Activity || step.activity != completed || revision != _flow.Revision)
        {
            Debug.LogWarning($"{Tag} 단계 완료 보고 {completed} rev {revision} 무시 — 현재 {ServerDescribe()}.", this);
            return false;
        }

        return ServerAdvance(_flow.StepIndex + 1, $"{completed} 완료 보고");
    }

    /// <summary>
    /// [서버] 브리핑(<see cref="LobbyBriefing"/>) 끝 — 지금 행이 입력 잠금 대사 행이고 revision 이 같을 때만 잠금 묶음(L31~L34) 뒤 행(L40)으로(true).
    /// </summary>
    public bool ServerReportBriefingCompleted(int revision)
    {
        if (!IsServer || _flow == null)
            return false;

        var step = ServerStep();
        if (step == null || step.kind != LobbyTutorialStepKind.Dialogue || !step.locksInput || revision != _flow.Revision)
        {
            Debug.LogWarning($"{Tag} 브리핑 완료 보고 rev {revision} 무시 — 현재 {ServerDescribe()}.", this);
            return false;
        }

        return ServerAdvance(LobbyTutorialRules.IndexAfterLockedBlock(Table.Steps, _flow.StepIndex), "브리핑 완료");
    }

    /// <summary>
    /// [서버] 튜토리얼 클리어 플래그를 켜고 끈다(<see cref="LobbyTutorialFlow.SetTrainingCleared"/>). 바뀌었으면 true.
    /// 켜서 명단에서 빠지면 구역 명단·체력 하한을 다시 맞추고 <see cref="ServerRosterMemberRemoved"/>, 훈련할 사람이 없어 출격 집결로 넘어가면 <paramref name="skippedToSortie"/>.
    /// </summary>
    public bool ServerSetTrainingCleared(ulong clientId, bool cleared, out bool skippedToSortie)
    {
        skippedToSortie = false;
        if (!IsServer || _flow == null)
            return false;

        var before = ServerDescribe();
        if (!_flow.SetTrainingCleared(clientId, cleared, out var leftRoster))
            return false;

        ServerClearedStore.Clear();
        ServerClearedStore.AddRange(_flow.TrainingCleared);
        skippedToSortie = cleared && TrySkipToSortieWhenNobodyTrains();
        Debug.Log($"[SceneFlow] {Tag} 튜토리얼 클리어 client {clientId} → {(cleared ? "켜짐" : "꺼짐")} — 훈련 명단 {(leftRoster ? "에서 뺌" : "그대로")} " +
                  $"[{string.Join(",", _flow.Roster)}], 클리어 [{string.Join(",", _flow.TrainingCleared)}] ({before}{(skippedToSortie ? $" → {ServerDescribe()} 훈련 인원 없음" : "")})");

        _zonesDirty = true;
        SyncReplicatedState();
        SyncHealthFloors();

        if (leftRoster)
            ServerRosterMemberRemoved?.Invoke(clientId);
        return true;
    }

    // ─── 서버 루프 ────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsSpawned)
            return;

        NotifyProgressIfChanged();

        if (!IsServer || _flow == null)
            return;

        // 같은 프레임에 미뤄 둔 전이(한 프레임 한 전이)를 먼저 적용한다.
        if (_flow.Tick(Time.frameCount))
        {
            Debug.Log($"[SceneFlow] {Tag} 미룬 전이 적용 → {ServerDescribe()}");
            OnFlowChanged();
        }

        _supportTimer -= Time.unscaledDeltaTime;
        if (_supportTimer <= 0f)
        {
            _supportTimer = SupportTickInterval;
            // 플레이어 오브젝트가 늦게 붙거나 다시 스폰돼도 하한이 맞게 주기적으로도 맞춘다(같은 값이면 아무 일 없음).
            SyncHealthFloors();
            TickResetReservations();
            TickLockerVisitServer();
        }

        TrySkipToSortieWhenNobodyTrains();
        TickDialogueServer();

        if (!_zonesDirty)
            return;

        // 씬 상주 구역이 이 진행기보다 늦게 스폰될 수 있다 — 전부 스폰된 뒤에 한 번에 넣는다.
        var allSpawned = true;
        ForEachZone((point, zone) => allSpawned &= zone.IsSpawned);
        if (!allSpawned)
        {
            if (!_loggedWaitingZones)
            {
                _loggedWaitingZones = true;
                Debug.Log($"{Tag} 집결 구역 스폰 대기 중 — 전부 스폰되면 {ServerDescribe()} 구역을 켠다.");
            }

            return;
        }

        _zonesDirty = false;
        ApplyZones();
    }

    // 할 일 157 ② 임시 — L00 라커 방문 = 캐릭터 선택 상호작용 반경 도착(서버 위치 판정, 1인 1회 누적). 라커 볼륨 판정은 커밋 ③.
    // 캐릭터 선택 오브젝트가 씬에 없으면 참여자 전원 방문으로 보고 바로 넘긴다(흐름이 막히지 않게).
    private void TickLockerVisitServer()
    {
        if (_flow.CurrentKind != LobbyTutorialStepKind.LockerVisit || _flow.HasPendingAdvance)
            return;

        var hasStation = false;
        var registry = LobbyInteractable.Registry;
        for (var i = 0; i < registry.Count && !hasStation; i++)
            hasStation = registry[i] != null && registry[i].Kind == LobbyInteractableKind.CharacterSelect;

        _participantBuffer.Clear();
        _participantBuffer.AddRange(_flow.Participants);
        foreach (var clientId in _participantBuffer)
        {
            var visited = !hasStation
                || (TryGetPlayer(clientId, out var player)
                    && LobbyInteractable.AnyWithinServerReach(LobbyInteractableKind.CharacterSelect, player.transform.position, out _));
            if (visited && _flow.MarkLockerVisited(clientId))
                Debug.Log($"[SceneFlow] {Tag} 라커 방문(임시 — {(hasStation ? "캐릭터 선택 반경" : "선택 오브젝트 없음, 자동")}) client {clientId} " +
                          $"[{string.Join(",", _flow.LockerVisited)}]");
        }

        if (_flow.AllParticipantsVisitedLocker())
            ServerAdvance(_flow.StepIndex + 1, "전원 라커 방문");
    }

    // 할 일 157 ② 임시 — 대사 행: 행 dialogueSeconds(0 이하 = 0) 뒤 자동 통과. 시각은 스냅샷에 싣는다(158 대사 패널). 길이 규칙·감독관 대사는 커밋 ③.
    // 입력 잠금 행(브리핑)은 LobbyBriefing 이 있으면 그 완료 보고로만 끝난다.
    private void TickDialogueServer()
    {
        if (_flow.CurrentKind != LobbyTutorialStepKind.Dialogue || _flow.HasPendingAdvance)
            return;

        var step = ServerStep();
        if (step == null || (step.locksInput && GetComponent<LobbyBriefing>() != null))
            return;

        var now = ServerNow();
        if (_timerRevision != _flow.Revision)
        {
            _timerRevision = _flow.Revision;
            var seconds = Math.Max(0d, step.dialogueSeconds);
            _flow.SetTimer(_flow.Revision, now, now + seconds);
            SyncReplicatedState();
            Debug.Log($"[SceneFlow] {Tag} 대사 {step.dialogueId} 시작(임시 자동 통과 {seconds:0.##}초) — {ServerDescribe()}");
            return;
        }

        if (now >= _flow.Snapshot.TimerEndServerTime)
            ServerAdvance(_flow.StepIndex + 1, $"대사 {step.dialogueId} 끝");
    }

    // 훈련할 사람이 없다(전원 클리어 플래그·명단 전원 퇴장) — 출격 집결(L40)로 건너뛴다. 접속자가 없으면 그대로(참여자 0 전이 금지).
    private bool TrySkipToSortieWhenNobodyTrains()
    {
        var table = Table;
        if (table == null || _flow.HasPendingAdvance || _flow.Connected.Count == 0)
            return false;

        var sortieIndex = LobbyTutorialRules.IndexOfGather(table.Steps, LobbyGatherPoint.Sortie);
        if (sortieIndex < 0 || _flow.StepIndex >= sortieIndex)
            return false;

        if ((_flow.IsRosterLocked ? _flow.Roster.Count : _flow.Participants.Count) > 0)
            return false;

        _flow.LockRoster();
        return ServerAdvance(sortieIndex, "훈련 인원 없음 — 출격 집결로", ignoreParticipants: true);
    }

    private void HandleZoneConfirmed(PartyGatherZone zone)
    {
        if (_flow == null)
            return;

        var point = PointOf(zone);
        var step = ServerStep();
        var before = ServerDescribe();

        // 시연 즉시 출격(명단 고정 전 출격 확정) — 훈련을 건너뛰고 로딩으로.
        if (point == LobbyGatherPoint.Sortie && IsInstantSortieOpen() && (step == null || step.gatherPoint != LobbyGatherPoint.Sortie))
        {
            _flow.LockRoster();
            Debug.Log($"[SceneFlow] {Tag} 출격 집결 확정(시연 즉시 출격 — 훈련 없이) — {before}");
            CommitLaunch(before);
            return;
        }

        if (step == null || step.kind != LobbyTutorialStepKind.Gather || step.gatherPoint != point || _flow.HasPendingAdvance)
        {
            Debug.LogWarning($"{Tag} {zone.name}({point}) 집결 확정 무시 — 현재 {before}.", this);
            return;
        }

        if (point == LobbyGatherPoint.Sortie)
        {
            Debug.Log($"[SceneFlow] {Tag} 출격 집결 확정 — {before}");
            CommitLaunch(before);
            return;
        }

        var lockIndex = LobbyTutorialRules.RosterLockIndex(Table.Steps);
        if (_flow.StepIndex == lockIndex && _flow.LockRoster())
            Debug.Log($"[SceneFlow] {Tag} 훈련 명단 고정 {_flow.Roster.Count}명 [{string.Join(",", _flow.Roster)}] — 이후 접속은 로비 대기");

        if (!ServerAdvance(_flow.StepIndex + 1, $"{point} 집결 확정"))
            return;

        var teleportTarget = LobbyTutorialRules.TeleportTargetOnConfirm(point);
        if (teleportTarget != LobbyGatherPoint.None)
            TeleportRosterInto(teleportTarget, point);
    }

    // 출격 확정 → L41 로딩 + 래치 → 메인 진입. 래치를 먼저 걸어 로딩 흐름의 로비 시작 조건(ServerHasLaunched)을 통과시키고, 재확정·중복은 무시한다.
    private void CommitLaunch(string before)
    {
        if (!_flow.TryCommitLaunch())
        {
            Debug.LogWarning($"{Tag} 메인 진입 중복 요청 무시 — 이미 내보냄({ServerDescribe()}).", this);
            return;
        }

        var loadingIndex = LobbyTutorialRules.IndexOfKind(Table.Steps, LobbyTutorialStepKind.Loading);
        if (loadingIndex > _flow.StepIndex)
            ServerAdvance(loadingIndex, "출격 확정", ignoreParticipants: true);
        else
            SyncReplicatedState();

        var launcher = NetworkManager.GetComponent<NetworkSessionLauncher>();
        Debug.Log($"[SceneFlow] {Tag} 메인 진입 1회 ({before} → {ServerDescribe()}) hasLauncher={launcher != null}");
        if (launcher == null)
        {
            Debug.LogError($"{Tag} NetworkSessionLauncher 가 없어 메인 진입을 시작하지 못했다.", this);
            return;
        }

        // 포드 탑승·강하 연출(할 일 100) 뒤 완전 암전에 로딩. 연출 컴포넌트·설정이 없으면 곧바로 로딩(폴백).
        var podLaunch = GetComponent<LobbyPodLaunch>();
        // 연출을 탄 경우만 진입 종류 Lobby — 맵에서 포드 착지 연출로 이어진다. 폴백(곧바로 로딩)은 Direct 라 맵도 낙하 스폰.
        if (podLaunch != null && podLaunch.TryBeginServer(launcher.StartGameLoadingFromLobbyPod))
            return;

        launcher.StartGameLoading();
    }

    // [서버] 전이 1회 — 미뤄졌으면(같은 프레임) 다음 프레임 Tick 이 적용하고 OnFlowChanged 를 부른다. 받아들였으면(적용·미룸) true.
    private bool ServerAdvance(int targetIndex, string reason, bool ignoreParticipants = false)
    {
        var before = ServerDescribe();
        var result = _flow.TryAdvanceTo(_flow.RunId, _flow.Revision, targetIndex, Time.frameCount, ServerNow(), ignoreParticipants);
        switch (result)
        {
            case LobbyTutorialAdvanceResult.Advanced:
                Debug.Log($"[SceneFlow] {Tag} {reason} — {before} → {ServerDescribe()}");
                OnFlowChanged();
                return true;
            case LobbyTutorialAdvanceResult.Deferred:
                Debug.Log($"[SceneFlow] {Tag} {reason} — {before} → #{targetIndex} (같은 프레임 전이라 다음 프레임에 적용)");
                return true;
            default:
                Debug.LogWarning($"{Tag} {reason} — {before} → #{targetIndex} 전이 거부({result}).", this);
                return false;
        }
    }

    // [서버] 집결 확정 텔레포트(L20 입구 → Train01Start, T50 종료 → Brief) — 명단 전원을 대상 구역 박스 안 자리(LobbyTrainingEndPlacement, 명단 순서)로.
    // 이동 = 기존 서버 텔레포트(오너 RPC — 바라보는 방향은 그대로). 높이 = 구역 원점 바닥면(박스 높이 범위 안으로) + 보정. 구역이 없으면 지점마다 경고 1회 후 생략.
    private void TeleportRosterInto(LobbyGatherPoint target, LobbyGatherPoint confirmed)
    {
        var zone = ZoneOf(target);
        var box = zone != null ? zone.GetComponent<BoxCollider>() : null;
        if (box == null)
        {
            if (_warnedNoTeleportZone.Add(target))
                Debug.LogWarning($"{Tag} {target} 집결 구역(BoxCollider)이 비었다 — {confirmed} 확정 텔레포트 생략.", this);

            return;
        }

        // 간격·여백은 월드 m — 박스 크기를 월드로 재고, 고른 자리를 다시 로컬로 나눈다(구역 스케일 대비).
        var scale = box.transform.lossyScale;
        var worldSize = new Vector2(box.size.x * Mathf.Abs(scale.x), box.size.z * Mathf.Abs(scale.z));
        var floorY = Mathf.Clamp(0f, box.center.y - box.size.y * 0.5f, box.center.y + box.size.y * 0.5f);

        var roster = _flow.Roster;
        for (var i = 0; i < roster.Count; i++)
        {
            var clientId = roster[i];
            var slot = LobbyTrainingEndPlacement.Slot(i, roster.Count, worldSize,
                LobbyTrainingEndPlacement.DefaultMargin, LobbyTrainingEndPlacement.DefaultSpacing);
            var local = new Vector3(
                box.center.x + (scale.x != 0f ? slot.x / Mathf.Abs(scale.x) : 0f),
                floorY,
                box.center.z + (scale.z != 0f ? slot.y / Mathf.Abs(scale.z) : 0f));
            var point = box.transform.TransformPoint(local);

            var fall = TryGetPlayer(clientId, out var player) ? player.GetComponent<PlayerFallRecovery>() : null;
            var destination = point + Vector3.up * TeleportClearance;
            var moved = fall != null && fall.RelocateServer(destination);
            Debug.Log($"[SceneFlow] {Tag} {confirmed} 확정 텔레포트 client {clientId} → {zone.name} 명단 {i + 1}번째 자리 {destination} moved={moved}");
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (_flow == null || !_flow.AddClient(clientId))
            return;

        // 명단 고정 전 구역은 명시 참여자(클리어 플래그 제외)라 접속을 스스로 더하지 않는다 — 다시 넣는다.
        _zonesDirty = true;
        Debug.Log(_flow.IsWaitingLateJoiner(clientId)
            ? $"[SceneFlow] {Tag} 접속 client {clientId} — 명단 고정 뒤({ServerDescribe()})라 로비 대기(출격 집결 인원에만 포함)"
            : $"[SceneFlow] {Tag} 접속 client {clientId} — 접속자 {_flow.Connected.Count}명");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        _resetReservations.Forget(clientId);
        ServerClearedStore.Remove(clientId);
        if (_flow == null || !_flow.RemoveClient(clientId))
            return;

        // 구역은 각자 연결 종료를 명단에서 뺀다 — 여기선 모델 명단(+ 복제 명단)만 맞춘다.
        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} 퇴장 client {clientId} — 접속자 {_flow.Connected.Count}명, 훈련 명단 {_flow.Roster.Count}명");
    }

    // [서버] 캐릭터 교체 완료(새 PlayerObject) — 이번 단계 훈련 보정을 새 플레이어에 바로 다시 건다(할 일 157 §10.2-6). 옛 플레이어 예약은 버린다.
    private void HandleCharacterSwapped(ulong clientId)
    {
        if (_flow == null)
            return;

        _resetReservations.Forget(clientId);
        SyncHealthFloors();
        Debug.Log($"[SceneFlow] {Tag} 캐릭터 교체 client {clientId} — 훈련 보정 재적용({ServerDescribe()})");
    }

    private void OnFlowChanged()
    {
        SyncReplicatedState();
        _zonesDirty = true;
        // 출격 진입 등 단계가 바뀌는 즉시 하한을 맞춘다(주기 갱신을 기다리지 않는다).
        SyncHealthFloors();
        LogStepContent();
        ServerFlowChanged?.Invoke();
    }

    /// <summary>
    /// [서버] 훈련 리셋 — 훈련 명단 전원에게 <see cref="TrainingPlayerReset.ServerApply"/>(체력·쿨타임·대시·어쌔신 R 최소 분노 — 옵션).
    /// 진행 중 행동이 있던 사람은 끝나는 순간 쿨타임을 한 번 더 지운다(예약). 대시를 채웠으면 오너 예측도 맞추게 일련번호를 올린다.
    /// </summary>
    public void ServerResetTrainingRoster(TrainingResetOptions options)
    {
        if (!IsServer || _flow == null)
            return;

        foreach (var clientId in _flow.Roster)
        {
            if (!TryGetPlayer(clientId, out var player))
            {
                Debug.LogWarning($"{Tag} 훈련 리셋 — client {clientId} 플레이어 오브젝트 없음, 건너뜀.", this);
                continue;
            }

            var busy = TrainingPlayerReset.ServerApply(player, options, out var summary);
            _resetReservations.OnReset(clientId, busy && options.ResetCooldowns);
            Debug.Log($"[SceneFlow] {Tag} 훈련 리셋 client {clientId} — {summary}");
        }

        if (options.RefillDash)
            trainingResetSerial.Value++;
    }

    private void TickResetReservations()
    {
        if (_resetReservations.Count == 0)
            return;

        _resetReservations.CopyTo(_idBuffer);
        foreach (var clientId in _idBuffer)
        {
            if (!TryGetPlayer(clientId, out var player))
            {
                _resetReservations.Forget(clientId);
                continue;
            }

            if (!_resetReservations.Tick(clientId, TutorialCooldownReset.IsBusy(player)))
                continue;

            var n = TrainingPlayerReset.ServerApplyReservedCooldowns(player);
            Debug.Log($"[SceneFlow] {Tag} 훈련 리셋 예약 실행 client {clientId} — 진행 중 행동 종료, 스킬 쿨 {n}개");
        }
    }

    // [서버] 훈련 보정 체력 하한 — 행 보정 Training + 고정 명단이면 1, 아니면 0(늦은 참가자·출격·브리핑·로비).
    private void SyncHealthFloors()
    {
        if (_flow == null || NetworkManager == null)
            return;

        var step = ServerStep();
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (player == null)
                continue;

            var floor = LobbyTutorialRules.HealthFloorFor(step, _flow.IsRosterLocked && ContainsRoster(client.ClientId));
            if (player.ServerHealthFloor == floor)
                continue;

            player.ServerSetHealthFloor(floor);
            Debug.Log($"[SceneFlow] {Tag} 훈련 보정 체력 하한 client {client.ClientId} → {floor} ({ServerDescribe()})");
        }
    }

    private void ClearHealthFloors()
    {
        if (NetworkManager == null)
            return;

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (player != null)
                player.ServerSetHealthFloor(0);
        }
    }

    private bool ContainsRoster(ulong clientId)
    {
        var roster = _flow.Roster;
        for (var i = 0; i < roster.Count; i++)
        {
            if (roster[i] == clientId)
                return true;
        }

        return false;
    }

    /// <summary>[서버] 접속자의 플레이어 컴포넌트.</summary>
    public bool TryGetPlayer(ulong clientId, out Player player)
    {
        player = null;
        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            return false;

        player = client.PlayerObject.GetComponent<Player>();
        return player != null;
    }

    // [오너] 서버가 대시를 가득 채웠다 — 내가 훈련 명단이면 예측 충전도 가득(서버 장부와 짝). 스폰 직후 값(이미 지난 리셋)은 무시.
    private void HandleTrainingResetSerialChanged(int previous, int current)
    {
        if (NetworkManager == null || !IsInTrainingRoster(NetworkManager.LocalClientId))
            return;

        var local = Player.LocalPlayer;
        if (local != null)
            local.GetComponent<PlayerDashController>()?.OwnerResetChargeToFull();
    }

    // [서버] 모델 → 구역. 지금 행의 집결 지점만 켠다(시연 즉시 출격이면 명단 고정 전 출격 구역도). 켜기 전에 명단·유지 시간(행 holdSeconds)을 넣고,
    // 다시 쓰는 구역(중앙 = L10·L30)은 확정 래치를 푼다.
    private void ApplyZones()
    {
        var table = Table;
        var step = ServerStep();
        var activePoint = step != null && step.kind == LobbyTutorialStepKind.Gather ? step.gatherPoint : LobbyGatherPoint.None;
        var instantSortie = IsInstantSortieOpen();
        var sortieIndex = table != null ? LobbyTutorialRules.IndexOfGather(table.Steps, LobbyGatherPoint.Sortie) : -1;

        _participantBuffer.Clear();
        _participantBuffer.AddRange(_flow.Participants);
        ForEachZone((point, zone) =>
        {
            var active = !_flow.LaunchCommitted && (point == activePoint || (instantSortie && point == LobbyGatherPoint.Sortie));
            if (LobbyTutorialRules.UsesConnectedClients(point))
                zone.ServerUseConnectedClients();
            else
                zone.ServerSetParticipants(_participantBuffer);

            var holdRow = point == activePoint ? step
                : point == LobbyGatherPoint.Sortie && sortieIndex >= 0 ? table[sortieIndex] : null;
            if (holdRow != null)
                zone.ServerSetHoldSeconds(holdRow.holdSeconds);

            if (active && zone.IsConfirmed)
                zone.ServerResetConfirmation();
            if (zone.IsActive != active)
                zone.ServerSetActive(active);
        });
    }

    private bool IsInstantSortieOpen() =>
        rules != null && rules.AllowInstantSortie && _flow != null && !_flow.IsRosterLocked && !_flow.LaunchCommitted;

    private bool ServerIsLaunching() =>
        _flow != null && (_flow.LaunchCommitted || _flow.CurrentKind == LobbyTutorialStepKind.Loading);

    // 각 단계 내용은 ServerFlowChanged 구독자(T10 = LobbyTrainingWeaponCheck, T22 = LobbyTrainingCombat, T31~T40 = LobbyTrainingSparring, L31 = LobbyBriefing).
    private void LogStepContent()
    {
        var step = ServerStep();
        if (step == null || step.kind != LobbyTutorialStepKind.Activity)
            return;

        Debug.Log($"[SceneFlow] {Tag} {step.stateId} 진행 시작 — {nameof(ServerReportActivityCompleted)}({step.activity}, rev {_flow.Revision}) 로 완료.");
    }

    private void SyncReplicatedState()
    {
        if (_flow == null)
            return;

        if (!snapshot.Value.Equals(_flow.Snapshot))
            snapshot.Value = _flow.Snapshot;

        // 명단은 L10 확정 때 한 번 정해지고 퇴장·클리어 플래그로만 준다 — 다르면 통째로 다시 쓴다.
        SyncList(trainingRoster, _flow.Roster);
        SyncList(trainingCleared, _flow.TrainingCleared);
        PublishLocal();
    }

    private static void SyncList(NetworkList<ulong> replicated, IReadOnlyList<ulong> source)
    {
        var same = replicated.Count == source.Count;
        for (var i = 0; same && i < source.Count; i++)
            same = replicated[i] == source[i];
        if (same)
            return;

        replicated.Clear();
        foreach (var clientId in source)
            replicated.Add(clientId);
    }

    private void HandleSnapshotChanged(LobbyTutorialSnapshot previous, LobbyTutorialSnapshot current) => PublishLocal();

    // [전 피어] 파사드 반영 + 단계가 바뀌었으면(회차·revision) StepChanged. 같은 revision 재반영(대사 시각 등)은 값만.
    private void PublishLocal()
    {
        if (!IsSpawned || Active != this)
            return;

        var current = snapshot.Value;
        LobbyTutorialState.Publish(Table, current);   // 단계표·스냅샷이 없으면 Clear(실시간 출처도 지운다) — 그래서 출처는 뒤에 건다
        LobbyTutorialState.SetLiveSource(this);

        if (_hasPublished && current.RunId == _publishedRunId && current.Revision == _publishedRevision)
            return;

        _hasPublished = true;
        _publishedRunId = current.RunId;
        _publishedRevision = current.Revision;
        StepChanged?.Invoke();
    }

    // [전 피어] 진행 줄 값(구역 n/N·체크 마스크·카운트다운 정수)이 바뀌면 파사드 ProgressChanged.
    private void NotifyProgressIfChanged()
    {
        if (!LobbyTutorialState.IsAvailable)
            return;

        var key = 17;
        if (LobbyTutorialState.TryGetProgress(out var n, out var total))
            key = (key * 31 + n) * 31 + total;
        if (LobbyTutorialState.TryGetLocalChecklist(out var mask, out _))
            key = key * 31 + mask;
        key = key * 31 + (int)LobbyTutorialState.ProgressMode;

        if (key == _progressKey)
            return;

        _progressKey = key;
        LobbyTutorialState.NotifyProgressChanged();
    }

    private LobbyTutorialStep ServerStep()
    {
        var table = Table;
        return _flow != null && table != null && table.TryGet(_flow.StepIndex, out var step) ? step : null;
    }

    private double ServerNow() => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

    private string ServerDescribe()
    {
        if (_flow == null)
            return "(없음)";

        var step = ServerStep();
        return $"{(step != null ? step.stateId : "?")}(#{_flow.StepIndex} rev {_flow.Revision})";
    }

    private string Describe() => ServerDescribe();

    /// <summary>[전 피어] 이 집결 지점의 씬 구역(없으면 null) — 로컬 위치 판정용(바닥 안내 스텝 <see cref="LobbyGuideSteps"/>·158 테두리).</summary>
    public PartyGatherZone GatherZone(LobbyGatherPoint point) => ZoneOf(point);

    private PartyGatherZone ZoneOf(LobbyGatherPoint point)
    {
        switch (point)
        {
            case LobbyGatherPoint.Entrance: return entranceZone;
            case LobbyGatherPoint.Sortie: return sortieZone;
            case LobbyGatherPoint.Train01Start: return train01StartZone;
            case LobbyGatherPoint.Train02Start: return train02StartZone;
            case LobbyGatherPoint.Train03Area: return train03AreaZone;
            case LobbyGatherPoint.TrainEnd: return trainEndZone;
            case LobbyGatherPoint.Brief: return briefZone;
            default: return null;
        }
    }

    private LobbyGatherPoint PointOf(PartyGatherZone zone)
    {
        foreach (var point in Points)
        {
            if (ZoneOf(point) == zone)
                return point;
        }

        return LobbyGatherPoint.None;
    }

    private void ForEachZone(Action<LobbyGatherPoint, PartyGatherZone> action)
    {
        foreach (var point in Points)
        {
            var zone = ZoneOf(point);
            if (zone != null)
                action(point, zone);
        }
    }

    private void WarnMissingZones()
    {
        foreach (var point in Points)
        {
            if (ZoneOf(point) == null)
                Debug.LogWarning($"{Tag} {point} 집결 구역이 비었다 — 그 집결로 넘어가는 단계에서 멈춘다.", this);
        }
    }
}
