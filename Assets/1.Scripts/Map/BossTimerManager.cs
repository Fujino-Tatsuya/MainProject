using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스 제한시간(서버 권한). 맵 생성 완료부터 돌고, 만료되면 보스전을 강제 개시한다.
/// 승인 계획: <c>PLAN-boss-timer.md</c> (T1~T17).
///
/// 흐름:
///  1) 서버가 <see cref="MapGenerator.OnGenerated"/> 를 받아 만료 시각을 확정한다.
///  2) 만료 시각만 <see cref="NetworkVariable{T}"/> 로 복제한다 — 매 프레임 복제는 없다(대역폭 0).
///     클라는 자기 시계와의 차이로 게이지를 그린다(<see cref="BossTimerHUD"/>).
///  · 시간 도메인 = <see cref="NetworkClock.GameNow"/>(일시정지·슬로우 모션 제외) — 슬로우·솔로 일시정지 동안
///    제한시간도 덜 흐른다(PLAN-interrupt-slowmo D16). 세션 시계가 없을 때만 <c>ServerTime</c>(<see cref="TimerNow"/>).
///  3) 만료 → <see cref="BossTeleportManager.ForceStartEncounter"/>. 이미 진행 중이면 **미루고
///     만료 사실은 유지**한다 — 그 진행이 취소되면 다음 틱에 다시 시도한다.
///  4) 제한시간 안에 스스로 보스방에 도착하면(<see cref="BossTeleportManager.AlivePlayersArrived"/>)
///     타이머는 멈추고 게이지는 0 으로 비운다.
///
/// 🔴 <b>배치</b> — <see cref="BossTeleportManager"/> 와 **같은 씬 상주 GameObject** 에 붙인다.
///    (하나의 NetworkObject 에 NetworkBehaviour 가 여럿인 것은 정상이다.)
/// </summary>
[DisallowMultipleComponent]
public sealed class BossTimerManager : NetworkBehaviour
{
    public enum TimerState : byte
    {
        /// <summary>아직 안 돌거나(맵 생성 전) 끝났다. 게이지는 비어 있다.</summary>
        Idle = 0,
        /// <summary>제한시간이 도는 중.</summary>
        Running = 1,
        /// <summary>만료. 강제 이동 경고 중이거나 개시를 기다리는 중.</summary>
        Expired = 2,
        /// <summary>제때 보스방에 도착해 멈췄다. 게이지는 0(빈 상태)으로 둔다.</summary>
        Stopped = 3,
    }

    [Header("제한시간")]
    [Tooltip("맵 생성 완료부터 보스전 강제 개시까지의 시간(게임 시간 초 — 슬로우·일시정지 동안 덜 흐른다). 팀장 확정 = 5분.")]
    [SerializeField, Min(5f)] private float limitSeconds = 300f;

    [Tooltip("만료 후 강제 이동까지의 경고 시간(초). 기존 패드 카운트다운 표시를 그대로 재사용한다.")]
    [SerializeField, Min(0f)] private float forceWarnSeconds = 3f;

    [Header("표시")]
    [Tooltip("게이지가 차오르는 방향(0 → 1). 끄면 줄어드는 방향(1 → 0)이 된다. " +
             "🔴 기획 미확정이라 코드 수정 없이 뒤집을 수 있게 빼 둔 값이다(PLAN-boss-timer T2).")]
    [SerializeField] private bool fillUpAsTimePasses = true;

