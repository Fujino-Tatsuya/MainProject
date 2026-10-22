using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>할 일 88 봇 설정. <see cref="Default"/> = 사양 기본값(시도 30, 스윕 −150~+50ms, 반응 150~350ms).</summary>
public struct QAInterruptBot88Config
{
    public QAInterruptBotMode Mode;
    public QAInterruptTargetKind Kind;
    /// <summary>런당 간파 시도 수 N(실제로 시전된 누름만 센다).</summary>
    public int Attempts;
    public int Seed;
    public double SweepMinMs, SweepMaxMs;
    public double ReactMinMs, ReactMaxMs;
    /// <summary>대상 중심에서 이 거리(m, 대상 반경 더함) 안이면 멈춘다.</summary>
    public float StopDistance;
    /// <summary>누를 때 대상 쪽을 조준한다(<see cref="QAInputController.UseSkillToward"/>). 끄면 게임 조준(마우스) 그대로.</summary>
    public bool AimAtTarget;

    // 23호(스모크 5차): 마우스 조준이라 23호를 못 보고 '대상 있음 0' — 조준을 켜고, 간파 박스(전방 1.7m)가 HurtBox(r 1.7)에
    // 넉넉히 닿게 1.0m 로 붙인다(중심 ≈ 2.5m). 중간보스는 5차 OK 라 그대로 둔다.
    public static QAInterruptBot88Config Default(QAInterruptTargetKind kind, QAInterruptBotMode mode) => new QAInterruptBot88Config
    {
        Mode = mode,
        Kind = kind,
        Attempts = QAInterruptLag88Plan.DefaultAttempts,
        Seed = 88,
        SweepMinMs = QAInterruptLag88Plan.SweepMinMs,
        SweepMaxMs = QAInterruptLag88Plan.SweepMaxMs,
        ReactMinMs = QAInterruptLag88Plan.ReactMinMs,
        ReactMaxMs = QAInterruptLag88Plan.ReactMaxMs,
        StopDistance = kind == QAInterruptTargetKind.TwentyThree ? 1.0f : 1.5f,
        AimAtTarget = kind == QAInterruptTargetKind.TwentyThree,
    };
}

/// <summary>
/// 할 일 88 간파 봇(QA 전용, 게임 코드 무수정) — 이 인스턴스의 로컬 플레이어로 대상 카운터 창을 보고 간파(<see cref="PlayerSkillSlot.Interrupt"/>)를 누른다.
/// 호스트·원격 어느 인스턴스에서도 돈다(원격 = 지연 그룹, 호스트 = 기준 그룹). 입력은 <see cref="QAInputController"/>(같은 하네스).
/// <para><b>창 관측</b>(이 인스턴스가 보는 것 그대로):
/// 중간보스 = <c>MonsterBase._counterVisual</c>(비공개 NetworkVariable, P572 프로브처럼 리플렉션으로 읽기만) 열림 엣지 —
/// 읽기 권한 Everyone·서버 쓰기라 원격 클라에도 복제되는 신호다(서버 전용 판정 <see cref="MonsterCounterWindow.IsOpen"/> 은 클라에서 늘 닫힘이라 쓰지 않는다),
/// 창 길이 = <see cref="MonsterCounterWindow.WindowDuration"/>(저작값 ÷ 공격속도 — 전 피어 동일). 표시는 판정보다 0.15초 먼저 꺼지지만 열림은 같은 순간이다.
/// 23호 = <see cref="TwentyThreeBoss.CounterWindowOpen"/>(판정 창 복제) 엣지, 창 길이는 복제되지 않아 관측한 열림→닫힘 간격으로 배운다
/// (<see cref="QAWindowDurationLearner"/>) — A 모드는 길이를 배우기 전 첫 창을 누르지 않고 배우기만 한다.
/// 23호는 <b>착지 창만</b> 본다(<see cref="QAInterruptLag88Plan.IsLandingWindow"/> — 체공 중 꺼지는 몸 콜라이더로 판별). 개전 돌진 창 등은 배우지도 누르지도 않는다.</para>
/// <para><b>누를 시각</b>: A = 관측 열림 + 창 길이 + 스윕 오프셋(<see cref="QAInterruptLag88Plan.SweepOffsetMs"/>, 시도 순서대로 고르게),
/// B = 관측 열림 + 반응(<see cref="QAInterruptLag88Plan.ReactionMs"/>). 창 하나에 1회. 쿨다운·시전 거부면 그 창은 건너뛴다(시도 수에 안 넣음).
/// 시각은 실시간(timeScale 무관 — 간파 슬로모션 영향 없음), 프레임 단위라 실제 오프셋은 로그 <c>offsetMs</c> 가 원본이다.</para>
/// <para><b>로그</b>(key=value, Q3 파서용): 누름마다 <c>[QA88] event=press …</c>, 창마다 <c>event=window</c>, 끝에 <c>event=bot_done</c>.
/// <c>pressT</c> = 누른 순간 <c>NetworkManager.ServerTime.Time</c> — 호스트 <c>[InterruptLag] event=judge</c> 의 <c>pressT</c> 와 같은 값이라 둘을 잇는다.</para>
/// </summary>
public sealed class QAInterruptBot88 : MonoBehaviour
{
    private const string Tag = "[QA88]";
    private const float RetargetSeconds = 1f;
    private const float DiagSeconds = 5f;

