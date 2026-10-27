#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

/// <summary>
/// 할 일 108 — 103 연속 루프(<see cref="QABoss103BatchRunner"/>)가 야간 배치(batch.txt 에 <c>nightly_seed</c>)일 때만 부르는 판 앞뒤 일.
/// <list type="bullet">
/// <item>녹화: 판 시작 전 OBS <c>StartRecord</c>, 판이 끝나 Play 가 멈춘 뒤 <c>StopRecord</c> → 영상 경로를 <c>recordings.txt</c> 에. 녹화 중 표시는 파일
///   (<c>rec-active.txt</c>)이라 Play 진입·정지 도메인 리로드를 넘는다.</item>
/// <item>수집: 판마다·배치 끝에 배치 폴더·48 리포트를 날짜별 공유 폴더로 복사(작은 파일은 바로, 영상은 robocopy 를 따로 띄운다 — 다음 판 도메인 리로드에 끊기지 않게).
///   배치 끝에는 Editor.log 사본도.</item>
/// </list>
/// 모든 실패는 경고(로그)만 — QA 는 계속 간다.
/// </summary>
public static class QANightlyHooks
{
    private const string ActiveFile = "rec-active.txt";
    private const long SmallFileLimit = 64L * 1024 * 1024;

    public static bool IsNightly(Dictionary<string, string> batch) =>
        batch != null && batch.TryGetValue(QANightlyHeader.KeySeedName, out string n) && !string.IsNullOrEmpty(n);

    public static Dictionary<string, string> ReadBatch(string dir)
    {
        string path = Path.Combine(dir, QABoss103Batch.BatchFile);
        try { return File.Exists(path) ? QABoss103Batch.ParseKeyValues(File.ReadAllText(path)) : new Dictionary<string, string>(StringComparer.Ordinal); }
        catch { return new Dictionary<string, string>(StringComparer.Ordinal); }
    }

    public static QANightlyLocalSettings LoadLocal()
    {
        string file = null;
        string path = Path.Combine(QANetSim88.OwnRoot, QANightlyLocalSettings.RelativePath);
        try
        {
            if (File.Exists(path))
                file = File.ReadAllText(path);
        }
        catch { /* 없음과 같다 */ }
        return QANightlyLocalSettings.Merge(file, Env);
    }

    /// <summary>프로세스 환경변수 → 사용자 범위(레지스트리 — 에디터를 어디서 띄웠든 setx 값을 읽는다).</summary>
    public static string Env(string name)
    {
        try
        {
            string v = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrEmpty(v) ? v : Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }
        catch { return null; }
    }

    // ── 판 앞뒤 ──────────────────────────────────────────────

    public static void AttemptStarting(string dir, int run, int retry, Action<string> log)
    {
        Dictionary<string, string> batch = ReadBatch(dir);
        if (!IsNightly(batch) || Val(batch, "record") != "obs")
            return;
        QANightlyLocalSettings s = LoadLocal();
        QAObsResult r = QAObsClient.Send(s.ObsHost, s.ObsPort, s.ObsPassword, QAObsProtocol.StartRecord);
        if (!r.Ok)
        {
            log($"⚠ 녹화 시작 실패(QA 는 계속) — {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)} {r.Error}");
            return;
        }
        TryWrite(Path.Combine(dir, ActiveFile), $"{run}\t{retry}\n");
        log($"녹화 시작 — 판 {run}{(retry > 0 ? " 재실행" : "")}{(r.AlreadyInState ? " (이미 녹화 중이었다 — 그 녹화를 이어 쓴다)" : "")}");
    }

    /// <summary>판 1회가 끝나 Play 가 멈춘 뒤.</summary>
    public static void AttemptEnded(string dir, int run, int retry, Action<string> log)
    {
        Dictionary<string, string> batch = ReadBatch(dir);
        if (!IsNightly(batch))
            return;
        StopRecording(dir, batch, run, retry, log);
        Collect(dir, batch, false, "", log);
    }

