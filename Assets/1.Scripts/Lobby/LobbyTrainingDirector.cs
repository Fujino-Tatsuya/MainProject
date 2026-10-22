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

    [Tooltip("출격(옛 Ready 패드 자리). Lobby 단계(훈련 없이 출격)와 브리핑 완료 뒤에 켜진다.")]
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

    /// <summary>스폰된 진행기(없으면 null).</summary>
    public static LobbyTrainingDirector Active { get; private set; }

    /// <summary>[전 피어] 단계 또는 하위 상태가 바뀌었다(스폰 직후 1회 포함) — 목표 UI·무전(T6)이 붙을 자리.</summary>
    public static event Action<LobbyTrainingStage, LobbyTrainingPhase> StageChanged;

    public LobbyTrainingStage Stage => IsSpawned ? stage.Value : LobbyTrainingStage.Lobby;
    public LobbyTrainingPhase Phase => IsSpawned ? phase.Value : LobbyTrainingPhase.Gathering;

    /// <summary>[전 피어] 훈련 시작(입구 확정) 뒤 — 회차 단위 잠금. 사람별 판정은 <see cref="CharacterChangeBlockFor"/>.</summary>
    public bool IsCharacterLocked => Stage != LobbyTrainingStage.Lobby;

    /// <summary>[전 피어] 출격 집결 카운트다운 중(행동 시작 제한 구간).</summary>
    public bool IsSortieCountdownActive => sortieZone != null && sortieZone.IsRestrictingActions;

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
        }

        StageChanged?.Invoke(Stage, Phase);
    }

    public override void OnNetworkDespawn()
    {
        stage.OnValueChanged -= HandleStageChanged;
        phase.OnValueChanged -= HandlePhaseChanged;

        if (IsServer)
        {
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
        if (!IsSpawned || !IsServer || !_zonesDirty)
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
        LogStageContent();
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

    // 각 단계 내용(표적·몹·상대·무전)은 T3~T6 — 지금은 무엇이 시작될 자리인지만 남긴다.
    private void LogStageContent()
    {
        if (_flow.Phase != LobbyTrainingPhase.InProgress)
            return;

        Debug.Log($"[SceneFlow] {Tag} {_flow.Stage} 진행 시작 — 내용 미연결(T3~T6), {nameof(ServerReportStageCompleted)}({_flow.Stage}) 로 완료.");
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