    private static readonly FieldInfo CounterVisualField =
        typeof(MonsterBase).GetField("_counterVisual", BindingFlags.Instance | BindingFlags.NonPublic);

    // 낭떠러지 검사 바닥 — 보행면(Ground)·Default·Env. 클라엔 NavMesh 가 없어(MapNavMeshBaker 는 서버만 굽는다) 직선 접근이 틈에 떨어졌다(스모크 4차).
    private static readonly int GroundMask = LayerMask.GetMask("Default", "Ground", "Env");

    private QAInputController _input;
    private QAInterruptBot88Config _cfg;
    private System.Random _rng;
    private bool _active;

    private MonsterBase _target;
    private MonsterCounterWindow _midbossWindow;
    private NetworkVariable<bool> _midbossVisual;
    private float _nextRetarget;
    private float _targetRadius;        // 켜진 콜라이더로 본 최대 반경 — 23호 체공 중 꺼진 콜라이더(bounds 0)로 줄지 않게
    private Collider _bodyCollider;     // 23호 루트 몸 콜라이더 — 꺼져 있으면 체공 중
    private Collider _hurtCollider;     // 대상 HurtBox — 누름 진단 거리용

    private double _lastAirborneSeenAt = -1.0;   // 23호 체공(몸 콜라이더 꺼짐)을 마지막으로 본 실시간
    private bool _windowCounts;                  // 이번 창이 측정 대상(23호 = 착지 창)인가 — 닫힐 때 학습 여부

    private readonly QAWindowDurationLearner _learner = new QAWindowDurationLearner();
    private bool _wasOpen;
    private double _openAt;          // 실시간(초) — 관측 열림
    private bool _pending;
    private double _pressAt;
    private double _estClose;        // 관측 열림 + 창 길이(모르면 NaN)
    private double _planOffsetMs;    // A = 계획 오프셋, B = NaN
    private double _planReactMs;     // B = 계획 반응, A = NaN

    // 진단(bot_diag) — 다음 실패 때 "창이 안 열렸나 / 못 봤나 / 못 갔나" 를 가른다.
    private float _nextDiag;
    private double _lastOpenSeenAt = -1.0;
    private MonsterState _lastTargetState;
    private int _attacksSeen;
    private int _edgeBlocks;
    private bool _edgeBlocked;

    public bool IsRunning => _active;
    public bool IsDone { get; private set; }
    public int Attempts { get; private set; }
    public int Windows { get; private set; }
    public int Skipped { get; private set; }
    public QAInterruptBot88Config Config => _cfg;
    public MonsterBase Target => _target;

    /// <summary>봇을 붙이고(이미 있으면 재사용) 시작한다. 같은 GameObject 에 <see cref="QAInputController"/> 가 있어야 한다.</summary>
    public static QAInterruptBot88 Begin(GameObject harness, QAInterruptBot88Config cfg)
    {
        var bot = harness.GetComponent<QAInterruptBot88>();
        if (bot == null)
            bot = harness.AddComponent<QAInterruptBot88>();
        bot.Run(cfg);
        return bot;
    }

