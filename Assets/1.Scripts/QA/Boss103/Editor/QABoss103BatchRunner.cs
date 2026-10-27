#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 103 C6 — 90판 무인 연속 루프(에디터 상태 기계). 할 일 119 C3 — 같은 루프로 30판(<c>Start 30</c>, 폴더 <c>119-…</c>). 판마다 Play 를 새로 시작하고(<c>QAScenario48Menu.Run</c> — 툴바 Play 와 같은 MPPM 경로,
/// isPlaying 직접 토글 없음) 판 기록(runs.csv 새 행)을 보면 Play 를 멈춘 뒤 다음 판으로 간다.
/// <list type="bullet">
/// <item>배치 폴더 <c>C:/UnityProject/QA/103-yyyyMMdd_HHmm/</c> — CSV 4종·parts/·hb/(심장박동)·report.html·progress.txt(루프 상태)·batch.txt(배치 머리)·logs/.</item>
/// <item>상태는 progress.txt 라 Play 진입·정지 도메인 리로드를 넘는다. 에디터를 다시 켜면 자동으로 이어 가지 않는다 — <c>Resume Last</c>.</item>
/// <item>다음 판 = runs.csv 로 정한다(<see cref="QABoss103Batch.TryNext"/>) — 끝난 판은 건너뛰고, 90판 뒤 오류 판만 1회 재실행.</item>
/// <item>판 오류(<see cref="QABoss103Batch.Judge"/> — 심장박동 120초·Combat 전 8분·Combat 30+3분, Play 시작 실패·판 기록 전 정지)는 runs.csv 에 Error 행을 쓰고
///   정리 → 10초(연속 3판마다 60초) 뒤 다음 판. C7d: 판 Play 중 클라 접속 거절(<c>NetworkConfig mismatch</c>) 로그를 보면 바로 Error(net_config_mismatch).</item>
/// <item>C7d — 정상 판 뒤에도 판마다 가상 플레이어 비활성 → 재활성 → Launched 확인 뒤 다음 판(접속 거절 예방).</item>
/// <item>데이터 출처: 판마다 시작 전에 툴바 출처를 테이블로 맞추고, 판 안에서 호스트가 런타임 적용 경로로 확인한다(<c>QABoss103Heartbeat.RuntimeDataSource</c>).</item>
/// </list>
/// 순수 규칙은 <see cref="QABoss103Batch"/>, 리포트는 <see cref="QABoss103Report"/>.
/// <para>EditorPrefs 는 레지스트리 하나를 모든 에디터 프로세스가 같이 쓴다(쓰기는 즉시, 읽기는 프로세스 캐시). MPPM 가상 플레이어(클론)는
/// SessionState 가 빈 새 에디터라 정적 생성자의 '에디터 재시작' 검사가 Running 을 꺼 버렸다(C7a — 판 1 오류 뒤 재활성에서 루프 정지).
/// 그래서 클론에서는 아무것도 하지 않고, 키는 워크트리 범위(<see cref="DevBootTarget.ScopedKey"/>)로 둔다.</para>
/// </summary>
[InitializeOnLoad]
public static class QABoss103BatchRunner
{
    private const string Tag = "[QA103/Batch]";
    private const string Root = "Tools/QA/103/Batch/";
    private static readonly string DirKey = DevBootTarget.ScopedKey("QA.103.Batch.Dir");
    private static readonly string RunningKey = DevBootTarget.ScopedKey("QA.103.Batch.Running");
    private const string LiveSessionKey = "QA.103.Batch.Live";
    private const double TickSeconds = 0.5;
    private const double RunningCheckSeconds = 2.0;
    private const double VpRecycleOffSeconds = 10.0;
    private const double VpSettleSeconds = 15.0;
    private const double VpReadyTimeoutSeconds = 180.0;
    private const double StopRetrySeconds = 30.0;

    private static double _nextTick;
    private static double _nextRunningCheck;
    private static Dictionary<string, string> _state;
    private static string _stateDir;

    static QABoss103BatchRunner()
    {
        if (IsVirtualPlayer)
            return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        if (Running && SessionState.GetString(LiveSessionKey, "") != "1")
        {
            // 에디터 재시작(SessionState 비어 있음) — 사람 없이 저절로 Play 를 시작하지 않는다.
            EditorPrefs.SetBool(RunningKey, false);
            Debug.LogWarning($"{Tag} 에디터를 다시 켜서 연속 루프를 멈춰 두었다 — 이어 하려면 {Root}Resume Last ({Dir}).");
        }
    }

