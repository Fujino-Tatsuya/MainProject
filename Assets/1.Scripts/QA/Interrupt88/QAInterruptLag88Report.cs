using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 할 일 88 Q3 — 스위트 설정·명령 파일·로그 파싱·런별 집계·리포트 표. 순수 계산(Unity·네트워크·파일 의존 없음, EditMode QAInterruptLag88ReportTests).
// 런타임 진행(호스트 드라이버·클론 팔로워)은 QAInterruptSuite88, 로그 형식 원본은 Docs/tech/networking.md 'Interrupt lag measurement' 와 QAInterruptBot88.

/// <summary>스위트의 런 하나 = 대상 × 원격 지연 × 봇 모드.</summary>
public struct QA88RunSpec
{
    public int Index;
    public QAInterruptTargetKind Kind;
    public int DelayMs;
    public QAInterruptBotMode Mode;

    public string Label =>
        $"{(Kind == QAInterruptTargetKind.Midboss ? "중간보스" : "23호")} · {DelayMs}ms · {(Mode == QAInterruptBotMode.Sweep ? "A 스윕" : "B 사람형")}";
}

/// <summary>
/// 스위트 설정 파일(메인 프로젝트 <c>Library/QA88/suite.txt</c>, 에디터 메뉴가 쓰고 호스트 런타임이 읽는다). 줄마다 <c>key=value</c>.
/// <c>runs</c> = <c>대상:지연:모드</c> 쉼표 목록(예 <c>midboss:5:sweep,23:120:human</c>).
/// </summary>
public sealed class QA88SuiteConfig
{
    public const int DefaultRunTimeoutSec = 600;

    public int Attempts = QAInterruptLag88Plan.DefaultAttempts;
    public int RunTimeoutSec = DefaultRunTimeoutSec;
    /// <summary>호스트도 봇으로 누른다(기준 그룹). 기본 끔 — 같은 창을 둘이 누르면 먼저 성공한 쪽이 창을 닫아 원격 측정이 오염된다.</summary>
    public bool HostBot;
    public readonly List<QA88RunSpec> Runs = new List<QA88RunSpec>();

    /// <summary>사양 순서(대상 → 지연 → 모드) 16 런. 같은 대상을 몰아 강제 개시를 대상마다 1번만 한다.</summary>
    public static QA88SuiteConfig FullSuite(int attempts = QAInterruptLag88Plan.DefaultAttempts)
    {
        var cfg = new QA88SuiteConfig { Attempts = attempts };
        foreach (QAInterruptTargetKind kind in new[] { QAInterruptTargetKind.Midboss, QAInterruptTargetKind.TwentyThree })
            foreach (int delay in QAInterruptLag88Plan.DelayStepsMs)
                foreach (QAInterruptBotMode mode in new[] { QAInterruptBotMode.Sweep, QAInterruptBotMode.Human })
                    cfg.Add(kind, delay, mode);
        return cfg;
    }

    public void Add(QAInterruptTargetKind kind, int delayMs, QAInterruptBotMode mode) =>
        Runs.Add(new QA88RunSpec { Index = Runs.Count, Kind = kind, DelayMs = delayMs, Mode = mode });

    public string Format()
    {
        var parts = new List<string>();
        foreach (QA88RunSpec r in Runs)
            parts.Add($"{QAInterruptLag88Plan.KindToken(r.Kind)}:{r.DelayMs}:{QAInterruptLag88Plan.ModeToken(r.Mode)}");
        return $"attempts={Attempts}\nrunTimeoutSec={RunTimeoutSec}\nhostBot={(HostBot ? 1 : 0)}\nruns={string.Join(",", parts)}\n";
    }