    public void Run(QAInterruptBot88Config cfg)
    {
        _input = GetComponent<QAInputController>();
        _cfg = cfg;
        if (_cfg.Attempts <= 0)
            _cfg.Attempts = QAInterruptLag88Plan.DefaultAttempts;
        _rng = new System.Random(_cfg.Seed);
        _learner.Clear();
        Attempts = Windows = Skipped = 0;
        IsDone = false;
        _pending = false;
        _wasOpen = false;
        _target = null;
        _nextRetarget = 0f;
        _nextDiag = 0f;
        _lastOpenSeenAt = -1.0;
        _attacksSeen = _edgeBlocks = 0;
        _edgeBlocked = false;
        _lastAirborneSeenAt = -1.0;
        _windowCounts = false;
        _active = _input != null;
        Debug.Log($"{Tag} event=bot_begin mode={QAInterruptLag88Plan.ModeToken(_cfg.Mode)} kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} " +
                  $"attempts={_cfg.Attempts} seed={_cfg.Seed} input={(_input != null)} netsimMs={QANetSim88.AppliedDelayMs}");
    }

    /// <summary>멈춘다(이동 정지). 결과 줄을 남긴다.</summary>
    public void Stop(string reason = "stop")
    {
        if (!_active)
            return;
        _active = false;
        _pending = false;
        _input?.SetMove(Vector3.zero);
        Debug.Log($"{Tag} event=bot_done reason={reason} mode={QAInterruptLag88Plan.ModeToken(_cfg.Mode)} kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} " +
                  $"attempts={Attempts} windows={Windows} skipped={Skipped} netsimMs={QANetSim88.AppliedDelayMs}");
    }

    private void OnDisable() => Stop("disabled");

    private void Update()
    {
        if (!_active)
            return;
        Player me = _input.Target;
        if (me == null)
            return;

        if (!ResolveTarget())
        {
            _input.SetMove(Vector3.zero);
            return;
        }

        Approach(me);

        double now = Time.realtimeSinceStartupAsDouble;
        if (_bodyCollider != null && !_bodyCollider.enabled)
            _lastAirborneSeenAt = now;
        bool open = IsWindowOpen();
        if (open && !_wasOpen)
            OnOpened(now);
        else if (!open && _wasOpen)
            OnClosed(now);
        _wasOpen = open;

        MonsterState targetState = _target.State;   // 복제값 — 이 인스턴스가 본 보스 공격
        if (targetState != _lastTargetState && targetState == MonsterState.Attack)
            _attacksSeen++;
        _lastTargetState = targetState;

        if (_pending && now >= _pressAt)
            Press(now, open);

        if (_active && Time.realtimeSinceStartup >= _nextDiag)
        {
            _nextDiag = Time.realtimeSinceStartup + DiagSeconds;
            LogDiag(me, open, now);
        }
    }

    private void LogDiag(Player me, bool open, double now)
    {
        Vector3 mine = me.transform.position;
        Vector3 boss = _target.transform.position;
        string observe = _cfg.Kind == QAInterruptTargetKind.TwentyThree ? "CounterWindowOpen" : _midbossVisual != null ? "_counterVisual" : "missing";
        string lastOpen = _lastOpenSeenAt < 0.0 ? "na" : Fmt(now - _lastOpenSeenAt);
        Debug.Log($"{Tag} event=bot_diag kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} target={QAInterruptLag88Plan.LogToken(_target.name)} " +
                  $"observe={observe} open={(open ? "true" : "false")} windows={Windows} lastOpenAgoS={lastOpen} attempts={Attempts} " +
                  $"dist={Fmt(QAPartyPlacement88.HorizontalDistance(mine, boss))} dy={Fmt(mine.y - boss.y)} " +
                  $"bossState={_target.State} attacksSeen={_attacksSeen} aggroId={_target.AggroTargetId} " +
                  $"edgeBlocked={(_edgeBlocked ? "true" : "false")} edgeBlocks={_edgeBlocks} " +
                  $"myPos={Fmt(mine)} bossPos={Fmt(boss)}");
    }

    // ── 대상 ───────────────────────────────────────────────────

