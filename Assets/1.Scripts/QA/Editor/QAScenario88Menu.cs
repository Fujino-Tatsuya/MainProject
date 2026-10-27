#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 88 간파 지연 QA 메뉴.
/// <list type="bullet">
/// <item><b>스위트(Q3)</b>: <c>Run Interrupt Lag Suite</c> = 대상 2 × 원격 지연 4(5/35/80/120) × 모드 2(A 스윕·B 사람형) = 16 런,
///   <c>Run Single Run (Selected)</c> = <c>Single/*</c> 에서 고른 대상·지연·모드 1 런. 런당 시도 N = <c>Settings/Attempts *</c>(기본 30).
///   설정을 <see cref="QAInterruptSuite88.ConfigPath"/> 에 쓰고 시나리오 <c>I88</c> 을 MPPM 으로 시작한다(<c>QAScenario48Menu.Run</c> 경로).
///   🔴 먼저 <c>Tools/QA/Activate Virtual Players (2,3)</c>. 끝나면(정상·중단·예외) <see cref="QAInterruptSuite88Watcher"/> 가
///   Play 정지·가상 플레이어 비활성·원격 지연 0·시나리오 모드 해제를 한다.</item>
/// <item><b>원격 지연(Q2)</b>: MPPM 가상 플레이어(클론) 루트 <c>Library/VP/mppm*/</c> 에만 <see cref="QANetSim88.FileName"/> 을 써서
///   원격 클라만 지연시킨다 — 메인 에디터(호스트)는 지연 0. Play 전·중 어느 때나(클론 하네스가 0.5초마다 다시 읽는다). 원복 = 파일 삭제(= 0).
///   <c>Tools/QA/48/Clear Scenario Mode</c> 도 원복한다.</item>
/// </list>
/// 리포트 = <c>C:/UnityProject/QA/88-&lt;시각&gt;/report.md</c>.
/// </summary>
public static class QAScenario88Menu
{
    private const string Root = "Tools/QA/88/";
    private const string KindKey = "QA.88.Single.Kind";
    private const string DelayKey = "QA.88.Single.Delay";
    private const string ModeKey = "QA.88.Single.Mode";
    private const string AttemptsKey = "QA.88.Attempts";
    private const string HostBotKey = "QA.88.HostBot";
    private const string RunTimeoutKey = "QA.88.RunTimeoutSec";

    private static int Attempts => EditorPrefs.GetInt(AttemptsKey, QAInterruptLag88Plan.DefaultAttempts);
    private static bool HostBot => EditorPrefs.GetBool(HostBotKey, false);
    private static int RunTimeoutSec => EditorPrefs.GetInt(RunTimeoutKey, QA88SuiteConfig.DefaultRunTimeoutSec);

    // ── 스위트 ────────────────────────────────────────────────

    [MenuItem(Root + "Run Interrupt Lag Suite", priority = 0)]
    public static void RunSuite() => Run(QA88SuiteConfig.FullSuite(Attempts));

    [MenuItem(Root + "Run Single Run (Selected)", priority = 1)]
    public static void RunSingle()
    {
        var cfg = new QA88SuiteConfig();
        cfg.Add((QAInterruptTargetKind)EditorPrefs.GetInt(KindKey, 0), EditorPrefs.GetInt(DelayKey, QAInterruptLag88Plan.DelayStepsMs[0]),
            (QAInterruptBotMode)EditorPrefs.GetInt(ModeKey, 0));
        Run(cfg);
    }

    [MenuItem(Root + "Stop Suite", priority = 2)]
    public static void StopSuite()
    {
        // 정리(가상 플레이어 비활성·지연 0·시나리오 모드 해제)는 Play 가 멈출 때 감시자가 한다.
        QAScenarioPlay.Stop();
    }

    [MenuItem(Root + "Open Latest Report Folder", priority = 3)]
    public static void OpenLatest()
    {
        string root = QAReportWriter.ScenarioRoot();
        string latest = null;
        if (Directory.Exists(root))
            foreach (string dir in Directory.GetDirectories(root, "88-*"))
                if (latest == null || string.CompareOrdinal(dir, latest) > 0)
                    latest = dir;
        if (latest == null)
        {
            Debug.Log($"[QA88] {root} 에 88-* 리포트 없음");
            return;
        }
        EditorUtility.RevealInFinder(Path.Combine(latest, "report.md"));
    }

