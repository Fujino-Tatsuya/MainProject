#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

/// <summary>
/// 할 일 108 — 103 연속 루프(<see cref="QABoss103BatchRunner"/>)가 야간 배치(batch.txt 에 <c>nightly_seed</c>)일 때만 부르는 판 앞뒤 일.
/// <list type="bullet">
/// <item>녹화: 판 시작 전 OBS <c>StartRecord</c>, 판이 끝나 Play 가 멈춘 뒤 <c>StopRecord</c> → 영상 경로를 <c>recordings.txt</c> 에. 녹화 중 표시는 파일
///   (<c>rec-active.txt</c>)이라 Play 진입·정지 도메인 리로드를 넘는다.</item>
/// <item>수집: 판마다·배치 끝에 배치 폴더·48 리포트를 날짜별 공유 폴더로 복사(작은 파일은 바로, 영상은 robocopy 를 따로 띄운다 — 다음 판 도메인 리로드에 끊기지 않게).
///   배치 끝에는 Editor.log 사본도.</item>
/// <item>할 일 120 — 배치 시작 1회: 공유 폴더 지난 영상 삭제(보관 일수) → OBS 화질(해상도·fps·비트레이트) 적용 → (2단계) 리플레이 버퍼 시작.
///   판 중: 호스트 로그를 이벤트 표(<see cref="QANightlyEvents.Table"/>)로 보고 시각표(<c>events.tsv</c>, 영상 타임코드 포함)를 쓴다.
///   2단계면 이벤트마다 클립 예약(<see cref="QANightlyClipPlanner"/>) → <c>SaveReplayBuffer</c> → 파일 이름 바꾸기 → 공유 폴더 video/ 복사.</item>
/// </list>
/// 모든 실패는 경고(로그)만 — QA 는 계속 간다.
/// </summary>
public static class QANightlyHooks
{
    private const string Tag = "[QA108/Nightly]";
    private const string ActiveFile = "rec-active.txt";
    private const string ReplayActiveFile = "replay-active.txt";
    private const string PendingFile = "clip-pending.txt";
    private const long SmallFileLimit = 64L * 1024 * 1024;
    private const double ClipSaveTimeoutSeconds = 15.0;
    private static readonly object FileLock = new object();

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

    // ── 배치 시작 (할 일 120) ────────────────────────────────

    /// <summary>
    /// 배치 시작(Start Night·Resume Last) 1회 — 공유 폴더 지난 영상 삭제 → OBS 화질 적용 → (2단계) 리플레이 버퍼 시작.
    /// <paramref name="log"/> null = 콘솔 + 배치 loop.log.
    /// </summary>
    public static void BatchStarting(string dir, Action<string> log)
    {
        if (string.IsNullOrEmpty(dir))
            return;
        Dictionary<string, string> batch = ReadBatch(dir);
        if (!IsNightly(batch))
            return;
        log = log ?? DirLog(dir);
        _live = null;
        QANightlyLocalSettings s = LoadLocal();
        PruneVideos(s.ShareRoot, Val(batch, QANightlyHeader.KeySeedName), s.VideoKeepDays, log);
        string mode = Val(batch, "record");
        if (mode != QANightlyLocalSettings.RecordFull && mode != QANightlyLocalSettings.RecordReplay)
            return;
        ApplyQuality(s, mode == QANightlyLocalSettings.RecordReplay, log);
        if (mode == QANightlyLocalSettings.RecordReplay)
            StartReplay(dir, s, log);
    }