    private bool ResolveTarget()
    {
        if (_target != null && _target.CurrentHealth > 0)
            return true;
        if (Time.realtimeSinceStartup < _nextRetarget)
            return false;
        _nextRetarget = Time.realtimeSinceStartup + RetargetSeconds;

        _target = null;
        _midbossWindow = null;
        _midbossVisual = null;
        _targetRadius = 0f;
        _bodyCollider = null;
        _hurtCollider = null;
        _wasOpen = false;
        _pending = false;
        if (_cfg.Kind == QAInterruptTargetKind.TwentyThree)
        {
            TwentyThreeBoss boss = FindAnyObjectByType<TwentyThreeBoss>();
            if (boss != null && boss.CurrentHealth > 0)
            {
                _target = boss;
                foreach (Collider c in boss.GetComponents<Collider>())
                    if (c != null && !c.isTrigger) { _bodyCollider = c; break; }
            }
        }
        else
        {
            Vector3 from = _input.Target.transform.position;
            float best = float.MaxValue;
            foreach (MonsterCounterWindow w in FindObjectsByType<MonsterCounterWindow>(FindObjectsSortMode.None))
            {
                MonsterBase m = w.GetComponent<MonsterBase>();
                if (m == null || m is TwentyThreeBoss || m.CurrentHealth <= 0 || m.IsDormant)
                    continue;
                float d = (m.transform.position - from).sqrMagnitude;
                if (d < best) { best = d; _target = m; _midbossWindow = w; }
            }
            if (_target != null && CounterVisualField != null)
                _midbossVisual = CounterVisualField.GetValue(_target) as NetworkVariable<bool>;
        }
        if (_target != null)
        {
            Hurtbox hurt = _target.GetComponentInChildren<Hurtbox>();
            _hurtCollider = hurt != null ? hurt.GetComponent<Collider>() : null;
            _lastTargetState = _target.State;
            Debug.Log($"{Tag} event=target kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} target={QAInterruptLag88Plan.LogToken(_target.name)} " +
                      $"observe={(_cfg.Kind == QAInterruptTargetKind.TwentyThree ? "CounterWindowOpen" : _midbossVisual != null ? "_counterVisual" : "missing")} " +
                      $"dist={Fmt(QAPartyPlacement88.HorizontalDistance(_input.Target.transform.position, _target.transform.position))}");
        }
        return _target != null;
    }

    private bool IsWindowOpen()
    {
        if (_target is TwentyThreeBoss boss)
            return boss.CounterWindowOpen;
        return _midbossVisual != null && _midbossVisual.Value;
    }

    /// <summary>
    /// 대상 쪽으로 직선 이동 — 앞 바닥이 없으면(틈·낭떠러지) 멈춘다. 클라엔 NavMesh 가 없어 길찾기를 못 하므로
    /// 파티는 서버가 보스 옆(같은 섬)에 놓고(<c>QABossStart88.TryFindBossRingPoints</c>), 그 뒤엔 보스가 쫓아온다.
    /// 스모크 4차: 이 검사 없이 섬 밖에서 걷다 두 번 떨어지고 부활 지점(80m 밖)에서 벽에 붙어 10분을 보냈다.
    /// </summary>
    private void Approach(Player me)
    {
        Vector3 delta = _target.transform.position - me.transform.position;
        delta.y = 0f;
        float radius = 0f;
        Collider c = _target.GetComponentInChildren<Collider>();
        if (c != null)
            radius = Mathf.Max(c.bounds.extents.x, c.bounds.extents.z);
        if (_cfg.Kind == QAInterruptTargetKind.TwentyThree)
        {
            // 체공 중엔 몸 콜라이더가 꺼져 bounds 가 0 — 켜져 있을 때 본 반경을 쓴다.
            if (c != null && c.enabled)
                _targetRadius = Mathf.Max(_targetRadius, radius);
            radius = _targetRadius;
        }
        if (delta.magnitude <= _cfg.StopDistance + radius)
        {
            _input.SetMove(Vector3.zero);
            return;
        }

        bool safe = IsSafeStep(me.transform.position, delta.normalized);
        if (!safe && !_edgeBlocked)
        {
            _edgeBlocks++;
            Debug.Log($"{Tag} event=bot_edge_block n={_edgeBlocks} dist={Fmt(delta.magnitude)} myPos={Fmt(me.transform.position)} bossPos={Fmt(_target.transform.position)}");
        }
        _edgeBlocked = !safe;
        _input.SetMove(safe ? delta : Vector3.zero);
    }

