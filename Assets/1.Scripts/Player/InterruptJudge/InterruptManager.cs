using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>[오너] 판정 전에 대기 판정을 버린 이유(할 일 191 D12 QA 기록).</summary>
public enum InterruptOwnerJudgeAbandon : byte
{
    /// <summary>판정 전 간파 스킬이 정상 완료 아닌 이유로 끝났다(넉백·끌기·잡기·사망·대시 — D5).</summary>
    SkillCut = 0,
    /// <summary>서버가 시전을 거부했다.</summary>
    CastRejected = 1,
    /// <summary>판정 시각이 이력(1초) 밖이다.</summary>
    OutOfHistory = 2,
}

/// <summary>
/// 간파의 단일 진입점(할 일 191, PLAN §3.1) — 모든 피어에 하나, <c>NetworkManager.prefab</c> 에 붙는다.
/// 판정 데이터를 모으고 예측·판정 함수(<see cref="Predict"/>·<see cref="Judge(in InterruptJudgeAttempt)"/> = <see cref="InterruptJudgeCalculator"/>)를 둔다.
/// <b>3단계</b>: 간파 성공은 오너 클라 판정 → 서버 검증으로 정한다(<see cref="BeginOwnerJudge"/> → 신고 → <see cref="InterruptClaimServer"/>).
/// <b>4단계</b>: 오너 연출은 이 매니저의 예측·판정·서버 결과 cue 로만 움직인다(<see cref="InterruptOwnerCueRouter"/> → 디렉터 <see cref="IInterruptOwnerCueSink"/>).
/// <b>5단계</b>: 서버 시도 슬로우 강·약도 같은 <see cref="Predict"/> 를 서버 이력으로 돌린다(<see cref="TryPredictServerAttempt"/>) — 옛 예측기(DashInterruptPredictor·창 추정)는 삭제.
/// 간파마다 QA 기록 한 줄(<see cref="InterruptQaRecorder"/> — 예측·판정·서버 결과·어긋난 이유)을 남긴다(D12).
/// <list type="bullet">
/// <item><b>세계 스냅샷</b>: 등록된 플레이어(<see cref="RegisterPlayer"/> — Player 스폰)와 간파 대상(<see cref="InterruptTargetRegistry"/> 그대로)을
///       매 프레임 LateUpdate 에서 샘플해 약 1초 이력(<see cref="InterruptSampleHistory{T}"/>)에 쌓는다. 클라는 보는 값, 서버는 실제 값.</item>
/// <item><b>창 시간표</b>: 보스·허수아비는 창 열림(닫힘 예정 길이·공격 시작·지목자)·닫힘만 <see cref="ServerReportWindow"/> 로 알린다.
///       서버가 기록(<see cref="InterruptWindowSchedule"/>)을 만들고 named message 로 모든 클라에 복제한다 — 열림·조기 닫힘·늦은 닫힘(갱신) 모두.</item>
/// <item><b>시각</b>: 전부 서버 시각(<see cref="NetworkClock.GameNow"/> 도메인). 로컬 게임초와는 <see cref="InterruptServerClock"/> 로 바꾼다.</item>
/// </list>
/// NetworkObject 가 없는 프리팹이라 NetworkBehaviour·RPC 대신 <see cref="GlobalTimeScale"/> 와 같은 named message 를 쓴다.
/// 늦은 합류 클라에는 접속 때 열린 창만 한 번 보낸다. 문서: Docs/tech/interrupt-client-judge.md.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
[DataTableSheet("SlowMotion", Order = 3)]
public sealed class InterruptManager : MonoBehaviour, IInterruptJudgeWorld
{
    public static InterruptManager Instance { get; private set; }

    /// <summary>이력 보관 길이(초).</summary>
    public const double HistorySeconds = 1.0;

    // 60fps 1초 + 여유. 넘치면 오래된 것부터 밀린다.
    const int HistoryCapacity = 96;

    const string WindowMessageName = "InterruptManager.Window";
    const string ClaimMessageName = "InterruptManager.Claim";
    const string ApprovalMessageName = "InterruptManager.Approval";
    const string RejectMessageName = "InterruptManager.Reject";

    [DataTableIgnore]
    [SerializeField]
    [Tooltip("창 시간표 열림·닫힘을 콘솔에 남긴다(에디터 전용 로그). 할 일 191 Play 확인용.")]
    bool logWindows;

    [DataTableIgnore]
    [SerializeField]
    [Tooltip("간파 QA 기록(할 일 191 D12)을 파일로 남긴다 — C:/UnityProject/QA/191interrupt-<시각>-<host|client><id>/interrupts.csv·summary.txt. " +
             "에디터·개발 빌드만(릴리스 빌드는 꺼짐). 끄면 파일만 안 쓰고 기록 이벤트(보정 랩)는 그대로.")]
    bool qaRecordFile = true;

    [Header("서버 검증 허용 오차 (할 일 191 D11)")]
    [SerializeField, Min(0f)]
    [Tooltip("시각 여유(초) — 그 클라 측정 지연(RTT) 절반에 더한다. 창 열림·닫힘(조기 닫힘 포함)과 위치 표본 시각에 쓴다.")]
    float verifyTimeSlack = 0.033f;

    [SerializeField, Min(0f)]
    [Tooltip("거리 여유(m) — 간파 상자 수평 반폭·반길이(구·캡슐 반지름)를 이만큼 키워 검증한다.")]
    float verifyDistanceSlack = 0.2f;

    [SerializeField, Min(0f)]
    [Tooltip("정면 각도 여유(도) — 대상 정면 허용 반각에 더한다(23호 정면 판정 등).")]
    float verifyAngleSlack = 5f;

    [Header("동시 성공 (할 일 191 D8)")]
    [SerializeField, Min(0f)]
    [Tooltip("창의 첫 승인 뒤 이 시간(초) 안에 도착한 승인 = 동시 성공(연출·성공 기록만, 보스 효과 없음). 그 뒤는 선착순 패배.")]
    float simultaneousSeconds = 0.05f;

    // ---- 검증 결과 이벤트(연출 cue 는 CueRouter — 4단계) ----

    /// <summary>[서버] 신고 하나를 처리했다(승인·거부 모두).</summary>
    public static event System.Action<InterruptClaimOutcome> ServerClaimProcessed;

