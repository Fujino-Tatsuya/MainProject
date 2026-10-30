using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>TRAIN_01 개인 체크 복제 단위 — 훈련 명단 1명.</summary>
public struct LobbyWeaponCheckEntry : INetworkSerializable, IEquatable<LobbyWeaponCheckEntry>
{
    public ulong clientId;
    public byte mask;

    public LobbyWeaponCheckEntry(ulong clientId, int mask)
    {
        this.clientId = clientId;
        this.mask = (byte)mask;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref mask);
    }

    public bool Equals(LobbyWeaponCheckEntry other) => clientId == other.clientId && mask == other.mask;
}

/// <summary>
/// TRAIN_01 무기 점검(할 일 86 PLAN §3.3·§7-1, 명세 v0.2 §7) — <see cref="LobbyTrainingDirector"/> 와 같은 오브젝트(씬 상주 NetworkObject)에 붙는다.
/// 규칙은 <see cref="LobbyWeaponCheckModel"/>·<see cref="LobbyWeaponCheckRules"/>, 여기는 서버 훅 구독·표적 스폰·복제만 한다.
/// <list type="bullet">
/// <item>시작 = 진행기가 T10(Activity BasicCheck, <see cref="LobbyTrainingSupportRules.Train01TargetsActive"/>)이 되는 순간 — 그 revision 을 기억한다: 명단 고정·체크 비움 →
/// 표적 3개 스폰(<see cref="targetPrefab"/> @ <see cref="targetSpots"/>) → 훈련 리셋(<see cref="TrainingResetOptions.TrainingStart"/> — 체력·쿨·대시·어쌔신 R 최소 분노).</item>
/// <item>판정(서버, 존1 훅 재사용 — 존1 코드는 그대로): 대시 <see cref="PlayerDashController.ServerDashApproved"/> · 기본 공격 <see cref="Player.ServerBasicAttackStarted"/> ·
/// Q·E·R·우클릭 <see cref="PlayerSkillController.ServerSkillUsed"/>(거너 Q 는 <see cref="GunnerChargeLaserSkill.ServerFired"/>). 행동한 플레이어 본인 체크만.</item>
/// <item>전원 6/6 → 다음 프레임 <see cref="LobbyTrainingDirector.ServerReportActivityCompleted"/>(BasicCheck, 시작 revision) 1회. 시간 초과 자동 완료 없음.</item>
/// <item>단계를 떠나면(완료·그 밖) 표적 디스폰·체크 비움.</item>
/// <item>복제: 명단별 체크 마스크(NetworkList) — 재접속(새 clientId = 늦은 참가자)·늦게 그린 클라도 지금 상태를 그대로 본다. 파티 완료 인원은 이 목록에서 센다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingWeaponCheck : NetworkBehaviour
{
    private const string Tag = "[LobbyTraining/Train01]";

    [Tooltip("표적 프리팹 — 간파 사이클 없는 허수아비(TrainingDummy_Tutorial). NetworkPrefabs 에 등록돼 있어야 한다.")]
    [SerializeField] private GameObject targetPrefab;

    [Tooltip("표적 자리(자식 Transform 3개). 훈련실 입구 쪽 — 임시 좌표, 최종 위치는 사용자.")]
    [SerializeField] private LobbyTrainingTargetSpots targetSpots;

    private readonly NetworkList<LobbyWeaponCheckEntry> entries = new NetworkList<LobbyWeaponCheckEntry>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버
    private LobbyTrainingDirector _director;
    private readonly LobbyWeaponCheckModel _model = new LobbyWeaponCheckModel();
    private readonly List<NetworkObject> _targets = new List<NetworkObject>();
    private readonly List<Transform> _spotBuffer = new List<Transform>();
    private bool _pendingComplete;
    private int _runRevision;

    /// <summary>스폰된 점검(없으면 null).</summary>
    public static LobbyTrainingWeaponCheck Active { get; private set; }

    /// <summary>[전 피어] 복제 체크가 바뀌었다(시작·체크·완료·정리).</summary>
    public static event Action Changed;

    /// <summary>[전 피어] 점검 진행 중(T10).</summary>
    public bool IsRunning
    {
        get
        {
            var director = LobbyTrainingDirector.Active;
            return IsSpawned && director != null && LobbyTrainingSupportRules.Train01TargetsActive(director.CurrentActivity);
        }
    }

    /// <summary>[전 피어] 이 사람의 체크 마스크. 명단 밖(늦은 참가자 등)이면 false.</summary>
    public bool TryGetMask(ulong clientId, out int mask)
    {
        mask = 0;
        if (!IsSpawned)
            return false;

        foreach (var entry in entries)
        {
            if (entry.clientId != clientId)
                continue;

            mask = entry.mask;
            return true;
        }

        return false;
    }

    /// <summary>[전 피어] 파티 완료 인원(6/6)과 명단 인원.</summary>
    public void GetPartyProgress(out int completed, out int total)
    {
        completed = 0;
        total = IsSpawned ? entries.Count : 0;
        for (var i = 0; i < total; i++)
        {
            if (LobbyWeaponCheckRules.IsComplete(entries[i].mask))
                completed++;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Active = this;
        entries.OnListChanged += HandleEntriesChanged;

        if (IsServer)
        {
            _director = GetComponent<LobbyTrainingDirector>();
            if (_director == null)
            {
                Debug.LogError($"{Tag} 같은 오브젝트에 LobbyTrainingDirector 가 없다 — 무기 점검이 동작하지 않는다.", this);
            }
            else
            {
                _director.ServerFlowChanged += EvaluateServer;
                PlayerDashController.ServerDashApproved += HandleDashApprovedServer;
                Player.ServerBasicAttackStarted += HandleBasicAttackServer;
                PlayerSkillController.ServerSkillUsed += HandleSkillUsedServer;
                GunnerChargeLaserSkill.ServerFired += HandleChargeLaserFiredServer;
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnectedServer;
                _director.ServerRosterMemberRemoved += HandleRosterMemberRemovedServer;
            }

            if (targetPrefab == null || targetSpots == null)
                Debug.LogWarning($"{Tag} 표적 프리팹·자리가 비었다 — TRAIN_01 표적 없이 진행한다(거너 R 대상이 없어 R 체크가 막힐 수 있다).", this);

            // 진행기가 먼저 스폰돼 첫 알림을 놓쳤을 수 있다 — 지금 상태로 한 번 맞춘다.
            EvaluateServer();
        }

        Changed?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        entries.OnListChanged -= HandleEntriesChanged;

        if (IsServer)
        {
            if (_director != null)
            {
                _director.ServerFlowChanged -= EvaluateServer;
                _director.ServerRosterMemberRemoved -= HandleRosterMemberRemovedServer;
            }
            PlayerDashController.ServerDashApproved -= HandleDashApprovedServer;
            Player.ServerBasicAttackStarted -= HandleBasicAttackServer;
            PlayerSkillController.ServerSkillUsed -= HandleSkillUsedServer;
            GunnerChargeLaserSkill.ServerFired -= HandleChargeLaserFiredServer;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnectedServer;

            DespawnTargetsServer();
            _model.Stop();
            _director = null;
        }

        if (Active == this)
            Active = null;
        base.OnNetworkDespawn();
        Changed?.Invoke();
    }

    public override void OnDestroy()
    {
        if (Active == this)
            Active = null;
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || !_pendingComplete)
            return;

        // 훅 콜백(스킬 승인·발사 도중) 안에서 단계를 넘기면 표적이 그 판정 중에 디스폰된다 — 다음 프레임에 보고한다.
        _pendingComplete = false;
        if (_director != null && _director.ServerReportActivityCompleted(LobbyTutorialActivity.BasicCheck, _runRevision))
            Debug.Log($"[SceneFlow] {Tag} 전원 6/6 — T10 완료 보고");
    }

    // ─── 서버: 시작·정리 ────────────────────────────────────────────────

    private void EvaluateServer()
    {
        if (_director == null)
            return;

        var active = LobbyTrainingSupportRules.Train01TargetsActive(_director.CurrentActivity);
        if (active && !_model.IsRunning)
            BeginServer();
        else if (!active && (_model.IsRunning || _targets.Count > 0))
            EndServer();
    }

    private void BeginServer()
    {
        _runRevision = _director.Revision;
        _model.Begin(_director.ServerTrainingRoster);
        _pendingComplete = false;
        SyncEntries();
        SpawnTargetsServer();
        _director.ServerResetTrainingRoster(TrainingResetOptions.TrainingStart);
        Debug.Log($"[SceneFlow] {Tag} 무기 점검 시작 — 명단 {_model.Roster.Count}명 [{string.Join(",", _model.Roster)}], 표적 {_targets.Count}개");
    }

    private void EndServer()
    {
        var completed = _model.CompletedCount;
        var total = _model.Roster.Count;
        _model.Stop();
        _pendingComplete = false;
        SyncEntries();
        var n = DespawnTargetsServer();
        Debug.Log($"[SceneFlow] {Tag} 무기 점검 정리 — 완료 {completed}/{total}, 표적 {n}개 디스폰");
    }

    private void SpawnTargetsServer()
    {
        if (targetPrefab == null || targetSpots == null)
            return;

        targetSpots.GetSpots(_spotBuffer);
        foreach (var spot in _spotBuffer)
        {
            var go = Instantiate(targetPrefab, spot.position, spot.rotation);
            // 활성 씬이 로비가 아닐 때(로딩 씬 등)도 로비 씬과 같이 정리되게 자리의 씬으로 옮긴다.
            SceneManager.MoveGameObjectToScene(go, targetSpots.gameObject.scene);
            go.name = $"{targetPrefab.name}_Train01_{_targets.Count}";
            var no = go.GetComponent<NetworkObject>();
            if (no == null || go.GetComponent<Unit>() == null)
            {
                Debug.LogError($"{Tag} 표적 프리팹 '{targetPrefab.name}' 에 NetworkObject/Unit 이 없다.", this);
                Destroy(go);
                return;
            }

            no.Spawn(true);
            _targets.Add(no);
        }
    }

    private int DespawnTargetsServer()
    {
        var n = 0;
        foreach (var no in _targets)
        {
            if (no == null || !no.IsSpawned)
                continue;

            no.Despawn(true);
            n++;
        }

        _targets.Clear();
        return n;
    }

    // ─── 서버: 판정 훅 ──────────────────────────────────────────────────

    private void HandleDashApprovedServer(Player player) => ReportServer(player, LobbyWeaponCheckItem.Dash);

    // 기본 공격 한 타 실제 시작(적중 무관 — 어쌔신 강타 포함). R 대상 확정 좌클릭은 이 경로를 안 탄다.
    private void HandleBasicAttackServer(Player player) => ReportServer(player, LobbyWeaponCheckItem.BasicAttack);

    // Q·E·R·우클릭 승인 = 실제 발동(거부·조준 취소는 안 온다). 발사형(거너 Q)은 승인 = 충전 시작이라 발사 신호에서 센다.
    private void HandleSkillUsedServer(Player player, PlayerSkillSlot slot)
    {
        if (player == null || !_model.IsRunning)
            return;

        var skills = player.GetComponent<PlayerSkillController>();
        var countsOnFire = skills != null && skills.GetSkill(slot) is GunnerChargeLaserSkill;
        if (LobbyWeaponCheckRules.TryGetApprovalItem(slot, countsOnFire, out var item))
            ReportServer(player, item);
    }

    private void HandleChargeLaserFiredServer(Player player) => ReportServer(player, LobbyWeaponCheckItem.SkillQ);

    private void ReportServer(Player player, LobbyWeaponCheckItem item)
    {
        if (player == null || !_model.IsRunning)
            return;

        var clientId = player.OwnerClientId;
        if (!_model.Report(clientId, item))
            return;

        SyncEntry(clientId);
        Debug.Log($"[SceneFlow] {Tag} client {clientId} {item} 체크 — {LobbyWeaponCheckRules.CountOf(_model.MaskOf(clientId))}/{LobbyWeaponCheckRules.ItemCount}, " +
                  $"파티 완료 {_model.CompletedCount}/{_model.Roster.Count}");
        if (_model.TryConsumeAllComplete())
            _pendingComplete = true;
    }

    private void HandleClientDisconnectedServer(ulong clientId) => RemoveFromRosterServer(clientId, "퇴장");

    // 튜토리얼 클리어 플래그(로비 Alt+1)로 훈련 명단에서 빠졌다 — 퇴장과 같이 남은 사람만으로 완료 판정.
    private void HandleRosterMemberRemovedServer(ulong clientId) => RemoveFromRosterServer(clientId, "튜토리얼 클리어로 명단 제외");

    private void RemoveFromRosterServer(ulong clientId, string reason)
    {
        if (!_model.RemoveClient(clientId))
            return;

        SyncEntries();
        Debug.Log($"[SceneFlow] {Tag} 명단 client {clientId} {reason} — 파티 완료 {_model.CompletedCount}/{_model.Roster.Count}");
        if (_model.TryConsumeAllComplete())
            _pendingComplete = true;
    }

    // ─── 복제 ──────────────────────────────────────────────────────────

    private void SyncEntry(ulong clientId)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].clientId != clientId)
                continue;

            entries[i] = new LobbyWeaponCheckEntry(clientId, _model.MaskOf(clientId));
            return;
        }

        SyncEntries();
    }

    private void SyncEntries()
    {
        entries.Clear();
        if (!_model.IsRunning)
            return;

        foreach (var clientId in _model.Roster)
            entries.Add(new LobbyWeaponCheckEntry(clientId, _model.MaskOf(clientId)));
    }

    private void HandleEntriesChanged(NetworkListEvent<LobbyWeaponCheckEntry> change) => Changed?.Invoke();
}
