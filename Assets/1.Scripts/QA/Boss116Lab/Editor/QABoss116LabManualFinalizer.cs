#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 116-C5 — 대쉬 간파 예측 랩 수동 모드 결과 마무리. 랩 디렉터(<see cref="QABoss116LabDirector"/>)가 Play 정지(OnDestroy) 때 판 파일·batch.txt 를 쓰고
/// <see cref="QABoss116LabManual.PendingKey"/>(SessionState)에 폴더를 남기면, 에디터로 돌아온 순간(EnteredEditMode — 도메인 리로드가 끼면 로드 직후)
/// C4 와 같은 집계(<see cref="QABoss116LabReport.AppendRun"/>)로 CSV 4종(lab_attempts·lab_windows·lab_dashes·dash_predict)+lab_runs·report.html 을 만들고 콘솔에 한 줄 요약.
/// 키가 없으면(다른 씬·배치) 아무것도 하지 않는다.
/// </summary>
[InitializeOnLoad]
public static class QABoss116LabManualFinalizer
{
    static QABoss116LabManualFinalizer()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                Run();
        };
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            Run();
    }

    /// <summary>남은 수동 결과 폴더가 있으면 CSV·리포트를 만든다. 만든 리포트 경로(없으면 null).</summary>
    public static string Run()
    {
        string dir = SessionState.GetString(QABoss116LabManual.PendingKey, "");
        if (string.IsNullOrEmpty(dir))
            return null;
        SessionState.EraseString(QABoss116LabManual.PendingKey);
        if (!Directory.Exists(dir))
        {
            Debug.LogWarning($"{QABoss116Lab.Tag} 수동 모드 결과 폴더가 없다: {dir}");
            return null;
        }
        try
        {
            string batchPath = Path.Combine(dir, QABoss103Batch.BatchFile);
            Dictionary<string, string> batch = QABoss103Batch.ParseKeyValues(File.Exists(batchPath) ? File.ReadAllText(batchPath) : "");
            batch["data_source_mode"] = DataSourcePlayMode.Current.ToString();
            batch["run_s"] = "수동";
            batch["max_attempts"] = "–";
            batch["zone_s"] = QABoss116LabDirector.ZoneToggleKey + " 키";
            batch["xlsx_svn_rev"] = QABoss103RunCsv.XlsxRevision();
            batch["svn_rev"] = QABoss103RunCsv.SvnWorkingRevision();
            batch["git_commit"] = QABoss103RunCsv.GitCommit();
            batch["git_branch"] = QABoss103RunCsv.Shell("git", "rev-parse --abbrev-ref HEAD");
            File.WriteAllText(batchPath, QABoss103Batch.FormatKeyValues(batch));

            var cfg = new QABoss103Config { Seed = 0, Run = 1, Retry = 0, OutDir = dir };
            QABoss116LabReport.AppendRun(dir, cfg, "ok", "수동 모드(사람 입력)", batch.TryGetValue("started_at", out string at) ? at : "", "manual");
            string report = QABoss116LabReport.Write(dir, "수동 모드");

            QABoss103CsvTable attempts = Table(dir, QABoss116Lab.AttemptsFile);
            QABoss103CsvTable windows = Table(dir, QABoss116Lab.WindowsFile);
            Debug.Log(QABoss116LabManual.SummaryLine(QABoss116Lab.Summarize(attempts, windows), dir) + " · 리포트 " + report);
            return report;
        }
        catch (Exception e)
        {
            Debug.LogError($"{QABoss116Lab.Tag} 수동 모드 CSV·리포트 만들기 실패({dir}): {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    private static QABoss103CsvTable Table(string dir, string file)
    {
        string path = Path.Combine(dir, file);
        return File.Exists(path) ? QABoss103CsvTable.Parse(File.ReadAllText(path, Encoding.UTF8)) : new QABoss103CsvTable();
    }
}
#endif
