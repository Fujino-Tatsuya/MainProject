#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 할 일 103 C4 — [호스트] 판 끝에 CSV 4종(runs·run_chars·run_patterns·run_gimmicks)을 출력 폴더(<see cref="QABoss103Config.OutDir"/>)에 append + flush.
/// 봇 판 파일(<see cref="QABoss103PartFile"/>)을 합치고, 판 머리(데이터 출처·xlsx SVN 리비전·git 커밋·시각·Unity 버전)를 채운다.
/// 로그 1줄 <c>[QA103] event=run_csv path=… rows=…</c>.
/// </summary>
public static class QABoss103RunCsv
{
    private const string Tag = QABoss103Plan.Tag;
    /// <summary>xlsx 원본(SVN) — 프로젝트 루트 기준.</summary>
    private const string XlsxRelative = "Assets/50.Art/DataTable~/GameData.xlsx";
    private const int ProcessTimeoutMs = 5000;

    /// <summary>판 끝 신호 파일을 쓴다 — 클론 RunClient 가 보고 봇을 닫는다(done). 출력 폴더가 없으면 아무것도 안 한다.</summary>
    public static void SignalEnd(QABoss103Config cfg)
    {
        if (!cfg.HasOut)
            return;
        try
        {
            string path = QABoss103PartFile.EndPath(cfg.OutDir, cfg.Run, cfg.Retry);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} event=run_end_signal ok=false error={QABoss103Plan.Token(e.Message)}");
        }
    }

    /// <summary>[클라] 이 판의 끝 신호가 <paramref name="sinceUtc"/> 뒤에 써졌나(이전 시도의 남은 파일은 무시).</summary>
    public static bool EndSignaled(QABoss103Config cfg, DateTime sinceUtc)
    {
        if (!cfg.HasOut)
            return false;
        try
        {
            string path = QABoss103PartFile.EndPath(cfg.OutDir, cfg.Run, cfg.Retry);
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= sinceUtc;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>파티 자리 중 판 파일에 끝 표시(<c>done</c>)가 아직 없는 자리 수.</summary>
    public static int PendingParts(QABoss103Config cfg, IEnumerable<int> slots)
    {
        if (!cfg.HasOut)
            return 0;
        int n = 0;
        foreach (int s in slots)
        {
            List<string> lines = SafeRead(QABoss103PartFile.PartPath(cfg.OutDir, cfg.Run, cfg.Retry, s));
            bool done = false;
            if (lines != null)
                foreach (string l in lines)
                    if (l.StartsWith("done", StringComparison.Ordinal))
                        done = true;
            if (!done)
                n++;
        }
        return n;
    }

    /// <summary>판 기록 + 계측(없으면 Combat 전 종료) → CSV 4종. 출력 폴더가 없으면 건너뛴다.</summary>
    public static void Write(QABoss103Config cfg, QABoss103RunRecord rec, QABoss103Recorder recorder, double bossHpPct, DateTime startedAt)
    {
        if (!cfg.HasOut)
        {
            Debug.Log($"{Tag} event=run_csv path=none rows=0 reason=no_out_dir");
            return;
        }
        try
        {
            QABoss103RunStats stats = recorder != null ? recorder.Stats : null;
            if (stats != null && stats.Started)
            {
                for (int s = 0; s < stats.Chars.Length; s++)
                {
                    List<string> lines = SafeRead(QABoss103PartFile.PartPath(cfg.OutDir, cfg.Run, cfg.Retry, s));
                    if (lines != null)
                        stats.AddPart(s, lines);
                }
                stats.Finish();
            }

            QABoss103RunMeta meta = Meta(cfg, rec, bossHpPct, startedAt);
            int rows = 0;
            rows += QABoss103Csv.Append(Path.Combine(cfg.OutDir, QABoss103Csv.RunsFile), QABoss103Metrics.RunsHeader, new[] { QABoss103CsvRows.Run(meta, stats) });
            rows += QABoss103Csv.Append(Path.Combine(cfg.OutDir, QABoss103Csv.CharsFile), QABoss103Metrics.CharsHeader, QABoss103CsvRows.Chars(meta, stats));
            rows += QABoss103Csv.Append(Path.Combine(cfg.OutDir, QABoss103Csv.PatternsFile), QABoss103Metrics.PatternsHeader, QABoss103CsvRows.Patterns(meta, stats));
            rows += QABoss103Csv.Append(Path.Combine(cfg.OutDir, QABoss103Csv.GimmicksFile), QABoss103Metrics.GimmicksHeader, QABoss103CsvRows.Gimmicks(meta, stats));
            string parts = stats != null && stats.Started ? stats.PartsNote() : "";
            Debug.Log($"{Tag} event=run_csv path={QABoss103Plan.Token(cfg.OutDir)} rows={rows} run={cfg.Run} retry={cfg.Retry} outcome={rec.Outcome} " +
                      $"dataSource={meta.DataSource} xlsxRev={meta.XlsxSvnRev} git={meta.GitCommit} parts={(parts.Length > 0 ? QABoss103Plan.Token(parts) : "ok")}");
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} event=run_csv path={QABoss103Plan.Token(cfg.OutDir)} rows=0 error={QABoss103Plan.Token(e.GetType().Name + ": " + e.Message)}");
        }
    }

    /// <summary>
    /// [에디터 루프] 판이 판정 없이 끊겼을 때(심장박동·단계 상한·Play 시작 실패 등) runs.csv 에 Error 행 1줄. 다른 CSV 는 쓰지 않는다(통계 제외 판).
    /// <paramref name="dataSource"/> = 마지막 심장박동에서 본 출처(모르면 na). 쓴 행 수.
    /// </summary>
    public static int WriteLoopError(QABoss103Config cfg, string reason, string note, string dataSource, DateTime startedAt)
    {
        if (!cfg.HasOut)
            return 0;
        var rec = new QABoss103RunRecord { Run = cfg.Run, BaseSeed = cfg.Seed, RunSeed = cfg.RunSeed, BotTier = cfg.BotTier, Outcome = QABoss103Outcome.Error, Detail = reason };
        QABoss103RunMeta meta = Meta(cfg, rec, double.NaN, startedAt, string.IsNullOrEmpty(dataSource) ? "na" : dataSource);
        meta.Note = note ?? "";
        int rows = QABoss103Csv.Append(Path.Combine(cfg.OutDir, QABoss103Csv.RunsFile), QABoss103Metrics.RunsHeader, new[] { QABoss103CsvRows.Run(meta, null) });
        Debug.Log($"{Tag} event=run_csv path={QABoss103Plan.Token(cfg.OutDir)} rows={rows} run={cfg.Run} retry={cfg.Retry} outcome=Error source=loop reason={QABoss103Plan.Token(reason)}");
        return rows;
    }

    /// <summary>xlsx 원본(SVN) 마지막 변경 리비전. 실패 = na.</summary>
    public static string XlsxRevision() =>
        Shell("svn", $"info --non-interactive --show-item last-changed-revision \"{Path.Combine(QANetSim88.OwnRoot, XlsxRelative)}\"");

    /// <summary>git 짧은 커밋. 실패 = na.</summary>
    public static string GitCommit() => Shell("git", "rev-parse --short HEAD");

    private static List<string> SafeRead(string path)
    {
        try { return QABoss103PartFile.ReadShared(path); }
        catch { return null; }
    }

    private static QABoss103RunMeta Meta(QABoss103Config cfg, QABoss103RunRecord rec, double bossHpPct, DateTime startedAt, string dataSource = null)
    {
        var m = new QABoss103RunMeta
        {
            Run = rec.Run,
            Retry = cfg.Retry,
            Tier = rec.BotTier,
            Seed = rec.BaseSeed,
            RunSeed = rec.RunSeed,
            Outcome = rec.Outcome,
            Detail = rec.Detail,
            CombatSeconds = rec.CombatSeconds,
            TraitsPaladin = Traits(rec, QABoss103Character.Paladin),
            TraitsAssassin = Traits(rec, QABoss103Character.Assassin),
            TraitsGunner = Traits(rec, QABoss103Character.Gunner),
            BossHpEndPct = rec.Outcome == QABoss103Outcome.Clear ? 0.0 : bossHpPct,
            DataSource = dataSource ?? QABoss103Heartbeat.CombinedDataSource(cfg),
            XlsxSvnRev = XlsxRevision(),
            GitCommit = GitCommit(),
            StartedAt = startedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            EndedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            UnityVersion = Application.unityVersion,
        };
        if (rec.Outcome == QABoss103Outcome.Clear || rec.Outcome == QABoss103Outcome.Wipe || rec.Outcome == QABoss103Outcome.Timeout)
            m.Note = rec.Detail;
        return m;
    }

    private static string Traits(QABoss103RunRecord rec, QABoss103Character c) =>
        rec.Traits.TryGetValue(c, out List<string> list) ? string.Join(";", list) : "";

    /// <summary>프로젝트 루트에서 명령 1개(상한 5초). 실패 = na.</summary>
    public static string Shell(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = QANetSim88.OwnRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (Process p = Process.Start(psi))
            {
                if (p == null)
                    return "na";
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(ProcessTimeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return "na";
                }
                output = output.Trim();
                return p.ExitCode == 0 && output.Length > 0 ? output : "na";
            }
        }
        catch
        {
            return "na";
        }
    }
}
#endif
