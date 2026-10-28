#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩 10회 배치(호스트 1명, MPPM 가상 플레이어 없음). 판마다 설정 파일(<see cref="QABoss116LabConfig"/>)을 쓰고
/// Dev Boot 와 같은 경로(<see cref="DevBootLauncher.Launch"/>, 툴바 캐릭터 그대로)로 <see cref="QABoss116Lab.ScenePath"/> 를 Play →
/// 씬의 <see cref="QABoss116LabDirector"/> 가 끝 파일을 쓰면 Play 를 멈추고 CSV·리포트를 갱신한 뒤 다음 판.
/// <list type="bullet">
/// <item>🔴 사람이 화면을 본다 — 창 최소화(<c>QAWindowMinimizer</c>)를 부르지 않는다. 103·116 배치(<c>QABoss103BatchRunner</c>)는 그대로.</item>
/// <item>폴더 <c>C:/UnityProject/QA/116lab-yyyyMMdd_HHmm/</c> — batch.txt·progress.txt·parts/·CSV 5종·report.html·logs/batch.txt.</item>
/// <item>판 오류(Play 진입 180초 안 됨·판 상한 + 150초 동안 끝 파일 없음·끝 파일 전 Play 정지)는 lab_runs.csv status 로 남기고 다음 판(재실행 없음).</item>
/// <item>상태는 progress.txt 라 Play 진입·정지 도메인 리로드를 넘는다. 에디터를 다시 켜면 멈춘다(저절로 Play 하지 않는다).</item>
/// </list>
/// </summary>
[InitializeOnLoad]
public static class QABoss116LabBatchRunner
{
    private const string Tag = "[QA116Lab/Batch]";
    private const string Root = "Tools/QA/116/";
    private const string StartMenu = Root + "Dash Predict Lab — Host 10";
    private const string StopMenu = Root + "Dash Predict Lab — Stop After Current Run";
    private const string ReportMenu = Root + "Dash Predict Lab — Open Report";
    private const string StatusMenu = Root + "Dash Predict Lab — Status";
    private static readonly string DirKey = DevBootTarget.ScopedKey("QA.116Lab.Batch.Dir");
    private static readonly string RunningKey = DevBootTarget.ScopedKey("QA.116Lab.Batch.Running");
    private static readonly string Batch103RunningKey = DevBootTarget.ScopedKey("QA.103.Batch.Running");
    private const string LiveSessionKey = "QA.116Lab.Batch.Live";
    public const int BaseSeed = 116;
    private const double TickSeconds = 0.5;
    private const double PlayStartTimeoutSeconds = 180.0;
    private const double EndGraceSeconds = 150.0;
    private const double StopRetrySeconds = 30.0;
    private const int CooldownSeconds = 3;

    private static double _nextTick;
    private static Dictionary<string, string> _state;
    private static string _stateDir;

    static QABoss116LabBatchRunner()
    {
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        if (Running && SessionState.GetString(LiveSessionKey, "") != "1")
        {
            EditorPrefs.SetBool(RunningKey, false);
            Debug.LogWarning($"{Tag} 에디터를 다시 켜서 랩 배치를 멈춰 두었다({Dir}) — 다시 하려면 {StartMenu}.");
        }
    }

    private static bool Running => EditorPrefs.GetBool(RunningKey, false);
    private static string Dir => EditorPrefs.GetString(DirKey, "");
    private static DateTime Now => DateTime.UtcNow;
    private static string ConfigPath => Path.Combine(QANetSim88.OwnRoot, QABoss116LabConfig.FileName);

    // ── 메뉴 ─────────────────────────────────────────────────

