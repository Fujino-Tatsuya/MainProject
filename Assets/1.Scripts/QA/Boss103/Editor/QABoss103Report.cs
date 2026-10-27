#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>할 일 103 C6 리포트 입력 — 배치 폴더 파일 내용 그대로(문자열). 파일 읽기는 <see cref="QABoss103ReportFiles"/>.</summary>
public sealed class QABoss103ReportInput
{
    /// <summary>batch.txt(key=value) 내용.</summary>
    public string BatchText = "";
    public string RunsCsv = "", CharsCsv = "", PatternsCsv = "", GimmicksCsv = "";
    /// <summary>할 일 119 C3 — run_fail.csv(클리어 실패 사유). 없으면 빈칸.</summary>
    public string FailCsv = "";
    /// <summary>할 일 116 — dash_predict.csv(대쉬 간파 예측 vs 서버 판정). 없으면 빈칸(섹션 생략).</summary>
    public string DashPredictCsv = "";
    /// <summary>할 일 119 C3 — 비교 기준 배치(batch.txt compare_dir) 입력. 없으면 null(비교 표 대신 안내).</summary>
    public QABoss103ReportInput Compare;
    /// <summary>비교 기준 폴더 경로(표시용). 비면 비교 안 함.</summary>
    public string CompareDir = "";
    /// <summary>fixes.md(사람이 적는 야간 수정 사항). 없으면 빈칸.</summary>
    public string Fixes = "";
    /// <summary>logs/notes.txt(루프 메모 — 연속 오류 휴식 등).</summary>
    public string Notes = "";
    public int Total = QABoss103Batch.TotalRuns;
    /// <summary>루프 상태 한 줄(실행 중 상태·멈춤).</summary>
    public string LoopState = "";
    public string GeneratedAt = "";
}

/// <summary>
/// 할 일 103 C6 HTML 리포트 생성(순수 — 문자열만, EditMode <c>QABoss103ReportTests</c>). 판마다 CSV 전체로 다시 만든다.
/// 외부 라이브러리·CDN 없는 단일 파일(인라인 CSS·SVG), 한국어. 오류 판(<see cref="QABoss103Batch.IsError"/>)은 통계에서 빼고 수만 따로 보인다.
/// 지표 = QA봇.md 14개(단계별 보통·숙련·초보 + 전체), 차트 4개, 특성 표, 판 목록.
/// </summary>
public static class QABoss103Report
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Dash = "–";

    /// <summary>단계 색(범주 1~3 — 모든 차트에서 단계마다 같은 색). 입력/실제 피격률은 단계가 아니라 측정값이라 따로.</summary>
    private static readonly string[] TierColor = { "var(--s1)", "var(--s2)", "var(--s3)" };

    private static readonly QABoss103Character[] Chars = { QABoss103Character.Paladin, QABoss103Character.Assassin, QABoss103Character.Gunner };
    private static readonly string[] ActionLabel = { "평타", "Q", "E", "R", "우클릭(간파)" };

    public static string CharLabel(QABoss103Character c) =>
        c == QABoss103Character.Paladin ? "팔라딘" : c == QABoss103Character.Assassin ? "어쌔신" : "거너";

    // ── 집계 묶음 ────────────────────────────────────────────

    /// <summary>한 단계(또는 전체)의 통계용 행들.</summary>
    private sealed class Group
    {
        public string Label;
        /// <summary>단계 묶음이면 그 단계(봇 충실도 표의 입력값), 전체면 null.</summary>
        public QABoss103BotTier? Tier;
        public readonly List<string[]> Runs = new List<string[]>();
        public readonly List<string[]> CharRows = new List<string[]>();
        public readonly List<string[]> PatternRows = new List<string[]>();
        public readonly List<string[]> GimmickRows = new List<string[]>();
    }

    private sealed class Ctx
    {
        public QABoss103CsvTable Runs, CharsT, Patterns, Gimmicks;
        public List<QABoss103Attempt> Attempts;
        /// <summary>통계용 (판:재실행) → runs 행.</summary>
        public readonly Dictionary<string, string[]> ValidRun = new Dictionary<string, string[]>(StringComparer.Ordinal);
        /// <summary>(판:재실행) → 전투 시간(초).</summary>
        public readonly Dictionary<string, double> Duration = new Dictionary<string, double>(StringComparer.Ordinal);
        public readonly Dictionary<string, double> Unrolled = new Dictionary<string, double>(StringComparer.Ordinal);
        public Group All;
        public readonly List<Group> Tiers = new List<Group>();
        public List<Group> Columns;
    }

    private static string Key(QABoss103CsvTable t, string[] row) => t.Get(row, "run") + ":" + t.Get(row, "retry");

    private static Ctx Load(QABoss103ReportInput input)
    {
        var c = new Ctx
        {
            Runs = QABoss103CsvTable.Parse(input.RunsCsv),
            CharsT = QABoss103CsvTable.Parse(input.CharsCsv),
            Patterns = QABoss103CsvTable.Parse(input.PatternsCsv),
            Gimmicks = QABoss103CsvTable.Parse(input.GimmicksCsv),
        };
        c.Attempts = QABoss103Batch.ReadAttempts(c.Runs);
        c.All = new Group { Label = "전체" };
        var byTier = new Dictionary<QABoss103BotTier, Group>();
        foreach (QABoss103BotTier t in QABoss103Batch.TierOrder)
        {
            var g = new Group { Label = QABoss103Batch.TierLabel(t), Tier = t };
            byTier[t] = g;
            c.Tiers.Add(g);
        }
        c.Columns = new List<Group>(c.Tiers) { c.All };

        var tierOfKey = new Dictionary<string, QABoss103BotTier>(StringComparer.Ordinal);
        for (int run = 1; run <= input.Total; run++)
        {
            QABoss103Attempt f = QABoss103Batch.Final(c.Attempts, run);
            if (f == null || f.IsError)
                continue;
            string[] row = c.Runs.Rows[f.Index];
            string key = Key(c.Runs, row);
            c.ValidRun[key] = row;
            c.Duration[key] = c.Runs.Num(row, "combat_sec");
            QABoss103BotTier tier = Enum.TryParse(c.Runs.Get(row, "tier"), out QABoss103BotTier tt) ? tt : QABoss103Batch.TierOf(run);
            tierOfKey[key] = tier;
            c.All.Runs.Add(row);
            byTier[tier].Runs.Add(row);
        }
        foreach (string[] r in c.CharsT.Rows)
        {
            string key = Key(c.CharsT, r);
            if (!tierOfKey.TryGetValue(key, out QABoss103BotTier tier))
                continue;
            c.All.CharRows.Add(r);
            byTier[tier].CharRows.Add(r);
            double u = c.CharsT.Num(r, "hits_unrolled");
            if (!double.IsNaN(u))
                c.Unrolled[key] = (c.Unrolled.TryGetValue(key, out double prev) ? prev : 0.0) + u;
        }
        foreach (string[] r in c.Patterns.Rows)
            if (tierOfKey.TryGetValue(Key(c.Patterns, r), out QABoss103BotTier tier))
            {
                c.All.PatternRows.Add(r);
                byTier[tier].PatternRows.Add(r);
            }
        foreach (string[] r in c.Gimmicks.Rows)
            if (tierOfKey.TryGetValue(Key(c.Gimmicks, r), out QABoss103BotTier tier))
            {
                c.All.GimmickRows.Add(r);
                byTier[tier].GimmickRows.Add(r);
            }
        return c;
    }

    // ── 지표 계산(순수 — 테스트가 직접 부른다) ───────────────

    /// <summary>지표 묶음 — 단계 하나의 결과. NaN = 데이터 없음.</summary>
    public sealed class Summary
    {
        public int Runs, Clears, Wipes, Timeouts;
        public double ClearRate = double.NaN, ClearMean = double.NaN, ClearMedian = double.NaN;
        public double LivesUsedAvg = double.NaN, DeathsAvg = double.NaN;
        public double InputHitRate = double.NaN, ActualHitRate = double.NaN, UnrolledPerRun = double.NaN;
        public double SuppressAvg = double.NaN, SuppressDamageAvg = double.NaN;
        public double CounterTries = double.NaN, CounterSuccess = double.NaN, CounterRate = double.NaN;
        public readonly List<double> ClearTimes = new List<double>();
        public readonly Dictionary<QABoss103Character, double> Dps = new Dictionary<QABoss103Character, double>();
    }

    /// <summary>CSV 내용 → 단계별 요약(보통·숙련·초보·전체 순서). 테스트·차트 공용.</summary>
    public static List<Summary> Summaries(QABoss103ReportInput input)
    {
        Ctx c = Load(input);
        var list = new List<Summary>();
        foreach (Group g in c.Columns)
            list.Add(Summarize(c, g));
        return list;
    }

    private static Summary Summarize(Ctx c, Group g)
    {
        QABoss103CsvTable R = c.Runs;
        var s = new Summary { Runs = g.Runs.Count };
        double lives = 0, livesN = 0, deaths = 0, deathsN = 0, threats = 0, take = 0, rolledHits = 0, unrolled = 0, unrolledN = 0;
        double sup = 0, supN = 0, tries = 0, triesSuccess = 0, success = 0, successN = 0;
        foreach (string[] r in g.Runs)
        {
            string o = R.Get(r, "outcome");
            if (o == "Clear")
            {
                s.Clears++;
                double t = R.Num(r, "combat_sec");
                if (!double.IsNaN(t))
                    s.ClearTimes.Add(t);
            }
            else if (o == "Wipe")
                s.Wipes++;
            else if (o == "Timeout")
                s.Timeouts++;
            Acc(R.Num(r, "party_lives_used"), ref lives, ref livesN);
            Acc(R.Num(r, "deaths_total"), ref deaths, ref deathsN);
            Acc(R.Num(r, "suppress_count"), ref sup, ref supN);
            Acc(R.Num(r, "counter_success"), ref success, ref successN);
            double th = R.Num(r, "threat_rolls");
            if (!double.IsNaN(th))
            {
                string key = Key(R, r);
                double un = c.Unrolled.TryGetValue(key, out double u) ? u : 0.0;
                threats += th;
                take += Z(R.Num(r, "rolled_take"));
                rolledHits += Math.Max(0.0, Z(R.Num(r, "actual_hits")) - un);
                unrolled += un;
                unrolledN++;
            }
            double tr = R.Num(r, "counter_tries");
            if (!double.IsNaN(tr))
            {
                tries += tr;
                triesSuccess += Z(R.Num(r, "counter_success"));
            }
        }
        if (s.Runs > 0)
            s.ClearRate = (double)s.Clears / s.Runs;
        s.ClearMean = Mean(s.ClearTimes);
        s.ClearMedian = Median(s.ClearTimes);
        s.LivesUsedAvg = Div(lives, livesN);
        s.DeathsAvg = Div(deaths, deathsN);
        s.InputHitRate = Div(take, threats);
        s.ActualHitRate = Div(rolledHits, threats);
        s.UnrolledPerRun = Div(unrolled, unrolledN);
        s.SuppressAvg = Div(sup, supN);
        s.CounterSuccess = Div(success, successN);
        s.CounterTries = Div(tries, CountNonEmpty(R, g.Runs, "counter_tries"));
        s.CounterRate = Div(triesSuccess, tries);
        s.SuppressDamageAvg = Div(SumChars(c, g, null, "dmg_during_suppress"), s.Runs);
        foreach (QABoss103Character ch in Chars)
            s.Dps[ch] = Div(SumChars(c, g, ch, "damage_dealt"), SumDur(c, g, ch));
        return s;
    }

    private static void Acc(double v, ref double sum, ref double n)
    {
        if (double.IsNaN(v))
            return;
        sum += v;
        n++;
    }

    private static double Z(double v) => double.IsNaN(v) ? 0.0 : v;
    private static double Div(double a, double b) => b > 0 && !double.IsNaN(a) ? a / b : double.NaN;

    private static int CountNonEmpty(QABoss103CsvTable t, List<string[]> rows, string col)
    {
        int n = 0;
        foreach (string[] r in rows)
            if (t.Get(r, col).Length > 0)
                n++;
        return n;
    }

    public static double Mean(List<double> v)
    {
        if (v == null || v.Count == 0)
            return double.NaN;
        double s = 0;
        foreach (double x in v)
            s += x;
        return s / v.Count;
    }

    public static double Median(List<double> v) => Quantile(v, 0.5);

    /// <summary>선형 보간 분위수(0~1). 비면 NaN.</summary>
    public static double Quantile(List<double> v, double q)
    {
        if (v == null || v.Count == 0)
            return double.NaN;
        var s = new List<double>(v);
        s.Sort();
        double pos = (s.Count - 1) * q;
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return s[lo] + (s[hi] - s[lo]) * (pos - lo);
    }

    private static IEnumerable<string[]> CharRowsOf(Ctx c, Group g, QABoss103Character? ch)
    {
        foreach (string[] r in g.CharRows)
            if (ch == null || c.CharsT.Get(r, "character") == ch.Value.ToString())
                yield return r;
    }

    private static double SumChars(Ctx c, Group g, QABoss103Character? ch, string col)
    {
        double s = 0;
        foreach (string[] r in CharRowsOf(c, g, ch))
            s += Z(c.CharsT.Num(r, col));
        return s;
    }

    private static double AvgChars(Ctx c, Group g, QABoss103Character ch, string col)
    {
        double s = 0, n = 0;
        foreach (string[] r in CharRowsOf(c, g, ch))
            Acc(c.CharsT.Num(r, col), ref s, ref n);
        return Div(s, n);
    }

    /// <summary>그 캐릭터 행들의 판 전투 시간 합(초). <paramref name="minus"/> 열 값을 판마다 뺀다(어쌔신 일반 상태 시간).</summary>
    private static double SumDur(Ctx c, Group g, QABoss103Character ch, string minus = null)
    {
        double s = 0;
        foreach (string[] r in CharRowsOf(c, g, ch))
        {
            if (!c.Duration.TryGetValue(Key(c.CharsT, r), out double d) || double.IsNaN(d))
                continue;
            s += minus != null ? Math.Max(0.0, d - Z(c.CharsT.Num(r, minus))) : d;
        }
        return s;
    }

    // ── HTML ─────────────────────────────────────────────────

    public static string Build(QABoss103ReportInput input)
    {
        Ctx c = Load(input);
        var sums = new List<Summary>();
        foreach (Group g in c.Columns)
            sums.Add(Summarize(c, g));
        Dictionary<string, string> batch = QABoss103Batch.ParseKeyValues(input.BatchText);

        var sb = new StringBuilder(64 * 1024);
        sb.Append("<!DOCTYPE html>\n<html lang=\"ko\"><head><meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>할 일 103 보스 밸런싱 QA 리포트</title>\n<style>").Append(Css).Append("</style></head>\n<body><main class=\"viz-root\">\n");
        Header(sb, c, input, batch);
        DashPredictSection(sb, input);
        Comparison(sb, c, sums);
        PreviousComparison(sb, input, sums);
        FailTable(sb, c, input);
        Charts(sb, c, sums);
        CharacterTables(sb, c);
        CharRuleTable(sb, c);
        PatternTable(sb, c);
        GimmickTable(sb, c);
        TraitTable(sb, c);
        RunList(sb, c);
        sb.Append("<footer>생성 ").Append(H(input.GeneratedAt)).Append(" · 오류 판(Error·Aborted·데이터 출처 불일치)은 모든 통계에서 뺀다 · 재실행이 있는 판은 재실행 결과를 쓴다.</footer>\n");
        sb.Append("</main></body></html>\n");
        return sb.ToString();
    }

    private const string Css = @"
