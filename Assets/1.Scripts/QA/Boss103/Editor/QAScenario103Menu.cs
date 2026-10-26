#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 103 3인 보스전 밸런싱 QA 메뉴(C1 — 1판 실행 진입점).
/// <list type="bullet">
/// <item><c>Run 1 Match</c> = 설정(기준 시드·판 번호)을 호스트·MPPM 클론 루트마다 <see cref="QABoss103Config.FileName"/> 로 쓰고
///   시나리오 <see cref="QALobbyScenarios.Boss103Id"/> 를 3인 MPPM 으로 시작한다(<c>QAScenario48Menu.Run</c> 경로 — isPlaying 직접 토글 없음).
///   🔴 먼저 <c>Tools/QA/Activate Virtual Players (2,3)</c>. 끝나면 <c>Tools/QA/Stop Active Scenario</c> → 가상 플레이어 비활성 → <c>Tools/QA/48/Clear Scenario Mode</c>.</item>
/// <item>판 기록 = 호스트 콘솔 <c>[QA103] event=run_end …</c> 1줄 + 48 리포트(<c>C:/UnityProject/QA/48-*/report.md</c>) 항목.</item>
/// </list>
/// 같은 기준 시드·같은 판 번호 = 같은 특성(<see cref="QABoss103Plan.RunSeed"/>).
/// </summary>
public static class QAScenario103Menu
{
    private const string Root = "Tools/QA/103/";
    private const string SeedKey = "QA.103.Seed";
    private const string RunKey = "QA.103.Run";

    private const string TierKey = "QA.103.Tier"; // -1 = 자동(판 번호), 그 밖 = QABoss103BotTier

    private static int Seed => EditorPrefs.GetInt(SeedKey, QABoss103Config.DefaultSeed);
    private static int Run => Mathf.Max(1, EditorPrefs.GetInt(RunKey, 1));

    private static QABoss103BotTier? Tier
    {
        get
        {
            int v = EditorPrefs.GetInt(TierKey, -1);
            return System.Enum.IsDefined(typeof(QABoss103BotTier), v) ? (QABoss103BotTier)v : (QABoss103BotTier?)null;
        }
    }

    private static QABoss103Config Current => new QABoss103Config { Seed = Seed, Run = Run, Tier = Tier };