    // 만료 시각(TimerNow 도메인 = GameNow). 0 = 비활성. 서버 write / 모두 read.
    private readonly NetworkVariable<double> _expiresAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<TimerState> _state = new NetworkVariable<TimerState>(
        TimerState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static BossTimerManager Instance { get; private set; }

    public TimerState State => _state.Value;
    public float LimitSeconds => limitSeconds;
    public bool FillUpAsTimePasses => fillUpAsTimePasses;

    /// <summary>남은 시간(초). 안 도는 중이면 0.</summary>
    public float RemainingSeconds
    {
        get
        {
            if (_state.Value != TimerState.Running)
                return 0f;

            return (float)RemainingAt(_expiresAt.Value, TimerNow(NetworkManager));
        }
    }

    /// <summary>
    /// 제한시간의 시계. 세션 시계가 돌면 <see cref="NetworkClock.GameNow"/>, 없거나 안 돌면 <c>ServerTime</c>(기존 동작).
    /// 서버·클라가 같은 규칙으로 고르므로(같은 NetworkManager 프리팹) <c>_expiresAt</c> 과 비교값이 늘 같은 도메인이다.
    /// ⚠️ 클라 GameNow 는 슬로우 Start 를 편도 지연만큼 늦게 받아 서버와 잠깐 어긋난다(역행은 단조 클램프가 막는다) — 표시 오차로 허용.
    /// </summary>
    private static double TimerNow(NetworkManager nm)
    {
        NetworkClock clock = NetworkClock.Instance;
        bool clockRunning = clock != null && clock.IsRunning;
        return SelectNow(clockRunning, clockRunning ? clock.GameNow : 0d, nm != null ? nm.ServerTime.Time : 0d);
    }

    // ---- 순수 계산(EditMode 테스트 대상) ----

    /// <summary>세션 시계가 돌면 GameNow, 아니면 ServerTime.</summary>
    public static double SelectNow(bool sessionClockRunning, double gameNow, double serverTime) =>
        sessionClockRunning ? gameNow : serverTime;

    /// <summary>남은 시간(초). 만료 시각 ≤ 0 = 비활성 → 0. 음수는 0.</summary>
    public static double RemainingAt(double expiresAt, double now) =>
        expiresAt <= 0d ? 0d : System.Math.Max(0d, expiresAt - now);

    /// <summary>만료 판정. 만료 시각 ≤ 0 = 비활성 → false.</summary>
    public static bool IsExpiredAt(double expiresAt, double now) =>
        expiresAt > 0d && now >= expiresAt;

    /// <summary>
    /// 게이지 채움 0~1. 🔴 표시 전용 — <see cref="TimerState.Stopped"/> 는 **항상 0**(빈 게이지)이다
    /// (팀장 확정: 제때 들어가면 0 인 상태로 둔다).
    /// </summary>
    public float Fill01
    {
        get
        {
            switch (_state.Value)
            {
                // 🔴 "제때 들어가면 게이지는 0" 은 방향과 무관한 계약이다(팀장 확정).
                //    반전 모드에서 1 을 돌려주면 만료와 같은 그림이 되어 정반대로 읽힌다.
                case TimerState.Stopped:
                    return 0f;
                case TimerState.Idle:
                    return fillUpAsTimePasses ? 0f : 1f;
                case TimerState.Expired:
                    return fillUpAsTimePasses ? 1f : 0f;
            }

            if (limitSeconds <= 0f)
                return 0f;

            float elapsed01 = Mathf.Clamp01(1f - RemainingSeconds / limitSeconds);
            return fillUpAsTimePasses ? elapsed01 : 1f - elapsed01;
        }
    }

    // 🔴 스폰 순서가 보장되지 않는다 — MapNetworkSync 와 이 오브젝트는 서로 다른 씬 NetworkObject 이고
    //    NGO 는 정렬 없는 검색으로 모아 순회한다. 그래서 스폰 전에 생성 이벤트가 올 수 있다.
    //    그때는 **실제 생성 시각을 보관**했다가 스폰 직후 한 번만 적용한다.
    //    (스폰 시각 기준으로 다시 재면 "맵 생성 완료부터 5분"이라는 계약을 어긴다.)
    private bool _pendingStart;
    private bool _pendingUsesSessionClock;
    private double _pendingStartTime;   // TimerNow 도메인
    private float _pendingStartRealtime;

    private BossTeleportManager _teleport;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        // 구독은 스폰 전에 건다 — 늦게 걸면 생성 이벤트 자체를 놓친다.
        MapGenerator.OnGenerated += HandleMapGenerated;
    }

    private void OnDisable()
    {
        MapGenerator.OnGenerated -= HandleMapGenerated;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        _teleport = BossTeleportManager.Instance != null
            ? BossTeleportManager.Instance
            : FindFirstObjectByType<BossTeleportManager>();

        if (_teleport != null)
            _teleport.AlivePlayersArrived += HandleAlivePlayersArrived;
        else
            Edit.LogError("[BossTimer] BossTeleportManager 를 찾지 못했습니다 — 강제 개시를 할 수 없습니다.", this);

        ApplyPendingStart();
    }