    /// <summary>파싱. 런이 하나도 없으면 false. 틀린 런 토큰은 건너뛴다.</summary>
    public static bool TryParse(string text, out QA88SuiteConfig cfg)
    {
        cfg = new QA88SuiteConfig();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        foreach (string raw in text.Replace("\r", string.Empty).Split('\n'))
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = raw.Substring(0, eq).Trim();
            string value = raw.Substring(eq + 1).Trim();
            switch (key)
            {
                case "attempts":
                    if (int.TryParse(value, out int a) && a > 0) cfg.Attempts = a;
                    break;
                case "runTimeoutSec":
                    if (int.TryParse(value, out int t) && t > 0) cfg.RunTimeoutSec = t;
                    break;
                case "hostBot":
                    cfg.HostBot = value == "1" || value == "true";
                    break;
                case "runs":
                    foreach (string token in value.Split(','))
                        if (TryParseRun(token.Trim(), out QAInterruptTargetKind k, out int d, out QAInterruptBotMode m))
                            cfg.Add(k, d, m);
                    break;
            }
        }
        return cfg.Runs.Count > 0;
    }

    private static bool TryParseRun(string token, out QAInterruptTargetKind kind, out int delayMs, out QAInterruptBotMode mode)
    {
        kind = QAInterruptTargetKind.Midboss;
        mode = QAInterruptBotMode.Sweep;
        delayMs = 0;
        string[] f = token.Split(':');
        if (f.Length != 3 || !int.TryParse(f[1], out delayMs) || delayMs < 0)
            return false;
        if (f[0] == "midboss") kind = QAInterruptTargetKind.Midboss;
        else if (f[0] == "23") kind = QAInterruptTargetKind.TwentyThree;
        else return false;
        if (f[2] == "sweep") mode = QAInterruptBotMode.Sweep;
        else if (f[2] == "human") mode = QAInterruptBotMode.Human;
        else return false;
        return true;
    }

    /// <summary>러너 감시 상한(초) — 런마다 상한 + 준비·유예, 앞뒤 준비(맵 진입·팔로워 대기·강제 개시).</summary>
    public float SuiteTimeLimitSeconds() => 900f + Runs.Count * (RunTimeoutSec + 90f);
}

/// <summary>
/// 호스트 → 클론 명령 파일(클론 루트 <c>qa-run88.txt</c>). 1줄 = <c>key=value</c>(공백 구분), 2줄 = 스위트 폴더(공백 허용).
/// <c>state</c> = <c>idle</c>(대기·런 정지) / <c>go</c>(런 시작) / <c>end</c>(스위트 끝). <c>seq</c> 가 바뀔 때만 클론이 반응한다.
/// </summary>
public struct QA88Command
{
    public const string FileName = "qa-run88.txt";

    public int Seq;
    public string State;
    public int Run;
    public QAInterruptTargetKind Kind;
    public QAInterruptBotMode Mode;
    public int Attempts;
    public int Seed;
    public bool Press;
    public string SuiteDir;

    public string Format() =>
        $"seq={Seq} state={State} run={Run} kind={QAInterruptLag88Plan.KindToken(Kind)} mode={QAInterruptLag88Plan.ModeToken(Mode)} " +
        $"attempts={Attempts} seed={Seed} press={(Press ? "true" : "false")}\n{SuiteDir}\n";

    public static bool TryParse(string text, out QA88Command cmd)
    {
        cmd = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] lines = text.Replace("\r", string.Empty).Split('\n');
        var values = QAInterruptLag88Log.ParseKeyValues(lines[0]);
        if (!values.TryGetValue("seq", out string seq) || !int.TryParse(seq, out cmd.Seq))
            return false;
        cmd.State = values.TryGetValue("state", out string s) ? s : "idle";
        cmd.Run = values.TryGetValue("run", out string r) && int.TryParse(r, out int run) ? run : -1;
        cmd.Kind = values.TryGetValue("kind", out string k) && k == "23" ? QAInterruptTargetKind.TwentyThree : QAInterruptTargetKind.Midboss;
        cmd.Mode = values.TryGetValue("mode", out string m) && m == "human" ? QAInterruptBotMode.Human : QAInterruptBotMode.Sweep;
        cmd.Attempts = values.TryGetValue("attempts", out string a) && int.TryParse(a, out int n) ? n : QAInterruptLag88Plan.DefaultAttempts;
        cmd.Seed = values.TryGetValue("seed", out string sd) && int.TryParse(sd, out int seed) ? seed : 88;
        cmd.Press = values.TryGetValue("press", out string p) && p == "true";
        cmd.SuiteDir = lines.Length > 1 ? lines[1].Trim() : string.Empty;
        return true;
    }
}

