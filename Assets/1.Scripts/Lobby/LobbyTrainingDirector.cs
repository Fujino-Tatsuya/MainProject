using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 훈련 진행기(할 일 86 PLAN §3.2·§7, 3.LobbyScene 씬 상주 NetworkObject, 서버 권한).
/// 규칙은 <see cref="LobbyTrainingFlow"/> 가 갖고, 여기는 NGO 복제·집결 구역 켜고 끄기·확정 구독·로그만 한다.
/// <list type="bullet">
/// <item>복제: 단계·하위 상태(NetworkVariable) — 늦게 붙은 클라·재접속도 현재 단계를 그대로 본다(지난 대사 재생 없음 — 대사는 T6).</item>
/// <item>구역: 단계가 바뀔 때마다 <see cref="PartyGatherZone.ServerSetActive"/>·명단·유지 시간을 다시 넣는다. 확정은 <see cref="PartyGatherZone.ServerConfirmed"/> 로 받는다.
/// 입구·출격 = 접속자 전원(자동), 훈련 내부·종료·브리핑 = 입구 확정 때 고정한 훈련 명단. 유지 시간 = <see cref="LobbyRules.GatherHoldSeconds"/>,
/// Train03 A→B·B→C 영역 안 확인만 0초.</item>
/// <item>단계 완료: 훈련 내용(T3 표적·T4 일반 몹·T5 훈련 상대·T6 브리핑)이 <see cref="ServerReportStageCompleted"/> 로 알린다. 현재 단계가 아니면 무시.</item>
/// <item>출격 확정 = 메인 진입(옛 Ready 패드·LobbySession 카운트다운 대체, PLAN §7-2): Sortie 진입 때 래치(<see cref="LobbyTrainingFlow.TryConsumeLaunch"/>)를 걸고
/// <see cref="NetworkSessionLauncher.StartGameLoading"/> 을 정확히 1회 부른다. 로딩 흐름의 로비 시작 조건은 <see cref="ServerHasLaunched"/>.</item>
/// <item>접속 승인: <see cref="NetworkSessionLauncher.LobbyCountdownActiveProvider"/> = 출격 집결 카운트다운 중 또는 출격 단계(<see cref="LobbyTrainingFlow.BlocksNewJoins"/>).
/// 훈련·브리핑 중 접속은 받는다(로비 대기).</item>
/// <item>캐릭터 잠금: <see cref="CharacterChangeBlockFor"/> — 훈련 명단(복제)·단계·출격 카운트다운으로 서버 RPC 와 클라 패널이 같은 판정을 한다.</item>
/// <item>판 끝 로비 복귀: 로비 씬이 다시 올라오면 새 진행기 = Lobby 단계부터(새 회차 — 출격 래치·명단도 새로).</item>
/// <item>훈련 보정(T3, 명세 §6): 훈련 단계(Train01~Train03D) 동안 훈련 명단 플레이어 체력 하한 1(<see cref="Player.ServerSetHealthFloor"/>) —
/// 단계가 바뀔 때마다 즉시 다시 맞춰 출격·종료 집결·브리핑에선 0, 진행기 디스폰(판 끝 로비 복귀·씬 전환) 때도 0.</item>
/// <item>훈련 리셋(T3, 공용): <see cref="ServerResetTrainingRoster"/> — 명단 전원 <see cref="TrainingPlayerReset"/> + 진행 중 행동 예약 + 오너 대시 예측 짝(복제 일련번호).</item>
/// <item>단계 내용 컴포넌트는 <see cref="ServerFlowChanged"/>(서버, 단계·하위 상태 확정 뒤 1회)를 구독한다 — 복제 값 두 개가 따로 도착하는 클라 이벤트와 달리 중간 조합이 없다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingDirector : NetworkBehaviour
{
    private const string Tag = "[LobbyTraining]";

    [Tooltip("집결 유지 시간(gatherHoldSeconds)을 읽는다. 비우면 각 구역 인스펙터 값을 쓴다.")]
    [SerializeField] private LobbyRules rules;

    [Header("집결 구역")]
    [Tooltip("훈련장 입구(훈련실 문 앞 복도). Lobby 단계에서 출격 구역과 함께 켜진다 — 확정 = 훈련 시작.")]
    [SerializeField] private PartyGatherZone entranceZone;

    [Tooltip("출격(SortieStation 패드 위 — 임시 위치, 최종 배치는 사용자). Lobby 단계(훈련 없이 출격)와 브리핑 완료 뒤에 켜진다.")]
    [SerializeField] private PartyGatherZone sortieZone;

    [SerializeField] private PartyGatherZone train01StartZone;
    [SerializeField] private PartyGatherZone train02StartZone;

    [Tooltip("세 번째 훈련 영역 — 시작 집결 + A→B·B→C 영역 안 확인(0초).")]
    [SerializeField] private PartyGatherZone train03AreaZone;

    [Tooltip("훈련 종료 집결(훈련실 출구).")]
    [SerializeField] private PartyGatherZone trainEndZone;

    [Tooltip("브리핑 위치(로비).")]
    [SerializeField] private PartyGatherZone briefZone;

    private readonly NetworkVariable<LobbyTrainingStage> stage = new NetworkVariable<LobbyTrainingStage>(
        LobbyTrainingStage.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<LobbyTrainingPhase> phase = new NetworkVariable<LobbyTrainingPhase>(
        LobbyTrainingPhase.Gathering, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 훈련 명단 복제 — 클라가 자기 캐릭터 잠금(명단 안 = 잠김, 늦은 참가자 = 허용)을 미리 보여 주려고.
    private readonly NetworkList<ulong> trainingRoster = new NetworkList<ulong>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 훈련 리셋 일련번호 — 오너가 바뀌는 것을 보고 대시 예측 충전을 서버 장부와 맞춘다(복도1 쿨 초기화와 같은 방식).
    private readonly NetworkVariable<int> trainingResetSerial = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private const float SupportTickInterval = 0.25f;

    private static readonly LobbyGatherPoint[] Points =
    {
        LobbyGatherPoint.Entrance, LobbyGatherPoint.Sortie, LobbyGatherPoint.Train01Start, LobbyGatherPoint.Train02Start,
        LobbyGatherPoint.Train03Area, LobbyGatherPoint.TrainEnd, LobbyGatherPoint.Brief,
    };

    // 서버 전용
    private LobbyTrainingFlow _flow;
    private bool _zonesDirty;
    private bool _loggedWaitingZones;
    private Func<bool> _joinBlockedProvider;
    private readonly TrainingResetReservations _resetReservations = new TrainingResetReservations();
    private readonly List<ulong> _idBuffer = new List<ulong>();
    private float _supportTimer;

    /// <summary>스폰된 진행기(없으면 null).</summary>
    public static LobbyTrainingDirector Active { get; private set; }

    /// <summary>[전 피어] 단계 또는 하위 상태가 바뀌었다(스폰 직후 1회 포함) — 목표 UI·무전(T6)이 붙을 자리.</summary>
    public static event Action<LobbyTrainingStage, LobbyTrainingPhase> StageChanged;

    /// <summary>[서버] 단계·하위 상태가 바뀌었다(전이 확정 뒤 1회, 스폰 직후 1회) — 단계 내용(표적·몹·상대) 시작·정리용.</summary>
    public event Action ServerFlowChanged;

    /// <summary>[전 피어] 이 사람이 훈련 명단(입구 확정 때 고정)에 있는가 — 복제 명단.</summary>
    public bool IsInTrainingRoster(ulong clientId) => IsSpawned && trainingRoster.Contains(clientId);

    public LobbyTrainingStage Stage => IsSpawned ? stage.Value : LobbyTrainingStage.Lobby;
    public LobbyTrainingPhase Phase => IsSpawned ? phase.Value : LobbyTrainingPhase.Gathering;

    /// <summary>[전 피어] 훈련 시작(입구 확정) 뒤 — 회차 단위 잠금. 사람별 판정은 <see cref="CharacterChangeBlockFor"/>.</summary>
    public bool IsCharacterLocked => Stage != LobbyTrainingStage.Lobby;

    /// <summary>[전 피어] 출격 집결 카운트다운 중 — 접속 승인·캐릭터 잠금이 본다(입력은 막지 않는다).</summary>
    public bool IsSortieCountdownActive => sortieZone != null && sortieZone.IsCountdownActive;

    /// <summary>[서버] 출격 확정으로 메인 진입을 내보냈다 — NetworkLoadingFlowController 의 로비 시작 조건.</summary>
    public bool ServerHasLaunched => _flow != null && _flow.HasLaunched;

    /// <summary>[전 피어] 이 사람의 캐릭터 변경을 막는 진행기 사유(없으면 None) — 서버 RPC 검증·클라 패널 잠금 공용.</summary>
    public LobbyCharacterChangeReject CharacterChangeBlockFor(ulong clientId)
    {
        return LobbyTrainingFlow.CharacterChangeBlock(Stage, IsSpawned && trainingRoster.Contains(clientId), IsSortieCountdownActive);
    }

    /// <summary>[서버] 훈련 참여 명단(입구 확정 때 고정). 서버가 아니면 빈 목록.</summary>
    public IReadOnlyList<ulong> ServerTrainingRoster => _flow != null ? _flow.TrainingRoster : Array.Empty<ulong>();

    /// <summary>[서버] 훈련·브리핑 중 접속해 로비에서 기다리는 사람인가.</summary>
    public bool ServerIsWaitingLateJoiner(ulong clientId) => _flow != null && _flow.IsWaitingLateJoiner(clientId);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Active = this;
        stage.OnValueChanged += HandleStageChanged;
        phase.OnValueChanged += HandlePhaseChanged;
        trainingResetSerial.OnValueChanged += HandleTrainingResetSerialChanged;

        if (IsServer)
        {
            _flow = new LobbyTrainingFlow();
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
                _flow.AddClient(clientId);

            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            ForEachZone((point, zone) => zone.ServerConfirmed += HandleZoneConfirmed);
            _joinBlockedProvider = () => _flow != null && LobbyTrainingFlow.BlocksNewJoins(_flow.Stage, IsSortieCountdownActive);
            NetworkSessionLauncher.LobbyCountdownActiveProvider = _joinBlockedProvider;
            WarnMissingZones();
            SyncReplicatedState();
            _zonesDirty = true;
            Debug.Log($"[SceneFlow] {Tag} 서버 스폰 — 새 회차 Lobby, 접속자 {_flow.Connected.Count}명");
            ServerFlowChanged?.Invoke();
        }

        StageChanged?.Invoke(Stage, Phase);
    }

    public override void OnNetworkDespawn()
    {
        stage.OnValueChanged -= HandleStageChanged;
        phase.OnValueChanged -= HandlePhaseChanged;
        trainingResetSerial.OnValueChanged -= HandleTrainingResetSerialChanged;

        if (IsServer)
        {
            // 판 끝·씬 전환 — 훈련 보정이 남지 않게 접속자 전원 하한 해제.
            ClearHealthFloors();
            _resetReservations.Clear();

            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            ForEachZone((point, zone) => zone.ServerConfirmed -= HandleZoneConfirmed);
            if (NetworkSessionLauncher.LobbyCountdownActiveProvider == _joinBlockedProvider)
                NetworkSessionLauncher.LobbyCountdownActiveProvider = null;
            _joinBlockedProvider = null;
            _flow = null;
        }

        if (Active == this)
            Active = null;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Active == this)
            Active = null;
        base.OnDestroy();
    }

    /// <summary>
    /// [서버] 단계 완료 보고 — 훈련 내용(T3~T6)이 부른다. <paramref name="completed"/> 가 현재 진행 중인 단계일 때만 다음으로 넘어가고 true.
    /// 다른 단계·집결 대기 중·중복 보고는 무시하고 false(경고 로그). Lobby·TrainEnd·Sortie 는 집결로만 끝난다.
    /// </summary>
    public bool ServerReportStageCompleted(LobbyTrainingStage completed)
    {
        if (!IsServer || _flow == null)
        {
            Debug.LogWarning($"{Tag} 단계 완료 보고 {completed} 무시 — 서버가 아니거나 스폰 전.", this);
            return false;
        }

        var before = Describe();
        if (!_flow.ReportStageCompleted(completed))
        {
            Debug.LogWarning($"{Tag} 단계 완료 보고 {completed} 무시 — 현재 {before}.", this);
            return false;
        }

        Debug.Log($"[SceneFlow] {Tag} {completed} 완료 보고 — {before} → {Describe()}");
        OnFlowChanged();
        return true;
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;

        _supportTimer -= Time.unscaledDeltaTime;
        if (_supportTimer <= 0f)
        {
            _supportTimer = SupportTickInterval;
            // 플레이어 오브젝트가 늦게 붙거나 다시 스폰돼도 하한이 맞게 주기적으로도 맞춘다(같은 값이면 아무 일 없음).
            SyncHealthFloors();
            TickResetReservations();
        }

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
                Debug.Log($"{Tag} 집결 구역 스폰 대기 중 — 전부 스폰되면 {Describe()} 구역을 켠다.");
            }

            return;
        }

        _zonesDirty = false;
        ApplyZones();
    }

    private void HandleZoneConfirmed(PartyGatherZone zone)
    {
        if (_flow == null)
            return;

        var point = PointOf(zone);
        var before = Describe();
        if (!_flow.OnGatherConfirmed(point))
        {
            Debug.LogWarning($"{Tag} {zone.name}({point}) 집결 확정 무시 — 현재 {before}.", this);
            return;
        }

        Debug.Log($"[SceneFlow] {Tag} {point} 집결 확정 — {before} → {Describe()}");
        if (point == LobbyGatherPoint.Entrance)
            Debug.Log($"[SceneFlow] {Tag} 훈련 시작 — 참여 명단 고정 {_flow.TrainingRoster.Count}명 [{string.Join(",", _flow.TrainingRoster)}]");
        OnFlowChanged();

        if (point == LobbyGatherPoint.Sortie)
            LaunchMainOnce();
    }

    // 출격 확정 → 메인 진입. 래치를 먼저 걸어 로딩 흐름의 로비 시작 조건(ServerHasLaunched)을 통과시키고, 재확정·중복은 무시한다.
    private void LaunchMainOnce()
    {
        if (!_flow.TryConsumeLaunch())
        {
            Debug.LogWarning($"{Tag} 메인 진입 중복 요청 무시 — 이미 내보냄({Describe()}).", this);
            return;
        }

        var launcher = NetworkManager.GetComponent<NetworkSessionLauncher>();
        Debug.Log($"[SceneFlow] {Tag} 출격 집결 확정({(_flow.HasStartedTraining ? "훈련 뒤" : "훈련 없이")}) — 메인 진입 1회 hasLauncher={launcher != null}");
        if (launcher == null)
        {
            Debug.LogError($"{Tag} NetworkSessionLauncher 가 없어 메인 진입을 시작하지 못했다.", this);
            return;
        }

        launcher.StartGameLoading();
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (_flow == null || !_flow.AddClient(clientId))
            return;

        Debug.Log(_flow.IsWaitingLateJoiner(clientId)
            ? $"[SceneFlow] {Tag} 접속 client {clientId} — 훈련 중({Describe()})이라 로비 대기(출격 집결 인원에만 포함)"
            : $"[SceneFlow] {Tag} 접속 client {clientId} — 접속자 {_flow.Connected.Count}명");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        _resetReservations.Forget(clientId);
        if (_flow == null || !_flow.RemoveClient(clientId))
            return;

        // 구역은 각자 연결 종료를 명단에서 뺀다 — 여기선 모델 명단(+ 복제 명단)만 맞춘다.
        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} 퇴장 client {clientId} — 접속자 {_flow.Connected.Count}명, 훈련 명단 {_flow.TrainingRoster.Count}명");
    }

    private void OnFlowChanged()
    {
        SyncReplicatedState();
        _zonesDirty = true;
        // 출격 진입 등 단계가 바뀌는 즉시 하한을 맞춘다(주기 갱신을 기다리지 않는다).
        SyncHealthFloors();
        LogStageContent();
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

        foreach (var clientId in _flow.TrainingRoster)
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

    // [서버] 훈련 보정 체력 하한 — 훈련 단계 + 훈련 명단이면 1, 아니면 0(늦은 참가자·출격·브리핑·로비).
    private void SyncHealthFloors()
    {
        if (_flow == null || NetworkManager == null)
            return;

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (player == null)
                continue;

            var floor = LobbyTrainingSupportRules.HealthFloorFor(_flow.Stage, ContainsRoster(client.ClientId));
            if (player.ServerHealthFloor == floor)
                continue;

            player.ServerSetHealthFloor(floor);
            Debug.Log($"[SceneFlow] {Tag} 훈련 보정 체력 하한 client {client.ClientId} → {floor} ({Describe()})");
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
        var roster = _flow.TrainingRoster;
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

    // [서버] 모델 → 구역. 켜기 전에 명단·유지 시간을 넣고, 다시 쓰는 구역(Train03 영역)은 확정 래치를 푼다.
    private void ApplyZones()
    {
        var hold = rules != null ? rules.GatherHoldSeconds : (float?)null;
        ForEachZone((point, zone) =>
        {
            var active = _flow.IsPointActive(point);
            if (LobbyTrainingFlow.UsesConnectedClients(point))
                zone.ServerUseConnectedClients();
            else
                zone.ServerSetParticipants(_flow.ParticipantsFor(point));

            if (point == LobbyGatherPoint.Train03Area && _flow.IsAreaCheckOnly)
                zone.ServerSetHoldSeconds(0f);
            else if (hold.HasValue)
                zone.ServerSetHoldSeconds(hold.Value);

            if (active && zone.IsConfirmed)
                zone.ServerResetConfirmation();
            if (zone.IsActive != active)
                zone.ServerSetActive(active);
        });
    }

    // 각 단계 내용은 ServerFlowChanged 구독자(T3 = LobbyTrainingWeaponCheck, T4 = LobbyTrainingCombat, T5 = LobbyTrainingSparring, T6 미연결) — 여기선 시작만 남긴다.
    private void LogStageContent()
    {
        if (_flow.Phase != LobbyTrainingPhase.InProgress)
            return;

        Debug.Log($"[SceneFlow] {Tag} {_flow.Stage} 진행 시작 — {nameof(ServerReportStageCompleted)}({_flow.Stage}) 로 완료.");
    }

    private void SyncReplicatedState()
    {
        if (stage.Value != _flow.Stage)
            stage.Value = _flow.Stage;
        if (phase.Value != _flow.Phase)
            phase.Value = _flow.Phase;

        // 명단은 입구 확정 때 한 번 정해지고 퇴장으로만 준다 — 다르면 통째로 다시 쓴다.
        var roster = _flow.TrainingRoster;
        var same = trainingRoster.Count == roster.Count;
        for (var i = 0; same && i < roster.Count; i++)
            same = trainingRoster[i] == roster[i];
        if (same)
            return;

        trainingRoster.Clear();
        foreach (var clientId in roster)
            trainingRoster.Add(clientId);
    }

    private void HandleStageChanged(LobbyTrainingStage previous, LobbyTrainingStage current) => StageChanged?.Invoke(current, Phase);

    private void HandlePhaseChanged(LobbyTrainingPhase previous, LobbyTrainingPhase current) => StageChanged?.Invoke(Stage, current);

    private string Describe() => _flow != null ? $"{_flow.Stage}/{_flow.Phase}" : "(없음)";

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

        if (rules == null)
            Debug.LogWarning($"{Tag} LobbyRules 가 비었다 — 집결 유지 시간은 각 구역 인스펙터 값을 쓴다.", this);
    }
}
