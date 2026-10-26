using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>할 일 103 C6 — runs.csv 한 행(판 1회 시도)에서 루프가 쓰는 값만.</summary>
public sealed class QABoss103Attempt
{
    public int Run;
    public int Retry;
    public QABoss103Outcome Outcome = QABoss103Outcome.None;
    public string OutcomeText = "";
    public string ErrorReason = "";
    public string DataSource = "";
    /// <summary>runs.csv 의 행 순서(0부터).</summary>
    public int Index;

    /// <summary>통계에 넣으면 안 되는 시도 — <see cref="QABoss103Batch.IsError"/>.</summary>
    public bool IsError => QABoss103Batch.IsError(this);
}

/// <summary>할 일 103 C6 — 판 1회 진행 감시 입력(초). NaN = 아직 모름.</summary>
public struct QABoss103Watch
{
    /// <summary>Play 진입부터.</summary>
    public double SincePlay;
    /// <summary>호스트 마지막 심장박동부터(NaN = 아직 박동 없음 → Play 진입부터로 본다).</summary>
    public double SinceHostBeat;
    /// <summary>클론 중 가장 오래된 마지막 심장박동부터(NaN = 클론 박동 없음 → 검사 안 함).</summary>
    public double SinceOldestCloneBeat;
    /// <summary>Combat 진입부터(NaN = 아직 Combat 전).</summary>
    public double SinceCombat;
}

/// <summary>
/// 할 일 103 C6 90판 연속 루프의 순수 규칙(EditMode <c>QABoss103BatchTests</c>) — 남은 판·재실행 목록·판 단계/시드·오류 판정·단계 상한·쉬는 시간·
/// 데이터 출처 판정·심장박동 파일 형식·CSV 읽기. Unity 의존 없음. 상태 기계는 에디터 <c>QABoss103BatchRunner</c>.
/// </summary>
public static class QABoss103Batch
{
    public const int TotalRuns = 90;
    /// <summary>오류 판 재실행 횟수(끝에서 1회).</summary>
    public const int MaxRetry = 1;

    public const double HeartbeatTimeoutSeconds = 120.0;
    /// <summary>Play 진입부터 Combat 진입까지 상한(8분).</summary>
    public const double StartTimeoutSeconds = 8 * 60.0;
    /// <summary>Combat 30분 판정(<see cref="QABoss103Plan.CombatLimitSeconds"/>) 뒤 끝 신호를 기다리는 여유(3분).</summary>
    public const double EndGraceSeconds = 3 * 60.0;
    /// <summary>Play 시작 요청부터 실제 Play 진입까지 상한.</summary>
    public const double PlayStartTimeoutSeconds = 180.0;
    /// <summary>호스트가 판 기록을 쓴 뒤 Play 정지까지(클론이 끝 신호를 읽고 판 파일을 닫을 시간).</summary>
    public const double EndToStopSeconds = 3.0;
    /// <summary>심장박동 간격(호스트·클론 봇이 파일을 갱신).</summary>
    public const float BeatIntervalSeconds = 5f;

    public const int NormalCooldownSeconds = 3;
    public const int ErrorCooldownSeconds = 10;
    /// <summary>연속 오류가 이 수의 배수가 되면 <see cref="ErrorStreakPauseSeconds"/> 쉰다(멈추지 않는다).</summary>
    public const int ErrorStreakPause = 3;
    public const int ErrorStreakPauseSeconds = 60;

    // ── 오류 사유(runs.csv error_reason 첫 토큰) ───────────────
    public const string ReasonHeartbeat = "heartbeat";
    public const string ReasonHeartbeatClient = "heartbeat_client";
    public const string ReasonStartTimeout = "start_timeout";
    public const string ReasonEndTimeout = "end_timeout";
    public const string ReasonPlayStart = "play_start";
    public const string ReasonPlayStopped = "play_stopped";
    public const string ReasonDataSource = "data_source";

    // ── 단계·시드 ────────────────────────────────────────────