    /// <summary>C7d — 이번 판 Play 중 본 접속 거절 로그(호스트 = 메인 에디터가 NGO 경고를 낸다). null = 없음. 판 시작마다 지운다.</summary>
    private static string _netMismatch;

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (_netMismatch == null && Running && EditorApplication.isPlaying && QABoss103Batch.IsNetConfigMismatchLog(condition))
            _netMismatch = condition.Length > 160 ? condition.Substring(0, 160) : condition;
    }

    /// <summary>MPPM 가상 플레이어 에디터(클론) — 루프는 메인 에디터만 돈다.</summary>
    private static bool IsVirtualPlayer => !Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;

    private static bool Running => EditorPrefs.GetBool(RunningKey, false);
    private static string Dir => EditorPrefs.GetString(DirKey, "");
    private static DateTime Now => DateTime.UtcNow;

    // ── 메뉴 ─────────────────────────────────────────────────

    [MenuItem(Root + "Start 90", priority = 100)]
    public static void Start90() =>
        StartBatch(QABoss103Batch.Prefix103, QABoss103Batch.TotalRuns, "할 일 103 3인 보스전(웰즈&23호 B) 밸런싱 QA", null);

    /// <summary>
    /// 할 일 119 C3 — 같은 조건 30판(보통→숙련→초보 번갈아 = 단계마다 10판, 기준 시드 103 그대로). 폴더 <c>119-yyyyMMdd_HHmm</c>.
    /// 리포트는 <see cref="CompareDir"/>(지난 103 90판) 와 단계별 요약을 나란히 보인다.
    /// </summary>
    [MenuItem(Root + "Start 30 (119 — 단계별 10판)", priority = 99)]
    public static void Start30() =>
        StartBatch(QABoss103Batch.Prefix119, QABoss103Batch.TotalRuns119, "할 일 119 3인 보스전(웰즈&23호 B) QA 봇 2차 — 30판", CompareDir);

    [MenuItem(Root + "Compare Folder (119 비교 기준)...", priority = 106)]
    public static void ChooseCompareDir()
    {
        string dir = EditorUtility.OpenFolderPanel("119 리포트 비교 기준 배치 폴더(지난 103)", QAScenario103Menu.QARoot, "");
        if (string.IsNullOrEmpty(dir))
            return;
        EditorPrefs.SetString(CompareDirKey, dir.Replace('\\', '/'));
        Debug.Log($"{Tag} 비교 기준 배치 = {CompareDir} (다음 Start 30 부터 batch.txt compare_dir 로 남는다 — 진행 중 배치는 그 폴더 batch.txt 를 고친다).");
    }

    private static readonly string CompareDirKey = DevBootTarget.ScopedKey("QA.103.Batch.CompareDir");

    /// <summary>할 일 119 C3 — 비교 기준 배치 폴더(설정값, 기본 <see cref="QABoss103Batch.CompareDirDefault"/>).</summary>
    public static string CompareDir => EditorPrefs.GetString(CompareDirKey, QABoss103Batch.CompareDirDefault);

    private static void StartBatch(string prefix, int total, string title, string compareDir)
    {
        if (!CanStart())
            return;
        DateTime at = DateTime.Now;
        string dir = $"{QAScenario103Menu.QARoot}/{QABoss103Batch.BatchFolderName(at, prefix)}";
        if (Directory.Exists(dir))
            dir += at.ToString("ss", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(dir);
        DataSourcePlayMode.Current = DataSource.Table;
        var batch = new List<KeyValuePair<string, string>>
        {
            KV("title", title),
            KV("started_at", at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            KV("seed", QAScenario103Menu.BaseSeed.ToString(CultureInfo.InvariantCulture)),
            KV("total", total.ToString(CultureInfo.InvariantCulture)),
            KV("data_source_mode", QABoss103Batch.SourceTable),
            KV("xlsx_svn_rev", QABoss103RunCsv.XlsxRevision()),
            KV("git_commit", QABoss103RunCsv.GitCommit()),
            KV("git_branch", QABoss103RunCsv.Shell("git", "rev-parse --abbrev-ref HEAD")),
            KV("unity_version", Application.unityVersion),
        };
        if (!string.IsNullOrEmpty(compareDir))
            batch.Add(KV("compare_dir", compareDir));
        File.WriteAllText(Path.Combine(dir, QABoss103Batch.BatchFile), QABoss103Batch.FormatKeyValues(batch));
        _state = new Dictionary<string, string>(StringComparer.Ordinal);
        _stateDir = dir;
        Set("state", "prepare");
        Set("seed", QAScenario103Menu.BaseSeed.ToString(CultureInfo.InvariantCulture));
        Set("total", total.ToString(CultureInfo.InvariantCulture));
        SetTime("notBefore", Now);
        Arm(dir);
        Log($"배치 시작 {dir} — {total}판 · 기준 시드 {Get("seed")} · 데이터 출처 테이블 · xlsx r{Find(batch, "xlsx_svn_rev")} · git {Find(batch, "git_commit")}" +
            (string.IsNullOrEmpty(compareDir) ? "" : $" · 비교 기준 {compareDir}{(Directory.Exists(compareDir) ? "" : "(폴더 없음 — 리포트에 비교 표 없음)")}"));
    }

    [MenuItem(Root + "Resume Last", priority = 101)]
    public static void ResumeLast()
    {
        string dir = Dir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            dir = LatestBatchDir();
        if (dir == null)
        {
            Debug.LogWarning($"{Tag} 이어 할 배치 폴더가 없다({QAScenario103Menu.QARoot}/103-*·119-*).");
            return;
        }
        Resume(dir);
    }

    [MenuItem(Root + "Resume (Choose Folder)...", priority = 102)]
    public static void ResumeChoose()
    {
        string dir = EditorUtility.OpenFolderPanel("이어 할 103 배치 폴더", QAScenario103Menu.QARoot, "");
        if (!string.IsNullOrEmpty(dir))
            Resume(dir);
    }

    [MenuItem(Root + "Stop After Current Run", priority = 103)]
    public static void StopAfterCurrent()
    {
        string dir = ActiveBatchDir();
        if (dir == null)
        {
            Debug.Log($"{Tag} 연속 루프가 돌고 있지 않고 끝나지 않은 배치도 없다.");
            return;
        }
        if (!Running)
        {
            // 루프가 꺼진 채 남은 배치 — 표시만 남긴다(Resume Last 가 stopAfter 를 0 으로 되돌리고 이어 간다).
            _stateDir = null;
            LoadState(dir);
            Set("stopAfter", "1");
            Log($"루프가 꺼져 있다(상태 {Get("state")}, 판 {Get("run")}) — 멈춤 표시만 남김. 이어 하려면 {Root}Resume Last.");
            return;
        }
        LoadState();
        Set("stopAfter", "1");
        Log($"지금 판(판 {Get("run")}·재실행 {Get("retry")}, 상태 {Get("state")})이 끝나면 멈춘다.");
    }

    /// <summary>돌고 있는 배치 폴더, 아니면 progress state 가 끝나지 않은(<see cref="QABoss103Batch.IsActiveState"/>) 최근 배치. 없으면 null.</summary>
    private static string ActiveBatchDir()
    {
        string dir = !string.IsNullOrEmpty(Dir) && Directory.Exists(Dir) ? Dir : LatestBatchDir();
        if (dir == null)
            return null;
        if (Running)
            return dir;
        string path = Path.Combine(dir, QABoss103Batch.StateFile);
        if (!File.Exists(path))
            return null;
        Dictionary<string, string> kv = QABoss103Batch.ParseKeyValues(File.ReadAllText(path));
        return QABoss103Batch.IsActiveState(kv.TryGetValue("state", out string s) ? s : "") ? dir : null;
    }

    [MenuItem(Root + "Open Report", priority = 104)]
    public static void OpenReport()
    {
        string dir = !string.IsNullOrEmpty(Dir) && Directory.Exists(Dir) ? Dir : LatestBatchDir();
        if (dir == null)
        {
            Debug.LogWarning($"{Tag} 배치 폴더가 없다.");
            return;
        }
        string path = QABoss103ReportFiles.Write(dir, Running ? StateLabel() : "멈춤");
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    [MenuItem(Root + "Status", priority = 105)]
    public static void Status()
    {
        string dir = !string.IsNullOrEmpty(Dir) && Directory.Exists(Dir) ? Dir : LatestBatchDir();
        if (dir == null)
        {
            Debug.Log($"{Tag} 배치 없음.");
            return;
        }
        _stateDir = null;
        LoadState(dir);
        List<QABoss103Attempt> attempts = Attempts(dir);
        int total = Total;
        QABoss103Batch.Summarize(attempts, total, out int done, out int retried, out int errors, out int valid);
        bool more = QABoss103Batch.TryNext(attempts, total, out int nRun, out int nRetry);
        Debug.Log($"{Tag} {(Running ? "실행 중" : "멈춤")} · {dir} · 상태 {Get("state")} · 판 {Get("run")}/재실행 {Get("retry")} · 진행 {done}/{total}(+재실행 {retried}) · " +
                  $"오류 확정 {errors} · 통계 판 {valid} · 연속 오류 {Get("errStreak")} · 휴식 {Get("pauses")}회 · 다음 {(more ? $"판 {nRun}·재실행 {nRetry}" : "없음(끝)")} · " +
                  $"재실행 대상 [{string.Join(",", QABoss103Batch.RetryList(attempts, total))}] · 툴바 출처 {DataSourcePlayMode.Current} · 가상 플레이어 {string.Join("/", VpStates())}");
    }

    [MenuItem(Root + "Start 90", true)]
    [MenuItem(Root + "Start 30 (119 — 단계별 10판)", true)]
    [MenuItem(Root + "Resume Last", true)]
    [MenuItem(Root + "Resume (Choose Folder)...", true)]
    private static bool CanStartCheck() => !Running;

    [MenuItem(Root + "Stop After Current Run", true)]
    private static bool StopCheck() => ActiveBatchDir() != null;

    private static bool CanStart()
    {
        if (Running)
        {
            Debug.LogWarning($"{Tag} 이미 돌고 있다({Dir}) — Status 로 확인, 멈추려면 Stop After Current Run.");
            return false;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"{Tag} Play 중이다 — Tools/QA/Stop Active Scenario 로 먼저 정지할 것.");
            return false;
        }
        return true;
    }

    private static void Resume(string dir)
    {
        if (!CanStart())
            return;
        dir = dir.Replace('\\', '/');
        _stateDir = null;
        LoadState(dir);
        if (!File.Exists(Path.Combine(dir, QABoss103Batch.BatchFile)))
            Debug.LogWarning($"{Tag} {dir} 에 batch.txt 가 없다 — 기준 시드 {Get("seed", QAScenario103Menu.BaseSeed.ToString(CultureInfo.InvariantCulture))} 로 이어 간다.");
        if (Get("seed").Length == 0)
            Set("seed", QAScenario103Menu.BaseSeed.ToString(CultureInfo.InvariantCulture));
        if (Get("total").Length == 0)
            Set("total", QABoss103Batch.TotalRuns.ToString(CultureInfo.InvariantCulture));
        string was = Get("state");
        Set("state", "prepare");
        Set("stopAfter", "0");
        Set("recycle", "0");
        SetTime("notBefore", Now);
        Arm(dir);
        Log($"이어 하기 {dir} (이전 상태 {was}, 판 {Get("run")}·재실행 {Get("retry")} — 판 기록이 없는 판은 처음부터 다시).");
    }

    private static void Arm(string dir)
    {
        EditorPrefs.SetString(DirKey, dir);
        EditorPrefs.SetBool(RunningKey, true);
        SessionState.SetString(LiveSessionKey, "1");
        DataSourcePlayMode.Current = DataSource.Table;
        Save();
        _nextTick = 0;
    }

    private static string LatestBatchDir()
    {
        if (!Directory.Exists(QAScenario103Menu.QARoot))
            return null;
        // 103-·119- 배치 중 폴더 이름의 시각이 가장 늦은 것(접두어 순서와 무관).
        string latest = null, latestStamp = null;
        foreach (string d in Directory.GetDirectories(QAScenario103Menu.QARoot, "*-2*"))
        {
            string stamp = QABoss103Batch.BatchStamp(Path.GetFileName(d));
            if (stamp != null && (latestStamp == null || string.CompareOrdinal(stamp, latestStamp) > 0))
            {
                latest = d;
                latestStamp = stamp;
            }
        }
        return latest?.Replace('\\', '/');
    }

    // ── 상태 기계 ────────────────────────────────────────────

    private static void Tick()
    {
        if (!Running)
            return;
        double t = EditorApplication.timeSinceStartup;
        if (t < _nextTick)
            return;
        // C7f — Play 중 스크립트가 바뀌면 컴파일이 정지 뒤로 미뤄져 isCompiling 이 계속 true 다.
        // 그때도 판 끝 기록·상한 초과 판정과 Play 정지는 처리한다(멈춰야 컴파일이 돈다). 다른 단계는 컴파일 뒤.
        if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && !(EditorApplication.isPlaying && LoadState() && IsPlayStopState(Get("state"))))
            return;
        _nextTick = t + TickSeconds;
        try
        {
            if (!LoadState())
                return;
            Step();
        }
        catch (Exception e)
        {
            _nextTick = t + 5.0;
            Debug.LogError($"{Tag} 루프 예외(5초 뒤 다시): {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>컴파일 대기 중에도 돌리는 단계 — 판 감시(기록·상한 판정 → 정지 요청)와 정지 재시도뿐. Play 를 새로 켜는 단계는 빠진다.</summary>
    private static bool IsPlayStopState(string state) =>
        state == "running" || state == "ending" || state == "stopping" || state == "finishing";

    private static void Step()
    {
        switch (Get("state"))
        {
            case "prepare": Prepare(); break;
            case "activating": Activating(); break;
            case "starting": Starting(); break;
            case "running": RunningStep(); break;
            case "ending":
                if (Seconds("endAt") >= QABoss103Batch.EndToStopSeconds)
                    RequestStop("판 기록 확인");
                break;
            case "stopping": Stopping(false); break;
            case "finishing": Stopping(true); break;
            default:
                EditorPrefs.SetBool(RunningKey, false);
                break;
        }
    }

    private static void Prepare()
    {
        if (Now < (GetTime("notBefore") ?? Now))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // 남은 Play(이어 하기 직후 등) — 먼저 멈춘다(30초에 한 번만 누른다 — 전환 중에 다시 누르면 Play 로 되돌아갈 수 있다).
            if (EditorApplication.isPlaying && Seconds("strayStopAt") > StopRetrySeconds)
            {
                SetTime("strayStopAt", Now);
                Log("남은 Play 를 멈춘다.");
                QAScenarioPlay.Stop();
            }
            return;
        }
        if (Get("stopAfter") == "1")
        {
            Finish("사람 요청(Stop After Current Run)", "stopped");
            return;
        }
        List<QABoss103Attempt> attempts = Attempts(_stateDir);
        if (!QABoss103Batch.TryNext(attempts, Total, out _, out _))
        {
            Finish($"{Total}판 + 오류 판 재실행 끝", "done");
            return;
        }
        if (Get("recycle") == "1")
        {
            Log("판 사이 가상 플레이어 비활성 → 재활성(C7d — 판마다, 접속 거절 예방).");
            QAScenarioPlay.DeactivateVirtualPlayers();
            Set("recycle", "0");
            Set("vp", "off");
            SetTime("vpAt", Now);
            Set("state", "activating");
            return;
        }
        if (!VpReady())
        {
            Log($"가상 플레이어 켜기(상태 {string.Join("/", VpStates())}).");
            QAScenarioPlay.ActivateVirtualPlayers();
            Set("vp", "on");
            SetTime("vpAt", Now);
            Set("state", "activating");
            return;
        }
        StartAttempt();
    }

    private static void Activating()
    {
        double elapsed = Seconds("vpAt");
        if (Get("vp") == "off")
        {
            if (elapsed >= VpRecycleOffSeconds)
            {
                QAScenarioPlay.ActivateVirtualPlayers();
                Set("vp", "on");
                SetTime("vpAt", Now);
            }
            return;
        }
        if (Get("stopAfter") == "1")
        {
            Set("state", "prepare");
            return;
        }
        if (VpReady() && elapsed >= VpSettleSeconds)
            StartAttempt();
        else if (elapsed > VpReadyTimeoutSeconds)
        {
            Log($"가상 플레이어 준비 확인 안 됨 {VpReadyTimeoutSeconds:0}초(상태 {string.Join("/", VpStates())}) — 그대로 시작(3인 집결 실패면 그 판 오류).");
            StartAttempt();
        }
    }

    private static void StartAttempt()
    {
        List<QABoss103Attempt> attempts = Attempts(_stateDir);
        if (!QABoss103Batch.TryNext(attempts, Total, out int run, out int retry))
        {
            Set("state", "prepare");
            return;
        }
        if (DataSourcePlayMode.Current != DataSource.Table)
        {
            Log($"툴바 데이터 출처가 {DataSourcePlayMode.Current} — 테이블로 되돌린다.");
            DataSourcePlayMode.Current = DataSource.Table;
        }
        QABoss103Config cfg = Config(run, retry);
        DeleteAttemptFiles(cfg);
        int written = QAScenario103Menu.WriteConfig(cfg);
        Set("run", run.ToString(CultureInfo.InvariantCulture));
        Set("retry", retry.ToString(CultureInfo.InvariantCulture));
        Set("keyRows", RowCount(attempts, run, retry).ToString(CultureInfo.InvariantCulture));
        SetTime("attemptAt", Now);
        Set("attemptLocal", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
        Set("playAt", "0");
        Set("endAt", "0");
        Set("state", "starting");
        _netMismatch = null;
        Log($"판 {run}{(retry > 0 ? " 재실행" : "")} 시작 — 단계 {QABoss103Batch.TierLabel(cfg.BotTier)} · 판 시드 {cfg.RunSeed} · 설정 {written}곳");
        if (!QAScenario48Menu.Run(QALobbyScenarios.Boss103Id, 3))
            Log("Play 시작 요청이 거절됐다(이미 Play 중) — 시작 상한까지 기다린다.");
    }

    private static void Starting()
    {
        if (EditorApplication.isPlaying)
        {
            SetTime("playAt", Now);
            Set("state", "running");
            return;
        }
        if (Seconds("attemptAt") > QABoss103Batch.PlayStartTimeoutSeconds && !EditorApplication.isPlayingOrWillChangePlaymode)
            Fail(QABoss103Batch.ReasonPlayStart, $"Play 진입 안 됨 {QABoss103Batch.PlayStartTimeoutSeconds:0}초(저장 안 된 씬·테이블 오류로 진입 취소·대화상자 — 콘솔 확인)", "na", false);
    }

    private static void RunningStep()
    {
        double t = EditorApplication.timeSinceStartup;
        if (t < _nextRunningCheck)
            return;
        _nextRunningCheck = t + RunningCheckSeconds;
        int run = GetInt("run"), retry = GetInt("retry");
        bool rowSeen = RowCount(Attempts(_stateDir), run, retry) > GetInt("keyRows");
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (rowSeen)
                AfterStop();
            else
                Fail(QABoss103Batch.ReasonPlayStopped, "판 기록 전에 Play 가 멈춤(사람·외부·크래시)", HostSource(run, retry), false);
            return;
        }
        if (rowSeen)
        {
            SetTime("endAt", Now);
            Set("state", "ending");
            return;
        }
        if (_netMismatch != null)
        {
            // C7d — 클라 접속 거절. 집결 상한(로비 92초)까지 기다리지 않고 바로 끝낸다(다음 판 전 가상 플레이어 재시작).
            Fail(QABoss103Batch.ReasonNetConfigMismatch, $"클라 접속 거절 로그 — {_netMismatch}", HostSource(run, retry), true);
            _netMismatch = null;
            return;
        }
        QABoss103Watch w = Watch(run, retry, out string phase, out string detail);
        string reason = QABoss103Batch.Judge(w);
        if (reason != null)
            Fail(reason, $"단계 {phase} · Play {w.SincePlay:F0}s · {detail}", HostSource(run, retry), true);
    }

    private static void Fail(string reason, string note, string source, bool stopPlay)
    {
        QABoss103Config cfg = Config(GetInt("run"), GetInt("retry"));
        DateTime started = DateTime.TryParse(Get("attemptLocal"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime s) ? s : DateTime.Now;
        QABoss103RunCsv.WriteLoopError(cfg, reason, note, source, started);
        Log($"판 {cfg.Run}{(cfg.Retry > 0 ? " 재실행" : "")} 오류 {reason} — {note}");
        if (stopPlay && EditorApplication.isPlaying)
            RequestStop($"오류 {reason}");
        else
            AfterStop();
    }

    private static void RequestStop(string why)
    {
        SetTime("stopAt", Now);
        Set("state", Get("state") == "finishing" ? "finishing" : "stopping");
        Log($"Play 정지 — {why}");
        QAScenarioPlay.Stop();
    }

    private static void Stopping(bool finishing)
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (finishing)
                Cleanup(Get("finishReason"), Get("finishState"));
            else
                AfterStop();
            return;
        }
        if (EditorApplication.isPlaying && Seconds("stopAt") > StopRetrySeconds)
        {
            SetTime("stopAt", Now);
            Log("Play 가 아직 안 멈췄다 — 정지 다시 요청.");
            QAScenarioPlay.Stop();
        }
    }

    /// <summary>판 1회가 끝나고 Play 가 멈춘 뒤 — 결과로 연속 오류·쉬는 시간을 정하고 리포트를 다시 만든다.</summary>
    private static void AfterStop()
    {
        int run = GetInt("run"), retry = GetInt("retry");
        List<QABoss103Attempt> attempts = Attempts(_stateDir);
        QABoss103Attempt a = QABoss103Batch.Effective(attempts, run, retry);
        bool error = a == null || a.IsError;
        int streak = error ? GetInt("errStreak") + 1 : 0;
        Set("errStreak", streak.ToString(CultureInfo.InvariantCulture));
        // C7d — 정상 판 뒤에도 가상 플레이어를 재시작한다(판 2 NetworkConfig mismatch 예방).
        Set("recycle", "1");
        int cooldown = QABoss103Batch.CooldownSeconds(error, streak);
        if (error && QABoss103Batch.IsStreakPause(streak))
        {
            Set("pauses", (GetInt("pauses") + 1).ToString(CultureInfo.InvariantCulture));
            Note($"연속 {streak}판 오류(마지막 판 {run}{(retry > 0 ? " 재실행" : "")}) → {cooldown}초 쉬고 계속");
        }
        Log($"판 {run}{(retry > 0 ? " 재실행" : "")} 끝 — {(a != null ? a.OutcomeText : "기록 없음")}{(error ? $" (오류 {QABoss103Batch.ReasonOf(a)})" : "")} · 다음까지 {cooldown}초");
        SetTime("notBefore", Now.AddSeconds(cooldown));
        Set("state", "prepare");
        WriteReport();
    }

    private static void Finish(string why, string endState)
    {
        Set("finishReason", why);
        Set("finishState", endState);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Set("state", "finishing");
            if (EditorApplication.isPlaying)
                RequestStop("배치 끝");
            return;
        }
        Cleanup(why, endState);
    }

    private static void Cleanup(string why, string endState)
    {
        QAScenarioPlay.DeactivateVirtualPlayers();
        QAScenario48Menu.Clear();
        QAScenario103Menu.DeleteConfig();
        Set("state", string.IsNullOrEmpty(endState) ? "stopped" : endState);
        SetTime("endedAt", Now);
        EditorPrefs.SetBool(RunningKey, false);
        string path = WriteReport();
        Log($"배치 {(endState == "done" ? "완료" : "멈춤")} — {why} · 가상 플레이어 비활성·시나리오 모드 해제·설정 파일 삭제 · 리포트 {path}");
    }

    // ── 판 도구 ──────────────────────────────────────────────

    private static int Total => Math.Max(1, GetInt("total", QABoss103Batch.TotalRuns));

    private static QABoss103Config Config(int run, int retry) => new QABoss103Config
    {
        Seed = GetInt("seed", QABoss103Config.DefaultSeed),
        Run = Math.Max(1, run),
        Tier = null,
        OutDir = _stateDir,
        Retry = retry,
        Batch = 1,
    };

    private static List<QABoss103Attempt> Attempts(string dir)
    {
        string path = Path.Combine(dir, QABoss103Csv.RunsFile);
        if (!File.Exists(path))
            return new List<QABoss103Attempt>();
        return QABoss103Batch.ReadAttempts(QABoss103CsvTable.Parse(QABoss103Heartbeat.ReadShared(path)));
    }

    private static int RowCount(List<QABoss103Attempt> attempts, int run, int retry)
    {
        int n = 0;
        foreach (QABoss103Attempt a in attempts)
            if (a.Run == run && a.Retry == retry)
                n++;
        return n;
    }

    /// <summary>이 (판, 재실행)의 지난 심장박동·끝 신호 파일을 지운다(이어 하기로 같은 판을 다시 돌 때 섞이지 않게). 판 파일(parts)은 봇이 덮어쓴다.</summary>
    private static void DeleteAttemptFiles(QABoss103Config cfg)
    {
        try
        {
            string hb = Path.Combine(cfg.OutDir, QABoss103Batch.BeatFolder);
            if (Directory.Exists(hb))
                foreach (string f in Directory.GetFiles(hb, QABoss103Batch.BeatPrefix(cfg.Run, cfg.Retry) + "*.txt"))
                    File.Delete(f);
            string end = QABoss103PartFile.EndPath(cfg.OutDir, cfg.Run, cfg.Retry);
            if (File.Exists(end))
                File.Delete(end);
        }
        catch (Exception e)
        {
            Log($"지난 판 파일 정리 실패: {e.Message}");
        }
    }

    private static Dictionary<string, Dictionary<string, string>> Beats(int run, int retry)
    {
        var d = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string dir = Path.Combine(_stateDir, QABoss103Batch.BeatFolder);
        if (!Directory.Exists(dir))
            return d;
        string prefix = QABoss103Batch.BeatPrefix(run, retry);
        foreach (string path in Directory.GetFiles(dir, prefix + "*.txt"))
        {
            try { d[Path.GetFileNameWithoutExtension(path).Substring(prefix.Length)] = QABoss103Batch.ParseKeyValues(QABoss103Heartbeat.ReadShared(path)); }
            catch { /* 쓰는 중 — 다음 검사 */ }
        }
        return d;
    }

    private static QABoss103Watch Watch(int run, int retry, out string phase, out string detail)
    {
        DateTime now = Now;
        Dictionary<string, Dictionary<string, string>> beats = Beats(run, retry);
        var w = new QABoss103Watch
        {
            SincePlay = Seconds("playAt"),
            SinceHostBeat = double.NaN,
            SinceOldestCloneBeat = double.NaN,
            SinceCombat = double.NaN,
        };
        phase = "na";
        var parts = new List<string>();
        foreach (KeyValuePair<string, Dictionary<string, string>> kv in beats)
        {
            double age = QABoss103Batch.Seconds(QABoss103Batch.FromTicks(Val(kv.Value, "beat")), now);
            double combat = QABoss103Batch.Seconds(QABoss103Batch.FromTicks(Val(kv.Value, "combat")), now);
            parts.Add($"{kv.Key}:{Val(kv.Value, "phase")}:{(double.IsNaN(age) ? "na" : age.ToString("F0", CultureInfo.InvariantCulture) + "s")}");
            if (kv.Key == "host")
            {
                w.SinceHostBeat = age;
                phase = Val(kv.Value, "phase");
                if (!double.IsNaN(combat))
                    w.SinceCombat = combat;
            }
            else if (!double.IsNaN(age) && (double.IsNaN(w.SinceOldestCloneBeat) || age > w.SinceOldestCloneBeat))
                w.SinceOldestCloneBeat = age;
        }
        detail = parts.Count > 0 ? "박동 " + string.Join(",", parts) : "박동 파일 없음";
        return w;
    }

    private static string HostSource(int run, int retry) =>
        Beats(run, retry).TryGetValue("host", out Dictionary<string, string> kv) && kv.TryGetValue("ds", out string ds) ? ds : "na";

    private static string Val(Dictionary<string, string> d, string key) => d.TryGetValue(key, out string v) ? v : "";

    // ── 가상 플레이어(MPPM 내부 API — QAScenarioPlay 와 같은 리플렉션) ─────

    private static List<string> VpStates()
    {
        var list = new List<string>();
        Type mppm = null;
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            if ((mppm = a.GetType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode")) != null)
                break;
        foreach (string prop in new[] { "PlayerTwo", "PlayerThree" })
        {
            object player = mppm?.GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            object state = player?.GetType().GetProperty("PlayerState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(player);
            list.Add(state?.ToString() ?? "na");
        }
        return list;
    }

    /// <summary>가상 플레이어 2·3 이 떠 있고(Launched·Communicative) 클론 폴더가 2개 이상.</summary>
    private static bool VpReady()
    {
        if (QANetSim88.CloneRoots().Count < 2)
            return false;
        foreach (string s in VpStates())
            if (s != "Launched" && s.IndexOf("Communicative", StringComparison.Ordinal) < 0)
                return false;
        return true;
    }

    // ── 상태 파일 ────────────────────────────────────────────

    private static bool LoadState(string dir = null)
    {
        dir = dir ?? Dir;
        if (string.IsNullOrEmpty(dir))
            return false;
        if (_state != null && _stateDir == dir)
            return true;
        string path = Path.Combine(dir, QABoss103Batch.StateFile);
        _state = File.Exists(path) ? QABoss103Batch.ParseKeyValues(File.ReadAllText(path)) : new Dictionary<string, string>(StringComparer.Ordinal);
        _stateDir = dir;
        return true;
    }

    private static void Save()
    {
        if (_state == null || string.IsNullOrEmpty(_stateDir))
            return;
        Directory.CreateDirectory(_stateDir);
        string path = Path.Combine(_stateDir, QABoss103Batch.StateFile);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, QABoss103Batch.FormatKeyValues(_state));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tmp, path);
    }

    private static string Get(string key, string fallback = "") =>
        _state != null && _state.TryGetValue(key, out string v) ? v : fallback;

    private static int GetInt(string key, int fallback = 0) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static void Set(string key, string value)
    {
        if (_state == null)
            return;
        _state[key] = value ?? "";
        _state["updated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        Save();
    }

    private static DateTime? GetTime(string key) => QABoss103Batch.FromTicks(Get(key));
    private static void SetTime(string key, DateTime utc) => Set(key, QABoss103Batch.Ticks(utc));

    /// <summary>그 시각부터 지금까지 초. 시각이 없으면 아주 큰 값(= 이미 지남).</summary>
    private static double Seconds(string key)
    {
        double s = QABoss103Batch.Seconds(GetTime(key), Now);
        return double.IsNaN(s) ? double.MaxValue : s;
    }

    private static string StateLabel() => $"{Get("state")} · 판 {Get("run")}{(Get("retry") == "1" ? " 재실행" : "")}";

    private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);

    private static string Find(List<KeyValuePair<string, string>> list, string key)
    {
        foreach (KeyValuePair<string, string> kv in list)
            if (kv.Key == key)
                return kv.Value;
        return "";
    }

    // ── 로그·리포트 ──────────────────────────────────────────

    private static void Log(string message)
    {
        Debug.Log($"{Tag} {message}");
        AppendLine(QABoss103Batch.LogFile, message);
    }

    /// <summary>리포트에 보일 루프 메모(연속 오류 휴식 등) — logs/notes.txt.</summary>
    private static void Note(string message)
    {
        Log(message);
        AppendLine(QABoss103ReportFiles.NotesFile, message);
    }

    private static void AppendLine(string file, string message)
    {
        if (string.IsNullOrEmpty(_stateDir))
            return;
        try
        {
            string dir = Path.Combine(_stateDir, QABoss103Batch.LogFolder);
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, file), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
        }
        catch { /* 로그 실패로 루프를 멈추지 않는다 */ }
    }

    private static string WriteReport()
    {
        try
        {
            return QABoss103ReportFiles.Write(_stateDir, Running ? StateLabel() : Get("state"));
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 리포트 생성 실패: {e.GetType().Name}: {e.Message}");
            return "";
        }
    }
}
#endif