    /// <summary>공유 루트의 날짜 폴더 중 보관 일수를 넘긴 것에서 <b>이 시드</b>의 video/ 영상 파일만 지운다. 리포트·로그·robocopy.log 는 그대로.</summary>
    private static void PruneVideos(string root, string seed, int keepDays, Action<string> log)
    {
        if (keepDays <= 0 || string.IsNullOrWhiteSpace(root) || string.IsNullOrEmpty(seed))
            return;
        try
        {
            root = root.Trim();
            if (!Directory.Exists(root))
                return;
            var names = new List<string>();
            foreach (string d in Directory.GetDirectories(root))
                names.Add(Path.GetFileName(d));
            int deleted = 0, failed = 0;
            long bytes = 0;
            foreach (string date in QANightlyRetention.ExpiredFolders(names, DateTime.Now, keepDays))
            {
                string video = Path.Combine(root, date, seed, QANightlyRetention.VideoFolder);
                if (!Directory.Exists(video))
                    continue;
                foreach (string f in Directory.GetFiles(video))
                {
                    if (!QANightlyRetention.IsVideoFile(f))
                        continue;
                    try
                    {
                        long len = new FileInfo(f).Length;
                        File.Delete(f);
                        deleted++;
                        bytes += len;
                    }
                    catch (Exception e)
                    {
                        if (++failed <= 3)
                            log($"⚠ 지난 영상 삭제 실패 — {f} {e.GetType().Name}: {e.Message}");
                    }
                }
            }
            if (deleted > 0 || failed > 0)
                log($"영상 보관 {keepDays}일 — 지난 영상 {deleted}개({bytes / (1024 * 1024)}MB) 삭제{(failed > 0 ? $" · 실패 {failed}개" : "")} · " +
                    $"{root}\\<날짜>\\{seed}\\video (리포트·로그는 그대로)");
        }
        catch (Exception e)
        {
            log($"⚠ 영상 보관 정리 실패(QA 는 계속) — {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// 화질 적용 — 설정이 비어 있으면 아무것도 바꾸지 않는다. 해상도·fps(SetVideoSettings)는 녹화·리플레이 버퍼가 꺼져 있을 때만,
    /// 비트레이트·리플레이 버퍼 길이는 프로필 파라미터(출력 모드별 키 — <see cref="QAObsProfileParams"/>, [확인 필요]).
    /// </summary>
    private static void ApplyQuality(QANightlyLocalSettings s, bool replay, Action<string> log)
    {
        string videoData = QAObsProtocol.VideoSettingsData(s.VideoWidth, s.VideoHeight, s.VideoFps);
        bool bitrate = s.BitrateKbps > 0;
        bool replaySeconds = replay && s.ReplaySeconds > 0;
        if (videoData == null && !bitrate && !replaySeconds)
            return;

        string mode = "";
        if (bitrate || replaySeconds)
        {
            QAObsResult m = Obs(s, QAObsProtocol.GetProfileParameter, QAObsProtocol.ProfileParameterQuery(QAObsProfileParams.OutputCategory, QAObsProfileParams.OutputModeName));
            if (m.Ok)
                mode = (m.ParameterValue ?? "").Trim();
            else
                log($"⚠ OBS 출력 모드 확인 실패 — {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)} {m.Error}");
        }

        if (videoData != null)
        {
            string busy = BusyOutputs(s);
            if (busy == null)
                log("⚠ OBS 녹화 상태 확인 실패 — 해상도·fps 적용 건너뜀(QA 는 계속)");
            else if (busy.Length > 0)
                log($"⚠ OBS {busy} 중이라 해상도·fps 를 바꾸지 않았다(SetVideoSettings 는 출력이 꺼져 있을 때만) — 다음 배치 시작 때 다시 시도");
            else
            {
                QAObsResult r = Obs(s, QAObsProtocol.SetVideoSettings, videoData);
                log(r.Ok ? $"OBS 화질 — 출력 {(s.VideoWidth > 0 && s.VideoHeight > 0 ? $"{s.VideoWidth}x{s.VideoHeight}" : "해상도 그대로")} · {(s.VideoFps > 0 ? s.VideoFps + "fps" : "fps 그대로")}"
                         : $"⚠ OBS 화질(SetVideoSettings) 적용 실패(QA 는 계속) — {r.Error}");
            }
        }
        if (bitrate)
        {
            if (QAObsProfileParams.TryBitrate(mode, s.BitrateParam, out string cat, out string name))
                SetParam(s, cat, name, s.BitrateKbps.ToString(CultureInfo.InvariantCulture), "녹화 비트레이트(kbps)", log);
            else
                log($"⚠ OBS 출력 모드 '{(mode.Length > 0 ? mode : "모름")}' — 녹화 비트레이트를 프로필 파라미터로 못 바꾼다(고급 모드는 인코더 설정). " +
                    "런북 §5.2 대로 OBS 에서 손으로 맞추거나 obsBitrateParam(카테고리/키)을 지정");
        }
        if (replaySeconds)
        {
            if (QAObsProfileParams.TryReplayEnable(mode, out string ecat, out string ename))
                SetParam(s, ecat, ename, "true", "리플레이 버퍼 사용", log);
            if (QAObsProfileParams.TryReplaySeconds(mode, out string rcat, out string rname))
                SetParam(s, rcat, rname, s.ReplaySeconds.ToString(CultureInfo.InvariantCulture), "리플레이 버퍼 길이(초)", log);
            else
                log($"⚠ OBS 출력 모드 '{(mode.Length > 0 ? mode : "모름")}' — 리플레이 버퍼 길이를 못 바꿨다. 런북 §5.3 대로 OBS 설정에서 손으로");
        }
    }

    /// <summary>켜져 있는 출력(녹화·리플레이 버퍼) 이름들. 꺼져 있으면 빈 값, 녹화 상태를 못 물으면 null. 리플레이 버퍼 상태 실패(버퍼 미사용 등)는 꺼짐으로 본다.</summary>
    private static string BusyOutputs(QANightlyLocalSettings s)
    {
        QAObsResult rec = Obs(s, QAObsProtocol.GetRecordStatus);
        if (!rec.Ok)
            return null;
        QAObsResult rb = Obs(s, QAObsProtocol.GetReplayBufferStatus);
        var busy = new List<string>();
        if (rec.OutputActive)
            busy.Add("녹화");
        if (rb.Ok && rb.OutputActive)
            busy.Add("리플레이 버퍼");
        return string.Join("·", busy);
    }

    private static void SetParam(QANightlyLocalSettings s, string category, string name, string value, string label, Action<string> log)
    {
        QAObsResult r = Obs(s, QAObsProtocol.SetProfileParameter, QAObsProtocol.ProfileParameterData(category, name, value));
        log(r.Ok ? $"OBS 프로필 {category}/{name} = {value} ({label})"
                 : $"⚠ OBS 프로필 {category}/{name} = {value} ({label}) 실패(QA 는 계속) — {r.Error}");
    }

    private static bool StartReplay(string dir, QANightlyLocalSettings s, Action<string> log)
    {
        QAObsResult r = Obs(s, QAObsProtocol.StartReplayBuffer);
        if (!r.Ok)
        {
            log($"⚠ 리플레이 버퍼 시작 실패(QA 는 계속 — 판마다 다시 시도. OBS 설정 → 출력 → 리플레이 버퍼 사용 확인) — {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)} {r.Error}");
            return false;
        }
        TryWrite(Path.Combine(dir, ReplayActiveFile), DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + "\n");
        log($"리플레이 버퍼 시작{(r.AlreadyInState ? " (이미 켜져 있었다)" : "")} — 이벤트 뒤 {s.ReplayAfterSeconds:0.#}초에 저장 · 쿨다운 {s.ReplayCooldownSeconds:0.#}초 · " +
            $"클립 이벤트 {(s.ClipEvents.Length > 0 ? s.ClipEvents : "표 전체")}");
        return true;
    }

    // ── 판 앞뒤 ──────────────────────────────────────────────

    public static void AttemptStarting(string dir, int run, int retry, Action<string> log)
    {
        Dictionary<string, string> batch = ReadBatch(dir);
        if (!IsNightly(batch))
            return;
        string mode = Val(batch, "record");
        if (mode == QANightlyLocalSettings.RecordReplay)
        {
            // 2단계 — 판 녹화 없음. 버퍼가 아직 안 켜졌으면(배치 시작 때 OBS 연결 실패) 다시 시도.
            if (!File.Exists(Path.Combine(dir, ReplayActiveFile)))
                StartReplay(dir, LoadLocal(), log);
            return;
        }
        if (mode != QANightlyLocalSettings.RecordFull)
            return;
        QANightlyLocalSettings s = LoadLocal();
        QAObsResult r = QAObsClient.Send(s.ObsHost, s.ObsPort, s.ObsPassword, QAObsProtocol.StartRecord);
        if (!r.Ok)
        {
            log($"⚠ 녹화 시작 실패(QA 는 계속) — {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)} {r.Error}");
            return;
        }
        // 할 일 120 — 타임코드 기준: 지금 녹화 경과(이미 녹화 중이었으면 그만큼)를 받아 둔다.
        var anchor = new QANightlyRecordAnchor { Run = run, Retry = retry, AnchorUtc = DateTime.UtcNow };
        QAObsResult st = QAObsClient.Send(s.ObsHost, s.ObsPort, s.ObsPassword, QAObsProtocol.GetRecordStatus);
        if (st.Ok)
        {
            anchor.AnchorUtc = DateTime.UtcNow;
            anchor.OffsetMs = QANightlyTimecode.OffsetMs(st.OutputDurationMs, st.OutputTimecode);
        }
        TryWrite(Path.Combine(dir, ActiveFile), anchor.Format());
        log($"녹화 시작 — 판 {run}{(retry > 0 ? " 재실행" : "")}{(r.AlreadyInState ? " (이미 녹화 중이었다 — 그 녹화를 이어 쓴다)" : "")} · " +
            $"타임코드 기준 {(st.Ok ? $"녹화 경과 {anchor.OffsetMs / 1000.0:F1}s" : "보정 없음(GetRecordStatus 실패)")}");
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

    /// <summary>배치 끝(완료·멈춤) — 남은 녹화를 멈추고(2단계면 남은 클립 저장·리플레이 버퍼 정지) 마지막 수집 + Editor.log 사본.</summary>
    public static void BatchEnded(string dir, string loopState, Action<string> log)
    {
        Dictionary<string, string> batch = ReadBatch(dir);
        if (!IsNightly(batch))
            return;
        StopRecording(dir, batch, -1, -1, log);
        if (Val(batch, "record") == QANightlyLocalSettings.RecordReplay)
            EndReplay(dir, log);
        Collect(dir, batch, true, loopState, log);
    }

    private static void StopRecording(string dir, Dictionary<string, string> batch, int run, int retry, Action<string> log)
    {
        string active = Path.Combine(dir, ActiveFile);
        if (!File.Exists(active))
            return;
        if (run < 0)
        {
            QANightlyRecordAnchor a = QANightlyRecordAnchor.Parse(SafeRead(active));
            run = a?.Run ?? 0;
            retry = a?.Retry ?? 0;
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
            string videoDir = Path.Combine(share, QANightlyRetention.VideoFolder);
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

    // ── 이벤트 (할 일 120) ───────────────────────────────────

    /// <summary>도메인 리로드 뒤 다시 읽는 배치 문맥(배치 머리·PC 설정·클립 예약).</summary>
    private sealed class Live
    {
        public string Dir;
        public bool Nightly;
        public string Mode = "";
        public string Seed = "";
        public string NightDate = "";
        public string ShareDir = "";
        public QANightlyLocalSettings Settings;
        public HashSet<string> ClipEnabled;
        public QANightlyClipPlanner Planner;
        public readonly QANightlyEventThrottle Throttle = new QANightlyEventThrottle();
    }

    private static Live _live;
    private static bool _inHook;

    private static Live GetLive(string dir)
    {
        if (string.IsNullOrEmpty(dir))
            return null;
        if (_live != null && _live.Dir == dir)
            return _live;
        Dictionary<string, string> batch = ReadBatch(dir);
        var l = new Live { Dir = dir, Nightly = IsNightly(batch) };
        if (l.Nightly)
        {
            l.Mode = Val(batch, "record");
            l.Seed = Val(batch, QANightlyHeader.KeySeedName);
            l.NightDate = NightDate(batch);
            l.ShareDir = Val(batch, "share_dir");
            l.Settings = LoadLocal();
            l.ClipEnabled = QANightlyEvents.ParseEnabled(l.Settings.ClipEvents);
            l.Planner = QANightlyClipPlanner.Parse(SafeRead(Path.Combine(dir, PendingFile)));
        }
        _live = l;
        return l;
    }

    /// <summary>[메인 에디터 로그 콜백] 판 Play 중 호스트 로그 한 줄 — 이벤트 표에 맞으면 시각표에 쓰고(2단계면 클립 예약). 여기서는 로그를 내지 않는다(재귀).</summary>
    public static void OnLog(string dir, int run, int retry, string condition, UnityEngine.LogType type)
    {
        if (_inHook || string.IsNullOrEmpty(condition) || condition.StartsWith(Tag, StringComparison.Ordinal))
            return;
        _inHook = true;
        try
        {
            Live l = GetLive(dir);
            if (l == null || !l.Nightly)
                return;
            QANightlyEventRule rule = QANightlyEvents.Match(condition, type, null);
            if (rule != null)
                Record(l, run, retry, rule.Name, condition, GameSeconds());
        }
        catch
        {
            // 시각표 실패로 QA 를 멈추지 않는다(여기서 로그를 내면 다시 이 콜백으로 들어온다).
        }
        finally
        {
            _inHook = false;
        }
    }

    /// <summary>루프가 직접 본 이벤트(판 오류 판정 등).</summary>
    public static void Event(string dir, int run, int retry, string name, string detail)
    {
        try
        {
            Live l = GetLive(dir);
            if (l != null && l.Nightly)
                Record(l, run, retry, name, detail, GameSeconds());
        }
        catch { /* 시각표 실패로 QA 를 멈추지 않는다 */ }
    }

    private static double GameSeconds() => EditorApplication.isPlaying ? UnityEngine.Time.timeAsDouble : double.NaN;

    private static void Record(Live l, int run, int retry, string name, string detail, double gameSeconds)
    {
        DateTime nowUtc = DateTime.UtcNow;
        if (!l.Throttle.Allow(run + "/" + retry, name, nowUtc))
            return;
        var ev = new QANightlyEvent
        {
            Run = run, Retry = retry, Name = name, At = nowUtc.ToLocalTime(), GameSeconds = gameSeconds, Detail = QANightlyEvents.Detail(detail),
        };
        if (l.Mode == QANightlyLocalSettings.RecordFull)
        {
            QANightlyRecordAnchor a = QANightlyRecordAnchor.Parse(SafeRead(Path.Combine(l.Dir, ActiveFile)));
            if (a != null && a.Run == run && a.Retry == retry)
                ev.VideoSeconds = QANightlyTimecode.VideoSeconds(a.AnchorUtc, a.OffsetMs, nowUtc);
        }
        else if (l.Mode == QANightlyLocalSettings.RecordReplay && (l.ClipEnabled == null || l.ClipEnabled.Contains(name)))
        {
            l.Planner.Add(nowUtc, name, run, retry, l.Settings.ReplayAfterSeconds, l.Settings.ReplayCooldownSeconds);
            SavePending(l);
        }
        AppendLine(Path.Combine(l.Dir, QANightlyPaths.EventsFile), ev.Format());
    }

    /// <summary>[루프 틱] 2단계 — 예약 시각이 된 클립을 저장한다. Play 중이면 스레드 풀에서(Play 를 막지 않게), 아니면 바로.</summary>
    public static void Tick(string dir, Action<string> log)
    {
        Live l = GetLive(dir);
        if (l == null || !l.Nightly || l.Mode != QANightlyLocalSettings.RecordReplay || !l.Planner.Pending)
            return;
        if (!l.Planner.TryTake(DateTime.UtcNow, false, out List<string> names, out int run, out int retry))
            return;
        SavePending(l);
        ClipJob job = Job(l, names, run, retry);
        if (EditorApplication.isPlaying)
            Task.Run(() => SaveClip(job, null));
        else
            SaveClip(job, log);
    }

    /// <summary>배치 끝 — 남은 예약을 바로 저장하고 리플레이 버퍼를 멈춘다.</summary>
    private static void EndReplay(string dir, Action<string> log)
    {
        Live l = GetLive(dir);
        if (l == null || !l.Nightly)
            return;
        if (l.Planner.TryTake(DateTime.UtcNow, true, out List<string> names, out int run, out int retry))
        {
            SavePending(l);
            SaveClip(Job(l, names, run, retry), log);
        }
        QAObsResult r = Obs(l.Settings, QAObsProtocol.StopReplayBuffer);
        TryDelete(Path.Combine(dir, ReplayActiveFile));
        log(r.Ok ? $"리플레이 버퍼 정지{(r.AlreadyInState ? " (이미 꺼져 있었다)" : "")}" : $"⚠ 리플레이 버퍼 정지 실패(QA 는 계속) — {r.Error}");
    }

    private sealed class ClipJob
    {
        public string Dir, Seed, NightDate, ShareDir, Host, Password;
        public int Port, Run, Retry;
        public List<string> Names;
    }

    private static ClipJob Job(Live l, List<string> names, int run, int retry) => new ClipJob
    {
        Dir = l.Dir, Seed = l.Seed, NightDate = l.NightDate, ShareDir = l.ShareDir ?? "",
        Host = l.Settings.ObsHost, Port = l.Settings.ObsPort, Password = l.Settings.ObsPassword,
        Run = run, Retry = retry, Names = names,
    };

    /// <summary>SaveReplayBuffer → 경로(ReplayBufferSaved 이벤트, 없으면 GetLastReplayBufferReplay) → 이름 바꾸기 → 공유 폴더 video/ 복사 → clips.txt. 실패는 경고만.</summary>
    private static void SaveClip(ClipJob j, Action<string> log)
    {
        Action<string> say = log ?? DirLog(j.Dir);
        string events = string.Join("+", j.Names);
        try
        {
            QAObsResult r = QAObsClient.Request(j.Host, j.Port, j.Password, QAObsProtocol.SaveReplayBuffer, null, ClipSaveTimeoutSeconds, QAObsProtocol.EventReplayBufferSaved);
            string path = r.SavedReplayPath ?? "";
            if (r.Ok && path.Length == 0)
            {
                QAObsResult last = QAObsClient.Request(j.Host, j.Port, j.Password, QAObsProtocol.GetLastReplayBufferReplay, null, 5.0, null);
                if (last.Ok)
                    path = last.SavedReplayPath ?? "";
            }
            var rec = new QANightlyRecording { Run = j.Run, Retry = j.Retry, Path = path, Note = events };
            if (!r.Ok)
            {
                rec.Note += " · 저장 실패: " + r.Error;
                say($"⚠ 리플레이 저장 실패(QA 는 계속) — 판 {j.Run} [{events}] {r.Error}");
            }
            else if (path.Length == 0)
                rec.Note += " · 클립 경로를 못 받음";
            else
            {
                string target = QANightlyClipName.Unique(Path.GetDirectoryName(path),
                    QANightlyClipName.FileName(j.NightDate, j.Seed, j.Run, j.Retry, j.Names, Path.GetExtension(path)), File.Exists);
                string err = MoveWithRetry(path, target);
                if (err == null)
                    rec.Path = target;
                else
                {
                    rec.Note += " · 이름 바꾸기 실패";
                    say($"⚠ 클립 이름 바꾸기 실패(원래 이름 유지) — {path} → {Path.GetFileName(target)} {err}");
                }
                if (j.ShareDir.Length > 0)
                {
                    string videoDir = Path.Combine(j.ShareDir, QANightlyRetention.VideoFolder);
                    rec.Shared = Path.Combine(videoDir, Path.GetFileName(rec.Path));
                    string cerr = LaunchRobocopy(rec.Path, videoDir);
                    if (cerr != null)
                    {
                        rec.Note += " · 복사 시작 실패: " + cerr;
                        say($"⚠ 클립 복사 시작 실패 — {cerr}");
                    }
                }
            }
            AppendLine(Path.Combine(j.Dir, QANightlyPaths.ClipsFile), rec.Format());
            say($"리플레이 클립 — 판 {j.Run}{(j.Retry > 0 ? " 재실행" : "")} [{events}] {(rec.Path.Length > 0 ? rec.Path : "경로 없음")}" +
                (rec.Shared.Length > 0 ? " → " + rec.Shared + " (robocopy)" : ""));
        }
        catch (Exception e)
        {
            say($"⚠ 리플레이 클립 처리 실패(QA 는 계속) — [{events}] {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>OBS 가 파일을 막 닫은 직후라 잠깐 잠겨 있을 수 있다 — 1초 간격 3번. 실패 사유 또는 null.</summary>
    private static string MoveWithRetry(string from, string to)
    {
        string err = null;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.Move(from, to);
                return null;
            }
            catch (Exception e)
            {
                err = $"{e.GetType().Name}: {e.Message}";
                Thread.Sleep(1000);
            }
        }
        return err;
    }

    private static void SavePending(Live l) => TryWrite(Path.Combine(l.Dir, PendingFile), l.Planner.Format());

    /// <summary>밤 날짜(배치 시작 날, <c>yyyy-MM-dd</c>) — 공유 폴더 이름과 같다.</summary>
    private static string NightDate(Dictionary<string, string> batch) =>
        DateTime.TryParseExact(Val(batch, "started_at"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)
            ? t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
            DateTime started = DateTime.TryParseExact(Val(batch, "started_at"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                                      DateTimeStyles.None, out DateTime t) ? t : DateTime.MinValue;
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
            List<QANightlyEvent> events = QANightlyEvent.ParseAll(SafeRead(Path.Combine(dir, QANightlyPaths.EventsFile)));
            List<QANightlyRecording> clips = QANightlyRecording.ParseAll(SafeRead(Path.Combine(dir, QANightlyPaths.ClipsFile)));
            string md = QANightlyHeader.Summary(batch, recs, attempts, loopState, Path.Combine(dir, QABoss103Batch.ReportFile), events, clips);
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

    private static QAObsResult Obs(QANightlyLocalSettings s, string requestType, string requestData = null) =>
        QAObsClient.Request(s.ObsHost, s.ObsPort, s.ObsPassword, requestType, requestData, 5.0, null);

    /// <summary>콘솔 + 배치 <c>logs/loop.log</c>(루프 로그와 같은 파일 — 스레드 풀에서도 쓴다).</summary>
    private static Action<string> DirLog(string dir) => msg =>
    {
        Debug.Log($"{Tag} {msg}");
        AppendLine(Path.Combine(dir, QABoss103Batch.LogFolder, QABoss103Batch.LogFile), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}");
    };

    private static void AppendLine(string path, string line)
    {
        try
        {
            lock (FileLock)
            {
                string d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d))
                    Directory.CreateDirectory(d);
                File.AppendAllText(path, line.EndsWith("\n", StringComparison.Ordinal) ? line : line + "\n", new UTF8Encoding(false));
            }
        }
        catch { /* 기록 실패로 QA 를 멈추지 않는다 */ }
    }

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