    /// <summary>[모든 피어] 승인 배포를 받았다(호스트는 직접). 같은 창의 동시 성공자 전체가 실린다.</summary>
    public static event System.Action<InterruptClaimApproval> ApprovalReceived;

    /// <summary>[오너] 내 신고가 거부됐다(호스트 오너는 직접).</summary>
    public static event System.Action<InterruptClaimRejection> RejectionReceived;

    /// <summary>[오너] Hit 타이밍 판정을 했다(시전자 NetworkObjectId, 결과). 성공이면 바로 신고를 보낸다 — 실패는 여기서 끝.</summary>
    public static event System.Action<ulong, InterruptJudgeResult> OwnerJudged;

    /// <summary>[오너] 누른 순간 예측을 했다(예측 입력, 결과) — 시도 cue 와 같은 때(QA 기록·보정 랩·기즈모).</summary>
    public static event System.Action<InterruptCastRequest, InterruptJudgeResult> OwnerPredicted;

    /// <summary>[오너] 판정 전에 대기 판정을 버렸다(시전자, 이유, 스킬 종료 사유 — 끊김일 때만 뜻이 있다).</summary>
    public static event System.Action<ulong, InterruptOwnerJudgeAbandon, SkillEndReason> OwnerJudgeAbandoned;

    /// <summary>[서버] 시도 슬로우 강·약 예측을 했다(<see cref="TryPredictServerAttempt"/> — 예측 입력, 결과).</summary>
    public static event System.Action<InterruptCastRequest, InterruptJudgeResult> ServerAttemptPredicted;

    /// <summary>[서버] 세션 중이라 간파 성공을 신고 검증으로 정한다 — 대상은 피격으로 성공을 정하지 않는다. 오프라인·매니저 없음 = false(기존 피격 판정).</summary>
    public static bool ServerVerifiesClaims => Instance != null && Instance.IsServerActive;

    /// <summary>지금 검증 수치.</summary>
    public InterruptVerifySettings VerifySettings => new InterruptVerifySettings
    {
        TimeSlack = verifyTimeSlack,
        DistanceSlack = verifyDistanceSlack,
        AngleSlack = verifyAngleSlack,
        SimultaneousSeconds = simultaneousSeconds,
    };

    // ---- 플레이어 등록(정적 — 매니저보다 플레이어가 먼저 스폰될 수 있다) ----

    static readonly List<Player> s_players = new List<Player>();

    /// <summary>[모든 피어] 플레이어 스폰 때 부른다(Player.OnNetworkSpawn).</summary>
    public static void RegisterPlayer(Player player)
    {
        if (player != null && !s_players.Contains(player))
            s_players.Add(player);
    }

    /// <summary>[모든 피어] 플레이어 디스폰 때 부른다(Player.OnNetworkDespawn).</summary>
    public static void UnregisterPlayer(Player player) => s_players.Remove(player);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_players.Clear();
        ServerClaimProcessed = null;
        ApprovalReceived = null;
        RejectionReceived = null;
        OwnerJudged = null;
        OwnerPredicted = null;
        OwnerJudgeAbandoned = null;
        ServerAttemptPredicted = null;
    }

    // ---- 상태 ----

    sealed class ActorTrack
    {
        public Player Player;
        public PlayerStateController States;
        public PlayerLifeCycleController Life;
        public PlayerSkillController Skills;
        public PlayerMotor Motor;
        public readonly InterruptSampleHistory<InterruptActorSample> History =
            new InterruptSampleHistory<InterruptActorSample>(HistoryCapacity, HistorySeconds);
        public bool Seen;
    }

    sealed class TargetTrack
    {
        public Unit Unit;
        public IInterruptJudgeTarget State;
        public readonly InterruptSampleHistory<InterruptTargetSample> History =
            new InterruptSampleHistory<InterruptTargetSample>(HistoryCapacity, HistorySeconds);
        public bool Seen;
    }

    readonly InterruptServerClock _clock = new InterruptServerClock();
    readonly InterruptWindowSchedule _schedule = new InterruptWindowSchedule();
    readonly Dictionary<ulong, ActorTrack> _actors = new Dictionary<ulong, ActorTrack>();
    readonly Dictionary<ulong, TargetTrack> _targets = new Dictionary<ulong, TargetTrack>();
    readonly List<InterruptTargetRegistry.Entry> _targetBuffer = new List<InterruptTargetRegistry.Entry>();
    readonly List<ulong> _removeBuffer = new List<ulong>();
    readonly List<ulong> _targetIds = new List<ulong>();
    readonly Collider[] _overlapBuffer = new Collider[16];
    readonly List<InterruptWindowRecord> _recordBuffer = new List<InterruptWindowRecord>();
    readonly InterruptClaimServer _claimServer = new InterruptClaimServer();

    // [오너] Hit 타이밍 판정 대기 — 누른 순간 예측으로 판정 시각을 잡고, 그 시각 표본이 쌓이면 판정한다.
    sealed class OwnerJudge
    {
        public ulong ActorId;
        public bool ViaDash;
        public InterruptCastRequest Cast;
        public bool Predicted;
        public InterruptJudgeAttempt Attempt;
    }

    readonly List<OwnerJudge> _ownerJudges = new List<OwnerJudge>(2);

    // [오너] 연출 cue(4단계, PLAN §3.3) — 예측·판정·서버 결과·스킬 끊김을 디렉터 cue 로 바꾼다. 받는 쪽 기본 = 디렉터.
    InterruptOwnerCueRouter _cueRouter;
    IInterruptOwnerCueSink _cueSinkOverride;

    InterruptOwnerCueRouter CueRouter => _cueRouter ??= new InterruptOwnerCueRouter(() => _cueSinkOverride ?? InterruptProductionDirector.Instance);

    /// <summary>오너 연출 cue 받는 쪽을 바꾼다(테스트·디버그). null = 디렉터.</summary>
    public void SetOwnerCueSink(IInterruptOwnerCueSink sink) => _cueSinkOverride = sink;

    // [각 피어] QA 기록(D12) — 세션마다 시작·끝. 매니저 이벤트를 구독하는 기록기 하나.
    InterruptQaRecorder _qaRecorder;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    bool QaFileEnabled => qaRecordFile;
#else
    bool QaFileEnabled => false;