    /// <summary>판 단계 = (판−1) mod 3 → 보통·숙련·초보(<see cref="QABoss103Bot.TierForRun"/>).</summary>
    public static QABoss103BotTier TierOf(int run) => QABoss103Bot.TierForRun(run);

    /// <summary>판 시드 = 기준 시드 × 판 번호(재실행도 같은 시드 — 같은 특성).</summary>
    public static int RunSeedOf(int baseSeed, int run) => QABoss103Plan.RunSeed(baseSeed, run);

    /// <summary>리포트 열 순서 = 판 순서(보통 → 숙련 → 초보).</summary>
    public static readonly QABoss103BotTier[] TierOrder = { QABoss103BotTier.Normal, QABoss103BotTier.Skilled, QABoss103BotTier.Beginner };

    public static string TierLabel(QABoss103BotTier t) =>
        t == QABoss103BotTier.Beginner ? "초보" : t == QABoss103BotTier.Skilled ? "숙련" : "보통";

    // ── 오류 판정 ────────────────────────────────────────────

    /// <summary>
    /// 통계에서 뺄 시도 — 결과가 클리어·전멸·시간초과가 아니거나(Error·Aborted(C1 집결 실패 등)·모름)
    /// 데이터 출처가 테이블이 아니다(<see cref="IsTable"/>).
    /// </summary>
    public static bool IsError(QABoss103Attempt a)
    {
        if (a == null)
            return true;
        bool final = a.Outcome == QABoss103Outcome.Clear || a.Outcome == QABoss103Outcome.Wipe || a.Outcome == QABoss103Outcome.Timeout;
        return !final || !IsTable(a.DataSource);
    }

    /// <summary>오류 사유 분류(리포트 표시) — error_reason 첫 토큰, 결과가 Aborted 면 aborted, 출처가 테이블이 아니면 data_source.</summary>
    public static string ReasonOf(QABoss103Attempt a)
    {
        if (a == null || !a.IsError)
            return "";
        if (!IsTable(a.DataSource) && a.Outcome != QABoss103Outcome.Error)
            return ReasonDataSource;
        string r = (a.ErrorReason ?? "").Trim();
        int cut = r.IndexOfAny(new[] { ' ', ':', '(' });
        string head = cut > 0 ? r.Substring(0, cut) : r;
        if (a.Outcome == QABoss103Outcome.Aborted)
            return "aborted";
        return head.Length > 0 ? head : a.OutcomeText.Length > 0 ? a.OutcomeText.ToLowerInvariant() : "unknown";
    }

    // ── 남은 판·재실행 ──────────────────────────────────────