    private static void Run(QA88SuiteConfig cfg)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA88] 이미 Play 중이다 — Tools/QA/Stop Active Scenario 로 먼저 정지할 것.");
            return;
        }
        List<string> clones = QANetSim88.CloneRoots();
        if (clones.Count == 0)
        {
            Debug.LogError("[QA88] MPPM 클론 폴더(Library/VP/mppm*)가 없다 — Tools/QA/Activate Virtual Players (2,3) 를 먼저 켤 것.");
            return;
        }

        cfg.Attempts = Attempts;
        cfg.RunTimeoutSec = RunTimeoutSec;
        cfg.HostBot = HostBot;
        QAInterruptSuite88.WriteConfig(cfg);
        QANetSim88.ResetRemoteDelayFiles();
        QAInterruptSuite88.DeleteCommandFiles();

        int players = 1 + Mathf.Min(2, clones.Count);
        Debug.Log($"[QA88] 간파 지연 스위트 {cfg.Runs.Count}런 · N={cfg.Attempts} · 런 상한 {cfg.RunTimeoutSec}s · 호스트 봇={cfg.HostBot} · {players}인 — 상한 {cfg.SuiteTimeLimitSeconds() / 60f:F0}분");
        QAInterruptSuite88Watcher.Arm();
        if (!QAScenario48Menu.Run(QALobbyScenarios.Interrupt88Id, players))
            QAInterruptSuite88Watcher.Disarm();
    }

    // ── 단일 런 선택 ──────────────────────────────────────────

    [MenuItem(Root + "Single/Target Midboss", priority = 20)]
    public static void SelMidboss() => EditorPrefs.SetInt(KindKey, (int)QAInterruptTargetKind.Midboss);

    [MenuItem(Root + "Single/Target Midboss", true)]
    public static bool SelMidbossCheck() => Check(Root + "Single/Target Midboss", EditorPrefs.GetInt(KindKey, 0) == (int)QAInterruptTargetKind.Midboss);

    [MenuItem(Root + "Single/Target 23", priority = 21)]
    public static void Sel23() => EditorPrefs.SetInt(KindKey, (int)QAInterruptTargetKind.TwentyThree);

    [MenuItem(Root + "Single/Target 23", true)]
    public static bool Sel23Check() => Check(Root + "Single/Target 23", EditorPrefs.GetInt(KindKey, 0) == (int)QAInterruptTargetKind.TwentyThree);

    [MenuItem(Root + "Single/Delay 5 ms", priority = 40)]
    public static void SelD5() => EditorPrefs.SetInt(DelayKey, 5);

    [MenuItem(Root + "Single/Delay 5 ms", true)]
    public static bool SelD5Check() => CheckDelay("Single/Delay 5 ms", 5);

    [MenuItem(Root + "Single/Delay 35 ms", priority = 41)]
    public static void SelD35() => EditorPrefs.SetInt(DelayKey, 35);

    [MenuItem(Root + "Single/Delay 35 ms", true)]
    public static bool SelD35Check() => CheckDelay("Single/Delay 35 ms", 35);

    [MenuItem(Root + "Single/Delay 80 ms", priority = 42)]
    public static void SelD80() => EditorPrefs.SetInt(DelayKey, 80);

    [MenuItem(Root + "Single/Delay 80 ms", true)]
    public static bool SelD80Check() => CheckDelay("Single/Delay 80 ms", 80);

    [MenuItem(Root + "Single/Delay 120 ms", priority = 43)]
    public static void SelD120() => EditorPrefs.SetInt(DelayKey, 120);

    [MenuItem(Root + "Single/Delay 120 ms", true)]
    public static bool SelD120Check() => CheckDelay("Single/Delay 120 ms", 120);

    [MenuItem(Root + "Single/Mode A Sweep", priority = 60)]
    public static void SelSweep() => EditorPrefs.SetInt(ModeKey, (int)QAInterruptBotMode.Sweep);

    [MenuItem(Root + "Single/Mode A Sweep", true)]
    public static bool SelSweepCheck() => Check(Root + "Single/Mode A Sweep", EditorPrefs.GetInt(ModeKey, 0) == (int)QAInterruptBotMode.Sweep);

    [MenuItem(Root + "Single/Mode B Human", priority = 61)]
    public static void SelHuman() => EditorPrefs.SetInt(ModeKey, (int)QAInterruptBotMode.Human);

    [MenuItem(Root + "Single/Mode B Human", true)]
    public static bool SelHumanCheck() => Check(Root + "Single/Mode B Human", EditorPrefs.GetInt(ModeKey, 0) == (int)QAInterruptBotMode.Human);

    // ── 설정 ─────────────────────────────────────────────────

    [MenuItem(Root + "Settings/Attempts 30 (default)", priority = 80)]
    public static void Attempts30() => EditorPrefs.SetInt(AttemptsKey, 30);

    [MenuItem(Root + "Settings/Attempts 30 (default)", true)]
    public static bool Attempts30Check() => Check(Root + "Settings/Attempts 30 (default)", Attempts == 30);

    [MenuItem(Root + "Settings/Attempts 10", priority = 81)]
    public static void Attempts10() => EditorPrefs.SetInt(AttemptsKey, 10);

    [MenuItem(Root + "Settings/Attempts 10", true)]
    public static bool Attempts10Check() => Check(Root + "Settings/Attempts 10", Attempts == 10);

    [MenuItem(Root + "Settings/Attempts 3 (smoke)", priority = 82)]
    public static void Attempts3() => EditorPrefs.SetInt(AttemptsKey, 3);

    [MenuItem(Root + "Settings/Attempts 3 (smoke)", true)]
    public static bool Attempts3Check() => Check(Root + "Settings/Attempts 3 (smoke)", Attempts == 3);

    [MenuItem(Root + "Settings/Host Bot Also Presses", priority = 90)]
    public static void ToggleHostBot() => EditorPrefs.SetBool(HostBotKey, !HostBot);

    [MenuItem(Root + "Settings/Host Bot Also Presses", true)]
    public static bool ToggleHostBotCheck() => Check(Root + "Settings/Host Bot Also Presses", HostBot);

    [MenuItem(Root + "Settings/Run Timeout 600s (default)", priority = 100)]
    public static void Timeout600() => EditorPrefs.SetInt(RunTimeoutKey, 600);

    [MenuItem(Root + "Settings/Run Timeout 600s (default)", true)]
    public static bool Timeout600Check() => Check(Root + "Settings/Run Timeout 600s (default)", RunTimeoutSec == 600);

    [MenuItem(Root + "Settings/Run Timeout 1200s", priority = 101)]
    public static void Timeout1200() => EditorPrefs.SetInt(RunTimeoutKey, 1200);

    [MenuItem(Root + "Settings/Run Timeout 1200s", true)]
    public static bool Timeout1200Check() => Check(Root + "Settings/Run Timeout 1200s", RunTimeoutSec == 1200);

    private static bool CheckDelay(string item, int ms) =>
        Check(Root + item, EditorPrefs.GetInt(DelayKey, QAInterruptLag88Plan.DelayStepsMs[0]) == ms);

    private static bool Check(string path, bool on)
    {
        Menu.SetChecked(path, on);
        return true;
    }

    // ── 원격 지연(Q2) ─────────────────────────────────────────

    [MenuItem(Root + "NetSim/Remote Delay 5 ms", priority = 110)]
    public static void Delay5() => SetRemoteDelay(5);

    [MenuItem(Root + "NetSim/Remote Delay 35 ms", priority = 111)]
    public static void Delay35() => SetRemoteDelay(35);

    [MenuItem(Root + "NetSim/Remote Delay 80 ms", priority = 112)]
    public static void Delay80() => SetRemoteDelay(80);

    [MenuItem(Root + "NetSim/Remote Delay 120 ms", priority = 113)]
    public static void Delay120() => SetRemoteDelay(120);

    [MenuItem(Root + "NetSim/Reset Remote Delay (0)", priority = 130)]
    public static void Reset() => ResetRemoteDelay();

    [MenuItem(Root + "NetSim/Status", priority = 131)]
    public static void Status()
    {
        var parts = new List<string>();
        foreach (string root in QANetSim88.CloneRoots())
        {
            string path = QANetSim88.FilePathFor(root);
            string value = File.Exists(path) ? File.ReadAllText(path).Trim() : "없음(0)";
            parts.Add($"{Path.GetFileName(root)}={value}");
        }
        Debug.Log($"[QA88] 원격 지연 파일 — {(parts.Count > 0 ? string.Join(", ", parts) : "MPPM 클론 폴더 없음")}");
    }

    /// <summary>MPPM 클론 전부에 원격 지연(ms, 송수신 각각)을 건다. 0 이하면 원복. 쓴 클론 수.</summary>
    public static int SetRemoteDelay(int delayMs)
    {
        if (delayMs <= 0)
            return ResetRemoteDelay();
        int n = QANetSim88.SetRemoteDelayFiles(delayMs);
        Debug.Log(n > 0
            ? $"[QA88] 원격 지연 {Mathf.Min(delayMs, QAInterruptLag88Plan.MaxDelayMs)}ms — 클론 {n}개에 기록(Play 중이면 0.5초 안에 적용)"
            : "[QA88] MPPM 클론 폴더(Library/VP/mppm*)가 없다 — Tools/QA/Activate Virtual Players (2,3) 를 먼저 한 번 켤 것.");
        return n;
    }

    /// <summary>원격 지연 원복(0) — 클론의 지연 파일을 지운다. 지운 수.</summary>
    public static int ResetRemoteDelay()
    {
        int n = QANetSim88.ResetRemoteDelayFiles();
        Debug.Log($"[QA88] 원격 지연 원복(0) — 지연 파일 {n}개 삭제");
        return n;
    }
}
#endif
