#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 103 3인 보스전 밸런싱 QA 실행부(C1 — 1판 실행 진입점, QA 창(Tools/QA) ▸ 103 섹션이 부른다).
/// <list type="bullet">
/// <item><c>Run 1 Match</c> = 설정(기준 시드·판 번호)을 호스트·MPPM 클론 루트마다 <see cref="QABoss103Config.FileName"/> 로 쓰고
///   시나리오 <see cref="QALobbyScenarios.Boss103Id"/> 를 3인 MPPM 으로 시작한다(<c>QAScenario48Menu.Run</c> 경로 — isPlaying 직접 토글 없음).
///   🔴 먼저 QA 창 ▸ 가상 플레이어 ▸ 켜기. 끝나면 QA 창 ▸ 시나리오 ▸ 정지 → 가상 플레이어 ▸ 끄기 → 48 로비 데모 ▸ 시나리오 모드 해제.</item>
/// <item>판 기록 = 호스트 콘솔 <c>[QA103] event=run_end …</c> 1줄 + 48 리포트(<c>C:/UnityProject/QA/48-*/report.md</c>) 항목.</item>
/// <item>판별 CSV(C4) = <c>C:/UnityProject/QA/103-single-&lt;시각&gt;/</c> runs·run_chars·run_patterns·run_gimmicks.csv + parts/(봇 판 파일).</item>
/// </list>
/// 같은 기준 시드·같은 판 번호 = 같은 특성(<see cref="QABoss103Plan.RunSeed"/>).
/// </summary>
public static class QAScenario103Menu
{
    private const string SeedKey = "QA.103.Seed";
    private const string RunKey = "QA.103.Run";

    private const string TierKey = "QA.103.Tier"; // -1 = 자동(판 번호), 그 밖 = QABoss103BotTier

    private static int Seed => EditorPrefs.GetInt(SeedKey, QABoss103Config.DefaultSeed);

    /// <summary>기준 시드(103 설정) — C6 연속 루프가 배치 시작 때 한 번 읽어 배치 파일에 고정한다.</summary>
    internal static int BaseSeed => Seed;

    /// <summary>다음 1판 실행의 판 번호(1 이상).</summary>
    public static int Run => Mathf.Max(1, EditorPrefs.GetInt(RunKey, 1));

    private static QABoss103BotTier? Tier
    {
        get
        {
            int v = EditorPrefs.GetInt(TierKey, -1);
            return System.Enum.IsDefined(typeof(QABoss103BotTier), v) ? (QABoss103BotTier)v : (QABoss103BotTier?)null;
        }
    }

    /// <summary>봇 단계 설정값 — -1 = 자동(판 번호 mod 3), 그 밖 = <see cref="QABoss103BotTier"/>. 바꾸면 Status 를 남긴다.</summary>
    public static int TierSetting
    {
        get => Tier.HasValue ? (int)Tier.Value : -1;
        set => SetTier(value);
    }

    private static QABoss103Config Current => new QABoss103Config { Seed = Seed, Run = Run, Tier = Tier };

    public static void RunOne()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"{QABoss103Plan.Tag} 이미 Play 중이다 — QA 창(Tools/QA) ▸ 시나리오 ▸ 정지 로 먼저 정지할 것.");
            return;
        }
        List<string> clones = QANetSim88.CloneRoots();
        if (clones.Count < 2)
        {
            Debug.LogError($"{QABoss103Plan.Tag} MPPM 클론 폴더(Library/VP/mppm*)가 {clones.Count}개 — 3인 판이라 QA 창(Tools/QA) ▸ 가상 플레이어 ▸ 켜기 를 먼저 켤 것.");
            return;
        }

        QABoss103Config cfg = Current;
        cfg.OutDir = SingleOutDir();
        int written = WriteConfig(cfg);
        Debug.Log($"{QABoss103Plan.Tag} event=menu_run seed={cfg.Seed} run={cfg.Run} runSeed={cfg.RunSeed} botTier={cfg.BotTier} tierSource={(cfg.Tier.HasValue ? "manual" : "run")} configs={written} out={QABoss103Plan.Token(cfg.OutDir)}");
        QAScenario48Menu.Run(QALobbyScenarios.Boss103Id, 3);
    }

    public static void Status()
    {
        QABoss103Config cfg = Current;
        Debug.Log($"{QABoss103Plan.Tag} 설정 — 기준 시드 {cfg.Seed} · 판 {cfg.Run} · 판 시드 {cfg.RunSeed} · " +
                  $"봇 단계 {cfg.BotTier}({(cfg.Tier.HasValue ? "수동" : "(판 번호 − 1) mod 3")}) [{QABoss103BotProfile.For(cfg.BotTier).Describe()}] · 클론 {QANetSim88.CloneRoots().Count}개");
    }

    // ── 봇 단계 ──────────────────────────────────────────────

    public static void TierAuto() => SetTier(-1);

    public static void TierBeginner() => SetTier((int)QABoss103BotTier.Beginner);

    public static void TierNormal() => SetTier((int)QABoss103BotTier.Normal);

    public static void TierSkilled() => SetTier((int)QABoss103BotTier.Skilled);

    private static void SetTier(int v)
    {
        EditorPrefs.SetInt(TierKey, v);
        Status();
    }

    public static void OpenReports() => QAScenario48Menu.OpenReports();

    // ── 설정 ─────────────────────────────────────────────────

    public static void NextRun()
    {
        EditorPrefs.SetInt(RunKey, Run + 1);
        Status();
    }

    public static void ResetRun()
    {
        EditorPrefs.SetInt(RunKey, 1);
        Status();
    }

    /// <summary>기준 시드가 기본값(<see cref="QABoss103Config.DefaultSeed"/>)인가.</summary>
    public static bool IsDefaultSeed => Seed == QABoss103Config.DefaultSeed;

    /// <summary>기준 시드(표시용).</summary>
    public static int SeedValue => Seed;

    public static void DefaultSeed()
    {
        EditorPrefs.SetInt(SeedKey, QABoss103Config.DefaultSeed);
        Status();
    }

    public static void RandomSeed()
    {
        EditorPrefs.SetInt(SeedKey, new System.Random().Next(1, int.MaxValue));
        Status();
    }

    /// <summary>QA 리포트 루트(48·88 과 같은 곳).</summary>
    internal const string QARoot = "C:/UnityProject/QA";

    /// <summary>1판 실행의 CSV 폴더 — <c>C:/UnityProject/QA/103-single-&lt;시각&gt;</c>(C4). 연속 실행(C6)은 <c>103-&lt;배치&gt;</c> 를 직접 넘긴다.</summary>
    internal static string SingleOutDir() => $"{QARoot}/103-single-{System.DateTime.Now:yyyyMMdd_HHmmss}";

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

    /// <summary>호스트·클론 루트의 설정 파일을 지운다(Clear Scenario Mode 와 함께 쓰면 평소 Play 에 남지 않는다).</summary>
    public static void DeleteConfigFiles() => DeleteConfig();

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