    /// <summary>runs.csv → 시도 목록(파일 순서). 모르는 열은 무시, 판 번호가 없는 행은 건너뛴다.</summary>
    public static List<QABoss103Attempt> ReadAttempts(QABoss103CsvTable runs)
    {
        var list = new List<QABoss103Attempt>();
        if (runs == null)
            return list;
        for (int i = 0; i < runs.Rows.Count; i++)
        {
            string[] row = runs.Rows[i];
            if (!int.TryParse(runs.Get(row, "run"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int run))
                continue;
            int.TryParse(runs.Get(row, "retry"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int retry);
            string outcome = runs.Get(row, "outcome");
            var a = new QABoss103Attempt
            {
                Run = run,
                Retry = retry,
                OutcomeText = outcome,
                Outcome = Enum.TryParse(outcome, true, out QABoss103Outcome o) && Enum.IsDefined(typeof(QABoss103Outcome), o) && !int.TryParse(outcome, out _) ? o : QABoss103Outcome.None,
                ErrorReason = runs.Get(row, "error_reason"),
                DataSource = runs.Get(row, "data_source"),
                Index = i,
            };
            list.Add(a);
        }
        return list;
    }

    /// <summary>
    /// (판, 재실행) 한 칸의 대표 시도 — 같은 칸에 행이 여럿이면(루프가 Error 를 쓴 뒤 호스트가 늦게 쓴 경우 등)
    /// 마지막 정상 행, 없으면 마지막 행. 없으면 null.
    /// </summary>
    public static QABoss103Attempt Effective(IReadOnlyList<QABoss103Attempt> attempts, int run, int retry)
    {
        QABoss103Attempt last = null, lastOk = null;
        foreach (QABoss103Attempt a in attempts)
        {
            if (a.Run != run || a.Retry != retry)
                continue;
            last = a;
            if (!a.IsError)
                lastOk = a;
        }
        return lastOk ?? last;
    }

    /// <summary>그 판의 최종 시도 — 재실행 행이 있으면 그것, 없으면 첫 실행. 둘 다 없으면 null.</summary>
    public static QABoss103Attempt Final(IReadOnlyList<QABoss103Attempt> attempts, int run)
    {
        for (int r = MaxRetry; r >= 0; r--)
        {
            QABoss103Attempt a = Effective(attempts, run, r);
            if (a != null)
                return a;
        }
        return null;
    }

    /// <summary>
    /// 다음에 돌릴 (판, 재실행). 1) 첫 실행 행이 없는 가장 작은 판(재실행 0) → 2) 90판이 다 있으면 첫 실행이 오류이고 재실행 행이 없는 가장 작은 판(재실행 1).
    /// 둘 다 없으면 false(배치 끝). 중간에 멈췄다 다시 시작해도 같은 규칙으로 이어 간다.
    /// </summary>
    public static bool TryNext(IReadOnlyList<QABoss103Attempt> attempts, int total, out int run, out int retry)
    {
        for (run = 1; run <= total; run++)
            if (Effective(attempts, run, 0) == null)
            {
                retry = 0;
                return true;
            }
        for (run = 1; run <= total; run++)
        {
            QABoss103Attempt first = Effective(attempts, run, 0);
            if (first != null && first.IsError && Effective(attempts, run, 1) == null)
            {
                retry = 1;
                return true;
            }
        }
        run = 0;
        retry = 0;
        return false;
    }

    /// <summary>재실행 대상(첫 실행이 오류인 판) 목록 — 오름차순.</summary>
    public static List<int> RetryList(IReadOnlyList<QABoss103Attempt> attempts, int total)
    {
        var list = new List<int>();
        for (int run = 1; run <= total; run++)
        {
            QABoss103Attempt first = Effective(attempts, run, 0);
            if (first != null && first.IsError)
                list.Add(run);
        }
        return list;
    }

    /// <summary>진행 요약: 첫 실행을 마친 판 수 · 재실행한 판 수 · 최종 오류 판 수 · 통계에 들어가는 판 수.</summary>
    public static void Summarize(IReadOnlyList<QABoss103Attempt> attempts, int total, out int done, out int retried, out int errors, out int valid)
    {
        done = retried = errors = valid = 0;
        for (int run = 1; run <= total; run++)
        {
            if (Effective(attempts, run, 0) == null)
                continue;
            done++;
            if (Effective(attempts, run, 1) != null)
                retried++;
            QABoss103Attempt f = Final(attempts, run);
            if (f.IsError)
                errors++;
            else
                valid++;
        }
    }

    /// <summary>이 행이 그 판의 통계용 최종 시도인가(오류 아님).</summary>
    public static bool IsValidFinal(IReadOnlyList<QABoss103Attempt> attempts, int run, int retry)
    {
        QABoss103Attempt f = Final(attempts, run);
        return f != null && !f.IsError && f.Retry == retry;
    }

    // ── 진행 감시 ────────────────────────────────────────────

    /// <summary>
    /// 판 진행 중 오류 판정. null = 계속. 우선순위: Combat 전 8분(start_timeout) → Combat 30분+3분(end_timeout) →
    /// 호스트 심장박동 120초(heartbeat, 박동 전엔 Play 진입부터) → 클론 심장박동 120초(heartbeat_client).
    /// </summary>
    public static string Judge(QABoss103Watch w)
    {
        bool combat = !double.IsNaN(w.SinceCombat);
        if (!combat && w.SincePlay > StartTimeoutSeconds)
            return ReasonStartTimeout;
        if (combat && w.SinceCombat > QABoss103Plan.CombatLimitSeconds + EndGraceSeconds)
            return ReasonEndTimeout;
        double hostAge = double.IsNaN(w.SinceHostBeat) ? w.SincePlay : w.SinceHostBeat;
        if (hostAge > HeartbeatTimeoutSeconds)
            return ReasonHeartbeat;
        if (!double.IsNaN(w.SinceOldestCloneBeat) && w.SinceOldestCloneBeat > HeartbeatTimeoutSeconds)
            return ReasonHeartbeatClient;
        return null;
    }

    /// <summary>판 사이 쉬는 시간(초). 오류면 10초, 연속 오류가 3의 배수가 되면 60초(멈추지 않는다), 정상이면 3초.</summary>
    public static int CooldownSeconds(bool error, int errorStreak)
    {
        if (!error)
            return NormalCooldownSeconds;
        return IsStreakPause(errorStreak) ? ErrorStreakPauseSeconds : ErrorCooldownSeconds;
    }

    public static bool IsStreakPause(int errorStreak) => errorStreak > 0 && errorStreak % ErrorStreakPause == 0;

    // ── 데이터 출처 ──────────────────────────────────────────

    public const string SourceTable = "table";
    public const string SourceInspector = "inspector";
    /// <summary>설정은 테이블인데 Play 진입 때 테이블 값이 메모리에 적용되지 않았다(xlsx 없음·진입 경로 이탈).</summary>
    public const string SourceTableUnapplied = "table_unapplied";

    public static bool IsTable(string source) => string.Equals((source ?? "").Trim(), SourceTable, StringComparison.Ordinal);

    /// <summary>
    /// 런타임 실제 적용 경로 기준 출처. <paramref name="playSnapshotJson"/> = <c>DataSourcePlayMode</c> 가 테이블 Play 진입 때
    /// 값을 메모리에 덮어쓰며 SessionState 에 남기는 스냅샷(덮어쓴 대상이 1개 이상이면 <c>instanceId</c> 가 있다).
    /// 스냅샷에 대상이 있으면 table, 없으면 설정이 인스펙터면 inspector, 아니면 table_unapplied.
    /// </summary>
    public static string SourceFromRuntime(bool prefInspector, string playSnapshotJson)
    {
        if (!string.IsNullOrEmpty(playSnapshotJson) && playSnapshotJson.IndexOf("\"instanceId\"", StringComparison.Ordinal) >= 0)
            return SourceTable;
        return prefInspector ? SourceInspector : SourceTableUnapplied;
    }

    /// <summary>
    /// 호스트 + 클론 출처 → runs.csv data_source. 호스트가 테이블이고 클론이 전부 테이블(또는 모름)이면 <c>table</c>,
    /// 아니면 <c>host=…;client-…=…</c>(테이블 아님 → 그 판 오류).
    /// </summary>
    public static string CombineSources(string host, IEnumerable<KeyValuePair<string, string>> clones)
    {
        bool ok = IsTable(host);
        var sb = new StringBuilder("host=").Append(string.IsNullOrEmpty(host) ? "na" : host);
        if (clones != null)
            foreach (KeyValuePair<string, string> kv in clones)
            {
                string v = string.IsNullOrEmpty(kv.Value) ? "na" : kv.Value;
                if (v != "na" && !IsTable(v))
                    ok = false;
                sb.Append(';').Append(kv.Key).Append('=').Append(v);
            }
        return ok ? SourceTable : sb.ToString();
    }

    // ── 파일 ─────────────────────────────────────────────────

    public const string BeatFolder = "hb";
    public const string StateFile = "progress.txt";
    public const string BatchFile = "batch.txt";
    public const string FixesFile = "fixes.md";
    public const string ReportFile = "report.html";
    public const string LogFolder = "logs";
    public const string LogFile = "loop.log";

    /// <summary>배치 폴더 이름 = <c>103-yyyyMMdd_HHmm</c>.</summary>
    public static string BatchFolderName(DateTime at) => "103-" + at.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

    /// <summary>심장박동 파일 — <c>&lt;배치&gt;/hb/run&lt;n&gt;_r&lt;retry&gt;_&lt;역할&gt;.txt</c>(역할 = host · client-mppm…).</summary>
    public static string BeatPath(string outDir, int run, int retry, string role) =>
        Path.Combine(outDir, BeatFolder, $"run{run}_r{retry}_{QABoss103Plan.Token(role)}.txt");

    public static string BeatPrefix(int run, int retry) => $"run{run}_r{retry}_";

    /// <summary>key=value 줄 파일(progress·batch·심장박동) 읽기. 모르는 줄은 무시.</summary>
    public static Dictionary<string, string> ParseKeyValues(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
            return d;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return d;
    }

    public static string FormatKeyValues(IEnumerable<KeyValuePair<string, string>> kvs)
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, string> kv in kvs)
            sb.Append(kv.Key).Append('=').Append((kv.Value ?? "").Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
        return sb.ToString();
    }

    public static string Ticks(DateTime utc) => utc.Ticks.ToString(CultureInfo.InvariantCulture);

    /// <summary>UTC 틱 문자열 → 시각. 비었거나 0 이면 null.</summary>
    public static DateTime? FromTicks(string s)
    {
        if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t) || t <= 0)
            return null;
        return new DateTime(t, DateTimeKind.Utc);
    }

    /// <summary><paramref name="since"/> 부터 <paramref name="now"/> 까지 초. since 가 null 이면 NaN.</summary>
    public static double Seconds(DateTime? since, DateTime now) => since.HasValue ? (now - since.Value).TotalSeconds : double.NaN;
}

