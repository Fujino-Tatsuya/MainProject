using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// QA 세션 결과를 로컬 리포트 파일로 남긴다.
/// 경로: &lt;repo&gt;/Docs/temp/qa-runs/&lt;timestamp&gt;/  (git 비추적, .gitignore 등록됨)
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
            string runDir = Path.Combine(RunsRoot(), Timestamp());
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

    private static string BuildMarkdown(QARecorder recorder, string sessionSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# QA 자동화 세션 리포트");
        sb.AppendLine();
        sb.AppendLine($"- 생성: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Unity: {Application.unityVersion}");
        sb.AppendLine($"- 요약: {sessionSummary}");
        sb.AppendLine();
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
            sb.AppendLine($"| {f.TimeSeconds:F1} | {f.Severity} | {f.Category} | {f.Scene} | {Escape(f.Summary)} | {f.Count} |");
        }

        if (findings.Count == 0)
            sb.AppendLine("| - | - | - | - | (발견 없음) | - |");

        return sb.ToString();
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

            sb.AppendLine($"[{f.TimeSeconds:F1}s][{f.Severity}][{f.Category}] (x{f.Count}) {f.Summary}");
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
