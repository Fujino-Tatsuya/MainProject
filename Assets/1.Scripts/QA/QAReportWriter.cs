using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// QA 세션 결과를 로컬 리포트 파일로 남긴다.
/// 기본 경로: &lt;repo&gt;/Docs/temp/qa-runs/&lt;timestamp&gt;/  (git 비추적, .gitignore 등록됨)
/// QA 창(Tools/QA) ▸ 설정 ▸ 리포트 위치에서 시스템 절대 경로를 지정하면 그 폴더 아래에 쌓는다.
///   - report.md   : 심각도별 요약 + 발견 타임라인
///   - errors.log  : 스택트레이스 포함 원문
/// 에디터 PlayMode 전제. 빌드에서는 persistentDataPath로 폴백한다.
/// </summary>
public static class QAReportWriter
{
    /// <summary>리포트를 기록하고 생성된 run 디렉터리 경로를 반환한다(실패 시 null).</summary>
    public static string Write(QARecorder recorder, string sessionSummary)
    {
        try
        {
            string baseDir = Path.Combine(RunsRoot(), Timestamp());
            string runDir = baseDir;
            int suffix = 2;
            while (Directory.Exists(runDir))     // 같은 초에 여러 사이클이 끝나도 폴더 충돌 방지
                runDir = baseDir + "_" + suffix++;
            Directory.CreateDirectory(runDir);

            File.WriteAllText(Path.Combine(runDir, "report.md"),
                BuildMarkdown(recorder, sessionSummary), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(runDir, "errors.log"),
                BuildErrorLog(recorder), new UTF8Encoding(true));

            Debug.Log($"[QA] 리포트 저장: {runDir}");
            return runDir;
        }
        catch (Exception e)
        {
            Debug.LogError($"[QA] 리포트 저장 실패: {e.Message}");
            return null;
        }
    }

    public static string RunsRoot()
    {
#if UNITY_EDITOR
        string configured = QASettings.ReportRoot;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            configured = configured.Trim();
            // 절대 경로만 허용 — 상대 경로는 에디터의 작업 디렉터리 기준이라 엉뚱한 곳에 쌓인다.
            if (Path.IsPathRooted(configured))
                return configured;

            Debug.LogWarning($"[QA] 리포트 경로가 절대 경로가 아니라 무시하고 기본 경로를 씁니다: {configured}");
        }

        // <project>/Assets 의 상위 = repo 루트.
        string repoRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(repoRoot, "Docs", "temp", "qa-runs");
#else
        return Path.Combine(Application.persistentDataPath, "qa-runs");
#endif
    }

    private static string Timestamp()
    {
        return DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }

    // ── 시나리오 리포트(할 일 48) ─────────────────────────────────

    /// <summary>
    /// 시나리오 리포트 루트: 설정 경로(절대)가 있으면 그것, 없으면 저장소 옆 C:/UnityProject/QA(= repo 상위/QA).
    /// 에디터를 다시 켜도 지워지지 않고 git 밖이다.
    /// </summary>
    public static string ScenarioRoot()
    {
#if UNITY_EDITOR
        string configured = QASettings.ReportRoot;
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathRooted(configured.Trim()))
            return configured.Trim();
        string repoRoot = Directory.GetParent(Application.dataPath).FullName;
        string parent = Directory.GetParent(repoRoot)?.FullName ?? repoRoot;
        return Path.Combine(parent, "QA");
#else
        return Path.Combine(Application.persistentDataPath, "qa-runs");
