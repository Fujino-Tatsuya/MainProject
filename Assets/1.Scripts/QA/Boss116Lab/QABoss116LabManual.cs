#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>랩 씬 진행 방식 — 할 일 116-C5.</summary>
public enum QABoss116LabMode
{
    /// <summary>배치 메뉴가 쓴 설정 파일(출력 폴더 있음) — 봇이 싸운다(C4).</summary>
    Batch,
    /// <summary>설정 파일 없음(사람이 Dev Boot 로 직접) — 사람 입력을 기록하고 Play 정지 때 결과 폴더를 쓴다(C5).</summary>
    Manual,
    /// <summary>설정 파일이 있지만 오래됐거나 깨졌다 — 봇·기록 없이 살려 두기만(C4 의 사람 모드 그대로).</summary>
    Plain,
}

/// <summary>
/// 할 일 116-C5 — 대쉬 간파 예측 랩 '수동 모드'의 순수 규칙(EditMode <c>QABoss116LabManualTests</c>).
/// <list type="bullet">
/// <item>모드 결정(<see cref="ResolveMode"/>) — 설정 파일(<c>qa116lab.txt</c>)이 없을 때만 수동.</item>
/// <item>사람 입력 시도 판정(<see cref="QABoss116ManualTracker"/>) — 대시·우클릭 원시 입력과 플레이어 상태만 보고 봇과 같은 결과 사유(<c>dashint</c> 줄)를 만든다.</item>
/// <item>결과 폴더 이름(<see cref="FolderName"/>)·콘솔 한 줄 요약(<see cref="SummaryLine"/>).</item>
/// </list>
/// </summary>
public static class QABoss116LabManual
{
    public const string Prefix = "116manual";
    /// <summary>QA 리포트 뿌리 — <c>QAScenario103Menu.QARoot</c>(에디터 어셈블리)와 같은 값.</summary>
    public const string QARoot = "C:/UnityProject/QA";
    /// <summary>Play 정지 뒤 CSV·리포트를 만들 폴더(디렉터가 적고 에디터 훅이 읽는다).</summary>
    public const string PendingKey = "QA.116Lab.Manual.PendingDir";
    public const string Source = "human";

    public static QABoss116LabMode ResolveMode(bool configExists, bool configIsFreshBatch) =>
        !configExists ? QABoss116LabMode.Manual : configIsFreshBatch ? QABoss116LabMode.Batch : QABoss116LabMode.Plain;

