using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 한 판의 플레이 시간과 플레이어별 통계(데미지·처치·간파·사망)를 집계한다. MapScene에 하나 배치한다.
///
/// 서버(또는 오프라인)에서만 집계한다. 판이 끝나는 지점에서 <see cref="Capture(SessionOutcome)"/>를 호출해
/// <see cref="SessionResult"/>에 확정한다 — 전멸 PartyWipeWatcher · 클리어 BossEncounterDirector ·
/// 중도 종료 MapSceneManager.GoToResult(ExitButton). 첫 확정만 반영된다.
///
/// 입력은 정적 채널뿐이다(몬스터·Unit 에 통계 의존성을 심지 않는다):
///   · <see cref="CombatStatsEvents.ServerDamageApplied"/> — 데미지·처치(막타)
///   · <see cref="CombatStatsEvents.ServerCounterSucceeded"/> — 간파 성공
///   · <see cref="PlayerLifeCycleController.ServerLifeStateChanged"/> — 행 등록·사망
/// 필터 규칙은 <see cref="SessionStatsAggregator"/> 가 고정한다. 여기서는 대상 분류만 한다.
/// </summary>
public sealed class SessionStatsTracker : MonoBehaviour
{
    public static SessionStatsTracker Active { get; private set; }

    [Tooltip("집계 시작을 플레이어 스폰까지 기다린다. 끄면 씬 시작부터 센다.")]
    [SerializeField] private bool startOnFirstPlayer = true;

    private readonly SessionStatsAggregator _aggregator = new SessionStatsAggregator();
    private readonly List<PlayerSessionStats> _snapshot = new List<PlayerSessionStats>();

    private float _startTime;
    private bool _running;
    private bool _captured;

    public float ElapsedSeconds => _running ? Time.time - _startTime : 0f;

    private void Awake()
    {
        Active = this;
        SessionResult.Clear(); // 새 판 진입 시 이전 결과 잔류 제거
    }

    private void OnEnable()
    {
        CombatStatsEvents.ServerDamageApplied += HandleDamageApplied;
        CombatStatsEvents.ServerCounterSucceeded += HandleCounterSucceeded;
        PlayerLifeCycleController.ServerLifeStateChanged += HandleLifeStateChanged;
    }

    private void OnDisable()
    {
        CombatStatsEvents.ServerDamageApplied -= HandleDamageApplied;
        CombatStatsEvents.ServerCounterSucceeded -= HandleCounterSucceeded;
        PlayerLifeCycleController.ServerLifeStateChanged -= HandleLifeStateChanged;

        if (Active == this)
            Active = null;
    }

    private void Update()
    {
        if (_running || _captured || !IsCountingAuthority())
            return;

        if (startOnFirstPlayer && !HasAnyPlayer())
            return;

        _startTime = Time.time;
        _running = true;
    }

    /// <summary>호환용 — 클리어/전멸 두 갈래만 아는 호출부(PartyWipeWatcher·BossEncounterDirector).</summary>
    public void Capture(bool cleared)
    {
        Capture(cleared ? SessionOutcome.Cleared : SessionOutcome.Wiped);
    }

    /// <summary>판 종료 확정. 여러 번 호출돼도 첫 호출만 반영한다. 누른 시점까지의 값을 그대로 쓴다.</summary>
    public void Capture(SessionOutcome outcome)
    {
        if (SessionResult.HasValue)
            return;

        // 접속 중인데 아직 행이 없는 플레이어(맞지도 때리지도 않았고 스폰 통지도 못 받은 경우)도 행을 남긴다.
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && networkManager.IsServer)
        {
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
                RememberPlayer(clientId);
        }

        _aggregator.Snapshot(_snapshot);
        SessionResult.Capture(outcome, ElapsedSeconds, _snapshot);
        _running = false;
        _captured = true;
    }

    private void HandleDamageApplied(Unit target, ulong attackerClientId, int amount, bool lethal)
    {
        if (_captured || !IsCountingAuthority())
            return;

        _aggregator.RecordDamage(attackerClientId, IsCountedMonster(target), amount, lethal);
    }

    private void HandleCounterSucceeded(Unit target, ulong attackerClientId)
    {
        if (_captured || !IsCountingAuthority())
            return;

        _aggregator.RecordCounterSuccess(attackerClientId);
    }

    private void HandleLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (_captured || !IsCountingAuthority())
            return;

        RememberPlayer(clientId);
        _aggregator.RecordLifeStateChange(clientId, previous, current);
    }

    // 행 등록 + 캐릭터 id 캐시. 스토어는 접속이 끊기면 그 clientId 를 지우므로(NetworkSessionLauncher)
    // 스폰 시점에 미리 받아 둔다 — 판 중 끊긴 플레이어 행도 캐릭터가 남는다.
    private void RememberPlayer(ulong clientId)
    {
        _aggregator.RegisterPlayer(clientId);
        if (ServerCharacterSelectionStore.TryGet(clientId, out int characterId))
            _aggregator.SetCharacterId(clientId, characterId);
    }

    // 집계 대상 = MonsterBase 파생만. 더미(TrainingDummy)·송전탑(BossChargingPylon)·ChargingObject·Player 는
    // 전부 Unit 직계라 여기서 빠진다.
    private static bool IsCountedMonster(Unit target)
    {
        return target is MonsterBase;
    }

    private static bool IsCountingAuthority()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private static bool HasAnyPlayer()
    {
        return FindAnyObjectByType<PlayerLifeCycleController>(FindObjectsInactive.Exclude) != null;
    }
}
