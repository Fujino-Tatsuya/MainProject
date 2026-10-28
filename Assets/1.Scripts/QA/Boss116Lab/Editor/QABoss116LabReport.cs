#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩 배치 폴더(<c>C:/UnityProject/QA/116lab-…</c>)의 판 끝 CSV 만들기와 report.html.
/// <list type="bullet">
/// <item><see cref="AppendRun"/> — 판 1회가 끝나면 그 판 판 파일(봇 <c>parts/run&lt;n&gt;_r0_s&lt;자리&gt;.txt</c> + 랩 <c>…_lab.txt</c>)을 읽어
///   <c>lab_attempts.csv</c>·<c>lab_windows.csv</c>·<c>lab_dashes.csv</c>·<c>dash_predict.csv</c>(116 과 같은 형식)·<c>lab_runs.csv</c> 에 붙인다.</item>
/// <item><see cref="Write"/> — CSV 를 읽어 요약(<see cref="QABoss116Lab.Summarize"/>)을 HTML 로.</item>
/// </list>
/// </summary>
public static class QABoss116LabReport
{
    public const string RunsFile = "lab_runs.csv";
    public const string ReportFile = "report.html";

    public static readonly string[] RunsHeader =
    {
        "run", "retry", "tier", "seed", "status", "end_reason", "character", "attempts", "elapsed_s", "windows", "dashes",
        "boss_heals", "player_heals", "cd_resets", "clients", "note", "started_at", "ended_at",
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>판 1회 결과를 CSV 들에 붙인다. 쓴 시도 행 수. <paramref name="tier"/> = 판 열 tier 값(비우면 봇 단계 — 수동 모드는 "manual").</summary>
    public static int AppendRun(string dir, QABoss103Config cfg, string status, string note, string startedAt, string tier = null)
    {
        var lines = new List<string>();
        for (int s = 0; s < 3; s++)
            AddLines(lines, QABoss103PartFile.PartPath(dir, cfg.Run, cfg.Retry, s));
        var lab = new List<string>();
        AddLines(lab, QABoss116Lab.LabPartPath(dir, cfg.Run, cfg.Retry));
        lines.AddRange(lab);

        string[] key = { cfg.Run.ToString(Inv), cfg.Retry.ToString(Inv), string.IsNullOrEmpty(tier) ? cfg.BotTier.ToString() : tier, cfg.Seed.ToString(Inv) };
        List<string[]> attempts = QABoss116Lab.BuildAttempts(key, lines);
        QABoss103Csv.Append(Path.Combine(dir, QABoss116Lab.AttemptsFile), QABoss116Lab.AttemptsHeader, attempts);
        QABoss103Csv.Append(Path.Combine(dir, QABoss116Lab.WindowsFile), QABoss116Lab.WindowsHeader, QABoss116Lab.BuildWindows(key, lines));
        QABoss103Csv.Append(Path.Combine(dir, QABoss116Lab.DashesFile), QABoss116Lab.DashesHeader, QABoss116Lab.BuildDashes(key, lines));
        List<string[]> dp = QABoss116DashPredict.BuildRows(key, lines);
        if (dp.Count > 0)
            QABoss103Csv.Append(Path.Combine(dir, QABoss116DashPredict.File), QABoss116DashPredict.Header, dp);

        Dictionary<string, string> end = LastOf(lab, QABoss116Lab.EndKind);
        Dictionary<string, string> start = LastOf(lab, "labrun");
        var row = new string[RunsHeader.Length];
        for (int i = 0; i < row.Length; i++)
            row[i] = "";
        void Set(string col, string v) => row[Array.IndexOf(RunsHeader, col)] = v ?? "";
        Set("run", key[0]);
        Set("retry", key[1]);
        Set("tier", key[2]);
        Set("seed", key[3]);
        Set("status", status);
        Set("end_reason", QABoss116Lab.Get(end, "reason"));
        Set("character", QABoss116Lab.Get(start, "character"));
        Set("attempts", attempts.Count.ToString(Inv));
        Set("elapsed_s", QABoss116Lab.Get(end, "elapsedS"));
        Set("windows", QABoss116Lab.Get(end, "windows"));
        Set("dashes", QABoss116Lab.Get(end, "dashes"));
        Set("boss_heals", QABoss116Lab.Get(end, "bossHeals"));
        Set("player_heals", QABoss116Lab.Get(end, "playerHeals"));
        Set("cd_resets", QABoss116Lab.Get(end, "cdResets"));
        Set("clients", QABoss116Lab.Get(start, "clients"));
        Set("note", note);
        Set("started_at", startedAt);
        Set("ended_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        QABoss103Csv.Append(Path.Combine(dir, RunsFile), RunsHeader, new[] { row });
        return attempts.Count;
    }

    private static void AddLines(List<string> into, string path)
    {
        try
        {
            List<string> l = QABoss103PartFile.ReadShared(path);
            if (l != null)
                into.AddRange(l);
        }
        catch { /* 쓰는 중·잠김 — 그 파일만 빠진다 */ }
    }

    private static Dictionary<string, string> LastOf(List<string> lines, string kind)
    {
        Dictionary<string, string> last = null;
        foreach (string l in lines)
            if (QABoss103PartFile.TryParse(l, out string k, out Dictionary<string, string> kv) && k == kind)
                last = kv;
        return last;
    }

    // ── report.html ──────────────────────────────────────────

    public static string Write(string dir, string loopState)
    {
        string html = Build(dir, loopState);
        string path = Path.Combine(dir, ReportFile);
        File.WriteAllText(path, html, new UTF8Encoding(false));
        return path;
    }

    private static QABoss103CsvTable Table(string dir, string file)
    {
        string path = Path.Combine(dir, file);
        try { return File.Exists(path) ? QABoss103CsvTable.Parse(File.ReadAllText(path, Encoding.UTF8)) : new QABoss103CsvTable(); }
        catch { return new QABoss103CsvTable(); }
    }

    public static string Build(string dir, string loopState)
    {
        QABoss103CsvTable runs = Table(dir, RunsFile);
        QABoss103CsvTable attempts = Table(dir, QABoss116Lab.AttemptsFile);
        QABoss103CsvTable windows = Table(dir, QABoss116Lab.WindowsFile);
        QABoss103CsvTable dashes = Table(dir, QABoss116Lab.DashesFile);
        string batchText = "";
        try { batchText = File.ReadAllText(Path.Combine(dir, QABoss103Batch.BatchFile)); } catch { }
        Dictionary<string, string> batch = QABoss103Batch.ParseKeyValues(batchText);
        QABoss116Lab.Summary s = QABoss116Lab.Summarize(attempts, windows);
        bool manual = Val(batch, "mode", "") == "manual";   // 할 일 116-C5 수동 모드(사람 입력)

        var sb = new StringBuilder(32 * 1024);
        sb.Append("<!DOCTYPE html>\n<html lang=\"ko\"><head><meta charset=\"utf-8\"><title>116 대쉬 간파 예측 랩</title><style>").Append(Css).Append("</style></head><body><main>\n");
        sb.Append(manual
                ? "<h1>116 대쉬 간파 예측 랩 — 수동 모드(사람 입력) · 23호 랩 Variant</h1>\n<div class=\"sub\">캐릭터 " + H(Val(batch, "character", "?")) +
                  " · " + H(Val(batch, "started_at", "?")) + " ~ " + H(Val(batch, "ended_at", "?")) + "<br>"
                : "<h1>116 대쉬 간파 예측 랩 — 호스트 1명 · 23호 랩 Variant</h1>\n<div class=\"sub\">")
          .Append(H(dir)).Append(" · 상태 ").Append(H(loopState ?? "")).Append(" · 판 ").Append(runs.Rows.Count).Append('/').Append(H(Val(batch, "total", "?")))
          .Append(" · git ").Append(H(Val(batch, "git_commit", "?"))).Append(" (").Append(H(Val(batch, "git_branch", "?"))).Append(")")
          .Append(" · SVN r").Append(H(Val(batch, "svn_rev", "?"))).Append(" · xlsx r").Append(H(Val(batch, "xlsx_svn_rev", "?")))
          .Append(" · 데이터 출처 ").Append(H(Val(batch, "data_source_mode", "?"))).Append(" · 판 ").Append(H(Val(batch, "run_s", "?"))).Append("초/시도 ")
          .Append(H(Val(batch, "max_attempts", "?"))).Append("회 상한</div>\n");

        QABoss116Lab.Bucket a = s.All;
        sb.Append("<div class=\"kpis\">");
        Kpi(sb, a.Attempts.ToString(Inv), manual ? "사람 대쉬 간파 시도" : "봇 대쉬 간파 시도");
        Kpi(sb, a.Casts.ToString(Inv), "서버 시전(예측 줄)");
        Kpi(sb, Pct(a.Accuracy), $"예측 적중률(판정 {a.Judged}건)");
        Kpi(sb, a.Shortened.ToString(Inv), "어긋남 — 대시 막힘(dash_shortened)");
        Kpi(sb, a.Blocked.ToString(Inv), "대시 궤적 막힘(≥0.3m 덜 감)");
        Kpi(sb, $"{s.WindowsWithAttempt}/{s.Windows}", "시도가 든 카운터 창");
        Kpi(sb, double.IsNaN(s.WindowMedianS) ? "–" : s.WindowMedianS.ToString("0.00", Inv) + "s", "카운터 창 길이 중앙값");
        sb.Append("</div>\n");

        DistanceTable(sb, s);
        StopReasonTable(sb, s);
        sb.Append("<h2>순서별</h2>\n");
        BucketTable(sb, "순서", s.ByOrder);
        if (manual)
        {
            sb.Append("<h2>카운터 창 안 vs 밖(첫 입력 순간 23호 창)</h2>\n");
            BucketTable(sb, "창", s.ByCounter);
        }

        sb.Append("<h2>").Append(manual ? "사람" : "봇").Append(" 시도 결과 사유 → 상세 원인</h2>\n<table><thead><tr><th class=\"l\">결과</th><th class=\"l\">상세 원인</th><th>건수</th></tr></thead><tbody>\n");
        foreach (KeyValuePair<string, SortedDictionary<string, int>> o in s.Causes)
            foreach (KeyValuePair<string, int> c in o.Value)
                sb.Append("<tr><td class=\"l\">").Append(H(o.Key)).Append("</td><td class=\"l\">").Append(H(c.Key)).Append("</td><td>").Append(c.Value).Append("</td></tr>\n");
        sb.Append("</tbody></table>\n");

        sb.Append("<h2>대시 막힌 충돌체</h2>\n");
        if (s.Blockers.Count == 0)
            sb.Append("<div class=\"muted\">막힌 대시 없음</div>\n");
        else
        {
            sb.Append("<table><thead><tr><th class=\"l\">충돌체</th><th>건수</th></tr></thead><tbody>\n");
            foreach (KeyValuePair<string, int> b in s.Blockers)
                sb.Append("<tr><td class=\"l\">").Append(H(b.Key)).Append("</td><td>").Append(b.Value).Append("</td></tr>\n");
            sb.Append("</tbody></table>\n");
        }

        sb.Append("<h2>dash_shortened·덜 간 대시 행(의도 vs 실제)</h2>\n<table><thead><tr><th>판</th><th>#</th><th class=\"l\">순서</th><th class=\"l\">캐릭터</th>" +
                  "<th class=\"l\">진입 전</th><th>전환</th><th>의도 m</th><th>실제 m</th><th>덜 감 m</th><th>판정 덜 감 m</th><th class=\"l\">끊긴 이유</th><th class=\"l\">근거</th>" +
                  "<th class=\"l\">다음 상태</th><th>틱</th><th>요청/적용 m</th><th>첫 이동 프레임(초)</th><th>이동/전체 프레임</th><th class=\"l\">충돌체(레이어)</th></tr></thead><tbody>\n");
        int shown = 0;
        foreach (string[] r in attempts.Rows)
        {
            if (attempts.Get(r, "mismatch") != "dash_shortened" && attempts.Get(r, "dash_blocked") != "true")
                continue;
            if (++shown > 200)
                break;
            sb.Append("<tr><td>").Append(H(attempts.Get(r, "run"))).Append("</td><td>").Append(H(attempts.Get(r, "n"))).Append("</td><td class=\"l\">")
              .Append(H(attempts.Get(r, "order"))).Append("</td><td class=\"l\">").Append(H(attempts.Get(r, "character"))).Append("</td><td class=\"l\">")
              .Append(H(attempts.Get(r, "dash_prev_frame"))).Append("</td><td>").Append(H(attempts.Get(r, "dash_transfer"))).Append("</td><td>")
              .Append(H(attempts.Get(r, "dash_intent_m"))).Append("</td><td>").Append(H(attempts.Get(r, "dash_actual_m"))).Append("</td><td>")
              .Append(H(attempts.Get(r, "dash_short_m"))).Append("</td><td>").Append(H(attempts.Get(r, "shortfall_m"))).Append("</td><td class=\"l\">")
              .Append(H(attempts.Get(r, "dash_stop_reason"))).Append("</td><td class=\"l\">").Append(H(attempts.Get(r, "dash_stop_detail"))).Append(' ')
              .Append(H(attempts.Get(r, "dash_caller"))).Append("</td><td class=\"l\">").Append(H(attempts.Get(r, "dash_next_state"))).Append("</td><td>")
              .Append(H(attempts.Get(r, "dash_ticks"))).Append("</td><td>").Append(H(attempts.Get(r, "dash_req_m"))).Append('/').Append(H(attempts.Get(r, "dash_applied_m")))
              .Append("</td><td>").Append(H(attempts.Get(r, "dash_first_move_frame"))).Append(" (").Append(H(attempts.Get(r, "dash_first_move_s"))).Append(")</td><td>")
              .Append(H(attempts.Get(r, "dash_move_frames"))).Append('/').Append(H(attempts.Get(r, "dash_frames"))).Append("</td><td class=\"l\">")
              .Append(H(attempts.Get(r, "dash_block_collider"))).Append(" (").Append(H(attempts.Get(r, "dash_block_layer"))).Append(")</td></tr>\n");
        }
        sb.Append("</tbody></table>\n");

        sb.Append("<h2>판</h2>\n<table><thead><tr>");
        foreach (string h in RunsHeader)
            sb.Append("<th>").Append(H(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (string[] r in runs.Rows)
        {
            sb.Append("<tr>");
            foreach (string h in RunsHeader)
                sb.Append("<td>").Append(H(runs.Get(r, h))).Append("</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody></table>\n");
        sb.Append("<footer>생성 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv)).Append(" · 대시 궤적 ").Append(dashes.Rows.Count)
          .Append("회 · CSV: lab_attempts·lab_windows·lab_dashes·dash_predict·lab_runs</footer>\n</main></body></html>\n");
        return sb.ToString();
    }

    /// <summary>116-C6 — 순서(dash_rmb = 대시→우클릭, rmb_dash = 우클릭→대시)×캐릭터별 의도 vs 실제 거리(묶인 대시 평균).</summary>
    private static void DistanceTable(StringBuilder sb, QABoss116Lab.Summary s)
    {
        sb.Append("<h2>순서×캐릭터별 의도 vs 실제 거리</h2>\n<div class=\"sub\">dash_rmb = 대시→우클릭, rmb_dash = 우클릭→대시. 의도 = 대시 속도 × 지속, 실제 = 시작→끝을 대시 방향에 투영. " +
                  "덜 감 = 덜 간 거리 ≥ ").Append(QABoss116Lab.ShortfallMeters.ToString("0.0", Inv)).Append("m 인 대시 수. 전환 = 우클릭→대시로 진행 중 간파가 대시에 실린 수.</div>\n");
        if (s.ByOrderCharacter.Count == 0)
        {
            sb.Append("<div class=\"muted\">시도 없음</div>\n");
            return;
        }
        sb.Append("<table><thead><tr><th class=\"l\">순서 × 캐릭터</th><th>시도</th><th>대시 묶임</th><th>전환</th><th>평균 의도 m</th><th>평균 실제 m</th>" +
                  "<th>평균 덜 감 m</th><th>덜 감</th><th class=\"l\">끊긴 이유</th></tr></thead><tbody>\n");
        foreach (KeyValuePair<string, QABoss116Lab.DistBucket> kv in s.ByOrderCharacter)
        {
            QABoss116Lab.DistBucket b = kv.Value;
            sb.Append("<tr><td class=\"l\">").Append(H(kv.Key)).Append("</td><td>").Append(b.Attempts).Append("</td><td>").Append(b.Dashes)
              .Append("</td><td>").Append(b.Transfers).Append("</td><td>").Append(M(b.MeanIntent)).Append("</td><td>").Append(M(b.MeanActual))
              .Append("</td><td>").Append(M(b.MeanShort)).Append("</td><td>").Append(b.Shortened).Append("</td><td class=\"l\">").Append(H(Join(b.Reasons)))
              .Append("</td></tr>\n");
        }
        sb.Append("</tbody></table>\n");
    }

    /// <summary>116-C6 — 대시 끊긴 이유 상위(묶인 대시, 많은 순) · 순서별 수 · 코드 근거.</summary>
    private static void StopReasonTable(StringBuilder sb, QABoss116Lab.Summary s)
    {
        sb.Append("<h2>대시 끊긴 이유 상위</h2>\n<div class=\"sub\">게임 [Dash] 로그(진입·종료·유닛 막힘·서버 취소)와 프레임 관측으로 분류 — 우선순위는 Docs/tech/dash-interrupt.md §13.1. " +
                  "이동 잠금·루트모션·상체 레이어·슬로우는 대시 이동 계산에 안 들어가 이유가 아니라 CSV 참고 열(dash_can_move_prev·dash_root_motion·dash_upper_w·dash_min_ts).</div>\n");
        if (s.StopReasons.Count == 0)
        {
            sb.Append("<div class=\"muted\">묶인 대시 없음</div>\n");
            return;
        }
        var rows = new List<KeyValuePair<string, SortedDictionary<string, int>>>(s.StopReasons);
        int Total(SortedDictionary<string, int> d) { int n = 0; foreach (int v in d.Values) n += v; return n; }
        rows.Sort((x, y) => Total(y.Value) != Total(x.Value) ? Total(y.Value).CompareTo(Total(x.Value)) : string.CompareOrdinal(x.Key, y.Key));
        sb.Append("<table><thead><tr><th class=\"l\">이유</th><th>합계</th><th>대시→우클릭</th><th>우클릭→대시</th><th>그 밖</th><th class=\"l\">설명 — 코드 근거</th></tr></thead><tbody>\n");
        foreach (KeyValuePair<string, SortedDictionary<string, int>> kv in rows)
        {
            int dashRmb = kv.Value.TryGetValue("dash_rmb", out int a) ? a : 0;
            int rmbDash = kv.Value.TryGetValue("rmb_dash", out int b) ? b : 0;
            int total = Total(kv.Value);
            sb.Append("<tr><td class=\"l\">").Append(H(kv.Key)).Append("</td><td>").Append(total).Append("</td><td>").Append(dashRmb).Append("</td><td>")
              .Append(rmbDash).Append("</td><td>").Append(total - dashRmb - rmbDash).Append("</td><td class=\"l\">").Append(H(QABoss116DashStop.Describe(kv.Key)))
              .Append("</td></tr>\n");
        }
        sb.Append("</tbody></table>\n");
    }

    private static string M(double v) => double.IsNaN(v) ? "–" : v.ToString("0.00", Inv);

    private static void BucketTable(StringBuilder sb, string label, SortedDictionary<string, QABoss116Lab.Bucket> d)
    {
        sb.Append("<table><thead><tr><th class=\"l\">").Append(H(label)).Append("</th><th>시도</th><th>시전</th><th>판정</th><th>적중</th><th>적중률</th>" +
                  "<th>dash_shortened</th><th>궤적 막힘</th><th class=\"l\">결과 사유</th><th class=\"l\">어긋남 이유</th></tr></thead><tbody>\n");
        foreach (KeyValuePair<string, QABoss116Lab.Bucket> kv in d)
        {
            QABoss116Lab.Bucket b = kv.Value;
            sb.Append("<tr><td class=\"l\">").Append(H(kv.Key)).Append("</td><td>").Append(b.Attempts).Append("</td><td>").Append(b.Casts)
              .Append("</td><td>").Append(b.Judged).Append("</td><td>").Append(b.Match).Append("</td><td>").Append(Pct(b.Accuracy))
              .Append("</td><td>").Append(b.Shortened).Append("</td><td>").Append(b.Blocked).Append("</td><td class=\"l\">").Append(H(Join(b.Outcomes)))
              .Append("</td><td class=\"l\">").Append(H(Join(b.Mismatch))).Append("</td></tr>\n");
        }
        sb.Append("</tbody></table>\n");
    }

    private static string Join(SortedDictionary<string, int> d)
    {
        var parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in d)
            parts.Add(kv.Key + " " + kv.Value);
        return string.Join(", ", parts);
    }

    private static void Kpi(StringBuilder sb, string value, string label) =>
        sb.Append("<div class=\"kpi\"><b>").Append(H(value)).Append("</b><span>").Append(H(label)).Append("</span></div>");

    private static string Pct(double v) => double.IsNaN(v) ? "–" : (v * 100.0).ToString("0.0", Inv) + "%";

    private static string Val(Dictionary<string, string> d, string k, string fallback) =>
        d.TryGetValue(k, out string v) && v.Length > 0 ? v : fallback;

    private static string H(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private const string Css = @"
body{margin:0;background:#fcfcfb;color:#0b0b0b;font-family:'Malgun Gothic',system-ui,sans-serif;font-size:14px;line-height:1.5}
main{max-width:1280px;margin:0 auto;padding:24px}h1{font-size:22px;margin:0 0 4px}h2{font-size:17px;margin:26px 0 8px;border-bottom:1px solid #e4e3df;padding-bottom:4px}
.sub{color:#52514e}.muted{color:#8a8984}.kpis{display:flex;flex-wrap:wrap;gap:10px;margin:12px 0}
.kpi{background:#fff;border:1px solid #e4e3df;border-radius:8px;padding:8px 12px;min-width:130px}.kpi b{display:block;font-size:20px}.kpi span{color:#52514e;font-size:12px}
table{border-collapse:collapse;background:#fff;margin:6px 0 12px;font-size:13px}th,td{border:1px solid #e4e3df;padding:4px 8px;text-align:right;vertical-align:top}
th{background:#f5f4f1}.l{text-align:left}footer{margin-top:24px;color:#8a8984;font-size:12px}
@media (prefers-color-scheme:dark){body{background:#1a1a19;color:#f0efec}.kpi,table{background:#222221}th{background:#2c2c2a}th,td,.kpi{border-color:#3a3a38}.sub{color:#c3c2b7}}";
}
#endif