    private static bool IsSafeStep(Vector3 foot, Vector3 dir)
    {
        Vector3 probe = foot + dir * QAPartyPlacement88.EdgeProbeAhead + Vector3.up;
        bool found = Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 1f + QAPartyPlacement88.EdgeMaxDrop + 0.5f, GroundMask, QueryTriggerInteraction.Ignore);
        return QAPartyPlacement88.IsSafeStep(found, foot.y, found ? hit.point.y : 0f);
    }

    // ── 창 엣지 ────────────────────────────────────────────────

    private void OnOpened(double now)
    {
        Windows++;
        _openAt = now;
        _lastOpenSeenAt = now;
        _planOffsetMs = double.NaN;
        _planReactMs = double.NaN;

        _windowCounts = _cfg.Kind != QAInterruptTargetKind.TwentyThree || QAInterruptLag88Plan.IsLandingWindow(now, _lastAirborneSeenAt);
        if (!_windowCounts)
        {
            _pending = false;
            _estClose = double.NaN;
            string airborneAgo = _lastAirborneSeenAt < 0.0 ? "na" : Fmt(now - _lastAirborneSeenAt);
            Debug.Log($"{Tag} event=window state=open landing=false skip=not_landing kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} " +
                      $"windows={Windows} airborneAgoS={airborneAgo}");
            return;
        }

        bool known = TryWindowDuration(out double durSec);
        _estClose = known ? now + durSec : double.NaN;

        if (_cfg.Mode == QAInterruptBotMode.Sweep)
        {
            if (!known)
            {
                _pending = false;
                Debug.Log($"{Tag} event=window state=open learn=true kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} windows={Windows}");
                return;
            }
            _planOffsetMs = QAInterruptLag88Plan.SweepOffsetMs(Attempts, _cfg.Attempts, _cfg.SweepMinMs, _cfg.SweepMaxMs);
            _pressAt = now + QAInterruptLag88Plan.SweepPressDelayMs(durSec * 1000.0, _planOffsetMs) / 1000.0;
        }
        else
        {
            _planReactMs = QAInterruptLag88Plan.ReactionMs(_rng, _cfg.ReactMinMs, _cfg.ReactMaxMs);
            _pressAt = now + _planReactMs / 1000.0;
        }
        _pending = true;
    }

    private void OnClosed(double now)
    {
        double durMs = (now - _openAt) * 1000.0;
        if (_cfg.Kind == QAInterruptTargetKind.TwentyThree && _windowCounts)
            _learner.Add(now - _openAt);
        Debug.Log($"{Tag} event=window state=closed kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} observedMs={Fmt(durMs)} windows={Windows} " +
                  $"learned={(_cfg.Kind == QAInterruptTargetKind.TwentyThree && _windowCounts ? "true" : "false")}");
    }

    private bool TryWindowDuration(out double seconds)
    {
        if (_midbossWindow != null)
        {
            seconds = _midbossWindow.WindowDuration;
            return seconds > 0.0;
        }
        return _learner.TryEstimate(out seconds);
    }

    // ── 누름 ───────────────────────────────────────────────────

    private void Press(double now, bool openNow)
    {
        _pending = false;
        string aimDiag = AimDiag(_input.Target);   // 시전 전(스냅 회전 전) — 누를 때 바라보던 방향
        string reason = _input.SkillBlockReason(PlayerSkillSlot.Interrupt);
        bool cast = reason == null && (_cfg.AimAtTarget
            ? _input.UseSkillToward(PlayerSkillSlot.Interrupt, _target)
            : _input.UseSkill(PlayerSkillSlot.Interrupt, _target));
        double offsetMs = double.IsNaN(_estClose) ? double.NaN : (now - _estClose) * 1000.0;
        double reactMs = (now - _openAt) * 1000.0;
        if (!cast)
        {
            Skipped++;
            Debug.Log($"{Tag} event=press_skip reason={QAInterruptLag88Plan.LogToken(reason ?? "cast_rejected")} mode={QAInterruptLag88Plan.ModeToken(_cfg.Mode)} " +
                      $"windows={Windows} attempts={Attempts} {aimDiag}");
            return;
        }

        int idx = Attempts;
        Attempts++;
        NetworkManager nm = NetworkManager.Singleton;
        double pressT = nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
        ulong clientId = nm != null ? nm.LocalClientId : 0;
        bool host = nm != null && nm.IsServer;
        Debug.Log($"{Tag} event=press clientId={clientId} host={(host ? "true" : "false")} mode={QAInterruptLag88Plan.ModeToken(_cfg.Mode)} " +
                  $"kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} target={QAInterruptLag88Plan.LogToken(_target.name)} idx={idx} n={_cfg.Attempts} " +
                  $"planOffsetMs={Fmt(_planOffsetMs)} offsetMs={Fmt(offsetMs)} planReactMs={Fmt(_planReactMs)} reactMs={Fmt(reactMs)} " +
                  $"openAtPress={(openNow ? "true" : "false")} pressT={pressT.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} netsimMs={QANetSim88.AppliedDelayMs} " +
                  aimDiag);

        if (Attempts >= _cfg.Attempts)
        {
            IsDone = true;
            Stop("done");
        }
    }

    /// <summary>
    /// 누름 진단(스모크 5차 '대상 있음 0' 대응): 이 인스턴스가 본 봇↔보스 수평 중심 거리, 판정 앵커 피벗↔HurtBox 표면 거리(surf),
    /// 전방위 사거리(reach — 앵커를 어느 방향으로 돌려도 닿는 최대 수평 거리, HUD 와 같은 <see cref="InterruptOmniRangeModel"/>),
    /// 사거리 판정(inReach = surf ≤ reach), 앵커 정면 ↔ 보스 방향 각도(faceDeg — 조준 전), 조준 방식(aim=target|game).
    /// 서버 판정은 서버 위치·스냅 회전 뒤 방향으로 하므로 원격은 지연만큼 다를 수 있다 — 원본 판정은 호스트 `[InterruptLag]` 줄.
    /// </summary>
    private string AimDiag(Player me)
    {
        string aim = _cfg.AimAtTarget ? "target" : "game";
        if (me == null || _target == null)
            return $"aim={aim} dist=na surf=na reach=na inReach=na faceDeg=na";

        Vector3 pivot = me.transform.position;
        Vector3 toBoss = _target.transform.position - pivot;
        double dist = QAPartyPlacement88.HorizontalDistance(pivot, _target.transform.position);

        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        ColliderInfo anchor = skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt)?.HitboxAnchor : null;
        double reach = double.NaN;
        if (anchor != null)
        {
            switch (anchor.OverlapCollider)
            {
                case OverlapCollider.Box:
                    BoxColliderInfo box = default;
                    anchor.GetBoxColliderInfo(ref box);
                    reach = InterruptOmniRangeModel.BoxReach(pivot, box.center, box.halfExtents, box.orientation);
                    break;
                case OverlapCollider.Sphere:
                    SphereColliderInfo sphere = default;
                    anchor.GetSphereColliderInfo(ref sphere);
                    reach = InterruptOmniRangeModel.SphereReach(pivot, sphere.center, sphere.radius);
                    break;
                case OverlapCollider.Capsule:
                    CapsuleColliderInfo capsule = default;
                    anchor.GetCapsuleColliderInfo(ref capsule);
                    reach = InterruptOmniRangeModel.CapsuleReach(pivot, capsule.point0, capsule.point1, capsule.radius);
                    break;
            }
        }

        double surf = _hurtCollider != null && _hurtCollider.enabled
            ? InterruptOmniRangeModel.HorizontalDistanceToBounds(pivot, _hurtCollider.bounds)
            : double.NaN;
        string inReach = double.IsNaN(surf) || double.IsNaN(reach) ? "na" : surf <= reach ? "true" : "false";
        Vector3 face = anchor != null ? anchor.transform.forward : me.transform.forward;
        double faceDeg = QAInterruptLag88Plan.FlatAngleDeg(face.x, face.z, toBoss.x, toBoss.z);
        return $"aim={aim} dist={Fmt(dist)} surf={Fmt(surf)} reach={Fmt(reach)} inReach={inReach} faceDeg={Fmt(faceDeg)} " +
               $"hurtOn={(_hurtCollider != null && _hurtCollider.enabled ? "true" : "false")}";
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "na" : v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    private static string Fmt(Vector3 v) => $"{Fmt(v.x)}/{Fmt(v.y)}/{Fmt(v.z)}";
}