.viz-root{color-scheme:light;--bg:#fcfcfb;--card:#ffffff;--ink:#0b0b0b;--ink2:#52514e;--muted:#8a8984;--line:#e4e3df;--grid:#efeeea;
--s1:#2a78d6;--s2:#eb6834;--s3:#1baf7a;--in:#8a8984;--act:#6250d6;--warn:#b25e00;--bad:#c0392b;}
@media (prefers-color-scheme:dark){.viz-root{color-scheme:dark;--bg:#1a1a19;--card:#222221;--ink:#f0efec;--ink2:#c3c2b7;--muted:#8f8e88;--line:#3a3a38;--grid:#2c2c2a;
--s1:#3987e5;--s2:#d95926;--s3:#199e70;--in:#8f8e88;--act:#9085e9;--warn:#e0a040;--bad:#e66767;}}
body{margin:0;background:var(--bg);}
.viz-root{font-family:'Malgun Gothic','Apple SD Gothic Neo',system-ui,sans-serif;color:var(--ink);background:var(--bg);max-width:1280px;margin:0 auto;padding:24px;font-size:14px;line-height:1.5}
h1{font-size:22px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 8px;border-bottom:1px solid var(--line);padding-bottom:4px}h3{font-size:15px;margin:18px 0 6px}
.sub{color:var(--ink2)}.muted{color:var(--muted)}
.kpis{display:flex;flex-wrap:wrap;gap:10px;margin:12px 0}.kpi{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:8px 12px;min-width:120px}
.kpi b{display:block;font-size:20px}.kpi span{color:var(--ink2);font-size:12px}
table{border-collapse:collapse;background:var(--card);margin:6px 0 12px;font-size:13px}th,td{border:1px solid var(--line);padding:4px 8px;text-align:right;vertical-align:top}
th{background:var(--grid);font-weight:600}td.l,th.l{text-align:left}tr.sec td{background:var(--grid);font-weight:600;text-align:left}
.err{color:var(--bad)}.warnbox{border-left:4px solid var(--warn);padding:6px 10px;background:var(--card);margin:8px 0}
pre{white-space:pre-wrap;background:var(--card);border:1px solid var(--line);padding:10px;border-radius:6px;font-size:13px}
.charts{display:flex;flex-wrap:wrap;gap:16px}.chart{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px}
.chart svg text{fill:var(--ink2);font-size:11px}.chart svg .v{fill:var(--ink);font-size:11px}.chart svg .grid{stroke:var(--grid)}.chart svg .axis{stroke:var(--muted)}
.legend{display:flex;gap:12px;font-size:12px;color:var(--ink2);margin:4px 0}.legend i{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:4px;vertical-align:-1px}
.scroll{overflow-x:auto}footer{margin-top:24px;color:var(--muted);font-size:12px}
";

    private static void Header(StringBuilder sb, Ctx c, QABoss103ReportInput input, Dictionary<string, string> batch)
    {
        QABoss103Batch.Summarize(c.Attempts, input.Total, out int done, out int retried, out int errors, out int valid);
        sb.Append("<h1>할 일 103 3인 보스전(웰즈&amp;23호 B) 밸런싱 QA</h1>\n");
        string title = Val(batch, "title", "");
        if (title.Length > 0 && title != "할 일 103 3인 보스전(웰즈&23호 B) 밸런싱 QA")
            sb.Append("<div class=\"sub\"><b>").Append(H(title)).Append("</b></div>\n");   // 할 일 119 30판 등 배치 제목
        sb.Append("<div class=\"sub\">파티 팔라딘·어쌔신·거너 · 봇 단계 보통→숙련→초보 번갈아 · 판 종료 = 보스 처치 / 공용 목숨 0 전멸 / Combat 30분</div>\n");
        sb.Append("<div class=\"kpis\">");
        Kpi(sb, $"{done}/{input.Total}", $"진행(+재실행 {retried})");
        Kpi(sb, valid.ToString(Inv), "통계에 들어간 판");
        Kpi(sb, errors.ToString(Inv), "오류 판(최종)", errors > 0);
        Kpi(sb, c.Attempts.Count.ToString(Inv), "시도 행(runs.csv)");
        sb.Append("</div>\n");

        // 데이터 출처·리비전 — 배치 머리 + 행마다 실제 기록.
        int tableRows = 0;
        var revs = new SortedSet<string>(StringComparer.Ordinal);
        var commits = new SortedSet<string>(StringComparer.Ordinal);
        string last = "";
        foreach (QABoss103Attempt a in c.Attempts)
        {
            string[] r = c.Runs.Rows[a.Index];
            if (QABoss103Batch.IsTable(a.DataSource))
                tableRows++;
            Add(revs, c.Runs.Get(r, "xlsx_svn_rev"));
            Add(commits, c.Runs.Get(r, "git_commit"));
            string e = c.Runs.Get(r, "ended_at");
            if (string.CompareOrdinal(e, last) > 0)
                last = e;
        }
        sb.Append("<table><tbody>\n");
        Row2(sb, "데이터 출처", $"{H(Val(batch, "data_source_mode", "table"))}(툴바 '데이터: 테이블' 고정) · 판 기록 중 런타임 출처 테이블 {tableRows}/{c.Attempts.Count}행" +
                             (tableRows < c.Attempts.Count ? " <span class=\"err\">— 테이블이 아닌 행은 오류 판</span>" : ""));
        Row2(sb, "xlsx SVN 리비전", $"배치 시작 r{H(Val(batch, "xlsx_svn_rev", "na"))} · 판 기록 {H(revs.Count > 0 ? "r" + string.Join(", r", revs) : Dash)}" +
                                  (revs.Count > 1 ? " <span class=\"err\">— 배치 중 리비전이 바뀌었다</span>" : ""));
        Row2(sb, "git 커밋", $"{H(Val(batch, "git_commit", "na"))} ({H(Val(batch, "git_branch", "na"))}) · 판 기록 {H(commits.Count > 0 ? string.Join(", ", commits) : Dash)}");
        if (batch.ContainsKey("svn_rev"))   // 할 일 116 배치 머리
            Row2(sb, "SVN 리비전(작업 사본)", "r" + H(Val(batch, "svn_rev", "na")));
        if (batch.ContainsKey("dash_interrupt_pct"))
            Row2(sb, "대쉬 간파 비율", H(Val(batch, "dash_interrupt_pct", "0")) + "% — 간파 창마다 굴림, 순서 대시 → 우클릭 / 우클릭 → 대시 반반, 누름 = 창 × 0.1~0.7" +
                                    (Val(batch, "minimize_windows", "0") == "1" ? " · 메인 에디터·가상 플레이어 창 최소화" : ""));
        Row2(sb, "Unity", H(Val(batch, "unity_version", "na")));
        Row2(sb, "기준 시드", H(Val(batch, "seed", "na")) + " · 판 시드 = 기준 시드 × 판 번호(재실행도 같은 시드)");
        Row2(sb, "측정 규칙", "부활 = F10 디버그 부활 경로(Soul 3초 뒤 봇이 요청, 1회 = 공용 목숨 1, 목숨 0 이면 거부 → 전멸 · run_chars qa_revives). " +
                             "할 일 119 이전 배치는 QA 서버 자동 부활(같은 지연·차감 + 부활 무적 2초)");
        Row2(sb, "측정 규칙", "즉사 패턴(북진 압박 방전·④ 폐기 처분 대폭발 — 할 일 105 무적 무시)은 회피 굴림·대시 없음 → '즉사(굴림 없음)' 따로 집계(run_chars·run_patterns lethal_no_roll)");
        Row2(sb, "측정 규칙", "벽 끼임 — 벽 속·보스방 경계 밖 0.25초면 보스 둘레 무작위 지점으로 즉시 재배치(run_chars qa_wall_escapes) · 판정 출처 runs.csv outcome_source");
        Row2(sb, "측정 규칙", "QA 끼임 복구 — 이동하려는데 목표에 못 다가간 채 2초 옆걸음 → 6초 뒤·옆 대시 → 20초 보스 둘레 링 지점으로 재배치(run_chars qa_relocates) · " +
                             "Combat 중 90초 동안 전투 활동(보스·탑 피해, 피격, 사망·부활, 공용 목숨, 기믹 진행, 보스 HP 변화)이 없으면 정체(stall) 오류 판");
        Row2(sb, "측정 규칙", "조기 시간초과: 공용 목숨 0 이고 보스 HP 5분 불변이면 Timeout 으로 처리(통계는 시간초과와 같이 집계 · 판 목록 note early_no_progress)");
        Row2(sb, "시작 / 최근", $"{H(Val(batch, "started_at", Dash))} / {H(last.Length > 0 ? last : Dash)}");
        Row2(sb, "루프 상태", H(input.LoopState.Length > 0 ? input.LoopState : Dash));
        sb.Append("</tbody></table>\n");

        // 할 일 119 C3 — 지난 103 배치와 달라진 봇·측정 규칙(비교 표를 읽기 전에 볼 것).
        sb.Append("<h3>119 규칙 변경점(지난 103 배치 대비)</h3>\n<table><tbody>\n");
        foreach (KeyValuePair<string, string> kv in Rules119)
            Row2(sb, kv.Key, H(kv.Value));
        sb.Append("</tbody></table>\n");

        if (errors > 0)
        {
            var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int run = 1; run <= input.Total; run++)
            {
                QABoss103Attempt f = QABoss103Batch.Final(c.Attempts, run);
                if (f != null && f.IsError)
                {
                    string k = QABoss103Batch.ReasonOf(f);
                    reasons[k] = (reasons.TryGetValue(k, out int n) ? n : 0) + 1;
                }
            }
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in reasons)
                parts.Add($"{H(kv.Key)} {kv.Value}");
            sb.Append("<div class=\"warnbox\">오류 판 ").Append(errors).Append("개(통계 제외) — ").Append(string.Join(" · ", parts)).Append("</div>\n");
        }
        string notes = (input.Notes ?? "").Trim();
        if (notes.Length > 0)
            sb.Append("<h3>루프 메모</h3><pre>").Append(H(notes)).Append("</pre>\n");

        sb.Append("<h3>봇 프로필(단계별 5변수)</h3>\n<table><thead><tr><th class=\"l\">변수</th>");
        foreach (QABoss103BotTier t in QABoss103Batch.TierOrder)
            sb.Append("<th>").Append(QABoss103Batch.TierLabel(t)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        ProfileRow(sb, "패턴 피격 확률(회피 실패율)", p => Pct(p.PatternHitChance, 0));
        ProfileRow(sb, "반응 지연", p => p.ReactionMs.ToString("F0", Inv) + "ms");
        ProfileRow(sb, "조준 오차 최대 반경", p => p.AimErrorRadius.ToString("F1", Inv) + "m");
        ProfileRow(sb, "스킬 사용 빈도", p => Pct(p.SkillUseChance, 0));
        ProfileRow(sb, "간파 성공률(시도 시)", p => Pct(p.InterruptSuccessChance, 0));
        sb.Append("</tbody></table>\n");

        sb.Append("<h3>야간 수정 사항</h3>\n");
        string fixes = (input.Fixes ?? "").Trim();
        sb.Append(fixes.Length > 0 ? "<pre>" + H(fixes) + "</pre>\n" : "<div class=\"muted\">없음 — 실행 중 고친 내용은 배치 폴더 fixes.md 에 적으면 여기에 그대로 보인다.</div>\n");
    }

    /// <summary>
    /// 할 일 116 — 대쉬 간파 예측 적중률(dash_predict.csv). 적중 = 서버 시전 순간 예측(성공/실패) = 서버 실제 판정(성공 / 실패·대상 없음).
    /// 중단(판정 전 종료)·시전 없음(봇 시도가 게임에 안 들어감)은 적중률에서 빼고 수만 보인다. 오너 열 = 원격 오너 관점 예측의 적중.
    /// </summary>
    private static void DashPredictSection(StringBuilder sb, QABoss103ReportInput input)
    {
        if (string.IsNullOrEmpty(input.DashPredictCsv))
            return;
        QABoss103CsvTable t = QABoss103CsvTable.Parse(input.DashPredictCsv);
        if (t.Rows.Count == 0)
            return;
        QABoss116DashPredict.Summary s = QABoss116DashPredict.Summarize(t);
        QABoss116DashPredict.Tally a = s.All;

        sb.Append("<h2>116 대쉬 간파 예측 적중률</h2>\n");
        sb.Append("<div class=\"sub\">예측 = 대쉬 간파 성립 순간(서버) '판정 시각에 보스가 간파 가능 + 판정 범위 안일 것' · 실제 = 서버 판정 결과. " +
                  "판정 시각 = max(예상 대시 끝, 시작 + 평소 준비), 위치 = 지금 + 대시 방향 × 속도 × 남은 대시 시간</div>\n");
        sb.Append("<div class=\"kpis\">");
        Kpi(sb, Pct(a.Accuracy, 1), $"예측 적중률(서버, 판정 {a.Judged}건)");
        Kpi(sb, a.PredSuccessActualFail.ToString(Inv), "예측 성공 → 실제 실패", a.PredSuccessActualFail > 0);
        Kpi(sb, a.PredFailActualSuccess.ToString(Inv), "예측 실패 → 실제 성공", a.PredFailActualSuccess > 0);
        Kpi(sb, Pct(a.OwnerAccuracy, 1), $"원격 오너 예측 적중률({a.OwnerJudged}건)");
        Kpi(sb, a.Aborted.ToString(Inv), "판정 전 중단");
        Kpi(sb, a.NoCast.ToString(Inv), "봇 시도 — 시전 없음");
        sb.Append("</div>\n");

        sb.Append("<table><thead><tr><th class=\"l\">묶음</th><th>행</th><th>판정</th><th>적중</th><th>적중률</th><th>예측 성공→실패</th><th>예측 실패→성공</th>" +
                  "<th>오너 적중률</th><th>중단</th><th>시전 없음</th></tr></thead><tbody>\n");
        TallyRow(sb, "전체", a);
        sb.Append("<tr class=\"sec\"><td colspan=\"10\">순서별</td></tr>\n");
        foreach (KeyValuePair<string, QABoss116DashPredict.Tally> kv in s.ByOrder)
            TallyRow(sb, OrderLabel(kv.Key), kv.Value);
        sb.Append("<tr class=\"sec\"><td colspan=\"10\">캐릭터별</td></tr>\n");
        foreach (KeyValuePair<string, QABoss116DashPredict.Tally> kv in s.ByCharacter)
            TallyRow(sb, Enum.TryParse(kv.Key, out QABoss103Character ch) ? CharLabel(ch) : kv.Key, kv.Value);
        sb.Append("</tbody></table>\n");

        sb.Append("<h3>어긋남 이유(많은 순)</h3>\n");
        List<KeyValuePair<string, int>> top = s.TopMismatch();
        if (top.Count == 0)
            sb.Append("<div class=\"muted\">어긋난 판정 없음</div>\n");
        else
        {
            sb.Append("<table><thead><tr><th class=\"l\">이유</th><th>건수</th></tr></thead><tbody>\n");
            foreach (KeyValuePair<string, int> kv in top)
                sb.Append("<tr><td class=\"l\">").Append(H(MismatchLabel(kv.Key))).Append("</td><td>").Append(kv.Value).Append("</td></tr>\n");
            sb.Append("</tbody></table>\n");
        }

        sb.Append("<h3>봇 대쉬 간파 시도 결과</h3>\n<table><thead><tr><th class=\"l\">결과</th><th>건수</th></tr></thead><tbody>\n");
        var outcomes = new List<KeyValuePair<string, int>>(s.BotOutcome);
        outcomes.Sort((x, y) => x.Value != y.Value ? y.Value.CompareTo(x.Value) : string.CompareOrdinal(x.Key, y.Key));
        foreach (KeyValuePair<string, int> kv in outcomes)
            sb.Append("<tr><td class=\"l\">").Append(H(kv.Key)).Append("</td><td>").Append(kv.Value).Append("</td></tr>\n");
        sb.Append("</tbody></table>\n");
    }

    private static void TallyRow(StringBuilder sb, string label, QABoss116DashPredict.Tally t)
    {
        sb.Append("<tr><td class=\"l\">").Append(H(label)).Append("</td><td>").Append(t.Rows).Append("</td><td>").Append(t.Judged)
          .Append("</td><td>").Append(t.Match).Append("</td><td>").Append(Pct(t.Accuracy, 1))
          .Append("</td><td>").Append(t.PredSuccessActualFail).Append("</td><td>").Append(t.PredFailActualSuccess)
          .Append("</td><td>").Append(Pct(t.OwnerAccuracy, 1)).Append("</td><td>").Append(t.Aborted)
          .Append("</td><td>").Append(t.NoCast).Append("</td></tr>\n");
    }

    public static string OrderLabel(string token) =>
        token == "dash_rmb" ? "대시 → 우클릭" : token == "rmb_dash" ? "우클릭 → 대시" : token;

    public static string MismatchLabel(string token)
    {
        switch (token)
        {
            case "dash_shortened": return "대시가 막혀 짧아짐 (dash_shortened)";
            case "window_closed_before_resolve": return "판정 전에 보스 간파 창이 닫힘 (window_closed_before_resolve)";
            case "resolve_delayed": return "판정이 예상보다 늦어 창이 닫힘 (resolve_delayed)";
            case "window_opened_after_cast": return "시전 뒤 간파 창이 열림 (window_opened_after_cast)";
            case "window_longer_than_estimated": return "창이 추정보다 오래 열림 (window_longer_than_estimated)";
            case "target_moved": return "보스 이동 (target_moved)";
            case "target_rotated": return "보스 회전 (target_rotated)";
            case "out_of_range": return "범위·각도 판단 어긋남 (out_of_range)";
            case "network_lag": return "원격 오너 — 지연 외 원인 못 찾음 (network_lag)";
            case "aborted": return "판정 전 중단 (aborted)";
            default: return "원인 불명 (" + token + ")";
        }
    }

    /// <summary>할 일 119 규칙 변경점 요약(119-결정.md 1·2·4·5 + F10 목숨 소비) — 리포트 머리.</summary>
    public static readonly KeyValuePair<string, string>[] Rules119 =
    {
        new KeyValuePair<string, string>("즉사 대시 안 함",
            "즉사 패턴(북진 압박 방전·④ 폐기 처분 대폭발 — 무적 무시)은 회피 굴림·대시 무적 회피를 하지 않는다. 피격은 '즉사(굴림 없음)'로 따로 센다(lethal_no_roll) · 103 은 대시로 피하려 했다"),
        new KeyValuePair<string, string>("벽 끼임 텔레포트",
            "벽 속·보스방 경계 밖(예: Boundary_ZMin)에 0.25초 있으면 보스 둘레 무작위 지점으로 즉시 재배치해 플레이를 이어 간다(qa_wall_escapes) · 103 은 20초 진척 없음 재배치만"),
        new KeyValuePair<string, string>("F10 부활 = 공용 목숨 1",
            "부활은 F10 디버그 부활 경로(Soul 3초 뒤 봇이 요청). 1회 = 공용 목숨 1, 목숨 0 이면 부활 불가 → 전멸(qa_revives) · 103 은 QA 서버 자동 부활(부활 무적 2초 포함)"),
        new KeyValuePair<string, string>("툴팁 규칙",
            "캐릭터 스킬 툴팁 근거 봇 규칙 — 거너 과열 시 평타 놓기·E(긴급 냉각) 우선, 어쌔신 분노 미달이면 R 대기·일반 E(강화) 사용·변신 E(칼날 폭풍) 사용. 표·발동 횟수는 아래 '캐릭터 스킬 툴팁 규칙'"),
        new KeyValuePair<string, string>("클리어 판정",
            "클리어 = 보스 Unit.Died(서버) 또는 디렉터 처치 확정 중 먼저(runs.csv outcome_source) · 실패 판은 run_fail.csv 에 사유 1행"),
    };

    private static void Add(SortedSet<string> set, string v)
    {
        if (!string.IsNullOrEmpty(v) && v != "na")
            set.Add(v);
    }

    private static void ProfileRow(StringBuilder sb, string label, Func<QABoss103BotProfile, string> f)
    {
        sb.Append("<tr><td class=\"l\">").Append(H(label)).Append("</td>");
        foreach (QABoss103BotTier t in QABoss103Batch.TierOrder)
            sb.Append("<td>").Append(H(f(QABoss103BotProfile.For(t)))).Append("</td>");
        sb.Append("</tr>\n");
    }

    private static void Kpi(StringBuilder sb, string value, string label, bool bad = false) =>
        sb.Append("<div class=\"kpi\"><b").Append(bad ? " class=\"err\"" : "").Append('>').Append(H(value)).Append("</b><span>").Append(H(label)).Append("</span></div>");

    private static void Row2(StringBuilder sb, string k, string vHtml) =>
        sb.Append("<tr><th class=\"l\">").Append(H(k)).Append("</th><td class=\"l\">").Append(vHtml).Append("</td></tr>\n");

    // 단계별 비교표 — QA봇.md 지표 14개.
    private static void Comparison(StringBuilder sb, Ctx c, List<Summary> sums)
    {
        sb.Append("<h2>단계별 비교 (오류 판 제외)</h2>\n<div class=\"scroll\"><table><thead><tr><th class=\"l\">지표</th>");
        foreach (Group g in c.Columns)
            sb.Append("<th>").Append(H(g.Label)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        int cols = c.Columns.Count;

        Sec(sb, "1. 클리어 여부·클리어율", cols);
        Line(sb, "판 수", sums, s => s.Runs.ToString(Inv));
        Line(sb, "클리어 판 / 클리어율", sums, s => s.Runs > 0 ? $"{s.Clears} / {Pct(s.ClearRate, 1)}" : Dash);
        Sec(sb, "2. 클리어 시간(Combat 부터)", cols);
        Line(sb, "평균", sums, s => Time(s.ClearMean));
        Line(sb, "중앙값", sums, s => Time(s.ClearMedian));
        Sec(sb, "3. 전멸·시간초과", cols);
        Line(sb, "전멸 판", sums, s => s.Wipes.ToString(Inv));
        Line(sb, "시간초과 판(30분)", sums, s => s.Timeouts.ToString(Inv));

        Sec(sb, "4. 캐릭터별 받은 피해", cols);
        foreach (QABoss103Character ch in Chars)
        {
            Line(sb, $"{CharLabel(ch)} 받은 피해/판", c, g => Num(AvgChars(c, g, ch, "damage_taken"), 0));
            Line(sb, $"{CharLabel(ch)} 분당 받은 피해", c, g => Num(Div(SumChars(c, g, ch, "damage_taken") * 60.0, SumDur(c, g, ch)), 1));
        }
        Sec(sb, "5. 사망·공용 목숨", cols);
        foreach (QABoss103Character ch in Chars)
            Line(sb, $"{CharLabel(ch)} 사망/판", c, g => Num(AvgChars(c, g, ch, "deaths"), 2));
        Line(sb, "파티 사망/판", sums, s => Num(s.DeathsAvg, 2));
        Line(sb, "공용 목숨 소모/판", sums, s => Num(s.LivesUsedAvg, 2));

        Sec(sb, "6. 피격률(입력 vs 실제) — 패턴별은 아래 표", cols);
        Line(sb, "입력 피격률(굴림 '맞기')", sums, s => Pct(s.InputHitRate, 1));
        Line(sb, "실제 피격률(굴림 있는 피격 / 굴림)", sums, s => Pct(s.ActualHitRate, 1));
        Line(sb, "굴림 없는 피격/판(감지 못한 위험)", sums, s => Num(s.UnrolledPerRun, 1));

        Sec(sb, "7. 캐릭터별 피해량·DPS·기여율", cols);
        foreach (QABoss103Character ch in Chars)
        {
            Line(sb, $"{CharLabel(ch)} 피해량/판", c, g => Num(AvgChars(c, g, ch, "damage_dealt"), 0));
            Line(sb, $"{CharLabel(ch)} DPS", c, g => Num(Div(SumChars(c, g, ch, "damage_dealt"), SumDur(c, g, ch)), 1));
            Line(sb, $"{CharLabel(ch)} 딜 기여율", c, g => Pct(Div(SumChars(c, g, ch, "damage_dealt"), SumChars(c, g, null, "damage_dealt")), 1));
        }

        Sec(sb, "8. 행동별 사용/판 · 적중률 · 피해 비중", cols);
        foreach (QABoss103Character ch in Chars)
            for (int a = 0; a < QABoss103Metrics.ActionPrefix.Length; a++)
            {
                string p = QABoss103Metrics.ActionPrefix[a];
                int ai = a;
                Line(sb, $"{CharLabel(ch)} {ActionLabel[ai]}", c, g =>
                {
                    double total = 0;
                    foreach (string q in QABoss103Metrics.ActionPrefix)
                        total += SumChars(c, g, ch, q + "_damage");
                    double use = SumChars(c, g, ch, p + "_use");
                    return $"{Num(AvgChars(c, g, ch, p + "_use"), 1)} · {Pct(Div(SumChars(c, g, ch, p + "_hit"), use), 0)} · {Pct(Div(SumChars(c, g, ch, p + "_damage"), total), 0)}";
                });
            }

        Sec(sb, "9. 어쌔신 변신", cols);
        QABoss103Character asn = QABoss103Character.Assassin;
        Line(sb, "변신 횟수/판", c, g => Num(AvgChars(c, g, asn, "assassin_transform_count"), 2));
        Line(sb, "평균 지속", c, g => Num(Div(SumChars(c, g, asn, "transform_sec_total"), SumChars(c, g, asn, "assassin_transform_count")), 1) + "s");
        Line(sb, "변신 점유율", c, g => Pct(Div(SumChars(c, g, asn, "transform_sec_total"), SumDur(c, g, asn)), 1));
        Sec(sb, "10. 어쌔신 일반 vs 변신", cols);
        Line(sb, "일반 피해/판", c, g => Num(AvgChars(c, g, asn, "dmg_normal"), 0));
        Line(sb, "변신 피해/판", c, g => Num(AvgChars(c, g, asn, "dmg_transformed"), 0));
        Line(sb, "일반 DPS", c, g => Num(Div(SumChars(c, g, asn, "dmg_normal"), SumDur(c, g, asn, "transform_sec_total")), 1));
        Line(sb, "변신 DPS", c, g => Num(Div(SumChars(c, g, asn, "dmg_transformed"), SumChars(c, g, asn, "transform_sec_total")), 1));

        Sec(sb, "11. 팔라딘 방어", cols);
        QABoss103Character pal = QABoss103Character.Paladin;
        Line(sb, "방어 성공/판", c, g => Num(AvgChars(c, g, pal, "paladin_guard_success"), 2));
        Line(sb, "  그중 반격 / 무시", c, g => $"{Num(AvgChars(c, g, pal, "paladin_guard_counter"), 2)} / {Num(AvgChars(c, g, pal, "paladin_guard_ignore"), 2)}");

        Sec(sb, "12. 간파", cols);
        Line(sb, "시도/판", sums, s => Num(s.CounterTries, 2));
        Line(sb, "성공/판", sums, s => Num(s.CounterSuccess, 2));
        Line(sb, "성공률(시도 기록이 있는 판)", sums, s => Pct(s.CounterRate, 1));
        foreach (QABoss103Character ch in Chars)
            Line(sb, $"{CharLabel(ch)} 시도 / 성공 /판", c, g => $"{Num(AvgChars(c, g, ch, "counter_tries"), 2)} / {Num(AvgChars(c, g, ch, "counter_success"), 2)}");

        Sec(sb, "13. 제압", cols);
        Line(sb, "제압 횟수/판", sums, s => Num(s.SuppressAvg, 2));
        Line(sb, "제압 중 가한 피해/판", sums, s => Num(s.SuppressDamageAvg, 0));

        Sec(sb, "14. 보스 기믹 — 기믹별 표는 아래", cols);
        Line(sb, "기믹 기록 수/판", c, g => Num(Div(g.GimmickRows.Count, g.Runs.Count), 1));

        Sec(sb, "15. 봇 충실도(입력 vs 달성) — C7e", cols);
        Line(sb, "입력 회피율 / 입력 간파 성공률", c, g => g.Tier.HasValue
            ? $"{Pct(1.0 - QABoss103BotProfile.For(g.Tier.Value).PatternHitChance, 0)} / {Pct(QABoss103BotProfile.For(g.Tier.Value).InterruptSuccessChance, 0)}"
            : Dash);
        foreach (QABoss103Character ch in Chars)
        {
            Line(sb, $"{CharLabel(ch)} 회피 달성(피함 굴림 → 피격 0)", c, g =>
                $"{Pct(Div(SumChars(c, g, ch, "dodge_success"), SumChars(c, g, ch, "dodge_rolls")), 0)} ({Num(SumChars(c, g, ch, "dodge_rolls"), 0)})");
            Line(sb, $"{CharLabel(ch)} 간파 달성(성공 굴림 → 서버 성공)", c, g =>
                $"{Pct(Div(SumChars(c, g, ch, "counter_roll_success"), SumChars(c, g, ch, "counter_hit_rolls")), 0)} ({Num(SumChars(c, g, ch, "counter_hit_rolls"), 0)})");
        }
        sb.Append("</tbody></table></div>\n<div class=\"muted\">15번 칸 = 달성률(굴림 수). 회피 달성 = '피함' 굴림 중 그 위험(감지 + 예상 피격 + 1초 안 같은 패턴)에 피격이 없던 비율, " +
                  "간파 달성 = 성공 굴림 누름 뒤 1초 안 서버 간파 성공 비율. C7e 전 판은 열이 비어 —.</div>\n<div class=\"muted\">8번 칸 = 사용/판 · 적중률(적중 판정/사용) · 피해 비중(그 캐릭터 행동별 피해 합 중). 행동별 피해는 클램프 전 값. " +
                  "실제 피격률 = 굴림이 있었던 위험에서 실제로 맞은 건수 / 굴림 수(같은 패턴 0.3초 안 다단은 1건). 굴림 없는 피격은 따로 센다.</div>\n");
    }

    // ── 할 일 119 C3 — 지난 배치 비교 ─────────────────────────

    /// <summary>비교 표 한 줄 — 단계 요약 → 표시값 · 차이 계산용 숫자(NaN = 차이 없음).</summary>
    private sealed class CompareMetric
    {
        public string Label;
        public Func<Summary, double> Value;
        public Func<double, string> Format;
        public Func<double, string> Delta;
    }

    private static readonly CompareMetric[] CompareMetrics = BuildCompareMetrics();

    private static CompareMetric[] BuildCompareMetrics()
    {
        Func<double, string> pct1 = v => Pct(v, 1);
        Func<double, string> dPp = v => double.IsNaN(v) ? Dash : (v >= 0 ? "+" : "") + (v * 100.0).ToString("F1", Inv) + "%p";
        Func<double, string> n2 = v => Num(v, 2);
        Func<double, string> d2 = v => double.IsNaN(v) ? Dash : (v >= 0 ? "+" : "") + v.ToString("N2", Inv);
        Func<double, string> tm = Time;
        Func<double, string> dT = v => double.IsNaN(v) ? Dash : (v >= 0 ? "+" : "−") + Time(Math.Abs(v));
        Func<double, string> n1 = v => Num(v, 1);
        Func<double, string> d1 = v => double.IsNaN(v) ? Dash : (v >= 0 ? "+" : "") + v.ToString("N1", Inv);
        var list = new List<CompareMetric>
        {
            new CompareMetric { Label = "판 수(통계)", Value = s => s.Runs, Format = v => Num(v, 0), Delta = v => double.IsNaN(v) ? Dash : (v >= 0 ? "+" : "") + v.ToString("0", Inv) },
            new CompareMetric { Label = "클리어율", Value = s => s.ClearRate, Format = pct1, Delta = dPp },
            new CompareMetric { Label = "전멸률", Value = s => s.Runs > 0 ? (double)s.Wipes / s.Runs : double.NaN, Format = pct1, Delta = dPp },
            new CompareMetric { Label = "시간초과율", Value = s => s.Runs > 0 ? (double)s.Timeouts / s.Runs : double.NaN, Format = pct1, Delta = dPp },
            new CompareMetric { Label = "클리어 시간 평균", Value = s => s.ClearMean, Format = tm, Delta = dT },
            new CompareMetric { Label = "클리어 시간 중앙값", Value = s => s.ClearMedian, Format = tm, Delta = dT },
            new CompareMetric { Label = "파티 사망/판", Value = s => s.DeathsAvg, Format = n2, Delta = d2 },
            new CompareMetric { Label = "공용 목숨 소모/판", Value = s => s.LivesUsedAvg, Format = n2, Delta = d2 },
            new CompareMetric { Label = "입력 피격률", Value = s => s.InputHitRate, Format = pct1, Delta = dPp },
            new CompareMetric { Label = "실제 피격률", Value = s => s.ActualHitRate, Format = pct1, Delta = dPp },
            new CompareMetric { Label = "굴림 없는 피격/판", Value = s => s.UnrolledPerRun, Format = n1, Delta = d1 },
            new CompareMetric { Label = "간파 시도/판", Value = s => s.CounterTries, Format = n2, Delta = d2 },
            new CompareMetric { Label = "간파 성공/판", Value = s => s.CounterSuccess, Format = n2, Delta = d2 },
            new CompareMetric { Label = "간파 성공률", Value = s => s.CounterRate, Format = pct1, Delta = dPp },
            new CompareMetric { Label = "제압 횟수/판", Value = s => s.SuppressAvg, Format = n2, Delta = d2 },
            new CompareMetric { Label = "제압 중 가한 피해/판", Value = s => s.SuppressDamageAvg, Format = v => Num(v, 0), Delta = v => double.IsNaN(v) ? Dash : (v >= 0 ? "+" : "") + v.ToString("N0", Inv) },
        };
        foreach (QABoss103Character ch in Chars)
        {
            QABoss103Character c = ch;
            list.Add(new CompareMetric { Label = $"{CharLabel(c)} DPS", Value = s => s.Dps.TryGetValue(c, out double v) ? v : double.NaN, Format = n1, Delta = d1 });
        }
        return list.ToArray();
    }

    /// <summary>
    /// 지난 배치(batch.txt <c>compare_dir</c>)와 단계별 요약을 나란히 — 같은 지표(<see cref="Summaries"/>, 오류 판 제외). 열 = 단계마다 지난 · 이번 · 차이.
    /// </summary>
    private static void PreviousComparison(StringBuilder sb, QABoss103ReportInput input, List<Summary> now)
    {
        if (string.IsNullOrEmpty(input.CompareDir))
            return;
        string prevName = Path.GetFileName(input.CompareDir.TrimEnd('/', '\\'));
        sb.Append("<h2>지난 배치 비교 — ").Append(H(prevName)).Append(" → 이번</h2>\n");
        if (input.Compare == null)
        {
            sb.Append("<div class=\"warnbox\">비교 기준 폴더를 읽지 못했다(없음·자기 폴더): ").Append(H(input.CompareDir)).Append(" — batch.txt compare_dir 를 고치면 다음 리포트부터 보인다.</div>\n");
            return;
        }
        List<Summary> prev = Summaries(input.Compare);
        Dictionary<string, string> pb = QABoss103Batch.ParseKeyValues(input.Compare.BatchText);
        sb.Append("<div class=\"sub\">기준 ").Append(H(input.CompareDir)).Append(" · xlsx r").Append(H(Val(pb, "xlsx_svn_rev", "na")))
          .Append(" · git ").Append(H(Val(pb, "git_commit", "na"))).Append(" · 총 ").Append(H(Val(pb, "total", input.Compare.Total.ToString(Inv))))
          .Append("판 — 판 수가 달라 비율·판당 값으로 본다. 위 '119 규칙 변경점'이 차이의 일부다.</div>\n");
        var labels = new List<string>();
        foreach (QABoss103BotTier t in QABoss103Batch.TierOrder)
            labels.Add(QABoss103Batch.TierLabel(t));
        labels.Add("전체");
        sb.Append("<div class=\"scroll\"><table><thead><tr><th class=\"l\" rowspan=\"2\">지표</th>");
        foreach (string l in labels)
            sb.Append("<th colspan=\"3\">").Append(H(l)).Append("</th>");
        sb.Append("</tr><tr>");
        for (int i = 0; i < labels.Count; i++)
            sb.Append("<th>지난</th><th>이번</th><th>차이</th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (CompareMetric m in CompareMetrics)
        {
            sb.Append("<tr><td class=\"l\">").Append(H(m.Label)).Append("</td>");
            for (int i = 0; i < labels.Count; i++)
            {
                double a = i < prev.Count && prev[i].Runs > 0 ? m.Value(prev[i]) : double.NaN;
                double b = i < now.Count && now[i].Runs > 0 ? m.Value(now[i]) : double.NaN;
                double d = double.IsNaN(a) || double.IsNaN(b) ? double.NaN : b - a;
                sb.Append("<td>").Append(H(double.IsNaN(a) ? Dash : m.Format(a))).Append("</td><td>").Append(H(double.IsNaN(b) ? Dash : m.Format(b)))
                  .Append("</td><td>").Append(H(m.Delta(d))).Append("</td>");
            }
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody></table></div>\n");
    }

    // ── 할 일 119 C3 — 클리어 실패 사유 ──────────────────────

    /// <summary>run_fail.csv → (판:재실행)별 마지막 행(같은 칸을 다시 돌면 뒤 행).</summary>
    private static List<string[]> FailRows(QABoss103CsvTable t)
    {
        var last = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string[] r in t.Rows)
        {
            string k = Key(t, r);
            if (!last.ContainsKey(k))
                order.Add(k);
            last[k] = r;
        }
        var rows = new List<string[]>();
        foreach (string k in order)
            rows.Add(last[k]);
        rows.Sort((a, b) =>
        {
            int c = t.Num(a, "run").CompareTo(t.Num(b, "run"));
            return c != 0 ? c : t.Num(a, "retry").CompareTo(t.Num(b, "retry"));
        });
        return rows;
    }

    /// <summary>패턴별 실패 집계(통계 판만) — 실패 패턴 → 단계 열(보통·숙련·초보·전체) 판 수. 테스트용 공개.</summary>
    public static SortedDictionary<string, int[]> FailPatternCounts(QABoss103ReportInput input)
    {
        Ctx c = Load(input);
        return FailPatternCounts(c, QABoss103CsvTable.Parse(input.FailCsv));
    }

    private static SortedDictionary<string, int[]> FailPatternCounts(Ctx c, QABoss103CsvTable t)
    {
        var map = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (string[] r in FailRows(t))
        {
            if (!c.ValidRun.TryGetValue(Key(t, r), out string[] run))
                continue;
            string p = t.Get(r, "fail_pattern");
            if (p.Length == 0)
                p = QABoss103Metrics.Unknown;
            if (!map.TryGetValue(p, out int[] cells))
                map[p] = cells = new int[c.Columns.Count];
            int tier = TierIndex(c, c.Runs.Get(run, "tier"), t.Num(r, "run"));
            if (tier >= 0)
                cells[tier]++;
            cells[c.Columns.Count - 1]++;
        }
        return map;
    }

    private static int TierIndex(Ctx c, string tierText, double run)
    {
        QABoss103BotTier tier = Enum.TryParse(tierText, out QABoss103BotTier tt) ? tt : QABoss103Batch.TierOf(double.IsNaN(run) ? 1 : (int)run);
        for (int i = 0; i < c.Tiers.Count; i++)
            if (c.Tiers[i].Tier == tier)
                return i;
        return -1;
    }

    private static void FailTable(StringBuilder sb, Ctx c, QABoss103ReportInput input)
    {
        sb.Append("<h2>클리어 실패 사유</h2>\n");
        QABoss103CsvTable t = QABoss103CsvTable.Parse(input.FailCsv);
        List<string[]> rows = FailRows(t);
        if (rows.Count == 0)
        {
            sb.Append("<div class=\"muted\">실패 사유 기록 없음(").Append(H(QABoss103FailReason.File)).Append(" — 할 일 119 C3 부터 전멸·시간초과 판마다 1행).</div>\n");
            return;
        }

        // 패턴별 실패 집계 — 통계 판만.
        SortedDictionary<string, int[]> counts = FailPatternCounts(c, t);
        var order = new List<string>(counts.Keys);
        int all = c.Columns.Count - 1;
        order.Sort((a, b) =>
        {
            int d = counts[b][all].CompareTo(counts[a][all]);
            return d != 0 ? d : string.CompareOrdinal(a, b);
        });
        sb.Append("<h3>패턴별 실패 집계(통계 판)</h3>\n<table><thead><tr><th class=\"l\">실패 패턴</th>");
        foreach (Group g in c.Columns)
            sb.Append("<th>").Append(H(g.Label)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (string p in order)
        {
            sb.Append("<tr><td class=\"l\">").Append(H(p)).Append("</td>");
            for (int i = 0; i < c.Columns.Count; i++)
            {
                int fails = c.Columns[i].Runs.Count - CountClears(c, c.Columns[i]);
                int n = counts[p][i];
                sb.Append("<td>").Append(n > 0 ? H($"{n} ({Pct(Div(n, fails), 0)})") : Dash).Append("</td>");
            }
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody></table>\n<div class=\"muted\">실패 패턴 = 판정 순간 진행 중 기믹 → 판 끝 ")
          .Append(QABoss103FailReason.RecentWaveSeconds.ToString("0", Inv))
          .Append("초 안에 실패한 송전탑 기믹 → (전멸) 마지막 사망 패턴 → 진행 중 보스 공격 → 보스 상태 순. 괄호 = 그 단계 실패 판 중 비율.</div>\n");

        sb.Append("<h3>판별 실패 사유</h3>\n<div class=\"scroll\"><table><thead><tr><th>판</th><th>재실행</th><th>단계</th><th class=\"l\">결과</th><th>전투 시간</th>" +
                  "<th class=\"l\">실패 패턴</th><th class=\"l\">보스 패턴 · 페이즈</th><th>남은 인원</th><th>남은 공용 목숨</th><th>보스 남은 HP</th><th class=\"l\">비고</th><th>통계</th></tr></thead><tbody>\n");
        foreach (string[] r in rows)
        {
            bool valid = c.ValidRun.ContainsKey(Key(t, r));
            string tier = Enum.TryParse(t.Get(r, "tier"), out QABoss103BotTier tt) ? QABoss103Batch.TierLabel(tt) : t.Get(r, "tier");
            string outcome = t.Get(r, "outcome");
            if (t.Get(r, "outcome_source") == QABoss103EarlyTimeout.Note)
                outcome += "(조기)";
            string attack = t.Get(r, "boss_attack");
            string boss = attack.Length > 0 ? attack : t.Get(r, "boss_state");
            if (t.Get(r, "active_gimmick").Length > 0)
                boss += " · 기믹 " + t.Get(r, "active_gimmick").Replace(";", "·");
            if (t.Get(r, "boss_phase").Length > 0)
                boss += " · 페이즈 " + t.Get(r, "boss_phase");
            sb.Append("<tr><td>").Append(H(t.Get(r, "run"))).Append("</td><td>").Append(t.Get(r, "retry") == "1" ? "재실행" : "")
              .Append("</td><td>").Append(H(tier)).Append("</td><td class=\"l\">").Append(H(outcome))
              .Append("</td><td>").Append(H(Time(t.Num(r, "combat_sec")))).Append("</td><td class=\"l\">").Append(H(t.Get(r, "fail_pattern")))
              .Append("</td><td class=\"l\">").Append(H(boss)).Append("</td><td>").Append(H(Blank(t.Get(r, "alive"))))
              .Append("</td><td>").Append(H(Blank(t.Get(r, "lives_left")))).Append("</td><td>").Append(H(Pct(t.Num(r, "boss_hp_pct") / 100.0, 1)))
              .Append("</td><td class=\"l\">").Append(H(t.Get(r, "note"))).Append("</td><td>").Append(valid ? "포함" : "<span class=\"muted\">제외</span>").Append("</td></tr>\n");
        }
        sb.Append("</tbody></table></div>\n<div class=\"muted\">남은 인원·목숨 = 판정 순간(전멸은 전멸 리스폰 전 생존 0 이 된 순간). 송전탑 수 = 그 기믹에 올라온 탑 중 부서진 수(서버). " +
                  "전체 열은 run_fail.csv(마지막 사망·사망 귀속·기믹 실패 목록 포함).</div>\n");
    }

    private static int CountClears(Ctx c, Group g)
    {
        int n = 0;
        foreach (string[] r in g.Runs)
            if (c.Runs.Get(r, "outcome") == "Clear")
                n++;
        return n;
    }

    private static string Blank(string s) => string.IsNullOrEmpty(s) ? Dash : s;

    private static void Sec(StringBuilder sb, string title, int cols) =>
        sb.Append("<tr class=\"sec\"><td colspan=\"").Append(cols + 1).Append("\">").Append(H(title)).Append("</td></tr>\n");

    private static void Line(StringBuilder sb, string label, List<Summary> sums, Func<Summary, string> f)
    {
        sb.Append("<tr><td class=\"l\">").Append(H(label)).Append("</td>");
        foreach (Summary s in sums)
            sb.Append("<td>").Append(H(s.Runs > 0 ? f(s) : Dash)).Append("</td>");
        sb.Append("</tr>\n");
    }

    private static void Line(StringBuilder sb, string label, Ctx c, Func<Group, string> f)
    {
        sb.Append("<tr><td class=\"l\">").Append(H(label)).Append("</td>");
        foreach (Group g in c.Columns)
            sb.Append("<td>").Append(H(g.Runs.Count > 0 ? f(g) : Dash)).Append("</td>");
        sb.Append("</tr>\n");
    }

    // ── 차트 ─────────────────────────────────────────────────

    private static void Charts(StringBuilder sb, Ctx c, List<Summary> sums)
    {
        sb.Append("<h2>차트</h2>\n<div class=\"charts\">\n");
        var tierNames = new List<string>();
        foreach (Group g in c.Tiers)
            tierNames.Add(g.Label);

        // 1) 단계별 클리어율 — 단계 이름이 축에 있어 범례 없음.
        var clear = new double[c.Tiers.Count];
        for (int i = 0; i < clear.Length; i++)
            clear[i] = sums[i].ClearRate;
        sb.Append("<div class=\"chart\"><h3>단계별 클리어율</h3>");
        Bars(sb, tierNames, new[] { new Series { Name = "클리어율", Values = clear, PerCategoryColor = true } }, 1.0, v => Pct(v, 0), 320);
        sb.Append("</div>\n");

        // 2) 클리어 시간 분포(점 + 상자).
        sb.Append("<div class=\"chart\"><h3>클리어 시간 분포(분)</h3>");
        Strip(sb, tierNames, sums);
        sb.Append("</div>\n");

        // 3) 캐릭터별 DPS — 묶음 = 캐릭터, 막대 = 단계.
        var charNames = new List<string>();
        foreach (QABoss103Character ch in Chars)
            charNames.Add(CharLabel(ch));
        var dps = new List<Series>();
        double dpsMax = 0;
        for (int t = 0; t < c.Tiers.Count; t++)
        {
            var v = new double[Chars.Length];
            for (int k = 0; k < Chars.Length; k++)
            {
                v[k] = sums[t].Dps[Chars[k]];
                if (!double.IsNaN(v[k]))
                    dpsMax = Math.Max(dpsMax, v[k]);
            }
            dps.Add(new Series { Name = c.Tiers[t].Label, Values = v, Color = TierColor[t] });
        }
        sb.Append("<div class=\"chart\"><h3>캐릭터별 DPS</h3>");
        Legend(sb, dps);
        Bars(sb, charNames, dps, NiceMax(dpsMax), v => Num(v, 0), 420);
        sb.Append("</div>\n");

        // 4) 입력 vs 실제 피격률 — 묶음 = 단계, 막대 = 측정값 2개.
        var input = new double[c.Tiers.Count];
        var actual = new double[c.Tiers.Count];
        double rateMax = 0;
        for (int t = 0; t < c.Tiers.Count; t++)
        {
            input[t] = sums[t].InputHitRate;
            actual[t] = sums[t].ActualHitRate;
            if (!double.IsNaN(input[t])) rateMax = Math.Max(rateMax, input[t]);
            if (!double.IsNaN(actual[t])) rateMax = Math.Max(rateMax, actual[t]);
        }
        var rate = new List<Series>
        {
            new Series { Name = "입력 피격률", Values = input, Color = "var(--in)" },
            new Series { Name = "실제 피격률", Values = actual, Color = "var(--act)" },
        };
        sb.Append("<div class=\"chart\"><h3>입력 vs 실제 피격률</h3>");
        Legend(sb, rate);
        Bars(sb, tierNames, rate, Math.Min(1.0, NiceMax(Math.Max(0.05, rateMax))), v => Pct(v, 0), 360);
        sb.Append("</div>\n</div>\n");
    }

    private sealed class Series
    {
        public string Name;
        public double[] Values;
        public string Color = "var(--s1)";
        /// <summary>막대 색을 범주(단계) 색으로 — 단일 계열 단계 차트.</summary>
        public bool PerCategoryColor;
    }

    private static void Legend(StringBuilder sb, List<Series> series)
    {
        sb.Append("<div class=\"legend\">");
        foreach (Series s in series)
            sb.Append("<span><i style=\"background:").Append(s.Color).Append("\"></i>").Append(H(s.Name)).Append("</span>");
        sb.Append("</div>");
    }

    /// <summary>묶음 세로 막대(한 축, 0 기준). 막대 위 값 라벨 + 마우스 올리면 제목 툴팁.</summary>
    private static void Bars(StringBuilder sb, List<string> cats, IList<Series> series, double max, Func<double, string> fmt, int width)
    {
        const int h = 220, top = 18, bottom = 28, left = 44, right = 8;
        double plotH = h - top - bottom, plotW = width - left - right;
        if (!(max > 0))
            max = 1;
        sb.Append($"<svg width=\"{width}\" height=\"{h}\" viewBox=\"0 0 {width} {h}\" role=\"img\">");
        for (int i = 0; i <= 4; i++)
        {
            double v = max * i / 4.0;
            double y = top + plotH - plotH * i / 4.0;
            sb.Append($"<line class=\"grid\" x1=\"{left}\" x2=\"{width - right}\" y1=\"{F(y)}\" y2=\"{F(y)}\"/>");
            sb.Append($"<text x=\"{left - 4}\" y=\"{F(y + 4)}\" text-anchor=\"end\">{H(fmt(v))}</text>");
        }
        double groupW = plotW / Math.Max(1, cats.Count);
        double gap = 2;
        double barW = Math.Max(4, Math.Min(36, (groupW * 0.7 - gap * (series.Count - 1)) / series.Count));
        double used = barW * series.Count + gap * (series.Count - 1);
        for (int k = 0; k < cats.Count; k++)
        {
            double gx = left + groupW * k + (groupW - used) / 2;
            for (int si = 0; si < series.Count; si++)
            {
                Series s = series[si];
                double v = k < s.Values.Length ? s.Values[k] : double.NaN;
                double x = gx + si * (barW + gap);
                string color = s.PerCategoryColor ? TierColor[k % TierColor.Length] : s.Color;
                string tip = $"{cats[k]} · {s.Name} {fmt(v)}";
                if (double.IsNaN(v))
                {
                    sb.Append($"<text x=\"{F(x + barW / 2)}\" y=\"{F(top + plotH - 4)}\" text-anchor=\"middle\">{Dash}</text>");
                    continue;
                }
                double bh = Math.Max(0, Math.Min(1, v / max)) * plotH;
                double y = top + plotH - bh;
                sb.Append($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(barW)}\" height=\"{F(Math.Max(bh, 0.5))}\" rx=\"3\" fill=\"{color}\"><title>{H(tip)}</title></rect>");
                sb.Append($"<text class=\"v\" x=\"{F(x + barW / 2)}\" y=\"{F(y - 4)}\" text-anchor=\"middle\">{H(fmt(v))}</text>");
            }
            sb.Append($"<text x=\"{F(left + groupW * k + groupW / 2)}\" y=\"{h - 10}\" text-anchor=\"middle\">{H(cats[k])}</text>");
        }
        sb.Append($"<line class=\"axis\" x1=\"{left}\" x2=\"{width - right}\" y1=\"{F(top + plotH)}\" y2=\"{F(top + plotH)}\"/>");
        sb.Append("</svg>");
    }

    /// <summary>단계별 가로 띠 — 클리어 판 하나 = 점, 상자 = 사분위(25~75%), 굵은 선 = 중앙값.</summary>
    private static void Strip(StringBuilder sb, List<string> tiers, List<Summary> sums)
    {
        const int width = 420, rowH = 46, top = 10, left = 44, right = 12, bottom = 26;
        int h = top + rowH * tiers.Count + bottom;
        double maxMin = 1;
        for (int t = 0; t < tiers.Count; t++)
            foreach (double v in sums[t].ClearTimes)
                maxMin = Math.Max(maxMin, v / 60.0);
        maxMin = Math.Min(30, NiceMax(maxMin));
        double plotW = width - left - right;
        Func<double, double> X = m => left + plotW * Math.Max(0, Math.Min(1, m / maxMin));
        sb.Append($"<svg width=\"{width}\" height=\"{h}\" viewBox=\"0 0 {width} {h}\" role=\"img\">");
        for (int i = 0; i <= 4; i++)
        {
            double m = maxMin * i / 4.0;
            sb.Append($"<line class=\"grid\" x1=\"{F(X(m))}\" x2=\"{F(X(m))}\" y1=\"{top}\" y2=\"{h - bottom}\"/>");
            sb.Append($"<text x=\"{F(X(m))}\" y=\"{h - 10}\" text-anchor=\"middle\">{m.ToString("0.#", Inv)}</text>");
        }
        for (int t = 0; t < tiers.Count; t++)
        {
            double cy = top + rowH * t + rowH / 2.0;
            string color = TierColor[t % TierColor.Length];
            sb.Append($"<text x=\"{left - 6}\" y=\"{F(cy + 4)}\" text-anchor=\"end\">{H(tiers[t])}</text>");
            List<double> v = sums[t].ClearTimes;
            if (v.Count == 0)
            {
                sb.Append($"<text x=\"{left + 4}\" y=\"{F(cy + 4)}\">클리어 없음</text>");
                continue;
            }
            double q1 = Quantile(v, 0.25) / 60.0, q2 = Quantile(v, 0.5) / 60.0, q3 = Quantile(v, 0.75) / 60.0;
            sb.Append($"<rect x=\"{F(X(q1))}\" y=\"{F(cy - 12)}\" width=\"{F(Math.Max(1, X(q3) - X(q1)))}\" height=\"24\" rx=\"3\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"><title>{H($"{tiers[t]} 25% {q1:F1}분 · 중앙 {q2:F1}분 · 75% {q3:F1}분")}</title></rect>");
            sb.Append($"<line x1=\"{F(X(q2))}\" x2=\"{F(X(q2))}\" y1=\"{F(cy - 12)}\" y2=\"{F(cy + 12)}\" stroke=\"{color}\" stroke-width=\"3\"/>");
            for (int i = 0; i < v.Count; i++)
            {
                double jitter = ((i * 7) % 5 - 2) * 3.0;   // 결정적 흩뿌림
                sb.Append($"<circle cx=\"{F(X(v[i] / 60.0))}\" cy=\"{F(cy + jitter)}\" r=\"4\" fill=\"{color}\" stroke=\"var(--card)\" stroke-width=\"2\"><title>{H($"{tiers[t]} {Time(v[i])}")}</title></circle>");
            }
        }
        sb.Append("</svg>");
    }

    private static double NiceMax(double v)
    {
        if (!(v > 0))
            return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
            if (m * p >= v)
                return m * p;
        return 10 * p;
    }

    // ── 표 ───────────────────────────────────────────────────

    private static void CharacterTables(StringBuilder sb, Ctx c)
    {
        sb.Append("<h2>캐릭터 요약(단계별 DPS · 받은 피해/분 · 사망/판)</h2>\n<table><thead><tr><th class=\"l\">캐릭터</th>");
        foreach (Group g in c.Columns)
            sb.Append("<th>").Append(H(g.Label)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (QABoss103Character ch in Chars)
            Line(sb, CharLabel(ch), c, g =>
                $"{Num(Div(SumChars(c, g, ch, "damage_dealt"), SumDur(c, g, ch)), 1)} · {Num(Div(SumChars(c, g, ch, "damage_taken") * 60.0, SumDur(c, g, ch)), 0)} · {Num(AvgChars(c, g, ch, "deaths"), 2)}");
        sb.Append("</tbody></table>\n");
    }

    /// <summary>할 일 119 C2 — 캐릭터 스킬 툴팁 근거 봇 규칙 표 + 발동 횟수(run_chars 규칙 열, 판당 평균 · 합계).</summary>
    private static void CharRuleTable(StringBuilder sb, Ctx c)
    {
        sb.Append("<h2>캐릭터 스킬 툴팁 규칙(").Append(H(QABoss103CharRules.Since)).Append(")</h2>\n");
        sb.Append("<div class=\"sub\">툴팁 = 게임이 띄우는 스킬 설명(StringTable 한국어). 툴팁에 근거 없는 추측 규칙은 넣지 않았다 · 스킬 사용 빈도·반응 지연은 단계 프로필 그대로.</div>\n");
        sb.Append("<div class=\"scroll\"><table><thead><tr><th class=\"l\">캐릭터</th><th class=\"l\">스킬</th><th class=\"l\">툴팁 원문 요지</th><th class=\"l\">봇 행동</th><th class=\"l\">상태</th><th class=\"l\">열</th></tr></thead><tbody>\n");
        foreach (QABoss103CharRuleRow r in QABoss103CharRules.Table)
            sb.Append("<tr><td class=\"l\">").Append(H(CharLabel(r.Character)))
              .Append("</td><td class=\"l\">").Append(H(r.Skill))
              .Append("</td><td class=\"l\">").Append(H(r.TooltipGist))
              .Append("</td><td class=\"l\">").Append(H(r.BotAction))
              .Append("</td><td class=\"l\">").Append(H(r.Status))
              .Append("</td><td class=\"l\">").Append(H(r.Column.Length > 0 ? r.Column : Dash))
              .Append("</td></tr>\n");
        sb.Append("</tbody></table></div>\n");

        sb.Append("<h3>규칙 발동 횟수(판당 평균 · 합계)</h3>\n<table><thead><tr><th class=\"l\">열</th>");
        foreach (Group g in c.Columns)
            sb.Append("<th>").Append(H(g.Label)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        for (int i = 0; i < QABoss103CharRules.Count; i++)
        {
            string col = QABoss103CharRules.Tokens[i];
            QABoss103Character owner = QABoss103CharRules.Owner((QABoss103CharRule)i);
            Line(sb, CharLabel(owner) + " " + col, c, g =>
            {
                double sum = 0, n = 0;
                foreach (string[] r in CharRowsOf(c, g, owner))
                    Acc(c.CharsT.Num(r, col), ref sum, ref n);
                return n > 0 ? $"{Num(sum / n, 2)} · {sum.ToString("0", Inv)}" : Dash;
            });
        }
        sb.Append("</tbody></table>\n");
    }

    private static void PatternTable(StringBuilder sb, Ctx c)
    {
        sb.Append("<h2>패턴별 피격·사망</h2>\n");
        var patterns = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string[] r in c.All.PatternRows)
            patterns.Add(c.Patterns.Get(r, "pattern"));
        if (patterns.Count == 0)
        {
            sb.Append("<div class=\"muted\">패턴 기록 없음.</div>\n");
            return;
        }
        sb.Append("<div class=\"scroll\"><table><thead><tr><th class=\"l\">패턴</th>");
        foreach (Group g in c.Columns)
            sb.Append("<th>").Append(H(g.Label)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (string p in patterns)
            Line(sb, p, c, g =>
            {
                double th = 0, take = 0, hits = 0, deaths = 0;
                foreach (string[] r in g.PatternRows)
                {
                    if (c.Patterns.Get(r, "pattern") != p)
                        continue;
                    th += Z(c.Patterns.Num(r, "threats"));
                    take += Z(c.Patterns.Num(r, "rolled_take"));
                    hits += Z(c.Patterns.Num(r, "actual_hits"));
                    deaths += Z(c.Patterns.Num(r, "deaths"));
                }
                return $"굴림 {th:0} · 입력 {Pct(Div(take, th), 0)} · 피격 {hits:0} · 사망 {deaths:0}";
            });
        sb.Append("</tbody></table></div>\n<div class=\"muted\">피격 = 그 패턴의 실제 피격 건수(굴림 없는 피격 포함). 사망 = 사망 직전 3초 안 마지막 피격 패턴.</div>\n");
    }

    private static void GimmickTable(StringBuilder sb, Ctx c)
    {
        sb.Append("<h2>보스 기믹별 성공·실패</h2>\n");
        var gimmicks = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string[] r in c.All.GimmickRows)
            gimmicks.Add(c.Gimmicks.Get(r, "gimmick"));
        if (gimmicks.Count == 0)
        {
            sb.Append("<div class=\"muted\">기믹 기록 없음.</div>\n");
            return;
        }
        sb.Append("<div class=\"scroll\"><table><thead><tr><th class=\"l\">기믹</th>");
        foreach (Group g in c.Columns)
            sb.Append("<th>").Append(H(g.Label)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (string gm in gimmicks)
            Line(sb, gm, c, g =>
            {
                int ok = 0, fail = 0, partial = 0, other = 0;
                foreach (string[] r in g.GimmickRows)
                {
                    if (c.Gimmicks.Get(r, "gimmick") != gm)
                        continue;
                    string res = c.Gimmicks.Get(r, "result");
                    if (res == "success") ok++;
                    else if (res == "fail") fail++;
                    else if (res == "partial") partial++;
                    else other++;
                }
                return $"성공 {ok} · 실패 {fail}" + (partial > 0 ? $" · 부분 {partial}" : "") + (other > 0 ? $" · 기타 {other}" : "");
            });
        sb.Append("</tbody></table></div>\n<div class=\"muted\">기타 = 결과 없이 끝남(ended)·판 종료(timeout) 등.</div>\n");
    }

    /// <summary>특성 하나의 단계 칸 — 등장 판 수 · 클리어율 · DPS(그 특성을 가진 캐릭터 기준).</summary>
    public struct TraitCell
    {
        public int Runs, Clears;
        public double Dealt, Seconds;
        public double ClearRate => Runs > 0 ? (double)Clears / Runs : double.NaN;
        public double Dps => Seconds > 0 ? Dealt / Seconds : double.NaN;
    }

    private static readonly string[] TraitColumns = { "traits_paladin", "traits_assassin", "traits_gunner" };

    /// <summary>특성 id → 칸(단계 열 순서 = 보통·숙련·초보·전체).</summary>
    public static SortedDictionary<string, TraitCell[]> Traits(QABoss103ReportInput input) => Traits(Load(input));

    private static SortedDictionary<string, TraitCell[]> Traits(Ctx c)
    {
        var map = new SortedDictionary<string, TraitCell[]>(StringComparer.Ordinal);
        for (int gi = 0; gi < c.Columns.Count; gi++)
        {
            Group g = c.Columns[gi];
            foreach (string[] r in g.Runs)
            {
                bool clear = c.Runs.Get(r, "outcome") == "Clear";
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int k = 0; k < Chars.Length; k++)
                    foreach (string t in Split(c.Runs.Get(r, TraitColumns[k])))
                        seen.Add(t);
                foreach (string t in seen)
                {
                    TraitCell[] cells = Cells(map, t, c.Columns.Count);
                    cells[gi].Runs++;
                    if (clear)
                        cells[gi].Clears++;
                }
                string key = Key(c.Runs, r);
                double dur = c.Duration.TryGetValue(key, out double d) ? d : double.NaN;
                if (double.IsNaN(dur))
                    continue;
                for (int k = 0; k < Chars.Length; k++)
                {
                    double dealt = CharDealt(c, key, Chars[k]);
                    if (double.IsNaN(dealt))
                        continue;
                    foreach (string t in new HashSet<string>(Split(c.Runs.Get(r, TraitColumns[k])), StringComparer.Ordinal))
                    {
                        TraitCell[] cells = Cells(map, t, c.Columns.Count);
                        cells[gi].Dealt += dealt;
                        cells[gi].Seconds += dur;
                    }
                }
            }
        }
        return map;
    }

    private static TraitCell[] Cells(SortedDictionary<string, TraitCell[]> map, string t, int n)
    {
        if (!map.TryGetValue(t, out TraitCell[] cells))
            map[t] = cells = new TraitCell[n];
        return cells;
    }

    private static double CharDealt(Ctx c, string key, QABoss103Character ch)
    {
        foreach (string[] r in c.All.CharRows)
            if (Key(c.CharsT, r) == key && c.CharsT.Get(r, "character") == ch.ToString())
                return c.CharsT.Num(r, "damage_dealt");
        return double.NaN;
    }

    private static IEnumerable<string> Split(string s)
    {
        foreach (string p in (s ?? "").Split(';'))
        {
            string t = p.Trim();
            if (t.Length > 0)
                yield return t;
        }
    }

    private static void TraitTable(StringBuilder sb, Ctx c)
    {
        sb.Append("<h2>특성별 등장·클리어율·DPS</h2>\n");
        SortedDictionary<string, TraitCell[]> map = Traits(c);
        if (map.Count == 0)
        {
            sb.Append("<div class=\"muted\">특성 기록 없음.</div>\n");
            return;
        }
        int all = c.Columns.Count - 1;
        var order = new List<string>(map.Keys);
        order.Sort((a, b) =>
        {
            int d = map[b][all].Runs.CompareTo(map[a][all].Runs);
            return d != 0 ? d : string.CompareOrdinal(a, b);
        });
        sb.Append("<div class=\"scroll\"><table><thead><tr><th class=\"l\">특성</th>");
        foreach (Group g in c.Columns)
            sb.Append("<th>").Append(H(g.Label)).Append("<br><span class=\"muted\">판 · 클리어율 · DPS</span></th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (string t in order)
        {
            sb.Append("<tr><td class=\"l\">").Append(H(t)).Append("</td>");
            foreach (TraitCell cell in map[t])
                sb.Append("<td>").Append(cell.Runs > 0 ? H($"{cell.Runs} · {Pct(cell.ClearRate, 0)} · {Num(cell.Dps, 1)}") : Dash).Append("</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody></table></div>\n<div class=\"muted\">판 = 파티 중 누군가 그 특성을 고른 판 수. DPS = 그 특성을 가진 캐릭터의 보스 피해 / 전투 시간.</div>\n");
    }

    private static void RunList(StringBuilder sb, Ctx c)
    {
        sb.Append("<h2>판 목록</h2>\n");
        if (c.Attempts.Count == 0)
        {
            sb.Append("<div class=\"muted\">아직 끝난 판이 없다.</div>\n");
            return;
        }
        sb.Append("<div class=\"scroll\"><table><thead><tr><th>판</th><th>재실행</th><th>단계</th><th>판 시드</th><th class=\"l\">결과</th><th>전투 시간</th><th>보스 남은 HP</th>" +
                  "<th class=\"l\">특성(팔라딘 / 어쌔신 / 거너)</th><th class=\"l\">오류 사유</th><th>통계</th></tr></thead><tbody>\n");
        foreach (QABoss103Attempt a in c.Attempts)
        {
            string[] r = c.Runs.Rows[a.Index];
            bool valid = QABoss103Batch.IsValidFinal(c.Attempts, a.Run, a.Retry) && ReferenceEquals(QABoss103Batch.Effective(c.Attempts, a.Run, a.Retry), a);
            string tier = Enum.TryParse(c.Runs.Get(r, "tier"), out QABoss103BotTier t) ? QABoss103Batch.TierLabel(t) : c.Runs.Get(r, "tier");
            string reason = a.IsError ? QABoss103Batch.ReasonOf(a) + (a.ErrorReason.Length > 0 && a.ErrorReason != QABoss103Batch.ReasonOf(a) ? " — " + a.ErrorReason : "") : "";
            if (a.IsError && !QABoss103Batch.IsTable(a.DataSource))
                reason += $" (출처 {a.DataSource})";
            sb.Append("<tr><td>").Append(a.Run).Append("</td><td>").Append(a.Retry > 0 ? "재실행" : "").Append("</td><td>").Append(H(tier))
              .Append("</td><td>").Append(H(c.Runs.Get(r, "run_seed"))).Append("</td><td class=\"l").Append(a.IsError ? " err" : "").Append("\">").Append(H(a.OutcomeText))
              .Append("</td><td>").Append(H(Time(c.Runs.Num(r, "combat_sec")))).Append("</td><td>").Append(H(Pct(c.Runs.Num(r, "boss_hp_end_pct") / 100.0, 0)))
              .Append("</td><td class=\"l\">").Append(H($"{c.Runs.Get(r, "traits_paladin")} / {c.Runs.Get(r, "traits_assassin")} / {c.Runs.Get(r, "traits_gunner")}"))
              .Append("</td><td class=\"l\">").Append(H(reason)).Append("</td><td>").Append(valid ? "포함" : "<span class=\"muted\">제외</span>").Append("</td></tr>\n");
        }
        sb.Append("</tbody></table></div>\n");
    }

    // ── 형식 ─────────────────────────────────────────────────

    public static string H(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        return sb.ToString();
    }

    private static string Val(Dictionary<string, string> d, string k, string fallback) =>
        d.TryGetValue(k, out string v) && v.Length > 0 ? v : fallback;

    public static string Pct(double v, int decimals) =>
        double.IsNaN(v) || double.IsInfinity(v) ? Dash : (v * 100.0).ToString("F" + decimals, Inv) + "%";

    public static string Num(double v, int decimals) =>
        double.IsNaN(v) || double.IsInfinity(v) ? Dash : v.ToString("N" + decimals, Inv);

    /// <summary>초 → <c>m:ss</c>. NaN·음수 = –.</summary>
    public static string Time(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
            return Dash;
        int s = (int)Math.Round(seconds);
        return $"{s / 60}:{(s % 60).ToString("00", Inv)}";
    }

    private static string F(double v) => v.ToString("0.##", Inv);
}

/// <summary>할 일 103 C6 리포트 파일 — 배치 폴더 파일을 읽어 <see cref="QABoss103Report.Build"/> → report.html.</summary>
public static class QABoss103ReportFiles
{
    public const string NotesFile = "notes.txt";

    public static QABoss103ReportInput Read(string dir, string loopState) => Read(dir, loopState, true);

    private static QABoss103ReportInput Read(string dir, string loopState, bool withCompare)
    {
        var input = new QABoss103ReportInput
        {
            BatchText = ReadOrEmpty(Path.Combine(dir, QABoss103Batch.BatchFile)),
            RunsCsv = ReadOrEmpty(Path.Combine(dir, QABoss103Csv.RunsFile)),
            CharsCsv = ReadOrEmpty(Path.Combine(dir, QABoss103Csv.CharsFile)),
            PatternsCsv = ReadOrEmpty(Path.Combine(dir, QABoss103Csv.PatternsFile)),
            GimmicksCsv = ReadOrEmpty(Path.Combine(dir, QABoss103Csv.GimmicksFile)),
            FailCsv = ReadOrEmpty(Path.Combine(dir, QABoss103FailReason.File)),
            DashPredictCsv = ReadOrEmpty(Path.Combine(dir, QABoss116DashPredict.File)),
            Fixes = ReadOrEmpty(Path.Combine(dir, QABoss103Batch.FixesFile)),
            Notes = ReadOrEmpty(Path.Combine(dir, QABoss103Batch.LogFolder, NotesFile)),
            LoopState = loopState ?? "",
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };
        Dictionary<string, string> batch = QABoss103Batch.ParseKeyValues(input.BatchText);
        if (batch.TryGetValue("total", out string t) && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total) && total > 0)
            input.Total = total;
        // 할 일 119 C3 — 비교 기준 배치(지난 103). 자기 폴더·없는 폴더는 비교하지 않는다(비교 기준의 비교 기준은 읽지 않는다).
        if (withCompare && batch.TryGetValue("compare_dir", out string cmp) && cmp.Length > 0)
        {
            input.CompareDir = cmp;
            if (Directory.Exists(cmp) && !SameDir(cmp, dir))
                input.Compare = Read(cmp, "", false);
        }
        return input;
    }

    private static bool SameDir(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>리포트를 다시 만든다(임시 파일 → 교체 — 브라우저가 반쯤 쓴 파일을 읽지 않게). 경로.</summary>
    public static string Write(string dir, string loopState)
    {
        string html = QABoss103Report.Build(Read(dir, loopState));
        string path = Path.Combine(dir, QABoss103Batch.ReportFile);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, html, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tmp, path);
        return path;
    }

    private static string ReadOrEmpty(string path)
    {
        if (!File.Exists(path))
            return "";
        try { return QABoss103Heartbeat.ReadShared(path); }
        catch { return ""; }
    }
}
#endif