/// <summary><c>[QA88]</c> 또는 <c>[InterruptLag]</c> 로그 한 줄의 key=value.</summary>
public sealed class QA88LogLine
{
    public string Tag;
    public Dictionary<string, string> Values;

    public string Event => Get("event");
    public bool IsLag => Tag == QAInterruptLag88Log.LagTag;
    public bool IsQA88 => Tag == QAInterruptLag88Log.QA88Tag;

    public string Get(string key) => Values.TryGetValue(key, out string v) ? v : null;

    /// <summary>숫자 값. 없거나 <c>na</c>·숫자가 아니면 NaN.</summary>
    public double GetDouble(string key) =>
        Values.TryGetValue(key, out string v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;

    public int GetInt(string key, int fallback = -1) =>
        Values.TryGetValue(key, out string v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : fallback;

    public bool GetBool(string key) => Get(key) == "true";
}

/// <summary>로그 줄 파싱·런 구간 나누기. 런 구간 = 같은 파일 안 <c>[QA88] event=run_begin run=K</c> ~ <c>event=run_end run=K</c>.</summary>
public static class QAInterruptLag88Log
{
    public const string QA88Tag = "[QA88]";
    public const string LagTag = "[InterruptLag]";

    /// <summary>줄 어디에든 태그가 있으면(tee 접두어 허용) 태그 뒤 key=value 를 읽는다.</summary>
    public static bool TryParse(string line, out QA88LogLine parsed)
    {
        parsed = null;
        if (string.IsNullOrEmpty(line))
            return false;
        string tag = QA88Tag;
        int at = line.IndexOf(QA88Tag, StringComparison.Ordinal);
        if (at < 0)
        {
            tag = LagTag;
            at = line.IndexOf(LagTag, StringComparison.Ordinal);
        }
        if (at < 0)
            return false;
        var values = ParseKeyValues(line.Substring(at + tag.Length));
        if (!values.ContainsKey("event"))
            return false;
        parsed = new QA88LogLine { Tag = tag, Values = values };
        return true;
    }

    /// <summary>공백 구분 <c>key=value</c>. '=' 없는 토큰은 건너뛴다.</summary>
    public static Dictionary<string, string> ParseKeyValues(string text)
    {
        var values = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(text))
            return values;
        foreach (string token in text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0)
                continue;
            values[token.Substring(0, eq)] = token.Substring(eq + 1);
        }
        return values;
    }

    /// <summary>런별 줄(마커 줄 포함). 끝 마커가 없으면 다음 run_begin 또는 파일 끝까지.</summary>
    public static Dictionary<int, List<QA88LogLine>> SplitByRun(IEnumerable<string> lines)
    {
        var runs = new Dictionary<int, List<QA88LogLine>>();
        List<QA88LogLine> current = null;
        foreach (string line in lines)
        {
            if (!TryParse(line, out QA88LogLine p))
                continue;
            if (p.IsQA88 && p.Event == "run_begin")
            {
                int run = p.GetInt("run");
                current = new List<QA88LogLine>();
                runs[run] = current;
            }
            if (current == null)
                continue;
            current.Add(p);
            if (p.IsQA88 && p.Event == "run_end")
                current = null;
        }
        return runs;
    }

    public static string FormatRunBegin(QA88RunSpec spec, string role, bool press) =>
        $"{QA88Tag} event=run_begin run={spec.Index} role={QAInterruptLag88Plan.LogToken(role)} kind={QAInterruptLag88Plan.KindToken(spec.Kind)} " +
        $"mode={QAInterruptLag88Plan.ModeToken(spec.Mode)} delayMs={spec.DelayMs} press={(press ? "true" : "false")}";

    public static string FormatRunEnd(int run, string role, string reason, int attempts, int windows, int skipped, int findings, string findingTop) =>
        $"{QA88Tag} event=run_end run={run} role={QAInterruptLag88Plan.LogToken(role)} reason={QAInterruptLag88Plan.LogToken(reason)} " +
        $"attempts={attempts} windows={windows} skipped={skipped} findings={findings} findingTop={QAInterruptLag88Plan.LogToken(findingTop)}";
}

