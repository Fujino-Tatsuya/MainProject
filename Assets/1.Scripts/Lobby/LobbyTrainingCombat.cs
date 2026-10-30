using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// TRAIN_02 일반 전투(할 일 86 PLAN §3.4, 명세 v0.2 §8) — <see cref="LobbyTrainingDirector"/> 와 같은 오브젝트(씬 상주 NetworkObject)에 붙는다.
/// 규칙은 <see cref="LobbyCombatLedger"/>·<see cref="LobbyCombatStuckTracker"/>, 여기는 스폰·사망 구독·Warp·복제만 한다. 몬스터 코드·MapScene 존2 는 그대로.
/// <list type="bullet">
/// <item>시작 = 진행기가 T22(Activity Combat, <see cref="LobbyCombatRules.MonstersActive"/>, T20 Gather_Train02Start 전원 집결 확정 뒤)가 되는 순간 — 그 revision 을 기억한다:
/// 존2 구성(<see cref="LobbyTrainingCombatSpawns"/> — ZoneS_typeA 의 ZoneMonsterSpawnSet 직접 참조) 9마리 스폰 → 훈련 리셋
/// (<see cref="TrainingResetOptions.CombatStart"/> — 체력·쿨·대시, 분노 재지급 없음). TRAIN_01 표적은 그 단계를 떠날 때 이미 디스폰된다.</item>
/// <item>판정(서버): <see cref="MonsterDeathEvents.ServerMonsterDied"/> → 장부. 전원 처치 → 다음 프레임 <see cref="LobbyTrainingDirector.ServerReportActivityCompleted"/>(Combat, 시작 revision) 1회.</item>
/// <item>끼임 복구: 0.5초마다 <see cref="LobbyCombatStuckTracker"/> — 훈련실 경계(<see cref="roomNavMesh"/> 볼륨) 밖 또는 오래 멈춤 → 초기 위치 Warp. 처치 아님.</item>
/// <item>단계를 떠나면(완료 뒤 남은 몹 없음·그 밖 이탈)·진행기 디스폰(판 끝·씬 전환) 때 남은 몹 디스폰.</item>
/// <item>복제: 남은 수·전체 수(NetworkVariable) — <see cref="LobbyTrainingCombatView"/> 가 읽는다.</item>
/// </list>
/// NavMesh 는 훈련실 NavMeshSurface 에디터 사전 베이크(Docs/tech/lobby-training.md) — 런타임 베이크 없음.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingCombat : NetworkBehaviour
{
    private const string Tag = "[LobbyTraining/Train02]";
    private const float StuckTickInterval = 0.5f;

    [Tooltip("스폰 기준점 + 존2 구성 참조(LobbyTraining/Train02Spawns).")]
    [SerializeField] private LobbyTrainingCombatSpawns spawns;

    [Tooltip("훈련실 NavMeshSurface(사전 베이크) — 볼륨이 끼임 복구의 훈련실 경계다.")]
    [SerializeField] private NavMeshSurface roomNavMesh;

    private readonly NetworkVariable<int> remaining = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> total = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버
    private LobbyTrainingDirector _director;
    private readonly LobbyCombatLedger _ledger = new LobbyCombatLedger();
    private readonly LobbyCombatStuckTracker _stuck = new LobbyCombatStuckTracker();
    private readonly Dictionary<ulong, MonsterBase> _monsters = new Dictionary<ulong, MonsterBase>();
    private readonly List<LobbyCombatSpawn> _spawnBuffer = new List<LobbyCombatSpawn>();
    private readonly List<ulong> _idBuffer = new List<ulong>();
    private bool _pendingComplete;
    private int _runRevision;
    private float _stuckTimer;

    /// <summary>스폰된 일반 전투(없으면 null).</summary>
    public static LobbyTrainingCombat Active { get; private set; }

    /// <summary>[전 피어] 일반 전투 진행 중(T22).</summary>
    public bool IsRunning
    {
        get
        {
            var director = LobbyTrainingDirector.Active;
            return IsSpawned && director != null && LobbyCombatRules.MonstersActive(director.CurrentActivity);
        }
    }

    /// <summary>[전 피어] 남은 상대 수.</summary>
    public int Remaining => IsSpawned ? remaining.Value : 0;

    /// <summary>[전 피어] 이번 전투 상대 수(스폰 목록).</summary>
    public int Total => IsSpawned ? total.Value : 0;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Active = this;

        if (!IsServer)
            return;

        _director = GetComponent<LobbyTrainingDirector>();
        if (_director == null)
        {
            Debug.LogError($"{Tag} 같은 오브젝트에 LobbyTrainingDirector 가 없다 — 일반 전투가 동작하지 않는다.", this);
            return;
        }

        if (spawns == null || spawns.Source == null)
            Debug.LogWarning($"{Tag} 스폰 구성(LobbyTrainingCombatSpawns·ZoneS_typeA)이 비었다 — TRAIN_02 가 끝나지 않는다.", this);
        if (roomNavMesh == null || roomNavMesh.navMeshData == null)
            Debug.LogWarning($"{Tag} 훈련실 NavMeshSurface/베이크 데이터가 없다 — 몬스터가 움직이지 못한다(Docs/tech/lobby-training.md 재베이크).", this);

        _director.ServerFlowChanged += EvaluateServer;
        MonsterDeathEvents.ServerMonsterDied += HandleMonsterDiedServer;

        // 진행기가 먼저 스폰돼 첫 알림을 놓쳤을 수 있다 — 지금 상태로 한 번 맞춘다.
        EvaluateServer();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (_director != null)
                _director.ServerFlowChanged -= EvaluateServer;
            MonsterDeathEvents.ServerMonsterDied -= HandleMonsterDiedServer;

            if (_ledger.IsRunning || _monsters.Count > 0)
                EndServer("진행기 디스폰");
            _director = null;
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

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;

        if (_pendingComplete)
        {
            // 사망 콜백(데미지 판정 도중) 안에서 단계를 넘기지 않는다 — 다음 프레임에 보고한다.
            _pendingComplete = false;
            if (_director != null && _director.ServerReportActivityCompleted(LobbyTutorialActivity.Combat, _runRevision))
                Debug.Log($"[SceneFlow] {Tag} 전원 처치 {_ledger.KilledCount}/{_ledger.Total} — T22 완료 보고 (끼임 복구 {_ledger.RecoveryCount}회)");
        }

        if (!_ledger.IsRunning)
            return;

        _stuckTimer -= Time.deltaTime;
        if (_stuckTimer > 0f)
            return;

        _stuckTimer = StuckTickInterval;
        TickStuckServer();
    }

    // ─── 서버: 시작·정리 ────────────────────────────────────────────────

    private void EvaluateServer()
    {
        if (_director == null)
            return;

        var active = LobbyCombatRules.MonstersActive(_director.CurrentActivity);
        if (active && !_ledger.IsRunning)
            BeginServer();
        else if (!active && (_ledger.IsRunning || _monsters.Count > 0))
            EndServer($"단계 이탈 → {_director.DescribeStep()}");
    }

    private void BeginServer()
    {
        _runRevision = _director.Revision;
        _pendingComplete = false;
        _monsters.Clear();
        _stuck.Clear();

        var ids = new List<ulong>();
        var now = Time.timeAsDouble;
        if (spawns != null && spawns.Resolve(_spawnBuffer))
        {
            foreach (var spawn in _spawnBuffer)
            {
                var monster = SpawnMonsterServer(spawn);
                if (monster == null)
                    continue;

                ids.Add(monster.NetworkObjectId);
                _monsters[monster.NetworkObjectId] = monster;
                _stuck.Track(monster.NetworkObjectId, spawn.Position, now);
            }
        }

        _ledger.Begin(ids);
        SyncCounts();
        _director.ServerResetTrainingRoster(TrainingResetOptions.CombatStart);
        Debug.Log($"[SceneFlow] {Tag} 일반 전투 시작 — 구성 {_spawnBuffer.Count}마리 중 스폰 {_ledger.Total}마리, 기준점 {(spawns != null ? spawns.transform.position.ToString() : "(없음)")}");
        if (_ledger.Total == 0)
            Debug.LogError($"{Tag} 스폰된 몬스터가 없다 — TRAIN_02 를 끝낼 수 없다(구성·NetworkPrefabs 확인).", this);
    }

    private MonsterBase SpawnMonsterServer(LobbyCombatSpawn spawn)
    {
        if (spawn.Prefab == null)
        {
            Debug.LogWarning($"{Tag} 지점 {spawn.PointName} 프리팹이 비었다 — 건너뜀.", this);
            return null;
        }

        if (spawn.NoNavMesh)
            Debug.LogError($"{Tag} 지점 {spawn.PointName} 후보 {spawn.Candidate} 주변에 NavMesh 가 없다 — 기준점에 세운다(훈련실 NavMesh 재베이크 필요).", this);
        else if (spawn.Snapped)
            Debug.Log($"{Tag} 지점 {spawn.PointName} 후보 {spawn.Candidate} 가 NavMesh 밖(훈련실 폭 초과) — 가장 가까운 NavMesh 점 {spawn.Position} 에 세운다.");

        var go = Instantiate(spawn.Prefab, spawn.Position, spawn.Rotation);
        // 활성 씬이 로비가 아닐 때도 로비 씬과 같이 정리되게 기준점의 씬으로 옮긴다.
        SceneManager.MoveGameObjectToScene(go, spawns.gameObject.scene);
        go.name = $"{spawn.Prefab.name}_Train02_{spawn.PointName}";
        var no = go.GetComponent<NetworkObject>();
        var monster = go.GetComponent<MonsterBase>();
        if (no == null || monster == null)
        {
            Debug.LogError($"{Tag} 프리팹 '{spawn.Prefab.name}' 에 NetworkObject/MonsterBase 가 없다.", this);
            Destroy(go);
            return null;
        }

        no.Spawn(true);
        return monster;
    }

    private void EndServer(string reason)
    {
        var killed = _ledger.KilledCount;
        var all = _ledger.Total;
        var recoveries = _ledger.RecoveryCount;
        _ledger.Stop(_idBuffer);
        _stuck.Clear();
        _pendingComplete = false;

        // 살아 남은 몹만 — 처치된 몹은 사망 연출 뒤 스스로 디스폰한다(마지막 처치 연출을 끊지 않는다).
        var n = 0;
        foreach (var id in _idBuffer)
        {
            if (!_monsters.TryGetValue(id, out var monster) || monster == null || !monster.IsSpawned)
                continue;

            monster.NetworkObject.Despawn(true);
            n++;
        }

        _monsters.Clear();
        SyncCounts();
        Debug.Log($"[SceneFlow] {Tag} 일반 전투 정리({reason}) — 처치 {killed}/{all}, 끼임 복구 {recoveries}회, 남은 몹 {n}마리 디스폰");
    }

    // ─── 서버: 판정 ────────────────────────────────────────────────────

    private void HandleMonsterDiedServer(Unit unit)
    {
        if (!(unit is MonsterBase monster) || !monster.IsSpawned)
            return;

        var id = monster.NetworkObjectId;
        if (!_ledger.MarkDead(id))
            return;

        _stuck.Forget(id);
        SyncCounts();
        Debug.Log($"[SceneFlow] {Tag} {monster.name} 처치 — 남은 {_ledger.RemainingCount}/{_ledger.Total}");
        if (_ledger.TryConsumeAllDead())
            _pendingComplete = true;
    }

    private void TickStuckServer()
    {
        var room = RoomBounds();
        if (!room.HasValue)
            return;

        var now = Time.timeAsDouble;
        _ledger.GetAlive(_idBuffer);
        foreach (var id in _idBuffer)
        {
            if (!_monsters.TryGetValue(id, out var monster) || monster == null || !monster.IsSpawned || monster.CurrentHealth <= 0)
                continue;

            var position = monster.transform.position;
            var verdict = _stuck.Evaluate(id, now, position, room.Value, NearestPlayerDistance(position));
            if (verdict == LobbyCombatStuckVerdict.None || !_stuck.TryGetHome(id, out var home))
                continue;

            // 넉백 등으로 에이전트가 꺼져 있으면 다음 틱에 — 끝나면 몬스터가 다시 켠다.
            var agent = monster.GetComponent<NavMeshAgent>();
            if (agent == null || !agent.enabled)
                continue;

            if (!agent.Warp(home))
            {
                Debug.LogWarning($"{Tag} {monster.name} 끼임 복구 Warp 실패 — {position} → {home}", monster);
                continue;
            }

            _stuck.OnRecovered(id, now);
            _ledger.RecordRecovery(id);
            Debug.Log($"[SceneFlow] {Tag} {monster.name} 끼임 복구({verdict}) — {position} → 초기 위치 {home} (처치 아님, 남은 {_ledger.RemainingCount}/{_ledger.Total})");
        }
    }

    private Bounds? RoomBounds()
    {
        if (roomNavMesh == null)
            return null;

        var t = roomNavMesh.transform;
        return new Bounds(t.TransformPoint(roomNavMesh.center), Vector3.Scale(roomNavMesh.size, t.lossyScale));
    }

    private float NearestPlayerDistance(Vector3 position)
    {
        var best = float.PositiveInfinity;
        if (NetworkManager == null)
            return best;

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (player == null || player.CurrentHealth <= 0)
                continue;

            best = Mathf.Min(best, LobbyCombatStuckTracker.HorizontalDistance(position, player.transform.position));
        }

        return best;
    }

    private void SyncCounts()
    {
        if (remaining.Value != _ledger.RemainingCount)
            remaining.Value = _ledger.RemainingCount;
        if (total.Value != _ledger.Total)
            total.Value = _ledger.Total;
    }
}