    public static string FolderName(DateTime at) => Prefix + "-" + at.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 사람 시도 1건 → 판 파일 <c>dashint</c> 줄(봇 줄과 같은 키 + <c>source=human</c>·입력 간격·카운터 창 열림). <paramref name="gate"/> = 게이트 값(key, value 순서 —
    /// <see cref="QABoss116LabProbe.Snapshot"/>). 이 줄이 C4 집계(<see cref="QABoss116Lab.BuildAttempts"/>)에 그대로 들어간다.
    /// </summary>
    public static string FormatAttempt(QABoss116ManualAttempt a, string character, int clientId, int windowN, string endState, string dashLog,
        IList<string> gate)
    {
        double serverT = a.SecondServerT >= 0.0 ? a.SecondServerT : a.FirstServerT;
        var kv = new List<string>
        {
            "slot", "0",
            "character", character ?? "",
            "clientId", clientId.ToString(CultureInfo.InvariantCulture),
            "order", QABoss116DashPlan.Token(a.Order),
            "outcome", a.Outcome ?? "",
            "delayS", F3(a.GapS),
            "window", windowN.ToString(CultureInfo.InvariantCulture),
            "serverT", F3(serverT),
            "source", Source,
            "firstT", F3(a.FirstServerT),
            "plannedS", F3(a.GapS),
            "gapS", F3(a.GapS),
            "dashAgeS", F3(a.DashAgeS),
            "dashTotalS", F3(a.DashTotalS),
            "waitS", F3(a.WaitS),
            "endState", Or(endState),
            "seen", Or(a.Seen),
            "dashLog", Or(dashLog),
            "counterOpenFirst", a.CounterOpenFirst ? "true" : "false",
            "counterOpenSecond", a.HasSecond ? (a.CounterOpenSecond ? "true" : "false") : "na",
            "sameFrame", a.SameFrame ? "true" : "false",
        };
        if (gate != null)
            kv.AddRange(gate);
        return QABoss103PartFile.Format(QABoss116DashPredict.BotKind, kv.ToArray());
    }

    /// <summary>콘솔 한 줄 — 시도·시전·적중률·사유 상위·카운터 창 안/밖·폴더.</summary>
    public static string SummaryLine(QABoss116Lab.Summary s, string dir)
    {
        QABoss116Lab.Bucket a = s.All;
        var top = new List<KeyValuePair<string, int>>(a.Outcomes);
        top.Sort((x, y) => x.Value != y.Value ? y.Value.CompareTo(x.Value) : string.CompareOrdinal(x.Key, y.Key));
        var sb = new StringBuilder();
        sb.Append(QABoss116Lab.Tag).Append(" 수동 모드 결과 — 시도 ").Append(a.Attempts).Append(" · 서버 시전 ").Append(a.Casts)
          .Append(" · 예측 적중률 ").Append(a.Judged > 0 ? (a.Accuracy * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "–")
          .Append("(판정 ").Append(a.Judged).Append(") · 사유 상위 ");
        if (top.Count == 0)
            sb.Append("없음");
        for (int i = 0; i < top.Count && i < 4; i++)
            sb.Append(i > 0 ? ", " : "").Append(top[i].Key).Append(' ').Append(top[i].Value);
        sb.Append(" · 카운터 창 안 ").Append(Count(s, "in")).Append("/밖 ").Append(Count(s, "out"))
          .Append(" · dash_shortened ").Append(a.Shortened).Append(" · 폴더 ").Append(dir);
        return sb.ToString();
    }

    private static int Count(QABoss116Lab.Summary s, string k) => s.ByCounter.TryGetValue(k, out QABoss116Lab.Bucket b) ? b.Attempts : 0;

    private static string F3(double v) => v < 0.0 || double.IsNaN(v) ? "na" : v.ToString("0.000", CultureInfo.InvariantCulture);
    private static string Or(string v) => string.IsNullOrEmpty(v) ? "na" : v;
}

/// <summary>사람 입력 1프레임 관측(<see cref="QABoss116ManualTracker.Step"/> 입력). 시각은 게임 시각(Time.time), 서버 시각은 기록용.</summary>
public struct QABoss116ManualFrame
{
    public float T;
    public double ServerT;
    /// <summary>이번 프레임 우클릭(간파)·대시 원시 입력.</summary>
    public bool Rmb, Dash;
    /// <summary>대시 상태(<c>PlayerActionState.Dash</c>)와 그 경과·남음(초).</summary>
    public bool InDash;
    public float DashElapsed, DashRemaining;
    /// <summary>Skill 상태 + 진행 스킬 = 간파.</summary>
    public bool InInterrupt;
    /// <summary>대시 상태에 간파가 실렸다(<c>PlayerDashState.AttachedSkill</c>).</summary>
    public bool Attached;
    public int Charge;
    public bool CounterOpen;
    /// <summary>상태 라벨(지나간 상태 순서 <c>seen</c>).</summary>
    public string State;
}

/// <summary>사람 시도 1건 결과.</summary>
public sealed class QABoss116ManualAttempt
{
    public QABoss116Order Order;
    public string Outcome;
    public float FirstT, SecondT = -1f;
    public double FirstServerT = -1.0, SecondServerT = -1.0;
    /// <summary>첫 입력 → 둘째 입력(초). 둘째가 없으면 -1.</summary>
    public float GapS = -1f;
    /// <summary>대시 → 우클릭: 우클릭 순간 대시 경과(초). 모르면 -1.</summary>
    public float DashAgeS = -1f;
    public float DashTotalS = -1f;
    /// <summary>첫 입력 → 결과 확정(초).</summary>
    public float WaitS;
    public bool CounterOpenFirst, CounterOpenSecond, HasSecond, SameFrame;
    /// <summary>게이트 값을 첫 입력 순간 것으로 쓴다(대시 → 우클릭의 대시 거부). 아니면 둘째 입력 순간.</summary>
    public bool GateAtFirst;
    public string Seen = "";
}

/// <summary>
/// 할 일 116-C5 — 사람 입력 대쉬 간파 시도 판정(순수, 봇 <c>QABoss103CombatBot.DashInterrupt</c> 결과 사유와 같은 말).
/// 시도 = 대시·우클릭 두 입력이 <see cref="PairSeconds"/> 안에 이어 눌린 것(한쪽만 누른 회피·일반 간파는 시도가 아니다).
/// <list type="bullet">
/// <item><b>대시 → 우클릭</b>(<c>dash_rmb</c>, 같은 프레임 포함): 대시가 안 섰으면 <c>dash_rejected</c>(게이트 = 대시 누른 순간), 우클릭 때 대시가 이미 끝났으면
///   <c>dash_ended_before_press</c>, 그 뒤 대시에 간파가 실리면 <c>attached</c>, 대시 끝나고 간파 Skill 이면 <c>skill_after_dash</c>, 아니면 <c>no_attach</c>.</item>
/// <item><b>우클릭 → 대시</b>(<c>rmb_dash</c>): 대시 누를 때 간파 Skill 이 아니면 <c>rmb_not_started</c>(한 번 섰다 끝났으면 <c>interrupt_ended_before_dash</c>),
///   충전 0 이면 <c>no_charge</c>, 대시에 간파가 실리면 <c>attached</c>, 실리지 않은 대시면 <c>dash_without_transfer</c>, 대시가 안 서면 <c>no_dash</c>.</item>
/// <item>결과를 지켜보는 상한 <see cref="ObserveSeconds"/>. 그 사이 새 입력이 오면 지금 값으로 끝내고 새 시도를 시작한다.</item>
/// </list>
/// </summary>
public sealed class QABoss116ManualTracker
{
    public const float PairSeconds = 0.6f;
    public const float ObserveSeconds = 1.0f;
    private const int SeenMax = 160;

    private enum Phase { Idle, AfterDash, AfterRmb, Observe }

    private Phase _phase = Phase.Idle;
    private QABoss116ManualAttempt _a;
    private bool _dashSeen, _interruptSeen;
    private float _dashStartT, _observeUntil;
    private string _seenLast;
    private readonly StringBuilder _seen = new StringBuilder();

    /// <summary>이번 Step 에서 첫 입력·둘째 입력을 받았나(호출자가 그 순간 게이트 값을 잡는다).</summary>
    public bool TookFirst { get; private set; }
    public bool TookSecond { get; private set; }
    public bool Busy => _phase != Phase.Idle;

    /// <summary>1프레임 진행. 끝난 시도가 있으면 돌려준다(그 프레임에 새 시도가 시작될 수도 있다 — <see cref="TookFirst"/>).</summary>
    public QABoss116ManualAttempt Step(in QABoss116ManualFrame f)
    {
        TookFirst = TookSecond = false;
        QABoss116ManualAttempt done = null;
        if (_phase != Phase.Idle)
        {
            See(f.State);
            NoteDash(f);
        }

        switch (_phase)
        {
            case Phase.AfterDash:
                if (f.Rmb)
                {
                    Second(f);
                    if (!_dashSeen && f.T > _a.FirstT)
                        return Finish(f, "dash_rejected", true);
                    if (_dashSeen && !f.InDash)
                    {
                        _a.DashAgeS = f.T - _dashStartT;
                        return Finish(f, "dash_ended_before_press", false);
                    }
                    if (_dashSeen)
                        _a.DashAgeS = f.T - _dashStartT;
                    Observe(f);
                    return null;
                }
                if (f.Dash)
                {
                    Reset();   // 우클릭 없이 대시를 다시 눌렀다 — 앞 대시는 회피(시도 아님)
                    break;
                }
                if (f.T - _a.FirstT > PairSeconds)
                    Reset();
                return null;

            case Phase.AfterRmb:
                if (f.InInterrupt)
                    _interruptSeen = true;
                if (f.Dash)
                {
                    Second(f);
                    if (!f.InInterrupt)
                        return Finish(f, _interruptSeen ? "interrupt_ended_before_dash" : "rmb_not_started", false);
                    if (f.Charge <= 0)
                        return Finish(f, "no_charge", false);
                    Observe(f);
                    return null;
                }
                if (f.Rmb)
                {
                    Reset();   // 대시 없이 우클릭을 다시 눌렀다 — 앞 것은 일반 간파(시도 아님)
                    break;
                }
                if (f.T - _a.FirstT > PairSeconds)
                    Reset();
                return null;

            case Phase.Observe:
                done = ObserveStep(f);
                if (done != null)
                {
                    if (!f.Rmb && !f.Dash)
                        return done;
                    break;   // 같은 프레임 새 입력 → 아래에서 새 시도
                }
                if (f.Rmb || f.Dash)
                {
                    done = Finish(f, TimeoutOutcome(), _a.Order == QABoss116Order.DashRmb && !_dashSeen);
                    break;
                }
                return null;
        }

        // Idle(또는 앞 시도를 막 끝냄) — 새 첫 입력
        if (!f.Rmb && !f.Dash)
            return done;
        Start(f);
        return done;
    }

    /// <summary>Play 정지 — 결과를 지켜보던 시도는 지금 값으로 끝낸다. 둘째 입력을 기다리던 것은 시도가 아니다(null).</summary>
    public QABoss116ManualAttempt Flush(in QABoss116ManualFrame f)
    {
        if (_phase != Phase.Observe)
        {
            Reset();
            return null;
        }
        return Finish(f, TimeoutOutcome(), _a.Order == QABoss116Order.DashRmb && !_dashSeen);
    }

    private void Start(in QABoss116ManualFrame f)
    {
        Reset();
        TookFirst = true;
        _a = new QABoss116ManualAttempt
        {
            FirstT = f.T,
            FirstServerT = f.ServerT,
            CounterOpenFirst = f.CounterOpen,
            Order = f.Dash ? QABoss116Order.DashRmb : QABoss116Order.RmbDash,
        };
        See(f.State);
        if (f.Dash && f.Rmb)
        {
            _a.SameFrame = true;
            Second(f);
            Observe(f);
            return;
        }
        _phase = f.Dash ? Phase.AfterDash : Phase.AfterRmb;
    }

    private void Second(in QABoss116ManualFrame f)
    {
        TookSecond = true;
        _a.HasSecond = true;
        _a.SecondT = f.T;
        _a.SecondServerT = f.ServerT;
        _a.GapS = f.T - _a.FirstT;
        _a.CounterOpenSecond = f.CounterOpen;
    }

    private void Observe(in QABoss116ManualFrame f)
    {
        _phase = Phase.Observe;
        _observeUntil = f.T + ObserveSeconds;
    }

    private void NoteDash(in QABoss116ManualFrame f)
    {
        if (!f.InDash || _dashSeen)
            return;
        _dashSeen = true;
        _dashStartT = f.T - f.DashElapsed;
        _a.DashTotalS = f.DashElapsed + f.DashRemaining;
    }

    private QABoss116ManualAttempt ObserveStep(in QABoss116ManualFrame f)
    {
        if (f.Attached)
            return Finish(f, "attached", false);
        if (_a.Order == QABoss116Order.DashRmb)
        {
            // 같은 프레임 누름 — 다음 프레임에도 대시가 안 섰으면 거부
            if (!_dashSeen && f.T > _a.SecondT)
                return Finish(f, "dash_rejected", true);
            if (_a.SameFrame && _dashSeen && _a.DashAgeS < 0f)
                _a.DashAgeS = Math.Max(0f, _a.SecondT - _dashStartT);
            if (_dashSeen && !f.InDash && f.InInterrupt)
                return Finish(f, "skill_after_dash", false);
        }
        else if (_dashSeen && !f.InDash)
            return Finish(f, "dash_without_transfer", false);
        if (f.T >= _observeUntil)
            return Finish(f, TimeoutOutcome(), _a.Order == QABoss116Order.DashRmb && !_dashSeen);
        return null;
    }

    private string TimeoutOutcome() =>
        _a.Order == QABoss116Order.DashRmb
            ? (_dashSeen ? "no_attach" : "dash_rejected")
            : (_dashSeen ? "dash_without_transfer" : "no_dash");

    private QABoss116ManualAttempt Finish(in QABoss116ManualFrame f, string outcome, bool gateAtFirst)
    {
        QABoss116ManualAttempt a = _a;
        a.Outcome = outcome;
        a.GateAtFirst = gateAtFirst;
        a.WaitS = f.T - a.FirstT;
        a.Seen = _seen.ToString();
        Reset();
        return a;
    }

    private void See(string state)
    {
        string now = string.IsNullOrEmpty(state) ? "na" : state;
        if (now == _seenLast || _seen.Length > SeenMax)
            return;
        if (_seen.Length > 0)
            _seen.Append('>');
        _seen.Append(now);
        _seenLast = now;
    }

    private void Reset()
    {
        _phase = Phase.Idle;
        _a = null;
        _dashSeen = _interruptSeen = false;
        _dashStartT = _observeUntil = 0f;
        _seen.Clear();
        _seenLast = null;
    }
}
#endif
