#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 88 간파 지연 QA 실행부(QA 창(Tools/QA) ▸ 88 섹션이 부른다).
/// <list type="bullet">
/// <item><b>스위트(Q3)</b>: <c>Run Interrupt Lag Suite</c> = 대상 2 × 원격 지연 4(5/35/80/120) × 모드 2(A 스윕·B 사람형) = 16 런,
///   <c>Run Single Run (Selected)</c> = 창에서 고른 대상·지연·모드 1 런. 런당 시도 N = <see cref="Attempts"/>(기본 30).
///   설정을 <see cref="QAInterruptSuite88.ConfigPath"/> 에 쓰고 시나리오 <c>I88</c> 을 MPPM 으로 시작한다(<c>QAScenario48Menu.Run</c> 경로).
///   🔴 먼저 QA 창 ▸ 가상 플레이어 ▸ 켜기. 끝나면(정상·중단·예외) <see cref="QAInterruptSuite88Watcher"/> 가
///   Play 정지·가상 플레이어 비활성·원격 지연 0·시나리오 모드 해제를 한다.</item>
/// <item><b>원격 지연(Q2)</b>: MPPM 가상 플레이어(클론) 루트 <c>Library/VP/mppm*/</c> 에만 <see cref="QANetSim88.FileName"/> 을 써서
///   원격 클라만 지연시킨다 — 메인 에디터(호스트)는 지연 0. Play 전·중 어느 때나(클론 하네스가 0.5초마다 다시 읽는다). 원복 = 파일 삭제(= 0).
///   QA 창 ▸ 48 로비 데모 ▸ 시나리오 모드 해제 도 원복한다.</item>
/// </list>
/// 리포트 = <c>C:/UnityProject/QA/88-&lt;시각&gt;/report.md</c>.
/// </summary>
public static class QAScenario88Menu
{
    private const string KindKey = "QA.88.Single.Kind";
    private const string DelayKey = "QA.88.Single.Delay";
    private const string ModeKey = "QA.88.Single.Mode";
    private const string AttemptsKey = "QA.88.Attempts";
    private const string HostBotKey = "QA.88.HostBot";
    private const string RunTimeoutKey = "QA.88.RunTimeoutSec";

    /// <summary>런당 시도 N(기본 30 · 10 · 3 스모크).</summary>
    public static int Attempts
    {
        get => EditorPrefs.GetInt(AttemptsKey, QAInterruptLag88Plan.DefaultAttempts);
        set => EditorPrefs.SetInt(AttemptsKey, value);
    }

    /// <summary>호스트 봇도 누른다.</summary>
    public static bool HostBot
    {
        get => EditorPrefs.GetBool(HostBotKey, false);
        set => EditorPrefs.SetBool(HostBotKey, value);
    }

    /// <summary>런 상한(초, 기본 600 · 1200).</summary>
    public static int RunTimeoutSec
    {
        get => EditorPrefs.GetInt(RunTimeoutKey, QA88SuiteConfig.DefaultRunTimeoutSec);
        set => EditorPrefs.SetInt(RunTimeoutKey, value);
    }

    /// <summary>단일 런 대상.</summary>
    public static QAInterruptTargetKind SingleTarget
    {
        get => (QAInterruptTargetKind)EditorPrefs.GetInt(KindKey, 0);
        set => EditorPrefs.SetInt(KindKey, (int)value);
    }

    /// <summary>단일 런 원격 지연(ms — 5·35·80·120).</summary>
    public static int SingleDelayMs
    {
        get => EditorPrefs.GetInt(DelayKey, QAInterruptLag88Plan.DelayStepsMs[0]);
        set => EditorPrefs.SetInt(DelayKey, value);
    }

    /// <summary>단일 런 봇 모드(A 스윕·B 사람형).</summary>
    public static QAInterruptBotMode SingleMode
    {
        get => (QAInterruptBotMode)EditorPrefs.GetInt(ModeKey, 0);
        set => EditorPrefs.SetInt(ModeKey, (int)value);
    }

    // ── 스위트 ────────────────────────────────────────────────

    public static void RunSuite() => Run(QA88SuiteConfig.FullSuite(Attempts));

    public static void RunSingle()
    {
        var cfg = new QA88SuiteConfig();
        cfg.Add(SingleTarget, SingleDelayMs, SingleMode);
        Run(cfg);
    }

    public static void StopSuite()
    {
        // 정리(가상 플레이어 비활성·지연 0·시나리오 모드 해제)는 Play 가 멈출 때 감시자가 한다.
        QAScenarioPlay.Stop();
    }

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
            Debug.LogWarning("[QA88] 이미 Play 중이다 — QA 창(Tools/QA) ▸ 시나리오 ▸ 정지 로 먼저 정지할 것.");
            return;
        }
        List<string> clones = QANetSim88.CloneRoots();
        if (clones.Count == 0)
        {
            Debug.LogError("[QA88] MPPM 클론 폴더(Library/VP/mppm*)가 없다 — QA 창(Tools/QA) ▸ 가상 플레이어 ▸ 켜기 를 먼저 켤 것.");
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

    // ── 원격 지연(Q2) ─────────────────────────────────────────

    public static void Delay5() => SetRemoteDelay(5);

    public static void Delay35() => SetRemoteDelay(35);

    public static void Delay80() => SetRemoteDelay(80);

    public static void Delay120() => SetRemoteDelay(120);

    public static void Reset() => ResetRemoteDelay();

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
            : "[QA88] MPPM 클론 폴더(Library/VP/mppm*)가 없다 — QA 창(Tools/QA) ▸ 가상 플레이어 ▸ 켜기 를 먼저 한 번 켤 것.");
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