    /// <summary>배치 끝(완료·멈춤) — 남은 녹화를 멈추고 마지막 수집 + Editor.log 사본.</summary>
    public static void BatchEnded(string dir, string loopState, Action<string> log)
    {
        Dictionary<string, string> batch = ReadBatch(dir);
        if (!IsNightly(batch))
            return;
        StopRecording(dir, batch, -1, -1, log);
        Collect(dir, batch, true, loopState, log);
    }

    private static void StopRecording(string dir, Dictionary<string, string> batch, int run, int retry, Action<string> log)
    {
        string active = Path.Combine(dir, ActiveFile);
        if (!File.Exists(active))
            return;
        if (run < 0)
        {
            string[] f = (SafeRead(active) ?? "").Trim().Split('\t');
            int.TryParse(f.Length > 0 ? f[0] : "", out run);
            int.TryParse(f.Length > 1 ? f[1] : "", out retry);
        }
        QANightlyLocalSettings s = LoadLocal();
        QAObsResult r = QAObsClient.Send(s.ObsHost, s.ObsPort, s.ObsPassword, QAObsProtocol.StopRecord, 10.0);
        TryDelete(active);
        var rec = new QANightlyRecording { Run = Math.Max(0, run), Retry = Math.Max(0, retry), Path = r.OutputPath ?? "" };
        if (!r.Ok)
        {
            rec.Note = "정지 실패: " + r.Error;
            log($"⚠ 녹화 정지 실패(QA 는 계속) — {r.Error}");
        }
        else if (rec.Path.Length == 0)
            rec.Note = r.AlreadyInState ? "OBS 가 녹화 중이 아니었다" : "영상 경로를 못 받음";

        string share = Val(batch, "share_dir");
        if (share.Length > 0 && rec.Path.Length > 0)
        {
            string videoDir = Path.Combine(share, "video");
            rec.Shared = Path.Combine(videoDir, Path.GetFileName(rec.Path));
            string err = LaunchRobocopy(rec.Path, videoDir);
            if (err != null)
            {
                rec.Note = (rec.Note + " 영상 복사 시작 실패: " + err).Trim();
                log($"⚠ 영상 복사 시작 실패 — {err}");
            }
        }
        try { File.AppendAllText(Path.Combine(dir, QANightlyPaths.RecordingsFile), rec.Format() + "\n", new UTF8Encoding(false)); }
        catch (Exception e) { log($"⚠ recordings.txt 쓰기 실패 — {e.Message}"); }
        log($"녹화 정지 — 판 {rec.Run}{(rec.Retry > 0 ? " 재실행" : "")} · 영상 {(rec.Path.Length > 0 ? rec.Path : "없음")}{(rec.Shared.Length > 0 ? " → " + rec.Shared + " (robocopy)" : "")}");
    }

    // ── 수집 ─────────────────────────────────────────────────

    /// <summary>아침 요약(nightly-summary.md)을 배치 폴더에 쓰고, 공유 폴더가 있으면 배치 폴더·48 리포트를 복사한다.</summary>
    public static void Collect(string dir, Dictionary<string, string> batch, bool final, string loopState, Action<string> log)
    {
        WriteSummary(dir, batch, loopState, log);
        string share = Val(batch, "share_dir");
        if (share.Length == 0)
            return;
        try
        {
            string dst = Path.Combine(share, Path.GetFileName(dir.TrimEnd('/', '\\')));
            int n = CopyTree(dir, dst, new[] { QABoss103Batch.BeatFolder });
            DateTime started = DateTime.TryParseExact(Val(batch, "started_at"), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                                                      System.Globalization.DateTimeStyles.None, out DateTime t) ? t : DateTime.MinValue;
            string root = QAReportWriter.ScenarioRoot();
            if (Directory.Exists(root) && started > DateTime.MinValue)
                foreach (string d in Directory.GetDirectories(root, QANightlyPaths.ScenarioReportPrefix + "*"))
                    if (QANightlyPaths.IsScenarioReportOf(Path.GetFileName(d), started))
                        n += CopyTree(d, Path.Combine(share, "48", Path.GetFileName(d)), null);
            if (final)
            {
                string editorLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
                if (File.Exists(editorLog))
                {
                    Directory.CreateDirectory(Path.Combine(share, "logs"));
                    CopyShared(editorLog, Path.Combine(share, "logs", $"Editor-{DateTime.Now:yyyyMMdd_HHmmss}.log"));
                    n++;
                }
            }
            log($"공유 폴더 수집 {n}개 → {share}");
        }
        catch (Exception e)
        {
            log($"⚠ 공유 폴더 복사 실패(QA 는 계속) — {share} {e.GetType().Name}: {e.Message}");
        }
    }