    public override void OnNetworkDespawn()
    {
        if (_teleport != null)
            _teleport.AlivePlayersArrived -= HandleAlivePlayersArrived;

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    /// <summary>
    /// 🔴 <see cref="MapGenerator.OnGenerated"/> 는 <b>서버·클라 양쪽에서</b> 발생한다
    /// (MapNetworkSync 가 시드를 맞춘 뒤 각 피어가 자기 <c>Generate</c> 를 돈다).
    /// 클라의 발생 시각은 서버와 다르므로 <b>서버만</b> 만료 시각을 정한다.
    /// </summary>
    private void HandleMapGenerated(MapGenerator generator)
    {
        // 🔴 여기서 `NetworkManager`(NetworkBehaviour 프로퍼티) 로 판정하면 안 된다 —
        //    **스폰 전에는 null** 이라 그 가드에 걸려 생성 이벤트를 통째로 버리게 된다.
        //    2026-09-20 실측: 타이머가 아예 시작되지 않았다(게이지가 계속 빈 상태).
        //    Codex 1차 교차검증이 "초기화 전 IsServer 가드로 이벤트를 버리는 구현"이라고
        //    지적했던 바로 그 함정이다. 싱글턴으로 본다.
        var nm = Unity.Netcode.NetworkManager.Singleton;

        // 클라는 서버가 정한 만료 시각을 복제받아 그리기만 한다.
        // ⚠️ 네트워크를 안 띄운 단독 실행에서는 이 컴포넌트가 스폰되지 않아 타이머가 돌지 않는다
        //    (서버 권한 값이라 그렇다). 확인하려면 호스트로 띄워야 한다.
        bool online = nm != null && nm.IsListening;
        if (online && !nm.IsServer)
            return;

        // 생성 시각을 붙잡아 둔다. 스폰이 늦어도 "맵 생성 완료부터 5분"(T1)이 지켜지려면
        // 스폰 시점이 아니라 **생성 시점**부터 재야 한다.
        //
        // 🔴 가능하면 **세션 시계 하나만**(TimerNow) 쓴다. NetworkManager·NetworkClock 의 것이라
        //    이 컴포넌트가 스폰되기 전에도 읽을 수 있다. 실시간 시계와 세션 시계를 섞으면
        //    둘의 샘플 시점이 달라(서버 시각은 PreUpdate 에서 프레임 단위로 누적된다)
        //    **로딩이 긴 프레임에서 만료가 앞당겨진다**(Codex 3차 지적).
        _pendingStart = true;
        _pendingUsesSessionClock = online;
        _pendingStartTime = online ? TimerNow(nm) : 0d;
        _pendingStartRealtime = Time.realtimeSinceStartup;

        if (IsSpawned && IsServer)
            ApplyPendingStart();
    }

    /// <summary>보류된 생성 시각을 실제 만료 시각으로 확정한다(서버 전용).</summary>
    private void ApplyPendingStart()
    {
        if (!IsServer || !_pendingStart) return;

        _pendingStart = false;

        // 세션 시계로 잡아 뒀으면 그 값을 그대로 쓴다 — 시계를 섞지 않는다.
        if (_pendingUsesSessionClock)
        {
            StartTimer(_pendingStartTime);
            return;
        }

        // 네트워크가 아직 안 떠 있던 경우에만 실시간 경과로 보정한다(폴백).
        // 세션 전이라 그 구간엔 일시정지·슬로우가 없다 — 실시간 경과를 게임 시간 경과로 봐도 된다.
        float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _pendingStartRealtime);
        StartTimer(TimerNow(NetworkManager) - elapsed);
    }

    /// <param name="startTime"><see cref="TimerNow"/> 도메인 시작 시각.</param>
    private void StartTimer(double startTime)
    {
        if (!IsServer) return;

        _expiresAt.Value = startTime + limitSeconds;
        _state.Value = TimerState.Running;
        _expired = false;

        Edit.Log($"[BossTimer] 제한시간 시작 — {limitSeconds:0}초(게임 시간) 후 보스전 강제 개시 " +
                 $"(만료 시각 {_expiresAt.Value:0.0}).", this);
    }

    /// <summary>제때 도착했다 — 타이머를 멈추고 게이지를 0 으로 비운다.</summary>
    private void HandleAlivePlayersArrived(System.Collections.Generic.IReadOnlyList<ulong> arrived)
    {
        if (!IsServer || _state.Value == TimerState.Stopped)
            return;

        _state.Value = TimerState.Stopped;
        _expiresAt.Value = 0d;

        // 🔴 만료 재시도를 여기서 끝낸다. 안 지우면 "만료 직전 패드 진입 → 패드 경로로 정상 도착"
        //    한 뒤에도 Update 가 강제 개시를 다시 걸어 **이미 도착한 플레이어를 또 끌고 간다.**
        _expired = false;

        Edit.Log("[BossTimer] 보스방 도착 — 제한시간 종료.", this);
    }

    // 만료는 한 번만 판정하고, **강제 개시에 성공할 때까지 사실을 유지**한다.
    private bool _expired;

    private void Update()
    {
        if (!IsServer || !IsSpawned)
            return;

        if (_state.Value == TimerState.Running &&
            IsExpiredAt(_expiresAt.Value, TimerNow(NetworkManager)))
        {
            _expired = true;
            _state.Value = TimerState.Expired;
            Edit.LogWarning("[BossTimer] 🔴 제한시간 만료 — 보스전을 강제 개시한다.", this);
        }

        if (!_expired || _teleport == null)
            return;

        // 🔴 여기가 예전 설계의 구멍을 막는 지점이다. "이미 진행 중이면 타이머를 끝낸다" 로 두면,
        //    만료 직전에 누가 패드를 밟았다가 나가는 것만으로 **보스 이동도 제한시간도 사라졌다.**
        //    그래서 만료 사실은 남겨 두고 다시 시도한다.
        //
        // 🔴 성공해도 `_expired` 를 여기서 지우지 않는다 — `ForceStartEncounter` 의 성공은
        //    "경고 카운트다운을 시작했다" 일 뿐이고, 그 뒤 **도착 ACK 타임아웃으로 취소**될 수 있다.
        //    만료 사실은 **실제 도착이 확정될 때**(HandleAlivePlayersArrived) 비로소 지운다.
        //    그래야 ACK 실패 후에도 제한시간 집행이 되살아난다.
        _retryTimer -= Time.deltaTime;
        if (_retryTimer > 0f)
            return;

        _retryTimer = ForceRetryInterval;
        _teleport.ForceStartEncounter(forceWarnSeconds);
    }

    // 매 프레임 두드리면 로그만 쌓인다 — 진행 중일 땐 이 간격으로만 다시 시도한다.
    private const float ForceRetryInterval = 0.5f;
    private float _retryTimer;
}
