using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 한 판의 생존 시간·처치 수를 집계한다. MapScene에 하나 배치한다.
///
/// 서버(또는 오프라인)에서만 집계한다. 판이 끝나는 지점(전멸 또는 클리어)에서
/// <see cref="Capture"/>를 호출해 <see cref="SessionResult"/>에 확정한다.
/// 호출자는 PartyWipeWatcher(전멸)이고, 보스 격파 쪽은 클리어로 호출하면 된다.
/// </summary>
public sealed class SessionStatsTracker : MonoBehaviour
{
    public static SessionStatsTracker Active { get; private set; }

    [Tooltip("집계 시작을 플레이어 스폰까지 기다린다. 끄면 씬 시작부터 센다.")]
    [SerializeField] private bool startOnFirstPlayer = true;

    private float _startTime;
    private bool _running;
    private int _kills;

    public float ElapsedSeconds => _running ? Time.time - _startTime : 0f;
    public int Kills => _kills;

    private void Awake()
    {
        Active = this;
        SessionResult.Clear(); // 새 판 진입 시 이전 결과 잔류 제거
    }

    private void OnEnable()
    {
        MonsterDeathEvents.ServerMonsterDied += HandleMonsterDied;
    }

    private void OnDisable()
    {
        MonsterDeathEvents.ServerMonsterDied -= HandleMonsterDied;

        if (Active == this)
            Active = null;
    }

    private void Update()
    {
        if (_running || !IsCountingAuthority())
            return;

        if (startOnFirstPlayer && !HasAnyPlayer())
            return;

        _startTime = Time.time;
        _running = true;
    }

    /// <summary>판 종료 확정. 여러 번 호출돼도 첫 호출만 반영한다.</summary>
    public void Capture(bool cleared)
    {
        if (SessionResult.HasValue)
            return;

        SessionResult.Capture(cleared, ElapsedSeconds, _kills);
        _running = false;
    }

    private void HandleMonsterDied(Unit unit)
    {
        if (!IsCountingAuthority())
            return;

        _kills++;
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