/// <summary>A 스윕 오프셋 구간(폭 <see cref="QAInterruptLag88Aggregate.BucketMs"/>) 하나.</summary>
public sealed class QA88Bucket
{
    public double LoMs;
    public int N, Success, WouldSucceed;
}

/// <summary>런 하나의 그룹(<c>remote</c>/<c>host</c>) 집계.</summary>
public sealed class QA88GroupStats
{
    public string Group;
    /// <summary>봇 누름 줄(<c>[QA88] event=press</c>) 수.</summary>
    public int Presses;
    /// <summary>호스트 판정 줄(<c>[InterruptLag] event=judge</c>) 수 = 시도.</summary>
    public int Judges;
    /// <summary>누름 ↔ 판정이 pressT 로 이어진 수.</summary>
    public int Matched;
    public int WithTarget, Success, WouldSucceed;
    /// <summary>보정했으면 성공인데 실제 실패.</summary>
    public int Rescued;
    /// <summary>실제 성공인데 보정 판정이면 실패(보정이 오히려 깎는 경우).</summary>
    public int Lost;
    public double RttAvgMs = double.NaN, RttMaxMs = double.NaN, LagAvgMs = double.NaN, LagMaxMs = double.NaN;
    public readonly List<QA88Bucket> Buckets = new List<QA88Bucket>();
}

/// <summary>호스트 판정 줄 + 봇 누름 줄 → 런별 그룹 집계와 A 모드 오프셋 구간별 성공률.</summary>
public static class QAInterruptLag88Aggregate
{
    /// <summary>누름 pressT 와 판정 pressT 가 같은 클라에서 이 안(초)이면 같은 시도로 본다(둘 다 F3 로 찍힘).</summary>
    public const double MatchToleranceSec = 0.02;
    public const double BucketMs = 25.0;

    public static double BucketLo(double offsetMs, double size = BucketMs) => Math.Floor(offsetMs / size) * size;

    /// <summary>
    /// <paramref name="lines"/> = 그 런의 호스트·모든 클론 줄을 합친 것. 그룹은 판정의 <c>host</c> / 누름의 <c>host</c> 로 나눈다.
    /// <c>remote</c> 는 늘 돌려주고, <c>host</c> 는 누름이나 판정이 있을 때만.
    /// </summary>
    public static List<QA88GroupStats> Aggregate(IEnumerable<QA88LogLine> lines)
    {
        var judges = new List<QA88LogLine>();
        var presses = new List<QA88LogLine>();
        foreach (QA88LogLine l in lines)
        {
            if (l.IsLag && l.Event == "judge") judges.Add(l);
            else if (l.IsQA88 && l.Event == "press") presses.Add(l);
        }

        // pressT 로 잇기 — 같은 clientId, 가장 가까운 미사용 판정.
        var pressToJudge = new Dictionary<QA88LogLine, QA88LogLine>();
        var used = new HashSet<QA88LogLine>();
        foreach (QA88LogLine p in presses)
        {
            double t = p.GetDouble("pressT");
            string client = p.Get("clientId");
            QA88LogLine best = null;
            double bestD = MatchToleranceSec;
            foreach (QA88LogLine j in judges)
            {
                if (used.Contains(j) || j.Get("clientId") != client)
                    continue;
                double d = Math.Abs(j.GetDouble("pressT") - t);
                if (d <= bestD + 1e-9) { bestD = d; best = j; }
            }
            if (best != null)
            {
                used.Add(best);
                pressToJudge[p] = best;
            }
        }

        var result = new List<QA88GroupStats>();
        foreach (bool host in new[] { false, true })
        {
            var g = new QA88GroupStats { Group = host ? "host" : "remote" };
            double rttSum = 0, lagSum = 0;
            int rttN = 0, lagN = 0;
            foreach (QA88LogLine j in judges)
            {
                if (j.GetBool("host") != host)
                    continue;
                g.Judges++;
                bool success = j.Get("result") == "success";
                bool would = j.GetBool("wouldSucceed");
                if (j.Get("target") != null && j.Get("target") != "none") g.WithTarget++;
                if (success) g.Success++;
                if (would) g.WouldSucceed++;
                if (would && !success) g.Rescued++;
                if (success && !would) g.Lost++;
                Accumulate(j.GetDouble("rttMs"), ref rttSum, ref rttN, ref g.RttMaxMs);
                Accumulate(j.GetDouble("lagMs"), ref lagSum, ref lagN, ref g.LagMaxMs);
            }
            g.RttAvgMs = rttN > 0 ? rttSum / rttN : double.NaN;
            g.LagAvgMs = lagN > 0 ? lagSum / lagN : double.NaN;

            var buckets = new SortedDictionary<double, QA88Bucket>();
            foreach (QA88LogLine p in presses)
            {
                if (p.GetBool("host") != host)
                    continue;
                g.Presses++;
                if (!pressToJudge.TryGetValue(p, out QA88LogLine j))
                    continue;
                g.Matched++;
                double offset = p.GetDouble("offsetMs");
                if (p.Get("mode") != "sweep" || double.IsNaN(offset))
                    continue;
                double lo = BucketLo(offset);
                if (!buckets.TryGetValue(lo, out QA88Bucket b))
                    buckets[lo] = b = new QA88Bucket { LoMs = lo };
                b.N++;
                if (j.Get("result") == "success") b.Success++;
                if (j.GetBool("wouldSucceed")) b.WouldSucceed++;
            }
            g.Buckets.AddRange(buckets.Values);

            if (!host || g.Presses > 0 || g.Judges > 0)
                result.Add(g);
        }
        return result;
    }