#endif

    NetworkManager _networkManager;
    NetworkClock _networkClock;
    CustomMessagingManager _registeredMessaging;   // NGO 는 세션마다 새로 만든다 — 등록한 객체를 기억한다(GlobalTimeScale 과 같은 이유)
    bool _clientConnectedHooked;
    bool _hasState;

    // ---- 읽기 API(2단계 예측·판정이 쓴다) ----

    bool IsSessionRunning => _networkManager != null && _networkManager.IsListening;
    bool IsServerActive => IsSessionRunning && _networkManager.IsServer;

    /// <summary>지금 서버 시각.</summary>
    public double ServerNow => _clock.ToServer(Time.timeAsDouble);

    /// <summary>로컬 게임초(Time.time 계열) → 서버 시각.</summary>
    public double ToServerTime(double localTime) => _clock.ToServer(localTime);

    /// <summary>서버 시각 → 로컬 게임초.</summary>
    public double ToLocalTime(double serverTime) => _clock.ToLocal(serverTime);

    /// <summary>플레이어(NetworkObjectId)의 <paramref name="serverTime"/> 표본 — 이력 범위 밖이면 false.</summary>
    public bool TryGetActorAt(ulong networkObjectId, double serverTime, out InterruptActorSample sample)
    {
        sample = default;
        return _actors.TryGetValue(networkObjectId, out ActorTrack track) && track.History.TryGetAt(serverTime, out sample);
    }

    /// <summary>간파 대상(NetworkObjectId)의 <paramref name="serverTime"/> 표본 — 이력 범위 밖이면 false.</summary>
    public bool TryGetTargetAt(ulong networkObjectId, double serverTime, out InterruptTargetSample sample)
    {
        sample = default;
        return _targets.TryGetValue(networkObjectId, out TargetTrack track) && track.History.TryGetAt(serverTime, out sample);
    }

    /// <summary>대상의 <paramref name="serverTime"/> 에 열려 있던 창.</summary>
    public bool TryGetWindowAt(ulong targetId, double serverTime, out InterruptWindowRecord record) =>
        _schedule.TryGetWindowAt(targetId, serverTime, out record);

    /// <summary>대상의 가장 최근 창.</summary>
    public bool TryGetLatestWindow(ulong targetId, out InterruptWindowRecord record) =>
        _schedule.TryGetLatest(targetId, out record);

    // ---- 예측·판정(2단계) — 판정은 오너 신고(3단계 BeginOwnerJudge)에 쓴다. 예측의 연출 연결은 4단계 ----

    /// <summary>
    /// [각 피어] 누르는 순간 예측 — <see cref="InterruptJudgeCalculator.Predict"/>(같은 순수 계산). 클라는 허용 오차 0.
    /// 결과의 <see cref="InterruptJudgeResult.Forecast"/> = 대상 없음/약/강.
    /// </summary>
    public InterruptJudgeResult Predict(in InterruptCastRequest cast) =>
        InterruptJudgeCalculator.Predict(this, cast, InterruptJudgeTolerance.Zero);

    /// <summary>[각 피어] Hit 타이밍 판정 — <see cref="InterruptJudgeCalculator.Judge"/>. 서버 검증(3단계)은 허용 오차를 넣은 오버로드로 서버 이력에서 다시 돌린다.</summary>
    public InterruptJudgeResult Judge(in InterruptJudgeAttempt attempt) =>
        InterruptJudgeCalculator.Judge(this, attempt, InterruptJudgeTolerance.Zero);

    public InterruptJudgeResult Judge(in InterruptJudgeAttempt attempt, in InterruptJudgeTolerance tolerance) =>
        InterruptJudgeCalculator.Judge(this, attempt, tolerance);

    /// <summary>
    /// 시전자·근접/대쉬·조준 방향·누른 로컬 게임초로 예측 입력을 만든다 — Hit 지연 = 우클릭 간파 스킬의 <see cref="PlayerInterruptSkillBase.TryGetJudgeHitDelay"/>.
    /// 우클릭이 간파 스킬이 아니거나 데이터가 없으면 false.
    /// </summary>
    public bool TryBuildCastRequest(Player caster, bool viaDash, Vector3 attackDirection, float pressLocalTime, out InterruptCastRequest cast)
    {
        cast = default;
        PlayerSkillController skills = caster != null ? caster.GetComponent<PlayerSkillController>() : null;
        if (skills == null || !(skills.GetSkill(PlayerSkillSlot.Interrupt) is PlayerInterruptSkillBase skill) ||
            !skill.TryGetJudgeHitDelay(out float hitDelay))
            return false;

        cast = new InterruptCastRequest
        {
            ActorId = caster.NetworkObjectId,
            ViaDash = viaDash,
            PressTime = ToServerTime(pressLocalTime),
            AttackDirection = attackDirection,
            HitDelay = hitDelay,
        };
        return true;
    }

    // ---- 오너 판정 → 신고(3단계, PLAN §3 "Hit 타이밍: 같은 함수로 판정 → 성공이면 1회 전송") ----

    /// <summary>
    /// [오너] 간파를 눌렀다 — 다음 샘플 뒤 누른 시각 표본으로 예측해 판정 시각(근접 = 누름 + Hit 지연, 대쉬 = max(대시 끝, Hit))을 잡고,
    /// 그 시각 표본이 쌓이면 같은 함수로 판정한다. 성공이면 서버에 1회 신고, 실패면 보내지 않는다. 호스트 오너도 같은 경로(직접 호출).
    /// 같은 시전자의 대기 판정이 있으면 바꾼다. <paramref name="pressLocalTime"/> = 누른 로컬 게임초(Time.time).
    /// </summary>
    public void BeginOwnerJudge(Player caster, bool viaDash, Vector3 attackDirection, float pressLocalTime)
    {
        if (!IsSessionRunning || caster == null || !caster.IsSpawned ||
            !TryBuildCastRequest(caster, viaDash, attackDirection, pressLocalTime, out InterruptCastRequest cast))
            return;

        RemoveOwnerJudges(caster.NetworkObjectId);
        _ownerJudges.Add(new OwnerJudge { ActorId = caster.NetworkObjectId, ViaDash = viaDash, Cast = cast });
    }

    /// <summary>[오너] 대기 중인 판정을 버린다 — 서버가 시전을 거부했다(연출 걷기는 호출측 기존 경로). 판정 전 끊김은 <see cref="OwnerSkillEnded"/>.</summary>
    public void CancelOwnerJudge(Player caster)
    {
        if (caster == null)
            return;

        if (RemoveOwnerJudges(caster.NetworkObjectId))
            OwnerJudgeAbandoned?.Invoke(caster.NetworkObjectId, InterruptOwnerJudgeAbandon.CastRejected, SkillEndReason.Completed);
        CueRouter.Forget(caster.NetworkObjectId);
    }

    /// <summary>
    /// [오너] 간파 스킬이 끝났다. 판정 전 + 정상 완료가 아니면(넉백·끌기·잡기·사망·대시 — 상태 전환, D5) 대기 판정을 버리고 즉시 종료 cue.
    /// 판정 전 정상 완료는 판정을 그대로 기다린다(판정 cue 가 결론). 이 시전을 cue 경로가 맡고 있으면 true — 호출측은 기존 걷기를 건너뛴다.
    /// </summary>
    public bool OwnerSkillEnded(Player caster, SkillEndReason reason)
    {
        if (caster == null)
            return false;

        ulong id = caster.NetworkObjectId;
        if (InterruptOwnerCueRouter.EndsBeforeJudge(reason) && RemoveOwnerJudges(id))
            OwnerJudgeAbandoned?.Invoke(id, InterruptOwnerJudgeAbandon.SkillCut, reason);
        return CueRouter.SkillEnded(id, reason);
    }

    // 지운 대기 판정이 있었으면 true.
    bool RemoveOwnerJudges(ulong actorId)
    {
        bool removed = false;
        for (int i = _ownerJudges.Count - 1; i >= 0; i--)
        {
            if (_ownerJudges[i].ActorId != actorId)
                continue;
            _ownerJudges.RemoveAt(i);
            removed = true;
        }
        return removed;
    }

    /// <summary>
    /// [서버] 시도 슬로우 강·약(할 일 191 5단계 — 옛 예측기 대체). 승인 순간 서버 이력의 가장 최근 시전자 표본을 누른 시각으로 보고 같은 <see cref="Predict"/> 를 돌린다
    /// (근접 = 그 자리 + Hit 지연, 대쉬 = 서버 대시를 같은 충돌 검사로 굴린 끝). 창은 서버 창 시간표 — 선착순으로 이미 소비된 창은 조기 닫힘이라 약.
    /// 오너 예측을 신고·요청에 싣지 않는 이유: 요청 RPC 는 누른 프레임에 나가고 오너 예측은 그 프레임 LateUpdate 샘플 뒤라 실을 수 없다(새 RPC·지연 없이 서버가 같은 함수를 쓴다 — PLAN §3.1 ③과 같은 방식).
    /// 세션·서버가 아니거나 시전자 이력이 아직 없으면 false.
    /// </summary>
    public bool TryPredictServerAttempt(Player caster, bool viaDash, Vector3 attackDirection, out InterruptJudgeResult prediction)
    {
        prediction = default;
        if (!IsServerActive || caster == null || !caster.IsSpawned ||
            !_actors.TryGetValue(caster.NetworkObjectId, out ActorTrack track) || !track.History.TryGetLatest(out double latest, out _) ||
            !TryBuildCastRequest(caster, viaDash, attackDirection, Time.time, out InterruptCastRequest cast))
            return false;

        cast.PressTime = latest;
        prediction = Predict(cast);
        if (logWindows)
            Edit.Log($"[InterruptManager] 서버 시도 예측 — {prediction.Forecast} {prediction.FailReason} · 대상 {prediction.TargetId} · 창 #{prediction.WindowId}", this);
        ServerAttemptPredicted?.Invoke(cast, prediction);
        return true;
    }

    /// <summary>
    /// [서버] 피해 판정 선착순(할 일 192 5a) — 이 시전자의 이 시전(<paramref name="castStartLocalTime"/> = 서버 로컬 게임초 Time.time)이
    /// 다른 사람이 이미 승인받은 <paramref name="target"/> 의 창에 늦게 들어갔는가(<see cref="InterruptClaimLedger.PreemptsCast"/>).
    /// true 면 간파 스킬이 그 피해의 간파 표식을 뗀다(피해만). 세션·서버가 아니면 false.
    /// </summary>
    public bool ServerPreemptsCast(Unit target, Player caster, float castStartLocalTime)
    {
        if (!IsServerActive || target == null || caster == null || !caster.IsSpawned)
            return false;

        ulong targetId = InterruptWindowReplicaTracker.TargetIdOf(target.gameObject);
        return targetId != 0 &&
               _claimServer.Ledger.PreemptsCast(targetId, caster.NetworkObjectId, ToServerTime(castStartLocalTime), ServerNow);
    }

    /// <summary>시전자(NetworkObjectId)의 등록된 플레이어. 없으면 false.</summary>
    public bool TryGetPlayer(ulong actorId, out Player player)
    {
        player = _actors.TryGetValue(actorId, out ActorTrack track) ? track.Player : null;
        return player != null;
    }

    // LateUpdate 샘플 직후 — 표본이 누른 시각·판정 시각까지 쌓였으면 예측·판정한다. 이력 밖으로 밀린 대기는 버린다.
    // 예측 = 시도 cue, 판정 = 성공/실패 cue(4단계) — 성공 cue 가 신고보다 먼저라 호스트 오너도 섬광이 서버 성공 슬로우보다 앞선다.
    void TickOwnerJudges(double now)
    {
        for (int i = _ownerJudges.Count - 1; i >= 0; i--)
        {
            OwnerJudge pending = _ownerJudges[i];
            if (!pending.Predicted)
            {
                if (now < pending.Cast.PressTime)
                    continue;

                InterruptJudgeResult prediction = Predict(pending.Cast);
                pending.Attempt = InterruptJudgeAttempt.FromPrediction(pending.ActorId, prediction);
                pending.Predicted = true;
                if (logWindows)
                    Edit.Log($"[InterruptManager] 오너 예측 — {prediction.Forecast} {prediction.FailReason} · 대상 {prediction.TargetId} · 판정까지 {prediction.JudgeTime - now:0.###}s", this);
                OwnerPredicted?.Invoke(pending.Cast, prediction);
                CueRouter.Predicted(BuildOwnerCue(pending.ActorId, prediction, pending.Cast.AttackDirection, pending.ViaDash));
            }

            if (now < pending.Attempt.JudgeTime)
            {
                if (pending.Attempt.JudgeTime - now > HistorySeconds)
                {
                    _ownerJudges.RemoveAt(i);
                    OwnerJudgeAbandoned?.Invoke(pending.ActorId, InterruptOwnerJudgeAbandon.OutOfHistory, SkillEndReason.Completed);
                    CueRouter.Dropped(pending.ActorId);
                }
                continue;
            }

            _ownerJudges.RemoveAt(i);
            InterruptJudgeResult result = Judge(pending.Attempt);
            if (logWindows)
                Edit.Log($"[InterruptManager] 오너 판정 — {result.Outcome} {result.FailReason} · 대상 {result.TargetId} · 창 #{result.WindowId}", this);
            OwnerJudged?.Invoke(pending.ActorId, result);
            CueRouter.Judged(BuildOwnerCue(pending.ActorId, result, pending.Cast.AttackDirection, pending.ViaDash));
            if (result.IsSuccess)
                SubmitClaim(InterruptClaim.FromJudge(pending.ActorId, pending.ViaDash, result));
        }
    }

    // cue 한 건 — 오너 캐릭터·간파 스킬·결과 대상(지금 등록 정보)·섬광 기준 위치(대상, 없으면 시전자).
    InterruptOwnerCue BuildOwnerCue(ulong actorId, in InterruptJudgeResult result, Vector3 attackDirection, bool viaDash)
    {
        _actors.TryGetValue(actorId, out ActorTrack actor);
        Unit target = result.TargetId != 0 && _targets.TryGetValue(result.TargetId, out TargetTrack track) ? track.Unit : null;
        Player player = actor != null ? actor.Player : null;
        return new InterruptOwnerCue
        {
            ActorId = actorId,
            Result = result,
            Owner = player != null ? player.gameObject : null,
            Target = target != null ? target.gameObject : null,
            Skill = actor != null && actor.Skills != null ? actor.Skills.GetSkill(PlayerSkillSlot.Interrupt) as PlayerInterruptSkillBase : null,
            AttackDirection = attackDirection,
            FlashPosition = target != null ? target.transform.position : player != null ? player.transform.position : Vector3.zero,
            ViaDash = viaDash,
        };
    }

    // 호스트 = 서버 처리 직접 호출, 원격 클라 = 서버로 named message.
    void SubmitClaim(in InterruptClaim claim)
    {
        if (_networkManager.IsServer)
        {
            ServerReceiveClaim(_networkManager.LocalClientId, claim);
            return;
        }

        CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
            return;

        using (var writer = new FastBufferWriter(InterruptClaim.SerializedSize, Allocator.Temp))
        {
            claim.Write(writer);
            messaging.SendNamedMessage(ClaimMessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }
    }

    // ---- 서버 검증(3단계, PLAN §3.1 ③) ----

    /// <summary>
    /// [서버] 신고 처리 — <see cref="InterruptClaimServer.Process"/>(소유 확인 → 검증 → 대상 규칙 → 선착순/동시 → 첫 승인만 대상 효과·승인마다 개인 기록),
    /// 그 시전자의 간파 스킬에 결과를 넘기고(<see cref="PlayerInterruptSkillBase.ServerOnClaimVerdict"/>), 승인은 모든 피어·거부는 그 오너에게 보낸다.
    /// </summary>
    void ServerReceiveClaim(ulong senderClientId, in InterruptClaim claim)
    {
        SampleClock();
        double now = ServerNow;

        _actors.TryGetValue(claim.ActorId, out ActorTrack actor);
        bool owns = actor != null && actor.Player != null && actor.Player.OwnerClientId == senderClientId;
        Unit targetUnit = _targets.TryGetValue(claim.TargetId, out TargetTrack target) ? target.Unit : null;
        IInterruptClaimTarget claimTarget = targetUnit != null
            ? targetUnit as IInterruptClaimTarget ?? targetUnit.GetComponent<IInterruptClaimTarget>()
            : null;

        InterruptClaimOutcome verdict = _claimServer.Process(this, claim, senderClientId, owns,
            InterruptClaimServer.HalfLagSeconds(InterruptLagMonitor.RttMs(senderClientId)), now, VerifySettings, claimTarget,
            owns ? actor.Player : null);

        if (logWindows || !verdict.Approved)
            Edit.Log($"[InterruptManager] 신고 client {senderClientId} — {(verdict.Approved ? (verdict.IsFirst ? "승인(첫)" : "승인(동시)") : $"거부 {verdict.RejectReason}/{verdict.JudgeFailReason}")}" +
                     $" · 대상 {claim.TargetId} · 창 #{claim.WindowId}→#{verdict.WindowId} · 판정 시각 {now - claim.JudgeTime:0.###}s 전", this);

        if (owns && actor.Skills != null && actor.Skills.GetSkill(PlayerSkillSlot.Interrupt) is PlayerInterruptSkillBase skill)
            skill.ServerOnClaimVerdict(verdict.Approved, verdict.IsFirst, targetUnit, claim.ViaDash);

        ServerClaimProcessed?.Invoke(verdict);

        if (verdict.Approved)
            ServerPublishApproval(claim.TargetId, verdict.WindowId, claim.ActorId);
        else
            ServerSendRejection(senderClientId, new InterruptClaimRejection(claim.ActorId, claim.TargetId, claim.WindowId,
                verdict.RejectReason, verdict.JudgeFailReason));
    }

    void ServerPublishApproval(ulong targetId, uint windowId, ulong newActorId)
    {
        IReadOnlyList<ulong> approved = _claimServer.Ledger.ApprovedActors(targetId, windowId);
        ulong first = approved.Count > 0 ? approved[0] : newActorId;
        var localApproval = new InterruptClaimApproval(targetId, windowId, first, newActorId, approved);
        ApprovalReceived?.Invoke(localApproval);
        CueRouter.Approved(localApproval);

        CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
            return;

        int count = Mathf.Min(approved.Count, byte.MaxValue);
        int size = sizeof(ulong) * 3 + sizeof(uint) + sizeof(byte) + sizeof(ulong) * count;
        IReadOnlyList<ulong> clients = _networkManager.ConnectedClientsIds;
        for (int c = 0; c < clients.Count; c++)
        {
            if (clients[c] == _networkManager.LocalClientId)
                continue;   // 호스트는 위에서 직접 알렸다

            using (var writer = new FastBufferWriter(size, Allocator.Temp))
            {
                writer.WriteValueSafe(targetId);
                writer.WriteValueSafe(windowId);
                writer.WriteValueSafe(first);
                writer.WriteValueSafe(newActorId);
                writer.WriteValueSafe((byte)count);
                for (int i = 0; i < count; i++)
                    writer.WriteValueSafe(approved[i]);
                messaging.SendNamedMessage(ApprovalMessageName, clients[c], writer, NetworkDelivery.Reliable);
            }
        }
    }

    void ServerSendRejection(ulong clientId, in InterruptClaimRejection rejection)
    {
        if (clientId == _networkManager.LocalClientId)
        {
            RejectionReceived?.Invoke(rejection);
            CueRouter.Rejected(rejection);
            return;
        }

        CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
            return;

        using (var writer = new FastBufferWriter(sizeof(ulong) * 2 + sizeof(uint) + sizeof(byte) * 2, Allocator.Temp))
        {
            writer.WriteValueSafe(rejection.ActorId);
            writer.WriteValueSafe(rejection.TargetId);
            writer.WriteValueSafe(rejection.WindowId);
            writer.WriteValueSafe((byte)rejection.Reason);
            writer.WriteValueSafe((byte)rejection.JudgeFailReason);
            messaging.SendNamedMessage(RejectMessageName, clientId, writer, NetworkDelivery.Reliable);
        }
    }

    void HandleClaimMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!_networkManager.IsServer)
            return;

        ServerReceiveClaim(senderClientId, InterruptClaim.Read(reader));
    }

    void HandleApprovalMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId || _networkManager.IsServer)
            return;

        reader.ReadValueSafe(out ulong targetId);
        reader.ReadValueSafe(out uint windowId);
        reader.ReadValueSafe(out ulong first);
        reader.ReadValueSafe(out ulong newActor);
        reader.ReadValueSafe(out byte count);
        var approved = new List<ulong>(count);
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong id);
            approved.Add(id);
        }

        if (logWindows)
            Edit.Log($"[InterruptManager] 승인 수신 — 대상 {targetId} · 창 #{windowId} · 승인 {count}명", this);
        var approval = new InterruptClaimApproval(targetId, windowId, first, newActor, approved);
        ApprovalReceived?.Invoke(approval);
        CueRouter.Approved(approval);
    }

    void HandleRejectMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId || _networkManager.IsServer)
            return;

        reader.ReadValueSafe(out ulong actorId);
        reader.ReadValueSafe(out ulong targetId);
        reader.ReadValueSafe(out uint windowId);
        reader.ReadValueSafe(out byte reason);
        reader.ReadValueSafe(out byte judgeReason);
        var rejection = new InterruptClaimRejection(actorId, targetId, windowId,
            (InterruptClaimRejectReason)reason, (InterruptJudgeFailReason)judgeReason);
        Edit.Log($"[InterruptManager] 내 간파 신고 거부 — {rejection.Reason}/{rejection.JudgeFailReason} · 대상 {targetId} · 창 #{windowId}", this);
        RejectionReceived?.Invoke(rejection);
        CueRouter.Rejected(rejection);
    }

    // ---- IInterruptJudgeWorld ----

    int IInterruptJudgeWorld.TargetCount => _targetIds.Count;

    ulong IInterruptJudgeWorld.GetTargetId(int index) => _targetIds[index];

    bool IInterruptJudgeWorld.TryGetLatestWindowOpenedBy(ulong targetId, double time, out InterruptWindowRecord record) =>
        _schedule.TryGetLatestOpenedBy(targetId, time, out record);

    // 대상의 지금 콜라이더로 질의한다 — 대상이 targetPosition 에 있었다고 보고 상자를 (지금 위치 − 그 위치)만큼 옮긴다(수평 회전 대칭 몸통 가정).
    // 걸린 콜라이더의 피해 수신 Unit(ResolveHitUnit — 기존 판정과 같은 해석)이 그 대상이면 닿은 것.
    bool IInterruptJudgeWorld.ShapeHitsTarget(ulong targetId, in HitboxAnchorShape shape, Vector3 targetPosition, int layers)
    {
        if (!_targets.TryGetValue(targetId, out TargetTrack track) || track.Unit == null)
            return false;

        int count = shape.Offset(track.Unit.transform.position - targetPosition).Overlap(_overlapBuffer, layers);
        for (int i = 0; i < count; i++)
        {
            Collider hit = _overlapBuffer[i];
            if (hit != null && PlayerSkillBase.ResolveHitUnit(hit, out _) == track.Unit)
                return true;
        }

        return false;
    }

    // 대시 경로 = 시전자 모터의 PredictDashPath(실제 대시 이동과 같은 스윕·유닛 막힘 검사). 모터가 없으면 직선.
    PlayerDashPathResult IInterruptJudgeWorld.SimulateDash(ulong actorId, Vector3 start, Vector3 direction, float speed, float remaining)
    {
        if (_actors.TryGetValue(actorId, out ActorTrack track) && track.Motor != null)
            return track.Motor.PredictDashPath(start, direction, speed, remaining);

        return new PlayerDashPathResult
        {
            End = start + InterruptSampleMath.Planar(direction) * (Mathf.Max(0f, speed) * Mathf.Max(0f, remaining)),
            Duration = Mathf.Max(0f, remaining),
        };
    }

    // ---- 서버 알림(보스·허수아비) ----

    /// <summary>
    /// [서버] 간파 창 열림·닫힘 알림. 서버가 아니거나 매니저·세션이 없으면 아무것도 안 한다(기존 동작 무영향).
    /// <paramref name="scheduledSeconds"/> = 열 때 닫힘 예정까지 길이(초, 로컬 게임초). 0 이하·무한 = 길이 모름(닫힘 배포 전까지 열림).
    /// <paramref name="attackStartedAt"/> = 공격(패턴) 시작 로컬 게임초(Time.time). 음수 = 열림 시각과 같음.
    /// 같은 대상에 열린 창이 있는데 다시 열면 앞 창을 닫고 새 창 번호를 준다. 닫힌 상태에서 닫기는 무시된다(멱등).
    /// </summary>
    public static void ServerReportWindow(GameObject owner, bool open, float scheduledSeconds = -1f,
        ulong designatedClientId = InterruptWindowRecord.NoDesignee, float attackStartedAt = -1f)
    {
        InterruptManager manager = Instance;
        if (manager == null || owner == null || !manager.IsServerActive)
            return;

        manager.ServerApplyWindow(owner, open, scheduledSeconds, designatedClientId, attackStartedAt);
    }

    void ServerApplyWindow(GameObject owner, bool open, float scheduledSeconds, ulong designatedClientId, float attackStartedAt)
    {
        ulong targetId = InterruptWindowReplicaTracker.TargetIdOf(owner);
        if (targetId == 0)
            return;

        SampleClock();
        double now = ServerNow;
        _hasState = true;

        if (!open)
        {
            if (_schedule.ServerClose(targetId, now, out InterruptWindowRecord closed))
                Publish(closed, owner);
            return;
        }

        double planned = scheduledSeconds > 0f && !float.IsInfinity(scheduledSeconds) ? now + scheduledSeconds : double.PositiveInfinity;
        double attackStart = attackStartedAt >= 0f ? ToServerTime(attackStartedAt) : now;
        InterruptWindowRecord record = _schedule.ServerOpen(targetId, attackStart, now, planned, designatedClientId,
            out bool closedPrevious, out InterruptWindowRecord previous);
        if (closedPrevious)
            Publish(previous, owner);
        Publish(record, owner);
    }

    void Publish(in InterruptWindowRecord record, Object context)
    {
        if (logWindows)
            Edit.Log($"[InterruptManager] 창 #{record.WindowId} {Describe(record)}", context);
        Broadcast(record);
    }

    static string Describe(in InterruptWindowRecord r)
    {
        string designee = r.DesignatedClientId == InterruptWindowRecord.NoDesignee ? string.Empty : $" · 지목 client {r.DesignatedClientId}";
        string planned = double.IsPositiveInfinity(r.PlannedCloseTime) ? "길이 모름" : $"예정 {r.PlannedCloseTime - r.OpenTime:0.###}s";
        return r.IsClosed
            ? $"닫힘 {r.CloseKind} — 대상 {r.TargetId} · 열린 {r.CloseTime - r.OpenTime:0.###}s · {planned}{designee}"
            : $"열림 — 대상 {r.TargetId} · 공격 시작 뒤 {r.OpenTime - r.AttackStartTime:0.###}s · {planned}{designee}";
    }

    // ---- 수명 ----

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        _networkManager = GetComponent<NetworkManager>();
        _networkClock = GetComponent<NetworkClock>();
        _qaRecorder = new InterruptQaRecorder(DescribeActor, () => ServerNow);
    }

    // QA 기록 칸 — 시전자 오너 clientId·캐릭터 이름(프리팹 이름).
    (ulong ClientId, string Character) DescribeActor(ulong actorId) =>
        TryGetPlayer(actorId, out Player player)
            ? (player.OwnerClientId, InterruptQaRules.CharacterOf(player.gameObject.name))
            : (0UL, "-");

    void OnDisable()
    {
        UnregisterHandlers();
        ResetState();
    }

    void OnDestroy()
    {
        UnregisterHandlers();
        ResetState();
        if (Instance == this)
            Instance = null;
    }

    void LateUpdate()
    {
        if (!IsSessionRunning || _networkManager.CustomMessagingManager == null)
        {
            if (_registeredMessaging != null || _hasState)
            {
                UnregisterHandlers();
                ResetState();
            }
            return;
        }

        EnsureHandlersRegistered();
        SampleClock();
        _hasState = true;
        if (_qaRecorder != null && !_qaRecorder.IsActive)
            _qaRecorder.Begin(_networkManager.IsServer, _networkManager.LocalClientId, QaFileEnabled, null, System.DateTime.Now);

        double now = ServerNow;
        SamplePlayers(now);
        SampleTargets(now);
        TickOwnerJudges(now);
        _qaRecorder?.Tick(now);
    }

    void SampleClock()
    {
        double local = Time.timeAsDouble;
        bool running = _networkClock != null && _networkClock.IsRunning;
        _clock.Sample(local, running ? _networkClock.GameNow : local);
    }

    void ResetState()
    {
        _qaRecorder?.End();   // 열린 줄 닫기·summary — 시계를 비우기 전에
        _clock.Reset();
        _schedule.Clear();
        _actors.Clear();
        _targets.Clear();
        _targetIds.Clear();
        _claimServer.Clear();
        _ownerJudges.Clear();
        _cueRouter?.Clear();
        _hasState = false;
    }

    // ---- 샘플 ----

    void SamplePlayers(double now)
    {
        foreach (ActorTrack track in _actors.Values)
            track.Seen = false;

        for (int i = s_players.Count - 1; i >= 0; i--)
        {
            Player player = s_players[i];
            if (player == null)
            {
                s_players.RemoveAt(i);
                continue;
            }

            if (!player.IsSpawned)
                continue;

            ulong id = player.NetworkObjectId;
            if (!_actors.TryGetValue(id, out ActorTrack track) || track.Player != player)
            {
                track = new ActorTrack
                {
                    Player = player,
                    States = player.GetComponent<PlayerStateController>(),
                    Life = player.GetComponent<PlayerLifeCycleController>(),
                    Skills = player.GetComponent<PlayerSkillController>(),
                    Motor = player.GetComponent<PlayerMotor>(),
                };
                _actors[id] = track;
            }

            track.Seen = true;
            track.History.Add(now, SamplePlayer(track, now));
        }

        PruneUnseen(_actors);
    }

    InterruptActorSample SamplePlayer(ActorTrack track, double now)
    {
        Transform t = track.Player.transform;
        PlayerActionState state = track.Player.CurrentState;
        var s = new InterruptActorSample
        {
            Position = t.position,
            Forward = InterruptSampleMath.Planar(t.forward),
            Alive = track.Life == null || track.Life.State == PlayerLifeState.Alive,
            CrowdControlled = state == PlayerActionState.Knockback || state == PlayerActionState.Restrained,
            ClientId = track.Player.OwnerClientId,
        };

        // 간파 상자 = 우클릭 간파 스킬의 판정 앵커(InterruptAttack) 지금 형상 그대로 — 기존 예측·서버 판정이 읽는 같은 값(HitboxAnchorShape.TryRead).
        if (track.Skills != null && track.Skills.GetSkill(PlayerSkillSlot.Interrupt) is PlayerInterruptSkillBase skill &&
            skill.HitboxAnchor != null && HitboxAnchorShape.TryRead(skill.HitboxAnchor, out HitboxAnchorShape hitbox))
        {
            s.HasHitbox = true;
            s.Hitbox = hitbox.Offset(-s.Position);
            s.HitboxForward = InterruptSampleMath.Planar(skill.HitboxAnchor.transform.forward);
            s.HitLayers = skill.Data != null ? skill.Data.HittableLayers.value : 0;
        }

        PlayerDashState dash = track.States != null ? track.States.ActiveDash : null;
        if (dash != null)
        {
            s.Dashing = true;
            s.DashStartTime = now - dash.Elapsed;
            s.DashDirection = dash.Direction;
            s.DashSpeed = dash.Speed;
            s.DashRemaining = dash.Remaining;
        }

        return s;
    }

    void SampleTargets(double now)
    {
        foreach (TargetTrack track in _targets.Values)
            track.Seen = false;

        InterruptTargetRegistry.CopyLiveTo(_targetBuffer);
        for (int i = 0; i < _targetBuffer.Count; i++)
        {
            Unit unit = _targetBuffer[i].Unit;
            ulong id = InterruptWindowReplicaTracker.TargetIdOf(unit.gameObject);
            if (id == 0)
                continue;

            if (!_targets.TryGetValue(id, out TargetTrack track) || track.Unit != unit)
            {
                track = new TargetTrack
                {
                    Unit = unit,
                    State = unit as IInterruptJudgeTarget ?? unit.GetComponent<IInterruptJudgeTarget>(),
                };
                _targets[id] = track;
            }

            track.Seen = true;
            Transform t = unit.transform;
            var s = new InterruptTargetSample
            {
                Position = t.position,
                Forward = InterruptSampleMath.Planar(t.forward),
                BlockedByState = track.State != null && track.State.InterruptBlockedByState,
                DesignatedClientId = _schedule.TryGetWindowAt(id, now, out InterruptWindowRecord window)
                    ? window.DesignatedClientId
                    : InterruptWindowRecord.NoDesignee,
                FrontAngle = _targetBuffer[i].FrontAngle,
            };
            track.History.Add(now, s);
        }

        _targetBuffer.Clear();

        // 빠진 대상(디스폰·사망·씬 언로드)은 이력과 창 시간표를 함께 지운다.
        _removeBuffer.Clear();
        foreach (KeyValuePair<ulong, TargetTrack> pair in _targets)
            if (!pair.Value.Seen)
                _removeBuffer.Add(pair.Key);
        for (int i = 0; i < _removeBuffer.Count; i++)
        {
            _targets.Remove(_removeBuffer[i]);
            _schedule.Forget(_removeBuffer[i]);
        }
        _removeBuffer.Clear();

        _targetIds.Clear();
        _targetIds.AddRange(_targets.Keys);
    }

    void PruneUnseen(Dictionary<ulong, ActorTrack> tracks)
    {
        _removeBuffer.Clear();
        foreach (KeyValuePair<ulong, ActorTrack> pair in tracks)
            if (!pair.Value.Seen)
                _removeBuffer.Add(pair.Key);
        for (int i = 0; i < _removeBuffer.Count; i++)
            tracks.Remove(_removeBuffer[i]);
        _removeBuffer.Clear();
    }

    // ---- CustomMessaging: 서버 → 클라 창 시간표 ----

    void EnsureHandlersRegistered()
    {
        CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
        if (_registeredMessaging == messaging)
            return;

        UnregisterHandlers();
        messaging.RegisterNamedMessageHandler(WindowMessageName, HandleWindowMessage);
        messaging.RegisterNamedMessageHandler(ClaimMessageName, HandleClaimMessage);
        messaging.RegisterNamedMessageHandler(ApprovalMessageName, HandleApprovalMessage);
        messaging.RegisterNamedMessageHandler(RejectMessageName, HandleRejectMessage);
        _registeredMessaging = messaging;

        if (_networkManager.IsServer)
        {
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _clientConnectedHooked = true;
        }
    }

    void UnregisterHandlers()
    {
        if (_registeredMessaging != null)
        {
            _registeredMessaging.UnregisterNamedMessageHandler(WindowMessageName);
            _registeredMessaging.UnregisterNamedMessageHandler(ClaimMessageName);
            _registeredMessaging.UnregisterNamedMessageHandler(ApprovalMessageName);
            _registeredMessaging.UnregisterNamedMessageHandler(RejectMessageName);
            _registeredMessaging = null;
        }

        if (_clientConnectedHooked && _networkManager != null)
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _clientConnectedHooked = false;
    }

    // 늦은 합류 — 지금 열린 창만 보낸다(닫힌 창은 1초 이력 밖이 곧 된다).
    void HandleClientConnected(ulong clientId)
    {
        if (!IsServerActive || clientId == _networkManager.LocalClientId)
            return;

        _schedule.CopyOpenTo(_recordBuffer);
        for (int i = 0; i < _recordBuffer.Count; i++)
            Send(_recordBuffer[i], clientId);
        _recordBuffer.Clear();
    }

    void Broadcast(in InterruptWindowRecord record)
    {
        if (_networkManager.CustomMessagingManager == null)
            return;

        IReadOnlyList<ulong> clients = _networkManager.ConnectedClientsIds;
        for (int i = 0; i < clients.Count; i++)
        {
            if (clients[i] == _networkManager.LocalClientId)
                continue;   // 호스트 자신은 이미 기록했다
            Send(record, clients[i]);
        }
    }

    void Send(in InterruptWindowRecord record, ulong clientId)
    {
        CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
            return;

        using (var writer = new FastBufferWriter(InterruptWindowRecord.SerializedSize, Allocator.Temp))
        {
            record.Write(writer);
            messaging.SendNamedMessage(WindowMessageName, clientId, writer);
        }
    }

    void HandleWindowMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId || _networkManager.IsServer)
            return;

        InterruptWindowRecord record = InterruptWindowRecord.Read(reader);
        _schedule.Apply(record);
        _hasState = true;
        if (logWindows)
            Edit.Log($"[InterruptManager] 창 #{record.WindowId} 수신 {Describe(record)}", this);
    }
}