/// <summary>할 일 103 C6 — CSV 읽기(RFC 4180: 따옴표·두 번 따옴표·칸 안 줄바꿈, UTF-8 BOM 제거). 첫 행 = 머리.</summary>
public sealed class QABoss103CsvTable
{
    public string[] Header = Array.Empty<string>();
    public readonly List<string[]> Rows = new List<string[]>();
    private readonly Dictionary<string, int> _col = new Dictionary<string, int>(StringComparer.Ordinal);

    public bool Has(string column) => _col.ContainsKey(column);

    /// <summary>열 값. 열이 없거나 행이 짧으면 빈칸.</summary>
    public string Get(string[] row, string column) =>
        row != null && _col.TryGetValue(column, out int i) && i < row.Length ? row[i] ?? "" : "";

    /// <summary>숫자 값. 비었거나 숫자가 아니면 NaN.</summary>
    public double Num(string[] row, string column) =>
        double.TryParse(Get(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    public static QABoss103CsvTable Parse(string text)
    {
        var t = new QABoss103CsvTable();
        if (string.IsNullOrEmpty(text))
            return t;
        if (text[0] == '﻿')
            text = text.Substring(1);
        List<List<string>> records = Records(text);
        if (records.Count == 0)
            return t;
        t.Header = records[0].ToArray();
        for (int i = 0; i < t.Header.Length; i++)
            if (!t._col.ContainsKey(t.Header[i]))
                t._col[t.Header[i]] = i;
        for (int r = 1; r < records.Count; r++)
        {
            List<string> rec = records[r];
            if (rec.Count == 1 && rec[0].Length == 0)
                continue;   // 빈 줄
            t.Rows.Add(rec.ToArray());
        }
        return t;
    }

    private static List<List<string>> Records(string text)
    {
        var records = new List<List<string>>();
        var cur = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                        quoted = false;
                }
                else
                    cell.Append(c);
                continue;
            }
            if (c == '"')
            {
                quoted = true;
                any = true;
            }
            else if (c == ',')
            {
                cur.Add(cell.ToString());
                cell.Clear();
                any = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                cur.Add(cell.ToString());
                cell.Clear();
                records.Add(cur);
                cur = new List<string>();
                any = false;
            }
            else
            {
                cell.Append(c);
                any = true;
            }
        }
        if (any || cell.Length > 0)
        {
            cur.Add(cell.ToString());
            records.Add(cur);
        }
        return records;
    }
}