    private static void Accumulate(double v, ref double sum, ref int n, ref double max)
    {
        if (double.IsNaN(v))
            return;
        sum += v;
        n++;
        if (double.IsNaN(max) || v > max)
            max = v;
    }
}

/// <summary>런 하나의 결과(리포트 행).</summary>
public sealed class QA88RunResult
{
    public QA88RunSpec Spec;
    /// <summary>실행 안 함(강제 개시 실패·지연 미확인 등) — 사유는 <see cref="Notes"/>.</summary>
    public bool Skipped;
    public readonly List<string> Notes = new List<string>();
    public List<QA88GroupStats> Groups = new List<QA88GroupStats>();
}

/// <summary>리포트 머리말 값.</summary>
public sealed class QA88ReportHeader
{
    public string Created = "";
    public string Status = "";
    public string UnityVersion = "";
    public int Attempts;
    public int RunTimeoutSec;
    public string Pressers = "";
    public readonly List<string> Notes = new List<string>();
    public readonly List<string> LogFiles = new List<string>();
}

/// <summary>report.md 생성(판정 없음 — 표만).</summary>
public static class QAInterruptLag88ReportBuilder
{
    public static string Build(QA88ReportHeader h, IList<QA88RunSpec> plan, IList<QA88RunResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 할 일 88 간파 지연 QA 리포트");
        sb.AppendLine();
        sb.AppendLine("> 측정만 한다 — PASS/FAIL 판정 없음. 로그 형식: Docs/tech/networking.md 'Interrupt lag measurement'.");
        sb.AppendLine();
        sb.AppendLine($"- 생성: {h.Created} · 상태: {h.Status} · Unity: {h.UnityVersion}");
        sb.AppendLine($"- 런: {results.Count}/{plan.Count} · 런당 시도 N={h.Attempts} · 런 상한 {h.RunTimeoutSec}s · 누르는 쪽: {h.Pressers}");
        foreach (string note in h.Notes)
            sb.AppendLine($"- {note}");
        sb.AppendLine();
        sb.AppendLine("열 뜻: 누름 = 봇 누름 줄 · 시도 = 호스트 판정 줄 · 매칭 = pressT 로 이어진 수 · 보정 성공 = `wouldSucceed` · " +
                      "구제 = 보정했으면 성공인데 실제 실패(`rescued`) · 손실 = 실제 성공인데 보정이면 실패 · 지연 = `lagMs`(arriveT − pressT).");
        sb.AppendLine();

        sb.AppendLine("## 런 요약");
        sb.AppendLine();
        sb.AppendLine("| # | 대상 | 원격 지연(ms) | 모드 | 그룹 | 누름 | 시도 | 매칭 | 대상 있음 | 성공 | 성공률 | 보정 성공 | 보정 성공률 | 구제 | 손실 | RTT 평균/최대(ms) | 지연 평균/최대(ms) | 비고 |");
        sb.AppendLine("|---:|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|");
        foreach (QA88RunSpec spec in plan)
        {
            QA88RunResult r = Find(results, spec.Index);
            string head = $"| {spec.Index + 1} | {KindLabel(spec.Kind)} | {spec.DelayMs} | {ModeLabel(spec.Mode)}";
            if (r == null)
            {
                sb.AppendLine($"{head} | - | - | - | - | - | - | - | - | - | - | - | - | - | - | 미실행 |");
                continue;
            }
            string notes = Escape(string.Join(" / ", r.Notes));
            if (r.Skipped || r.Groups.Count == 0)
            {
                sb.AppendLine($"{head} | - | - | - | - | - | - | - | - | - | - | - | - | - | - | 건너뜀{(notes.Length > 0 ? ": " + notes : "")} |");
                continue;
            }
            foreach (QA88GroupStats g in r.Groups)
                sb.AppendLine($"{head} | {g.Group} | {g.Presses} | {g.Judges} | {g.Matched} | {g.WithTarget} | {g.Success} | {Pct(g.Success, g.Judges)} | " +
                              $"{g.WouldSucceed} | {Pct(g.WouldSucceed, g.Judges)} | {g.Rescued} | {g.Lost} | {Ms(g.RttAvgMs)}/{Ms(g.RttMaxMs)} | {Ms(g.LagAvgMs)}/{Ms(g.LagMaxMs)} | {notes} |");
        }
        sb.AppendLine();

        sb.AppendLine($"## A 스윕 — 오프셋 구간별({QAInterruptLag88Aggregate.BucketMs:0}ms, 창 닫힘 기준 · 음수 = 닫히기 전, 클라가 본 닫힘)");
        sb.AppendLine();
        int sweepTables = 0;
        foreach (QA88RunResult r in results)
        {
            if (r.Spec.Mode != QAInterruptBotMode.Sweep || r.Skipped)
                continue;
            foreach (QA88GroupStats g in r.Groups)
            {
                if (g.Buckets.Count == 0)
                    continue;
                sweepTables++;
                sb.AppendLine($"### #{r.Spec.Index + 1} {r.Spec.Label} · {g.Group}");
                sb.AppendLine();
                sb.AppendLine("| 오프셋 구간(ms) | 시도 | 성공 | 성공률 | 보정 성공 | 보정 성공률 |");
                sb.AppendLine("|---|---:|---:|---:|---:|---:|");
                foreach (QA88Bucket b in g.Buckets)
                    sb.AppendLine($"| {b.LoMs:0} ~ {b.LoMs + QAInterruptLag88Aggregate.BucketMs:0} | {b.N} | {b.Success} | {Pct(b.Success, b.N)} | {b.WouldSucceed} | {Pct(b.WouldSucceed, b.N)} |");
                sb.AppendLine();
            }
        }
        if (sweepTables == 0)
        {
            sb.AppendLine("(매칭된 A 스윕 누름 없음)");
            sb.AppendLine();
        }

        sb.AppendLine("## 원본 로그");
        sb.AppendLine();
        foreach (string f in h.LogFiles)
            sb.AppendLine($"- [{f}]({f})");
        if (h.LogFiles.Count == 0)
            sb.AppendLine("- (없음)");
        return sb.ToString();
    }

    private static QA88RunResult Find(IList<QA88RunResult> results, int index)
    {
        foreach (QA88RunResult r in results)
            if (r.Spec.Index == index)
                return r;
        return null;
    }

    public static string Pct(int part, int whole) =>
        whole <= 0 ? "-" : (100.0 * part / whole).ToString("F1", CultureInfo.InvariantCulture) + "%";

    public static string Ms(double v) => double.IsNaN(v) ? "-" : v.ToString("F1", CultureInfo.InvariantCulture);

    private static string KindLabel(QAInterruptTargetKind k) => k == QAInterruptTargetKind.Midboss ? "중간보스" : "23호";

    private static string ModeLabel(QAInterruptBotMode m) => m == QAInterruptBotMode.Sweep ? "A 스윕" : "B 사람형";

    private static string Escape(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