    private static void WriteSummary(string dir, Dictionary<string, string> batch, string loopState, Action<string> log)
    {
        try
        {
            string runs = Path.Combine(dir, QABoss103Csv.RunsFile);
            List<QABoss103Attempt> attempts = File.Exists(runs)
                ? QABoss103Batch.ReadAttempts(QABoss103CsvTable.Parse(QABoss103Heartbeat.ReadShared(runs)))
                : new List<QABoss103Attempt>();
            List<QANightlyRecording> recs = QANightlyRecording.ParseAll(SafeRead(Path.Combine(dir, QANightlyPaths.RecordingsFile)));
            string md = QANightlyHeader.Summary(batch, recs, attempts, loopState, Path.Combine(dir, QABoss103Batch.ReportFile));
            File.WriteAllText(Path.Combine(dir, QANightlyPaths.SummaryFile), md, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            log($"⚠ 요약 쓰기 실패 — {e.Message}");
        }
    }

    /// <summary>폴더 복사(크기·수정 시각이 같으면 건너뜀, 64MB 넘는 파일 제외). 복사한 파일 수.</summary>
    private static int CopyTree(string src, string dst, string[] skipDirs)
    {
        int n = 0;
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src))
        {
            var fi = new FileInfo(f);
            if (fi.Length > SmallFileLimit || fi.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;
            string to = Path.Combine(dst, fi.Name);
            var ti = new FileInfo(to);
            if (ti.Exists && ti.Length == fi.Length && ti.LastWriteTimeUtc >= fi.LastWriteTimeUtc)
                continue;
            CopyShared(f, to);
            n++;
        }
        foreach (string d in Directory.GetDirectories(src))
        {
            string name = Path.GetFileName(d);
            if (skipDirs != null && Array.IndexOf(skipDirs, name) >= 0)
                continue;
            n += CopyTree(d, Path.Combine(dst, name), null);
        }
        return n;
    }

    /// <summary>쓰는 중인 파일(Editor.log·CSV)도 읽게 공유 모드로 복사.</summary>
    private static void CopyShared(string from, string to)
    {
        using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None))
            src.CopyTo(dst);
    }

    /// <summary>영상 1개를 robocopy 로 따로 복사(기다리지 않는다 — 결과는 &lt;video&gt;/robocopy.log). 실패 사유 또는 null.</summary>
    private static string LaunchRobocopy(string file, string dstDir)
    {
        try
        {
            Directory.CreateDirectory(dstDir);
            string srcDir = (Path.GetDirectoryName(file) ?? "").TrimEnd('\\', '/');
            string name = Path.GetFileName(file);
            string logPath = Path.Combine(dstDir, "robocopy.log");
            var psi = new ProcessStartInfo("robocopy", $"\"{srcDir}\" \"{dstDir.TrimEnd('\\', '/')}\" \"{name}\" /R:10 /W:15 /NP /LOG+:\"{logPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi)?.Dispose();
            return null;
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}: {e.Message}";
        }
    }

    // ── 도구 ─────────────────────────────────────────────────

    private static string Val(Dictionary<string, string> d, string k) => d != null && d.TryGetValue(k, out string v) ? v ?? "" : "";

    private static string SafeRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    private static void TryWrite(string path, string text)
    {
        try { File.WriteAllText(path, text); }
        catch { /* 표시 실패 — 정지 때 녹화가 남을 수 있다(OBS 가 다음 시작에 500 을 돌려준다) */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* 무시 */ }
    }
}
#endif