    [MenuItem(Root + "Run 1 Match (Boss 3P)", priority = 0)]
    public static void RunOne()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"{QABoss103Plan.Tag} 이미 Play 중이다 — Tools/QA/Stop Active Scenario 로 먼저 정지할 것.");
            return;
        }
        List<string> clones = QANetSim88.CloneRoots();
        if (clones.Count < 2)
        {
            Debug.LogError($"{QABoss103Plan.Tag} MPPM 클론 폴더(Library/VP/mppm*)가 {clones.Count}개 — 3인 판이라 Tools/QA/Activate Virtual Players (2,3) 를 먼저 켤 것.");
            return;
        }

        QABoss103Config cfg = Current;
        int written = WriteConfig(cfg);
        Debug.Log($"{QABoss103Plan.Tag} event=menu_run seed={cfg.Seed} run={cfg.Run} runSeed={cfg.RunSeed} botTier={cfg.BotTier} tierSource={(cfg.Tier.HasValue ? "manual" : "run")} configs={written}");
        QAScenario48Menu.Run(QALobbyScenarios.Boss103Id, 3);
    }

    [MenuItem(Root + "Status", priority = 1)]
    public static void Status()
    {
        QABoss103Config cfg = Current;
        Debug.Log($"{QABoss103Plan.Tag} 설정 — 기준 시드 {cfg.Seed} · 판 {cfg.Run} · 판 시드 {cfg.RunSeed} · " +
                  $"봇 단계 {cfg.BotTier}({(cfg.Tier.HasValue ? "수동" : "(판 번호 − 1) mod 3")}) [{QABoss103BotProfile.For(cfg.BotTier).Describe()}] · 클론 {QANetSim88.CloneRoots().Count}개");
    }

    // ── 봇 단계 ──────────────────────────────────────────────

    [MenuItem(Root + "Settings/Bot Tier - Auto (run mod 3)", priority = 60)]
    public static void TierAuto() => SetTier(-1);

    [MenuItem(Root + "Settings/Bot Tier - Beginner", priority = 61)]
    public static void TierBeginner() => SetTier((int)QABoss103BotTier.Beginner);

    [MenuItem(Root + "Settings/Bot Tier - Normal", priority = 62)]
    public static void TierNormal() => SetTier((int)QABoss103BotTier.Normal);

    [MenuItem(Root + "Settings/Bot Tier - Skilled", priority = 63)]
    public static void TierSkilled() => SetTier((int)QABoss103BotTier.Skilled);

    [MenuItem(Root + "Settings/Bot Tier - Auto (run mod 3)", true)]
    public static bool TierAutoCheck() => CheckTier("Settings/Bot Tier - Auto (run mod 3)", -1);

    [MenuItem(Root + "Settings/Bot Tier - Beginner", true)]
    public static bool TierBeginnerCheck() => CheckTier("Settings/Bot Tier - Beginner", (int)QABoss103BotTier.Beginner);

    [MenuItem(Root + "Settings/Bot Tier - Normal", true)]
    public static bool TierNormalCheck() => CheckTier("Settings/Bot Tier - Normal", (int)QABoss103BotTier.Normal);

    [MenuItem(Root + "Settings/Bot Tier - Skilled", true)]
    public static bool TierSkilledCheck() => CheckTier("Settings/Bot Tier - Skilled", (int)QABoss103BotTier.Skilled);

    private static void SetTier(int v)
    {
        EditorPrefs.SetInt(TierKey, v);
        Status();
    }

    private static bool CheckTier(string item, int v)
    {
        Menu.SetChecked(Root + item, (Tier.HasValue ? (int)Tier.Value : -1) == v);
        return true;
    }

    [MenuItem(Root + "Open Report Folder", priority = 2)]
    public static void OpenReports() => QAScenario48Menu.OpenReports();

    // ── 설정 ─────────────────────────────────────────────────

    [MenuItem(Root + "Settings/Next Run (+1)", priority = 20)]
    public static void NextRun()
    {
        EditorPrefs.SetInt(RunKey, Run + 1);
        Status();
    }

    [MenuItem(Root + "Settings/Run = 1", priority = 21)]
    public static void ResetRun()
    {
        EditorPrefs.SetInt(RunKey, 1);
        Status();
    }

    [MenuItem(Root + "Settings/Seed 103 (default)", priority = 40)]
    public static void DefaultSeed()
    {
        EditorPrefs.SetInt(SeedKey, QABoss103Config.DefaultSeed);
        Status();
    }

    [MenuItem(Root + "Settings/Seed 103 (default)", true)]
    public static bool DefaultSeedCheck()
    {
        Menu.SetChecked(Root + "Settings/Seed 103 (default)", Seed == QABoss103Config.DefaultSeed);
        return true;
    }

    [MenuItem(Root + "Settings/New Random Seed", priority = 41)]
    public static void RandomSeed()
    {
        EditorPrefs.SetInt(SeedKey, new System.Random().Next(1, int.MaxValue));
        Status();
    }

    /// <summary>호스트·클론 루트마다 설정 파일을 쓴다. 쓴 수.</summary>
    internal static int WriteConfig(QABoss103Config cfg)
    {
        int n = 0;
        var roots = new List<string> { QANetSim88.OwnRoot };
        roots.AddRange(QANetSim88.CloneRoots());
        foreach (string root in roots)
        {
            File.WriteAllText(Path.Combine(root, QABoss103Config.FileName), cfg.Format());
            n++;
        }
        return n;
    }

    /// <summary>호스트·클론 루트의 설정 파일을 지운다(Clear Scenario Mode 와 함께 쓰면 평소 Play 에 남지 않는다). 지운 수.</summary>
    [MenuItem(Root + "Delete Config Files", priority = 60)]
    public static void DeleteConfigMenu() => DeleteConfig();

    internal static int DeleteConfig()
    {
        int n = 0;
        var roots = new List<string> { QANetSim88.OwnRoot };
        roots.AddRange(QANetSim88.CloneRoots());
        foreach (string root in roots)
        {
            string path = Path.Combine(root, QABoss103Config.FileName);
            if (!File.Exists(path))
                continue;
            File.Delete(path);
            n++;
        }
        Debug.Log($"{QABoss103Plan.Tag} 설정 파일 {n}개 삭제");
        return n;
    }
}
#endif