    [MenuItem(StartMenu, priority = 120)]
    public static void StartHost10()
    {
        if (!CanStart())
            return;
        DateTime at = DateTime.Now;
        string dir = $"{QAScenario103Menu.QARoot}/{QABoss116Lab.BatchFolderName(at)}";
        if (Directory.Exists(dir))
            dir += at.ToString("ss", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(dir);

        // 호스트 1명 — 48 시나리오 모드·가상 플레이어를 끈다(이 배치는 MPPM 을 쓰지 않는다).
        QAScenario48Menu.Clear();
        QAScenarioPlay.DeactivateVirtualPlayers();
        DataSourcePlayMode.Current = DataSource.Table;

        var batch = new List<KeyValuePair<string, string>>
        {
            KV("title", "할 일 116-C4 대쉬 간파 예측 랩 — 호스트 1명 10회"),
            KV("started_at", at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            KV("scene", QABoss116Lab.ScenePath),
            KV("character_prefab", DevBootTarget.PlayerPrefabPath),
            KV("seed", BaseSeed.ToString(CultureInfo.InvariantCulture)),
            KV("total", QABoss116Lab.TotalRuns.ToString(CultureInfo.InvariantCulture)),
            KV("run_s", QABoss116Lab.RunSeconds.ToString("0", CultureInfo.InvariantCulture)),
            KV("max_attempts", QABoss116Lab.MaxAttempts.ToString(CultureInfo.InvariantCulture)),
            KV("zone_s", QABoss116Lab.ZoneSeconds.ToString("0", CultureInfo.InvariantCulture)),
            KV("dash_interrupt_pct", QABoss116Lab.DashInterruptPct.ToString(CultureInfo.InvariantCulture)),
            KV("data_source_mode", QABoss103Batch.SourceTable),
            KV("minimize_windows", "0"),
            KV("xlsx_svn_rev", QABoss103RunCsv.XlsxRevision()),
            KV("svn_rev", QABoss103RunCsv.SvnWorkingRevision()),
            KV("git_commit", QABoss103RunCsv.GitCommit()),
            KV("git_branch", QABoss103RunCsv.Shell("git", "rev-parse --abbrev-ref HEAD")),
            KV("unity_version", Application.unityVersion),
        };
        File.WriteAllText(Path.Combine(dir, QABoss103Batch.BatchFile), QABoss103Batch.FormatKeyValues(batch));
        _state = new Dictionary<string, string>(StringComparer.Ordinal);
        _stateDir = dir;
        Set("state", "prepare");
        Set("run", "0");
        SetTime("notBefore", Now);
        EditorPrefs.SetString(DirKey, dir);
        EditorPrefs.SetBool(RunningKey, true);
        SessionState.SetString(LiveSessionKey, "1");
        _nextTick = 0;
        Log($"랩 배치 시작 {dir} — {QABoss116Lab.TotalRuns}판 · 판 {QABoss116Lab.RunSeconds:0}초/시도 {QABoss116Lab.MaxAttempts}회 상한 · 캐릭터 = Dev Boot 툴바({DevBootTarget.PlayerPrefabPath}) · 창 최소화 없음");
    }

    [MenuItem(StopMenu, priority = 121)]
    public static void StopAfterCurrent()
    {
        if (!Running || !LoadState())
        {
            Debug.Log($"{Tag} 랩 배치가 돌고 있지 않다.");
            return;
        }
        Set("stopAfter", "1");
        Log($"지금 판(판 {Get("run")}, 상태 {Get("state")})이 끝나면 멈춘다.");
    }

    [MenuItem(ReportMenu, priority = 122)]
    public static void OpenReport()
    {
        string dir = !string.IsNullOrEmpty(Dir) && Directory.Exists(Dir) ? Dir : null;
        if (dir == null)
        {
            Debug.LogWarning($"{Tag} 랩 배치 폴더가 없다.");
            return;
        }
        string path = QABoss116LabReport.Write(dir, Running ? Get("state") : "멈춤");
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    [MenuItem(StatusMenu, priority = 123)]
    public static void Status()
    {
        if (string.IsNullOrEmpty(Dir))
        {
            Debug.Log($"{Tag} 랩 배치 없음.");
            return;
        }
        _stateDir = null;
        LoadState(Dir);
        Debug.Log($"{Tag} {(Running ? "실행 중" : "멈춤")} · {Dir} · 상태 {Get("state")} · 판 {Get("run")}/{QABoss116Lab.TotalRuns} · 오류 {Get("errors", "0")} · " +
                  $"툴바 출처 {DataSourcePlayMode.Current} · 캐릭터 {DevBootTarget.PlayerPrefabPath}");
    }

    [MenuItem(StartMenu, true)]
    private static bool CanStartCheck() => !Running;

    [MenuItem(StopMenu, true)]
    private static bool StopCheck() => Running;

    private static bool CanStart()
    {
        if (Running)
        {
            Debug.LogWarning($"{Tag} 이미 돌고 있다({Dir}).");
            return false;
        }
        if (EditorPrefs.GetBool(Batch103RunningKey, false))
        {
            Debug.LogWarning($"{Tag} 103/116 배치가 돌고 있다 — Tools/QA/103/Batch/Stop After Current Run 뒤에 시작할 것.");
            return false;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"{Tag} Play 중이다 — 먼저 정지할 것.");
            return false;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(QABoss116Lab.ScenePath) == null)
        {
            Debug.LogError($"{Tag} 랩 씬이 없다: {QABoss116Lab.ScenePath}");
            return false;
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                // Dev Boot 가 저장 확인 대화상자를 띄우면 무인 루프가 멈춘다 — 시작 전에 막는다.
                Debug.LogError($"{Tag} 열린 씬 {SceneManager.GetSceneAt(i).path} 에 저장 안 된 변경이 있다 — 저장하거나 되돌린 뒤 시작할 것.");
                return false;
            }
        return true;
    }

    // ── 상태 기계 ────────────────────────────────────────────

    private static void Tick()
    {
        if (!Running)
            return;
        double t = EditorApplication.timeSinceStartup;
        if (t < _nextTick)
            return;
        if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && !EditorApplication.isPlaying)
            return;
        _nextTick = t + TickSeconds;
        try
        {
            if (!LoadState())
                return;
            switch (Get("state"))
            {
                case "prepare": Prepare(); break;
                case "starting": Starting(); break;
                case "running": RunningStep(); break;
                case "stopping": Stopping(); break;
                default: EditorPrefs.SetBool(RunningKey, false); break;
            }
        }
        catch (Exception e)
        {
            _nextTick = t + 5.0;
            Debug.LogError($"{Tag} 루프 예외(5초 뒤 다시): {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
    }

    private static void Prepare()
    {
        if (Now < (GetTime("notBefore") ?? Now))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (EditorApplication.isPlaying && Seconds("strayStopAt") > StopRetrySeconds)
            {
                SetTime("strayStopAt", Now);
                Log("남은 Play 를 멈춘다.");
                EditorApplication.ExitPlaymode();
            }
            return;
        }
        int run = GetInt("run") + 1;
        if (Get("stopAfter") == "1" || run > QABoss116Lab.TotalRuns)
        {
            Cleanup(Get("stopAfter") == "1" ? "사람 요청(Stop After Current Run)" : $"{QABoss116Lab.TotalRuns}판 끝", Get("stopAfter") == "1" ? "stopped" : "done");
            return;
        }
        if (DataSourcePlayMode.Current != DataSource.Table)
        {
            Log($"툴바 데이터 출처가 {DataSourcePlayMode.Current} — 테이블로 되돌린다.");
            DataSourcePlayMode.Current = DataSource.Table;
        }
        QABoss103Config cfg = Config(run);
        TryDelete(QABoss116Lab.DonePath(_stateDir, run, 0));
        var lab = new QABoss116LabConfig { Bot = cfg };
        File.WriteAllText(ConfigPath, lab.Format());
        Set("run", run.ToString(CultureInfo.InvariantCulture));
        SetTime("attemptAt", Now);
        Set("attemptLocal", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set("playAt", "0");
        Set("state", "starting");
        Log($"판 {run} 시작 — 단계 {QABoss103Batch.TierLabel(cfg.BotTier)} · 판 시드 {cfg.RunSeed} · 대쉬 간파 {cfg.DashInterruptPct}% · Dev Boot {QABoss116Lab.ScenePath}");
        DevBootLauncher.Launch(QABoss116Lab.ScenePath);
    }

    private static void Starting()
    {
        if (EditorApplication.isPlaying)
        {
            SetTime("playAt", Now);
            Set("state", "running");
            return;
        }
        if (Seconds("attemptAt") > PlayStartTimeoutSeconds && !EditorApplication.isPlayingOrWillChangePlaymode)
            EndRun("error", $"Play 진입 안 됨 {PlayStartTimeoutSeconds:0}초(저장 확인·테이블 오류 대화상자 — 콘솔 확인)", false);
    }

    private static void RunningStep()
    {
        int run = GetInt("run");
        bool done = File.Exists(QABoss116Lab.DonePath(_stateDir, run, 0));
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EndRun(done ? "ok" : "error", done ? "" : "끝 파일 전에 Play 가 멈춤(사람·외부·크래시)", false);
            return;
        }
        if (done)
        {
            EndRun("ok", "", true);
            return;
        }
        if (Seconds("playAt") > QABoss116Lab.RunSeconds + EndGraceSeconds)
            EndRun("error", $"판 상한 {QABoss116Lab.RunSeconds:0}+{EndGraceSeconds:0}초 동안 끝 파일 없음(랩 디렉터·보스·플레이어 스폰 확인)", true);
    }

    /// <summary>판 결과를 정하고(Play 정지가 필요하면 요청) CSV 는 정지 뒤 <see cref="AfterStop"/> 에서 붙인다.</summary>
    private static void EndRun(string status, string note, bool stopPlay)
    {
        Set("runStatus", status);
        Set("runNote", note ?? "");
        if (status != "ok")
            Set("errors", (GetInt("errors") + 1).ToString(CultureInfo.InvariantCulture));
        if (stopPlay && EditorApplication.isPlaying)
        {
            SetTime("stopAt", Now);
            Set("state", "stopping");
            Log($"판 {Get("run")} {status} — Play 정지{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
            EditorApplication.ExitPlaymode();
            return;
        }
        AfterStop();
    }

    private static void Stopping()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            AfterStop();
            return;
        }
        if (EditorApplication.isPlaying && Seconds("stopAt") > StopRetrySeconds)
        {
            SetTime("stopAt", Now);
            Log("Play 가 아직 안 멈췄다 — 정지 다시 요청.");
            EditorApplication.ExitPlaymode();
        }
    }

    private static void AfterStop()
    {
        int run = GetInt("run");
        int rows = 0;
        try
        {
            rows = QABoss116LabReport.AppendRun(_stateDir, Config(run), Get("runStatus"), Get("runNote"), Get("attemptLocal"));
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 판 {run} CSV 만들기 실패: {e.GetType().Name}: {e.Message}");
        }
        Log($"판 {run} 끝 — {Get("runStatus")} · 시도 {rows}행{(Get("runNote").Length > 0 ? " · " + Get("runNote") : "")} · 다음까지 {CooldownSeconds}초");
        WriteReport();
        SetTime("notBefore", Now.AddSeconds(CooldownSeconds));
        Set("state", "prepare");
    }

    private static void Cleanup(string why, string endState)
    {
        TryDelete(ConfigPath);
        QAScenario48Menu.Clear();
        Set("state", string.IsNullOrEmpty(endState) ? "stopped" : endState);
        SetTime("endedAt", Now);
        EditorPrefs.SetBool(RunningKey, false);
        string path = WriteReport();
        Log($"랩 배치 {(endState == "done" ? "완료" : "멈춤")} — {why} · 설정 파일 삭제·시나리오 모드 해제 · 리포트 {path}");
    }

    // ── 도구 ─────────────────────────────────────────────────

    private static QABoss103Config Config(int run) => new QABoss103Config
    {
        Seed = BaseSeed,
        Run = Math.Max(1, run),
        Tier = null,
        OutDir = _stateDir,
        Retry = 0,
        Batch = 1,
        DashInterruptPct = QABoss116Lab.DashInterruptPct,
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Log($"파일 지우기 실패 {path}: {e.Message}");
        }
    }

    private static string WriteReport()
    {
        try
        {
            return QABoss116LabReport.Write(_stateDir, Running ? $"{Get("state")} · 판 {Get("run")}" : Get("state"));
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 리포트 생성 실패: {e.GetType().Name}: {e.Message}");
            return "";
        }
    }

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

    private static double Seconds(string key)
    {
        double s = QABoss103Batch.Seconds(GetTime(key), Now);
        return double.IsNaN(s) ? double.MaxValue : s;
    }

    private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);

    private static void Log(string message)
    {
        Debug.Log($"{Tag} {message}");
        if (string.IsNullOrEmpty(_stateDir))
            return;
        try
        {
            string dir = Path.Combine(_stateDir, QABoss103Batch.LogFolder);
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "batch.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
        }
        catch { /* 로그 실패로 루프를 멈추지 않는다 */ }
    }
}
#endif
