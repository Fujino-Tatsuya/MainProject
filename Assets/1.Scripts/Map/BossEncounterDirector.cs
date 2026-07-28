using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 보스 등장 연출과 전투 전환의 <b>단일 소유자</b>(서버 권한).
/// (승인 계획 <c>Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md</c> Task 3)
///
/// 소유 범위:
/// <list type="bullet">
/// <item><see cref="BossTeleportManager.AlivePlayersArrived"/>를 받아 참가자를 스냅샷한다(생존자만).</item>
/// <item>참가자를 <see cref="PlayerEncounterLock"/>으로 잠그고, 보스를 <b>한 번만</b> NetworkSpawn한다.</item>
/// <item>상공 → 착지점 하강을 서버가 구동하고, 착지 후 전투로 전환한다.</item>
/// <item>보스룸 충전 기둥 4개를 보스의 <see cref="ChargeController"/>에 주입한다(패턴 재사용 전제).</item>
/// <item><see cref="EnemyBTActivator.OpenBT"/> 호출과 참가자 잠금 해제를 한 전환점에서 수행한다.</item>
/// <item>보스 사망을 클리어 판정(<see cref="SessionStatsTracker"/>)과 결과 화면 전환에 연결한다.</item>
/// </list>
///
/// 씬에 하나만 배치한다(MapScene 상주 NetworkObject). MapScene에는 보스를 스폰하는 다른 주체가
/// 없어야 한다 — <c>TwentyThreeArenaContext</c>는 KMKScene·PlayerBossTest 전용이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossEncounterDirector : NetworkBehaviour
{
    [Header("보스")]
    [Tooltip("스폰할 보스 프리팹(NetworkObject 필수). No.23 = TwentyThree.prefab")]
    [SerializeField] private GameObject bossPrefab;

    [Tooltip("착지 지점. 비어 있으면 bossroom의 BossLandingPoint를 이름으로 찾는다.")]
    [SerializeField] private Transform bossLandingPoint;

    [Tooltip("착지점 기준 스폰 높이(m).")]
    [SerializeField, Min(1f)] private float spawnHeight = 18f;

    [Header("연출 타이밍")]
    [Tooltip("하강에 걸리는 시간(초).")]
    [SerializeField, Min(0.1f)] private float descendDuration = 1.2f;

    [Tooltip("하강 보간 곡선. 0~1 정규화. 끝을 급하게 하면 낙하감이 커진다.")]
    [SerializeField] private AnimationCurve descendCurve =
        new AnimationCurve(new Keyframe(0f, 0f, 0f, 0.4f), new Keyframe(1f, 1f, 2.2f, 0f));

    [Tooltip("착지 후 전투 시작까지 정지 시간(초).")]
    [SerializeField, Min(0f)] private float impactHoldSeconds = 0.9f;

    [Header("충전 기둥 (bossroom)")]
    [Tooltip("보스룸 충전 기둥. 정확히 4개를 넣는다. 보스 스폰 직후 ChargeController에 주입한다.")]
    [SerializeField] private List<ChargingObject> chargingObjects = new List<ChargingObject>();

    [Header("클리어 판정")]
    [Tooltip("보스 격파 시 결과 화면으로 전환한다. 비어 있으면 씬에서 찾는다.")]
    [SerializeField] private MapSceneManager mapSceneManager;

    [Tooltip("보스 격파 후 결과 화면 전환까지 대기 시간(초). 사망 연출 여유분.")]
    [SerializeField, Min(0f)] private float defeatResultDelaySeconds = 3f;

    private readonly NetworkVariable<BossEncounterPhase> phase =
        new NetworkVariable<BossEncounterPhase>(
            BossEncounterPhase.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> phaseStartServerTime =
        new NetworkVariable<double>(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> eligibleCount =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// <summary>현재 연출 단계(전 피어). HUD·로컬 피드백이 구독한다.</summary>
    public BossEncounterPhase Phase => phase.Value;

    /// <summary>현재 단계 시작 서버 시간. 로컬 연출 보간용.</summary>
    public double PhaseStartServerTime => phaseStartServerTime.Value;

    /// <summary>연출 대상 참가자 수(전 피어). 스킵 분수 표시용.</summary>
    public int EligibleCount => eligibleCount.Value;

    public static BossEncounterDirector Instance { get; private set; }

    private readonly List<ulong> _eligibleClientIds = new List<ulong>();

    private BossTeleportManager _teleportManager;
    private NetworkObject _bossNetworkObject;
    private Unit _bossUnit;
    private EnemyBTActivator _btActivator;
    private ChargeController _chargeController;
    private JumpController _jumpController;
    private NavMeshAgent _bossAgent;

    private Vector3 _descendFrom;
    private Vector3 _descendTo;
    private bool _combatStarted;
    private bool _defeatHandled;
    private double _resultTransitionAt;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        ResolveSceneReferences();

        if (_teleportManager != null)
        {
            _teleportManager.AlivePlayersArrived += HandleAlivePlayersArrived;
            _teleportManager.ArrivalAborted += HandleArrivalAborted;
        }
        else
        {
            Edit.LogError("[BossEncounter] BossTeleportManager를 찾지 못했습니다 — 도착 신호를 받을 수 없습니다.", this);
        }

        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;

        ValidateSetup();
    }

    public override void OnNetworkDespawn()
    {
        // 씬 전환 중 콜백이 남으면 사라진 오브젝트를 만지게 된다 — 반드시 대칭 해제.
        if (_teleportManager != null)
        {
            _teleportManager.AlivePlayersArrived -= HandleAlivePlayersArrived;
            _teleportManager.ArrivalAborted -= HandleArrivalAborted;
        }

        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnect;

        UnsubscribeBossDeath();

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    private void ResolveSceneReferences()
    {
        if (_teleportManager == null)
            _teleportManager = BossTeleportManager.Instance != null
                ? BossTeleportManager.Instance
                : FindFirstObjectByType<BossTeleportManager>();

        if (mapSceneManager == null)
            mapSceneManager = FindFirstObjectByType<MapSceneManager>();

        if (bossLandingPoint == null)
            bossLandingPoint = FindLandingPointByName();
    }

    private static Transform FindLandingPointByName()
    {
        GameObject found = GameObject.Find("BossLandingPoint");
        return found != null ? found.transform : null;
    }

    private void ValidateSetup()
    {
        if (bossPrefab == null)
            Edit.LogError("[BossEncounter] bossPrefab이 비어 있습니다 — 보스가 스폰되지 않습니다.", this);
        else if (bossPrefab.GetComponent<NetworkObject>() == null)
            Edit.LogError($"[BossEncounter] {bossPrefab.name}에 NetworkObject가 없습니다.", this);

        if (bossLandingPoint == null)
            Edit.LogError("[BossEncounter] 착지점(BossLandingPoint)을 찾지 못했습니다.", this);

        chargingObjects.RemoveAll(o => o == null);
        if (chargingObjects.Count != ChargePillarCount)
        {
            Edit.LogWarning(
                $"[BossEncounter] 충전 기둥이 {chargingObjects.Count}개입니다(기대 {ChargePillarCount}). " +
                "충전 패턴이 인원수만큼 활성되지 않을 수 있습니다.", this);
        }
    }

    private const int ChargePillarCount = 4;

    // ── 연출 진입 ──────────────────────────────────────────────────────────

    private void HandleAlivePlayersArrived(IReadOnlyList<ulong> arrivedClientIds)
    {
        if (!IsServer)
            return;

        if (phase.Value != BossEncounterPhase.Idle && phase.Value != BossEncounterPhase.FailedSafe)
        {
            Edit.LogWarning($"[BossEncounter] 이미 진행 중({phase.Value})이라 도착 신호를 무시합니다.", this);
            return;
        }

        SnapshotEligible(arrivedClientIds);

        if (_eligibleClientIds.Count == 0)
        {
            Edit.LogWarning("[BossEncounter] 생존 참가자가 없어 연출을 시작하지 않습니다.", this);
            SetPhase(BossEncounterPhase.Idle);
            return;
        }

        SetPhase(BossEncounterPhase.Preparing);

        // 잠금을 먼저 걸어야 스폰 직후 보스 판정·플레이어 행동이 겹치지 않는다.
        for (int i = 0; i < _eligibleClientIds.Count; i++)
            GetLock(_eligibleClientIds[i])?.BeginCinematicServer();

        if (!SpawnBossServer())
        {
            AbortEncounterServer("보스 스폰 실패");
            return;
        }

        BeginDescent();
    }

    private void HandleArrivalAborted()
    {
        if (!IsServer)
            return;

        // 도착 실패는 연출 진입 전이다. 진행 중 상태만 정리한다.
        if (phase.Value != BossEncounterPhase.Idle)
            AbortEncounterServer("도착 ACK 실패");
    }

    /// <summary>
    /// 도착 목록을 다시 검증해 <b>Alive 참가자만</b> 스냅샷한다.
    /// Soul을 넣으면 연출 중 목숨 소진으로 PermanentDead가 되며 PartyWipeWatcher가
    /// Director 아래에서 결과 화면으로 전환할 수 있다(계획 Revised Premises 5).
    /// </summary>
    private void SnapshotEligible(IReadOnlyList<ulong> arrivedClientIds)
    {
        _eligibleClientIds.Clear();

        if (arrivedClientIds == null)
            return;

        for (int i = 0; i < arrivedClientIds.Count; i++)
        {
            ulong clientId = arrivedClientIds[i];
            if (IsAliveParticipant(clientId))
                _eligibleClientIds.Add(clientId);
        }

        eligibleCount.Value = _eligibleClientIds.Count;
    }

    private bool IsAliveParticipant(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            return false;

        NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
        if (playerObject == null || !playerObject.IsSpawned)
            return false;

        PlayerLifeCycleController lifeCycle = playerObject.GetComponent<PlayerLifeCycleController>();
        return lifeCycle == null || lifeCycle.State == PlayerLifeState.Alive;
    }

    private PlayerEncounterLock GetLock(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            return null;

        NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
        return playerObject != null ? playerObject.GetComponent<PlayerEncounterLock>() : null;
    }

    // ── 보스 스폰 / 하강 ───────────────────────────────────────────────────

    private bool SpawnBossServer()
    {
        if (_bossNetworkObject != null && _bossNetworkObject.IsSpawned)
        {
            Edit.LogWarning("[BossEncounter] 보스가 이미 스폰되어 있어 재스폰하지 않습니다.", this);
            return true;
        }

        if (bossPrefab == null || bossLandingPoint == null)
            return false;

        Vector3 landing = bossLandingPoint.position;
        Vector3 spawnPosition = landing + Vector3.up * spawnHeight;

        GameObject bossInstance = Instantiate(bossPrefab, spawnPosition, bossLandingPoint.rotation);
        _bossNetworkObject = bossInstance.GetComponent<NetworkObject>();
        if (_bossNetworkObject == null)
        {
            Edit.LogError("[BossEncounter] 보스 프리팹에 NetworkObject가 없습니다.", this);
            Destroy(bossInstance);
            return false;
        }

        CacheBossComponents(bossInstance);

        _bossNetworkObject.Spawn();

        // ⚠️ 순서 주의: RunningOnlyOnServer가 OnNetworkSpawn에서 navMeshAgent.enabled = IsServer로
        // 되돌린다. 그래서 에이전트 차단은 반드시 Spawn 이후다 — 켜진 상태로 두면 상공의 보스를
        // NavMesh로 끌어내려 하강 연출과 싸운다.
        if (_bossAgent != null)
            _bossAgent.enabled = false;

        // 연출 착지가 전투 판정·장판을 만들지 않게 한다.
        _jumpController?.SetCinematicLandingMode(true);

        // 충전 기둥은 스폰 이후에 주입해야 ChargeController의 서버 게이트를 통과한다.
        InjectChargingObjects();
        SubscribeBossDeath();

        _descendFrom = spawnPosition;
        _descendTo = landing;

        Edit.Log(
            $"[BossEncounter] 보스 스폰 — 참가자 {_eligibleClientIds.Count}명, " +
            $"착지점 {landing}, 스폰 높이 {spawnHeight}m", this);
        return true;
    }

    private void CacheBossComponents(GameObject bossInstance)
    {
        _bossUnit = bossInstance.GetComponent<Unit>();
        _btActivator = bossInstance.GetComponent<EnemyBTActivator>();
        _chargeController = bossInstance.GetComponentInChildren<ChargeController>(true);
        _jumpController = bossInstance.GetComponentInChildren<JumpController>(true);
        _bossAgent = bossInstance.GetComponent<NavMeshAgent>();

        if (_bossUnit == null)
            Edit.LogError("[BossEncounter] 보스에 Unit이 없어 사망(클리어) 판정을 연결할 수 없습니다.", this);

        if (_btActivator == null)
            Edit.LogError("[BossEncounter] 보스에 EnemyBTActivator가 없어 전투를 시작할 수 없습니다.", this);

        if (_chargeController == null)
            Edit.LogWarning("[BossEncounter] 보스에 ChargeController가 없습니다 — 충전 패턴이 동작하지 않습니다.", this);

        if (_bossAgent == null)
            Edit.LogWarning("[BossEncounter] 보스에 NavMeshAgent가 없습니다 — 이동 확인이 필요합니다.", this);
    }

    private void InjectChargingObjects()
    {
        if (_chargeController == null)
            return;

        chargingObjects.RemoveAll(o => o == null);
        if (chargingObjects.Count == 0)
        {
            Edit.LogError(
                "[BossEncounter] 충전 기둥 목록이 비어 있습니다 — 충전 패턴에서 " +
                "ChargeController가 오류를 냅니다. bossroom의 ChargingObject 4개를 연결하세요.", this);
            return;
        }

        _chargeController.SetList(chargingObjects);
        Edit.Log($"[BossEncounter] 충전 기둥 {chargingObjects.Count}개 주입 완료.", this);
    }

    private void BeginDescent()
    {
        MoveBoss(_descendFrom);
        SetPhase(BossEncounterPhase.Descending);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned)
            return;

        TickResultTransition();

        if (!IsCinematicPhase(phase.Value))
            return;

        // 사망·연결 해제로 참가자가 사라지면 연출을 붙잡고 있지 않는다.
        PruneEligible();
        if (_eligibleClientIds.Count == 0)
        {
            AbortEncounterServer("참가자 전원 이탈");
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        double elapsed = now - phaseStartServerTime.Value;

        switch (phase.Value)
        {
            case BossEncounterPhase.Descending:
                TickDescending(elapsed);
                break;

            case BossEncounterPhase.Impact:
                if (elapsed >= impactHoldSeconds)
                    BeginCombatServer();
                break;
        }
    }

    private void TickDescending(double elapsed)
    {
        float t = descendDuration <= 0f ? 1f : Mathf.Clamp01((float)(elapsed / descendDuration));
        float eased = descendCurve != null ? descendCurve.Evaluate(t) : t;
        MoveBoss(Vector3.LerpUnclamped(_descendFrom, _descendTo, eased));

        if (t < 1f)
            return;

        MoveBoss(_descendTo);
        SetPhase(BossEncounterPhase.Impact);
    }

    private void MoveBoss(Vector3 position)
    {
        if (_bossNetworkObject == null)
            return;

        _bossNetworkObject.transform.position = position;
    }

    // ── 전투 전환 / 중단 ───────────────────────────────────────────────────

    /// <summary>
    /// 모든 종료 경로(정상 완료·스킵·오류)가 수렴하는 단일 전환점. 멱등.
    /// </summary>
    public void BeginCombatServer()
    {
        if (!IsServer || _combatStarted)
            return;

        _combatStarted = true;

        SnapBossToNavMesh();

        // 연출 가드 해제가 먼저다 — BT가 열린 뒤 첫 패턴이 판정 없이 지나가지 않게.
        _jumpController?.SetCinematicLandingMode(false);

        UnlockAllParticipants();

        _btActivator?.OpenBT();

        SetPhase(BossEncounterPhase.Combat);
        Edit.Log($"[BossEncounter] 전투 시작 — 참가자 {_eligibleClientIds.Count}명, BT 개방.", this);
    }

    /// <summary>연출을 안전하게 중단한다. 참가자 잠금을 풀고 보스를 되돌린다. 멱등.</summary>
    public void AbortEncounterServer(string reason)
    {
        if (!IsServer)
            return;

        Edit.LogWarning($"[BossEncounter] 연출 중단 — {reason}", this);

        UnlockAllParticipants();
        DespawnBoss();

        _eligibleClientIds.Clear();
        eligibleCount.Value = 0;
        _combatStarted = false;

        SetPhase(BossEncounterPhase.FailedSafe);
    }

    private void SnapBossToNavMesh()
    {
        if (_bossNetworkObject == null)
            return;

        MoveBoss(_descendTo);

        if (_bossAgent == null)
            return;

        _bossAgent.enabled = true;

        if (NavMesh.SamplePosition(_descendTo, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            _bossAgent.Warp(hit.position);
            return;
        }

        Edit.LogError(
            $"[BossEncounter] 착지점 {_descendTo} 주변 5m에 NavMesh가 없습니다 — " +
            "보스가 이동하지 못합니다(보스룸 바닥이 베이크에 포함됐는지 확인).", this);
    }

    private void UnlockAllParticipants()
    {
        // 잠금은 참가자 스냅샷이 아니라 현재 연결된 전원 기준으로 푼다 —
        // 스냅샷에서 빠진 플레이어가 잠금만 남은 채로 조작 불가가 되는 것을 막는다.
        if (NetworkManager == null)
            return;

        foreach (KeyValuePair<ulong, NetworkClient> pair in NetworkManager.ConnectedClients)
        {
            NetworkObject playerObject = pair.Value?.PlayerObject;
            if (playerObject == null)
                continue;

            playerObject.GetComponent<PlayerEncounterLock>()?.EndCinematicServer();
        }
    }

    private void DespawnBoss()
    {
        UnsubscribeBossDeath();

        if (_bossNetworkObject != null && _bossNetworkObject.IsSpawned)
            _bossNetworkObject.Despawn();

        _bossNetworkObject = null;
        _bossUnit = null;
        _btActivator = null;
        _chargeController = null;
        _jumpController = null;
        _bossAgent = null;
    }

    private void PruneEligible()
    {
        for (int i = _eligibleClientIds.Count - 1; i >= 0; i--)
        {
            if (!IsAliveParticipant(_eligibleClientIds[i]))
                _eligibleClientIds.RemoveAt(i);
        }

        if (eligibleCount.Value != _eligibleClientIds.Count)
            eligibleCount.Value = _eligibleClientIds.Count;
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        if (!IsServer)
            return;

        _eligibleClientIds.Remove(clientId);
        eligibleCount.Value = _eligibleClientIds.Count;
    }

    // ── 클리어 판정 ────────────────────────────────────────────────────────

    private void SubscribeBossDeath()
    {
        if (_bossUnit != null)
            _bossUnit.Died += HandleBossDefeated;
    }

    private void UnsubscribeBossDeath()
    {
        if (_bossUnit != null)
            _bossUnit.Died -= HandleBossDefeated;
    }

    private void HandleBossDefeated()
    {
        if (!IsServer || _defeatHandled)
            return;

        _defeatHandled = true;

        // 결과 화면이 읽을 값을 씬 전환 전에 확정한다(전멸 경로는 PartyWipeWatcher가 false로 확정).
        SessionStatsTracker.Active?.Capture(cleared: true);

        // 충전 기둥이 올라와 있는 채로 끝날 수 있어 원위치로 되돌린다.
        _chargeController?.EndCharge();

        _resultTransitionAt = NetworkManager.ServerTime.Time + Mathf.Max(0f, defeatResultDelaySeconds);
        Edit.Log("[BossEncounter] 보스 격파 — 클리어로 기록, 결과 화면 전환 예약.", this);
    }

    private void TickResultTransition()
    {
        if (_resultTransitionAt <= 0d || NetworkManager.ServerTime.Time < _resultTransitionAt)
            return;

        _resultTransitionAt = 0d;

        if (mapSceneManager == null)
            mapSceneManager = FindFirstObjectByType<MapSceneManager>();

        if (mapSceneManager == null)
        {
            Edit.LogError("[BossEncounter] MapSceneManager가 없어 결과 화면으로 전환하지 못했습니다.", this);
            return;
        }

        mapSceneManager.GoToResult();
    }

    // ── 공통 ───────────────────────────────────────────────────────────────

    private static bool IsCinematicPhase(BossEncounterPhase value)
    {
        return value == BossEncounterPhase.Preparing ||
            value == BossEncounterPhase.Descending ||
            value == BossEncounterPhase.Impact ||
            value == BossEncounterPhase.Dialogue;
    }

    private void SetPhase(BossEncounterPhase next)
    {
        BossEncounterPhase previous = phase.Value;
        phase.Value = next;
        phaseStartServerTime.Value = NetworkManager.ServerTime.Time;

        Edit.Log(
            $"[BossEncounter] phase {previous} → {next} " +
            $"(참가자 {_eligibleClientIds.Count})", this);
    }
}
