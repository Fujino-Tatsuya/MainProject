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
        StopDistance = 1.5f,
    };
}

/// <summary>
/// 할 일 88 간파 봇(QA 전용, 게임 코드 무수정) — 이 인스턴스의 로컬 플레이어로 대상 카운터 창을 보고 간파(<see cref="PlayerSkillSlot.Interrupt"/>)를 누른다.
/// 호스트·원격 어느 인스턴스에서도 돈다(원격 = 지연 그룹, 호스트 = 기준 그룹). 입력은 <see cref="QAInputController"/>(같은 하네스).
/// <para><b>창 관측</b>(이 인스턴스가 보는 것 그대로):
/// 중간보스 = <c>MonsterBase._counterVisual</c>(비공개 NetworkVariable, P572 프로브처럼 리플렉션으로 읽기만) 열림 엣지,
/// 창 길이 = <see cref="MonsterCounterWindow.WindowDuration"/>(저작값 ÷ 공격속도 — 전 피어 동일). 표시는 판정보다 0.15초 먼저 꺼지지만 열림은 같은 순간이다.
/// 23호 = <see cref="TwentyThreeBoss.CounterWindowOpen"/>(판정 창 복제) 엣지, 창 길이는 복제되지 않아 관측한 열림→닫힘 간격으로 배운다
/// (<see cref="QAWindowDurationLearner"/>) — A 모드는 길이를 배우기 전 첫 창을 누르지 않고 배우기만 한다.</para>
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

    private static readonly FieldInfo CounterVisualField =
        typeof(MonsterBase).GetField("_counterVisual", BindingFlags.Instance | BindingFlags.NonPublic);

    private QAInputController _input;
    private QAInterruptBot88Config _cfg;
    private System.Random _rng;
    private bool _active;

    private MonsterBase _target;
    private MonsterCounterWindow _midbossWindow;
    private NetworkVariable<bool> _midbossVisual;
    private float _nextRetarget;

    private readonly QAWindowDurationLearner _learner = new QAWindowDurationLearner();
    private bool _wasOpen;
    private double _openAt;          // 실시간(초) — 관측 열림
    private bool _pending;
    private double _pressAt;
    private double _estClose;        // 관측 열림 + 창 길이(모르면 NaN)
    private double _planOffsetMs;    // A = 계획 오프셋, B = NaN
    private double _planReactMs;     // B = 계획 반응, A = NaN

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
        bool open = IsWindowOpen();
        if (open && !_wasOpen)
            OnOpened(now);
        else if (!open && _wasOpen)
            OnClosed(now);
        _wasOpen = open;

        if (_pending && now >= _pressAt)
            Press(now, open);
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
        _wasOpen = false;
        _pending = false;
        if (_cfg.Kind == QAInterruptTargetKind.TwentyThree)
        {
            TwentyThreeBoss boss = FindAnyObjectByType<TwentyThreeBoss>();
            if (boss != null && boss.CurrentHealth > 0)
                _target = boss;
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
            Debug.Log($"{Tag} event=target kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} target={QAInterruptLag88Plan.LogToken(_target.name)} " +
                      $"observe={(_cfg.Kind == QAInterruptTargetKind.TwentyThree ? "CounterWindowOpen" : _midbossVisual != null ? "_counterVisual" : "missing")}");
        return _target != null;
    }

    private bool IsWindowOpen()
    {
        if (_target is TwentyThreeBoss boss)
            return boss.CounterWindowOpen;
        return _midbossVisual != null && _midbossVisual.Value;
    }

    private void Approach(Player me)
    {
        Vector3 delta = _target.transform.position - me.transform.position;
        delta.y = 0f;
        float radius = 0f;
        Collider c = _target.GetComponentInChildren<Collider>();
        if (c != null)
            radius = Mathf.Max(c.bounds.extents.x, c.bounds.extents.z);
        _input.SetMove(delta.magnitude > _cfg.StopDistance + radius ? delta : Vector3.zero);
    }

    // ── 창 엣지 ────────────────────────────────────────────────

    private void OnOpened(double now)
    {
        Windows++;
        _openAt = now;
        _planOffsetMs = double.NaN;
        _planReactMs = double.NaN;

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
        if (_cfg.Kind == QAInterruptTargetKind.TwentyThree)
            _learner.Add(now - _openAt);
        Debug.Log($"{Tag} event=window state=closed kind={QAInterruptLag88Plan.KindToken(_cfg.Kind)} observedMs={Fmt(durMs)} windows={Windows}");
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
        string reason = _input.SkillBlockReason(PlayerSkillSlot.Interrupt);
        bool cast = reason == null && _input.UseSkill(PlayerSkillSlot.Interrupt, _target);
        double offsetMs = double.IsNaN(_estClose) ? double.NaN : (now - _estClose) * 1000.0;
        double reactMs = (now - _openAt) * 1000.0;
        if (!cast)
        {
            Skipped++;
            Debug.Log($"{Tag} event=press_skip reason={QAInterruptLag88Plan.LogToken(reason ?? "cast_rejected")} mode={QAInterruptLag88Plan.ModeToken(_cfg.Mode)} " +
                      $"windows={Windows} attempts={Attempts}");
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
                  $"openAtPress={(openNow ? "true" : "false")} pressT={pressT.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} netsimMs={QANetSim88.AppliedDelayMs}");

        if (Attempts >= _cfg.Attempts)
        {
            IsDone = true;
            Stop("done");
        }
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "na" : v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
}