#endif
    }

    /// <summary>"&lt;prefix&gt;-&lt;시각&gt;[-suffix]" 실행 폴더를 만든다. 실패하면 null.</summary>
    public static string ScenarioRunDir(string prefix, string suffix)
    {
        try
        {
            string name = $"{prefix}-{Timestamp()}" + (string.IsNullOrEmpty(suffix) ? string.Empty : "-" + suffix);
            string dir = Path.Combine(ScenarioRoot(), name);
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch (Exception e)
        {
            Debug.LogError($"[QA] 시나리오 리포트 폴더 생성 실패: {e.Message}");
            return null;
        }
    }

    /// <summary>항목별 PASS/FAIL/눈확인 표 + 탐지기 타임라인을 report.md 로, 에러 원문을 errors.log 로 (덮어)쓴다.</summary>
    public static void WriteScenarioReport(QAScenarioContext ctx, string[] scenarioIds, string status)
    {
        if (string.IsNullOrEmpty(ctx.RunDir))
            return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 할 일 48 로비 데모 QA 시나리오 리포트");
            sb.AppendLine();
            sb.AppendLine($"- 생성: {DateTime.Now:yyyy-MM-dd HH:mm:ss} · 상태: {status}");
            sb.AppendLine($"- 역할: {ctx.Role} · 인원: {ctx.ExpectedPlayers} · 시나리오: {string.Join(",", scenarioIds)}");
            sb.AppendLine($"- Unity: {Application.unityVersion} · 거친 씬: {string.Join(" → ", ctx.SceneLog)}");
            sb.AppendLine();
            ctx.Stats.AppendMarkdown(sb);

            sb.AppendLine("## 시나리오 요약");
            sb.AppendLine();
            sb.AppendLine("| 시나리오 | PASS | FAIL | 눈확인 | 건너뜀 | 판정 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|");
            foreach (string id in scenarioIds)
            {
                int pass = 0, fail = 0, eye = 0, skip = 0;
                foreach (QAScenarioItem it in ctx.Items)
                {
                    if (it.Scenario != id) continue;
                    switch (it.Status)
                    {
                        case QAItemStatus.Pass: pass++; break;
                        case QAItemStatus.Fail: fail++; break;
                        case QAItemStatus.Eye: eye++; break;
                        default: skip++; break;
                    }
                }
                string verdict = pass + fail + eye + skip == 0 ? "미실행" : fail > 0 ? "FAIL" : eye > 0 ? "PASS(눈확인 남음)" : "PASS";
                sb.AppendLine($"| {id} | {pass} | {fail} | {eye} | {skip} | {verdict} |");
            }
            sb.AppendLine();

            sb.AppendLine("## 항목");
            sb.AppendLine();
            sb.AppendLine("| 시각(s) | 시나리오 | 항목 | 결과 | 근거 | 스크린샷 |");
            sb.AppendLine("|---:|---|---|---|---|---|");
            foreach (QAScenarioItem it in ctx.Items)
            {
                string shot = string.IsNullOrEmpty(it.Screenshot) ? "" : $"[{Path.GetFileName(it.Screenshot)}]({it.Screenshot})";
                sb.AppendLine($"| {it.TimeSeconds:F1} | {it.Scenario} | {Escape(it.Name)} | {StatusLabel(it.Status)} | {Escape(it.Evidence)} | {shot} |");
            }
            if (ctx.Items.Count == 0)
                sb.AppendLine("| - | - | (항목 없음) | - | - | - |");
            sb.AppendLine();

            AppendExcluded(sb, ctx.Recorder);

            sb.AppendLine("## 탐지기(상시 감시) 집계");
            sb.AppendLine();
            AppendFindings(sb, ctx.Recorder);

            File.WriteAllText(Path.Combine(ctx.RunDir, "report.md"), sb.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(ctx.RunDir, "errors.log"), BuildErrorLog(ctx.Recorder), new UTF8Encoding(true));
        }
        catch (Exception e)
        {
            Debug.LogError($"[QA] 시나리오 리포트 저장 실패: {e.Message}");
        }
    }

    /// <summary>판정에서 뺀 발견(QAKnownNoise — 무관 노이즈·감지기 오탐 제외) 집계. FAIL 사유·에러 건수에 들어가지 않는다.</summary>
    private static void AppendExcluded(StringBuilder sb, QARecorder recorder)
    {
        sb.AppendLine("## 무관 노이즈·오탐 제외 집계 (판정 제외)");
        sb.AppendLine();
        sb.AppendLine("| 분류 | 요약 | 처음 | 횟수 |");
        sb.AppendLine("|---|---|---|---:|");
        int rows = 0, total = 0;
        foreach (QAFinding f in recorder.Findings)
        {
            if (!QAKnownNoise.IsExcluded(f))
                continue;
            rows++;
            total += f.Count;
            sb.AppendLine($"| {f.Category} | {Escape(f.Summary)} | {f.Scene} {f.TimeSeconds:F1}s | {f.Count} |");
        }
        if (rows == 0)
            sb.AppendLine("| - | (없음) | - | 0 |");
        sb.AppendLine();
        sb.AppendLine($"- 합계 {total}건 — 목록 원본: QAModel.cs `QAKnownNoise`");
        sb.AppendLine();
    }

    private static string StatusLabel(QAItemStatus s)
    {
        switch (s)
        {
            case QAItemStatus.Pass: return "✅ PASS";
            case QAItemStatus.Fail: return "❌ FAIL";
            case QAItemStatus.Eye: return "👀 눈확인";
            default: return "건너뜀";
        }
    }

    private static string BuildMarkdown(QARecorder recorder, string sessionSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# QA 자동화 세션 리포트");
        sb.AppendLine();
        sb.AppendLine($"- 생성: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Unity: {Application.unityVersion}");
        sb.AppendLine($"- 요약: {sessionSummary}");
        sb.AppendLine();
        AppendFindings(sb, recorder);
        return sb.ToString();
    }

    private static void AppendFindings(StringBuilder sb, QARecorder recorder)
    {
        sb.AppendLine("## 집계");
        sb.AppendLine();
        sb.AppendLine($"- Critical: {recorder.CountBySeverity(QASeverity.Critical)}");
        sb.AppendLine($"- Error: {recorder.CountBySeverity(QASeverity.Error)}");
        sb.AppendLine($"- Warning: {recorder.CountBySeverity(QASeverity.Warning)}");
        sb.AppendLine($"- Info: {recorder.CountBySeverity(QASeverity.Info)}");
        sb.AppendLine();
        sb.AppendLine("## 발견 타임라인");
        sb.AppendLine();
        sb.AppendLine("| 시각(s) | 심각도 | 분류 | 씬 | 요약 | 횟수 |");
        sb.AppendLine("|---:|---|---|---|---|---:|");

        var findings = recorder.Findings;
        for (int i = 0; i < findings.Count; i++)
        {
            QAFinding f = findings[i];
            // 최초 발생과 다른 씬/훨씬 뒤 시각까지 재발했다면 "1회성"으로 오해하지 않도록 병기한다
            // (dedup 특성상 Count>1이어도 최초 발생 씬·시각만 보이면 로딩 중 반짝 일었다 끝난 것처럼
            // 보일 수 있는데, 실제로는 본 게임플레이 씬에서 한참 뒤까지 재발했을 수 있다).
            string recurrence = f.Count > 1 && (f.LastScene != f.Scene || f.LastTimeSeconds - f.TimeSeconds > 1f)
                ? $" (최근 재발: {f.LastScene} {f.LastTimeSeconds:F1}s)"
                : string.Empty;
            sb.AppendLine($"| {f.TimeSeconds:F1} | {f.Severity} | {f.Category} | {f.Scene} | {Escape(f.Summary)}{recurrence} | {f.Count} |");
        }

        if (findings.Count == 0)
            sb.AppendLine("| - | - | - | - | (발견 없음) | - |");
    }

    private static string BuildErrorLog(QARecorder recorder)
    {
        var sb = new StringBuilder();
        var findings = recorder.Findings;
        for (int i = 0; i < findings.Count; i++)
        {
            QAFinding f = findings[i];
            if (f.Severity != QASeverity.Error && f.Severity != QASeverity.Critical)
                continue;

            string recurrence = f.Count > 1 && (f.LastScene != f.Scene || f.LastTimeSeconds - f.TimeSeconds > 1f)
                ? $" (최근 재발: {f.LastScene} {f.LastTimeSeconds:F1}s)"
                : string.Empty;
            sb.AppendLine($"[{f.TimeSeconds:F1}s][{f.Severity}][{f.Category}] (x{f.Count}){recurrence} {f.Summary}");
            if (!string.IsNullOrEmpty(f.Detail))
            {
                sb.AppendLine(f.Detail.TrimEnd());
            }
            sb.AppendLine("----");
        }

        if (sb.Length == 0)
            sb.AppendLine("(에러/크리티컬 없음)");

        return sb.ToString();
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        // 마크다운 표 셀 파손 방지: 파이프·개행 치환.
        return s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
