#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
/// <item>녹화(공유 모드가 아닐 때만): 판 시작 전 OBS <c>StartRecord</c>, 판이 끝나 Play 가 멈춘 뒤 <c>StopRecord</c> → 영상 경로를 <c>recordings.txt</c> 에. 녹화 중 표시는 파일
///   (<c>rec-active.txt</c>)이라 Play 진입·정지 도메인 리로드를 넘는다. 영상은 OBS 녹화 경로에 남는다(복사 없음).</item>
/// <item>할 일 120 — 배치 시작 1회: 공유 폴더 지난 자기 클립 삭제(보관 일수) → 공유 날짜 폴더 만들기·OBS 녹화 경로를 그 폴더로(원래 경로는 PC 파일에 보관) →
///   OBS 화질(해상도·fps·비트레이트) → (리플레이 모드) 리플레이 버퍼 시작 + 실제 켜졌는지 확인.
///   판 중: 호스트 로그를 이벤트 표(<see cref="QANightlyEvents.Table"/>)로 보고 시각표(<c>events.tsv</c>, 영상 타임코드 포함)를 쓴다.
///   리플레이 모드면 이벤트마다 클립 예약(<see cref="QANightlyClipPlanner"/>) → <c>SaveReplayBuffer</c> → <c>&lt;이름&gt;_&lt;HHmmss&gt;_r&lt;판&gt;_&lt;이벤트&gt;</c> 로 이름 바꿔 날짜 폴더 바로 아래로.</item>
/// <item>배치 끝: 남은 클립 저장·리플레이 버퍼 정지 → OBS 녹화 경로 되돌리기 → 로컬 요약(<c>nightly-summary.md</c>). 원본 로그·CSV·report.html 은 공유하지 않는다.</item>
/// </list>
/// 모든 실패는 경고(로그)만 — QA 는 계속 간다.
/// </summary>
public static class QANightlyHooks
{
    private const string Tag = "[QA108/Nightly]";
    private const string ActiveFile = "rec-active.txt";
    private const string ReplayActiveFile = "replay-active.txt";
    private const string PendingFile = "clip-pending.txt";
    /// <summary>OBS 원래 녹화 경로 — PC 파일(gitignore 경로). 배치 폴더가 아니라 PC 단위라 배치가 중간에 끊겨도 다음 배치가 원래 값을 덮지 않는다.</summary>
    public const string ObsRecordDirFile = "UserSettings/QANightly.obs-record-dir.txt";
    private const double ClipSaveTimeoutSeconds = 15.0;
    private const int ReplayVerifyTries = 3;
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
    /// 배치 시작(Start Night·Resume Last) 1회 — 공유 폴더 지난 클립 삭제 → 날짜 폴더·OBS 녹화 경로 → OBS 화질 → (리플레이 모드) 리플레이 버퍼 시작·확인.
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
        PruneClips(s.ShareRoot, Val(batch, QANightlyHeader.KeySeedName), s.VideoKeepDays, log);
        string share = Val(batch, "share_dir");
        if (share.Length > 0)
        {
            try { Directory.CreateDirectory(share); }
            catch (Exception e) { Warn(dir, log, $"공유 날짜 폴더를 못 만들었다(클립은 OBS 녹화 경로에 남는다) — {share} {e.GetType().Name}: {e.Message}"); }
        }
        string mode = Val(batch, "record");
        if (mode != QANightlyLocalSettings.RecordFull && mode != QANightlyLocalSettings.RecordReplay)
            return;
        if (share.Length > 0)
            PointRecordDirectory(dir, s, share, log);
        ApplyQuality(s, mode == QANightlyLocalSettings.RecordReplay, log);
        if (mode == QANightlyLocalSettings.RecordReplay)
            StartReplay(dir, s, log);
    }

    /// <summary>공유 루트의 <c>MM-dd</c> 날짜 폴더 중 보관 일수를 넘긴 것에서 <b>이 이름의 클립</b>(<see cref="QANightlyClipName.IsOwnClip"/>)만 지운다. 리포트·다른 사람 파일은 그대로.</summary>
    private static void PruneClips(string root, string seed, int keepDays, Action<string> log)
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
                foreach (string f in Directory.GetFiles(Path.Combine(root, date)))
                {
                    if (!QANightlyClipName.IsOwnClip(f, seed))
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
                            log($"⚠ 지난 클립 삭제 실패 — {f} {e.GetType().Name}: {e.Message}");
                    }
                }
            }
            if (deleted > 0 || failed > 0)
                log($"영상 보관 {keepDays}일 — 지난 클립 {deleted}개({bytes / (1024 * 1024)}MB) 삭제{(failed > 0 ? $" · 실패 {failed}개" : "")} · " +
                    $"{root}\\<MM-dd>\\{seed}_*.영상 (리포트·다른 사람 파일은 그대로)");
        }
        catch (Exception e)
        {
            log($"⚠ 영상 보관 정리 실패(QA 는 계속) — {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// OBS 녹화 경로(리플레이 클립 저장 위치)를 공유 날짜 폴더로. 원래 경로는 PC 파일(<see cref="ObsRecordDirFile"/>)에 — 이미 있으면(지난 배치가 못 되돌림) 덮지 않는다.
    /// 출력(녹화·리플레이 버퍼)이 켜져 있으면 OBS 를 건드리지 않고 경고만 — 클립은 저장 뒤 이름 바꾸기에서 날짜 폴더로 옮긴다(<see cref="SaveClip"/>).
    /// </summary>
    private static void PointRecordDirectory(string dir, QANightlyLocalSettings s, string share, Action<string> log)
    {
        string busy = BusyOutputs(s);
        if (busy == null)
        {
            Warn(dir, log, $"OBS 연결 실패 — 녹화 경로를 날짜 폴더로 못 바꿨다(클립은 저장 뒤 날짜 폴더로 옮긴다) · {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)}");
            return;
        }
        if (busy.Length > 0)
        {
            Warn(dir, log, $"OBS {busy} 중이라 녹화 경로를 바꾸지 않았다(켜진 출력은 건드리지 않는다) — 클립은 저장 뒤 날짜 폴더로 옮긴다. " +
                           "다음 밤 전에 OBS 출력을 끄고 시작할 것");
            return;
        }
        QAObsResult cur = Obs(s, QAObsProtocol.GetRecordDirectory);
        if (!cur.Ok)
        {
            Warn(dir, log, $"OBS 녹화 경로 확인 실패(GetRecordDirectory — OBS 30+ 필요) — 경로를 바꾸지 않았다(클립은 저장 뒤 날짜 폴더로 옮긴다) · {cur.Error}");
            return;
        }
        string saved = Path.Combine(QANetSim88.OwnRoot, ObsRecordDirFile);
        string current = (cur.RecordDirectory ?? "").Trim();
        if (!File.Exists(saved))
        {
            if (IsUnder(current, s.ShareRoot))
                log($"⚠ OBS 녹화 경로가 이미 공유 폴더 안이다({current}) — 원래 경로로 보관하지 않는다(끝나도 되돌리지 않음). OBS 설정에서 원래 경로를 손으로 맞출 것");
            else if (current.Length > 0)
                TryWrite(saved, current + "\n");
        }
        if (PathsEqual(current, share))
        {
            log($"OBS 녹화 경로 = 공유 날짜 폴더(이미) — {share}");
            return;
        }
        QAObsResult r = Obs(s, QAObsProtocol.SetRecordDirectory, QAObsProtocol.RecordDirectoryData(share));
        if (r.Ok)
            log($"OBS 녹화 경로 → 공유 날짜 폴더 {share} (원래 {(current.Length > 0 ? current : "모름")} — 배치 끝에 되돌린다)");
        else
            Warn(dir, log, $"OBS 녹화 경로 바꾸기 실패(SetRecordDirectory) — 클립은 저장 뒤 날짜 폴더로 옮긴다 · {r.Error}");
    }

    /// <summary>배치 끝 — 보관해 둔 원래 녹화 경로로 되돌린다(출력이 꺼져 있을 때만). 되돌리면 보관 파일을 지운다.</summary>
    private static void RestoreRecordDirectory(string dir, Action<string> log)
    {
        string saved = Path.Combine(QANetSim88.OwnRoot, ObsRecordDirFile);
        string original = (SafeRead(saved) ?? "").Trim();
        if (original.Length == 0)
            return;
        QANightlyLocalSettings s = LoadLocal();
        string busy = BusyOutputs(s);
        if (busy == null || busy.Length > 0)
        {
            Warn(dir, log, $"OBS 녹화 경로를 원래({original})로 못 되돌렸다 — {(busy == null ? "OBS 연결 실패" : $"OBS {busy} 중")}. 다음 배치 끝에 다시 시도(또는 OBS 설정에서 손으로)");
            return;
        }
        QAObsResult r = Obs(s, QAObsProtocol.SetRecordDirectory, QAObsProtocol.RecordDirectoryData(original));
        if (r.Ok)
        {
            TryDelete(saved);
            log($"OBS 녹화 경로 되돌림 — {original}");
        }
        else
            Warn(dir, log, $"OBS 녹화 경로 되돌리기 실패 — {original} {r.Error}. 다음 배치 끝에 다시 시도(또는 OBS 설정에서 손으로)");
    }

    /// <summary>
    /// 화질 적용 — 설정이 비어 있으면 아무것도 바꾸지 않는다. 해상도·fps(SetVideoSettings)는 녹화·리플레이 버퍼가 꺼져 있을 때만,
    /// 비트레이트·리플레이 버퍼 길이는 프로필 파라미터(출력 모드별 키 — <see cref="QAObsProfileParams"/>).
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

    /// <summary>
    /// 리플레이 버퍼 시작 → <c>GetReplayBufferStatus</c> 로 실제로 켜졌는지 확인(1초 간격 3번). OBS 설정 창이 열려 있으면 시작 요청이 성공만 돌려주고
    /// 실제로는 안 켜진다(2026-10-27 실측). 안 켜졌으면 경고(loop.log·리포트 메모)만 남기고 표시 파일을 쓰지 않는다 — 다음 판 시작 때 다시 시도.
    /// </summary>
    private static bool StartReplay(string dir, QANightlyLocalSettings s, Action<string> log)
    {
        QAObsResult r = Obs(s, QAObsProtocol.StartReplayBuffer);
        if (!r.Ok)
        {
            Warn(dir, log, $"리플레이 버퍼 시작 실패(QA 는 계속 — 판마다 다시 시도. OBS 설정 → 출력 → 리플레이 버퍼 사용 체크 후 OBS 재시작) — {QAObsProtocol.Endpoint(s.ObsHost, s.ObsPort)} {r.Error}");
            return false;
        }
        bool active = false;
        for (int i = 0; i < ReplayVerifyTries && !active; i++)
        {
            if (i > 0)
                Thread.Sleep(1000);
            QAObsResult st = Obs(s, QAObsProtocol.GetReplayBufferStatus);
            active = st.Ok && st.OutputActive;
        }
        if (!active)
        {
            Warn(dir, log, "리플레이 버퍼가 실제로 안 켜졌다(시작 요청은 성공) — OBS 설정 창이 열려 있으면 닫을 것 · 출력 → 리플레이 버퍼 사용 체크 후 OBS 재시작. " +
                           "이대로면 클립이 없다 — 다음 판 시작 때 다시 시도");
            return false;
        }
        TryWrite(Path.Combine(dir, ReplayActiveFile), DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + "\n");
        log($"리플레이 버퍼 시작·확인(active){(r.AlreadyInState ? " (이미 켜져 있었다)" : "")} — 이벤트 뒤 {s.ReplayAfterSeconds:0.#}초에 저장 · 쿨다운 {s.ReplayCooldownSeconds:0.#}초 · " +
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
            // 판 녹화 없음. 버퍼가 아직 안 켜졌으면(배치 시작 때 OBS 연결 실패·설정 창 열림) 다시 시도.
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
        StopRecording(dir, run, retry, log);
        WriteSummary(dir, batch, "", log);
    }

    /// <summary>배치 끝(완료·멈춤) — 남은 녹화를 멈추고(리플레이면 남은 클립 저장·리플레이 버퍼 정지) OBS 녹화 경로를 되돌린 뒤 로컬 요약.</summary>
    public static void BatchEnded(string dir, string loopState, Action<string> log)
    {
        Dictionary<string, string> batch = ReadBatch(dir);
        if (!IsNightly(batch))
            return;
        StopRecording(dir, -1, -1, log);
        if (Val(batch, "record") == QANightlyLocalSettings.RecordReplay)
            EndReplay(dir, log);
        RestoreRecordDirectory(dir, log);
        WriteSummary(dir, batch, loopState, log);
    }

    /// <summary>
    /// progress.txt 완료 키(<see cref="QANightlyCompletion"/>) — 야간 배치면 공유 날짜 폴더·이름·제안 리포트 이름을 채운다. OBS 호출 없음(에디터 시작 중에도 부른다).
    /// </summary>
    public static List<KeyValuePair<string, string>> CompletionKeys(string dir, string end, string reason, int runsDone, int runsTotal, int runsError)
    {
        Dictionary<string, string> batch = ReadBatch(dir);
        string share = "", name = "", report = "";
        if (IsNightly(batch))
        {
            name = Val(batch, QANightlyHeader.KeySeedName);
            share = Val(batch, "share_dir");
            var existing = new List<string>();
            try
            {
                if (share.Length > 0 && Directory.Exists(share))
                    existing.AddRange(Directory.GetFiles(share, "report-*.md"));
            }
            catch { /* 목록 실패 — 첫 이름을 제안(에이전트가 쓰기 직전에 다시 확인) */ }
            report = QANightlyPaths.ReportFileName(name, existing);
        }
        return QANightlyCompletion.Format(end, reason, DateTime.Now, dir, runsDone, runsTotal, runsError, share, name, report);
    }

    private static void StopRecording(string dir, int run, int retry, Action<string> log)
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
        AppendLine(Path.Combine(dir, QANightlyPaths.RecordingsFile), rec.Format());
        log($"녹화 정지 — 판 {rec.Run}{(rec.Retry > 0 ? " 재실행" : "")} · 영상 {(rec.Path.Length > 0 ? rec.Path : "없음")}");
    }

    // ── 이벤트 (할 일 120) ───────────────────────────────────

    /// <summary>도메인 리로드 뒤 다시 읽는 배치 문맥(배치 머리·PC 설정·클립 예약).</summary>
    private sealed class Live
    {
        public string Dir;
        public bool Nightly;
        public string Mode = "";
        public string Seed = "";
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
            l.ShareDir = Val(batch, "share_dir");
            l.Settings = LoadLocal();
            l.ClipEnabled = QANightlyEvents.ParseEnabled(l.Settings.ClipEvents);
            l.Planner = QANightlyClipPlanner.Parse(SafeRead(Path.Combine(dir, PendingFile)));
        }
        _live = l;
        return l;
    }

    /// <summary>[메인 에디터 로그 콜백] 판 Play 중 호스트 로그 한 줄 — 이벤트 표에 맞으면 시각표에 쓰고(리플레이 모드면 클립 예약). 여기서는 로그를 내지 않는다(재귀).</summary>
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

    /// <summary>[루프 틱] 리플레이 모드 — 예약 시각이 된 클립을 저장한다. Play 중이면 스레드 풀에서(Play 를 막지 않게), 아니면 바로.</summary>
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
        public string Dir, Seed, ShareDir, Host, Password;
        public int Port, Run, Retry;
        public List<string> Names;
    }

    private static ClipJob Job(Live l, List<string> names, int run, int retry) => new ClipJob
    {
        Dir = l.Dir, Seed = l.Seed, ShareDir = l.ShareDir ?? "",
        Host = l.Settings.ObsHost, Port = l.Settings.ObsPort, Password = l.Settings.ObsPassword,
        Run = run, Retry = retry, Names = names,
    };

    /// <summary>
    /// SaveReplayBuffer → 경로(ReplayBufferSaved 이벤트, 없으면 GetLastReplayBufferReplay) → <c>&lt;이름&gt;_&lt;HHmmss&gt;_r&lt;판&gt;_&lt;이벤트&gt;</c> 로 이름 바꿔
    /// 공유 날짜 폴더 바로 아래로(공유 모드가 아니면 그 자리에서 이름만) → clips.txt. 녹화 경로가 이미 날짜 폴더면 이름 바꾸기, 아니면 옮기기 — 같은 한 번의 Move. 실패는 경고만.
    /// </summary>
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
                string targetDir = j.ShareDir.Length > 0 ? j.ShareDir : Path.GetDirectoryName(path);
                string target = QANightlyClipName.Unique(targetDir,
                    QANightlyClipName.FileName(j.Seed, DateTime.Now, j.Run, j.Retry, j.Names, Path.GetExtension(path)), File.Exists);
                string err = MoveWithRetry(path, target);
                if (err == null)
                    rec.Path = target;
                else
                {
                    rec.Note += " · 이름 바꾸기 실패";
                    say($"⚠ 클립 이름 바꾸기·옮기기 실패(원래 자리·이름 유지) — {path} → {target} {err}");
                }
            }
            AppendLine(Path.Combine(j.Dir, QANightlyPaths.ClipsFile), rec.Format());
            say($"리플레이 클립 — 판 {j.Run}{(j.Retry > 0 ? " 재실행" : "")} [{events}] {(rec.Path.Length > 0 ? rec.Path : "경로 없음")}");
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
                string d = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(d))
                    Directory.CreateDirectory(d);
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

    // ── 요약 ─────────────────────────────────────────────────

    /// <summary>로컬 아침 요약(nightly-summary.md)을 배치 폴더에 쓴다. 공유 폴더에는 쓰지 않는다 — 공유 리포트는 에이전트가 쓴다.</summary>
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

    // ── 도구 ─────────────────────────────────────────────────

    private static QAObsResult Obs(QANightlyLocalSettings s, string requestType, string requestData = null) =>
        QAObsClient.Request(s.ObsHost, s.ObsPort, s.ObsPassword, requestType, requestData, 5.0, null);

    /// <summary>경고 — loop.log 에 ⚠ 로, 리포트(report.html 루프 메모 — logs/notes.txt)에도 남긴다.</summary>
    private static void Warn(string dir, Action<string> log, string msg)
    {
        log("⚠ " + msg);
        AppendLine(Path.Combine(dir, QABoss103Batch.LogFolder, QABoss103ReportFiles.NotesFile), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [야간·OBS] {msg}");
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root)
    {
        string p = Norm(path), r = Norm(root);
        return p.Length > 0 && r.Length > 0 && (p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string Norm(string p) => (p ?? "").Trim().Replace('\\', '/').TrimEnd('/');

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
        try
        {
            string d = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(d))
                Directory.CreateDirectory(d);
            File.WriteAllText(path, text);
        }
        catch { /* 표시 실패 — 정지 때 녹화가 남을 수 있다(OBS 가 다음 시작에 500 을 돌려준다) */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* 무시 */ }
    }
}
#endif
