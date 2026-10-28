#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>할 일 116-C4 — 대쉬 간파 예측 랩 순수 규칙(<see cref="QABoss116Lab"/>)과 격리(랩 씬·컴포넌트가 밖으로 새지 않음).</summary>
public sealed class QABoss116LabTests
{
    private static readonly string[] Key = { "1", "0", "Normal", "116" };

    private static Dictionary<string, string> Kv(params string[] p)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < p.Length; i += 2)
            d[p[i]] = p[i + 1];
        return d;
    }

    // ── 구간·판 ──────────────────────────────────────────────

    [TestCase(0.5f, ExpectedResult = "wall")]
    [TestCase(2.75f, ExpectedResult = "wall")]   // 복도 한가운데(안쪽 폭 5.5m)
    [TestCase(3.0f, ExpectedResult = "wall")]
    [TestCase(3.1f, ExpectedResult = "open")]
    [TestCase(-1f, ExpectedResult = "open")]
    public string 벽_거리로_구간을_가른다(float wallM) => QABoss116Lab.Zone(wallM);

    [TestCase(0.0, ExpectedResult = "wall")]
    [TestCase(29.9, ExpectedResult = "wall")]
    [TestCase(30.0, ExpectedResult = "open")]
    [TestCase(61.0, ExpectedResult = "wall")]
    public string 계획_구간은_벽부터_교대한다(double elapsed) => QABoss116Lab.PlannedZone(elapsed, 30.0);

    [Test]
    public void 판은_시간_상한과_시도_상한_중_먼저()
    {
        Assert.IsNull(QABoss116Lab.EndReason(10, 5, 180, 60));
        Assert.AreEqual("time", QABoss116Lab.EndReason(180, 5, 180, 60));
        Assert.AreEqual("attempts", QABoss116Lab.EndReason(20, 60, 180, 60));
        Assert.AreEqual("attempts", QABoss116Lab.EndReason(200, 60, 180, 60));
        Assert.IsNull(QABoss116Lab.EndReason(1000, 1000, 0, 0));
    }

    [Test]
    public void 설정_파일_왕복()
    {
        var cfg = new QABoss116LabConfig { RunSeconds = 120, MaxAttempts = 25, ZoneSeconds = 20 };
        cfg.Bot.Run = 4;
        cfg.Bot.OutDir = "C:/UnityProject/QA/116lab-x";
        Assert.IsTrue(QABoss116LabConfig.TryParse(cfg.Format(), out QABoss116LabConfig back));
        Assert.AreEqual(4, back.Bot.Run);
        Assert.AreEqual(QABoss116Lab.DashInterruptPct, back.Bot.DashInterruptPct);
        Assert.AreEqual("C:/UnityProject/QA/116lab-x", back.Bot.OutDir);
        Assert.AreEqual(120, back.RunSeconds, 1e-9);
        Assert.AreEqual(25, back.MaxAttempts);
        Assert.AreEqual(20, back.ZoneSeconds, 1e-9);
        Assert.IsFalse(QABoss116LabConfig.TryParse("", out _));
    }

    // ── 상세 원인 ─────────────────────────────────────────────

    [Test]
    public void 대시_게이트는_게임_검사_순서대로_첫_막힘()
    {
        Assert.AreEqual("dash_disabled", QABoss116Lab.DashGate(false, false, false, false, 0, "Idle"));
        Assert.AreEqual("not_input_source", QABoss116Lab.DashGate(true, false, false, false, 0, "Idle"));
        Assert.AreEqual("cannot_move_Hit", QABoss116Lab.DashGate(true, true, false, false, 0, "Hit"));
        Assert.AreEqual("airborne", QABoss116Lab.DashGate(true, true, true, false, 0, "Idle"));
        Assert.AreEqual("no_charge", QABoss116Lab.DashGate(true, true, true, true, 0, "Idle"));
        Assert.AreEqual("state_enter_rejected", QABoss116Lab.DashGate(true, true, true, true, 1, "Idle"));
    }

    [Test]
    public void 시도_결과_사유_7종의_상세_원인()
    {
        Assert.AreEqual("cannot_move_Skill",
            QABoss116Lab.Cause("dash_rejected", Kv("dashOn", "true", "inputSrc", "true", "canMove", "false", "grounded", "true", "charge", "1", "state", "Skill")));
        Assert.AreEqual("no_charge", QABoss116Lab.Cause("dash_rejected", Kv("canMove", "true", "grounded", "true", "charge", "0")));
        Assert.AreEqual("charge_0of2_next_1.25s", QABoss116Lab.Cause("no_charge", Kv("charge", "0", "maxCharge", "2", "nextChargeS", "1.25")));
        Assert.AreEqual("interrupt_cooldown", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "3.00")));
        Assert.AreEqual("other_skill_PaladinGuardSkill", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "activeSkill", "PaladinGuardSkill")));
        Assert.AreEqual("dashing", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "activeSkill", "none", "endState", "Dash")));
        Assert.AreEqual("awaiting_approval", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "endState", "Idle", "skillBusy", "true")));
        Assert.AreEqual("not_started_Hit", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "endState", "Hit", "skillBusy", "false")));
        Assert.AreEqual("delay_beyond_dash", QABoss116Lab.Cause("dash_ended_before_press", Kv("plannedS", "0.300", "dashTotalS", "0.250", "dashAgeS", "0.250")));
        Assert.AreEqual("dash_cut_Hit", QABoss116Lab.Cause("dash_ended_before_press", Kv("plannedS", "0.050", "dashTotalS", "0.250", "dashAgeS", "0.040", "endState", "Hit")));
        Assert.AreEqual("dash_ended_normal", QABoss116Lab.Cause("dash_ended_before_press", Kv("plannedS", "0.050", "dashTotalS", "0.250", "dashAgeS", "0.250")));
        Assert.AreEqual("press_after_window", QABoss116Lab.Cause("skill_after_dash", Kv("dashAgeS", "0.150", "windowS", "0.100")));
        Assert.AreEqual("dash_ended_before_attach", QABoss116Lab.Cause("skill_after_dash", Kv("dashAgeS", "0.050", "windowS", "0.100")));
        Assert.AreEqual("press_at_0.05s_of_0.10s", QABoss116Lab.Cause("attached", Kv("dashAgeS", "0.050", "windowS", "0.100")));
        Assert.AreEqual("bot_idle", QABoss116Lab.Cause("idle_phase_none", Kv()));
    }

    [Test]
    public void 모르는_값_na_는_빈칸으로_읽는다()
    {
        // 판 파일은 빈 값을 na 로 쓴다 — 원인 계산이 na 를 숫자·문자로 오해하지 않는다.
        Assert.AreEqual("not_started_na", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "na", "activeSkill", "na", "endState", "na", "state", "na")));
    }

    // ── 대시 거리 ─────────────────────────────────────────────

    [Test]
    public void 대시_의도_거리와_실제_거리()
    {
        QABoss116Lab.DashDistances(20f, 0.25f, 0f, 0f, 5f, 0f, 1f, 0f, out float intent, out float actual, out float shortfall, out bool blocked);
        Assert.AreEqual(5f, intent, 1e-4f);
        Assert.AreEqual(5f, actual, 1e-4f);
        Assert.AreEqual(0f, shortfall, 1e-4f);
        Assert.IsFalse(blocked);

        QABoss116Lab.DashDistances(20f, 0.25f, 0f, 0f, 3f, 2f, 1f, 0f, out _, out actual, out shortfall, out blocked);
        Assert.AreEqual(3f, actual, 1e-4f, "옆으로 미끄러진 거리는 실제 거리에 넣지 않는다(대시 방향 투영)");
        Assert.AreEqual(2f, shortfall, 1e-4f);
        Assert.IsTrue(blocked);

        QABoss116Lab.DashDistances(20f, 0.25f, 0f, 0f, 4.8f, 0f, 1f, 0f, out _, out _, out shortfall, out blocked);
        Assert.AreEqual(0.2f, shortfall, 1e-4f);
        Assert.IsFalse(blocked, "0.3m 미만은 막힘이 아니다");
    }

    // ── 행 묶기 ──────────────────────────────────────────────

    private static List<string> SampleLines() => new List<string>
    {
        "labwin event=open n=1 t=10.000 attack=Dash bossState=Attack zonePlanned=wall wallM=1.20 bossM=3.10",
        "labwin event=close n=1 t=11.500 durS=1.500",
        "labwin event=open n=2 t=20.000 attack=Jump zonePlanned=open wallM=7.00 bossM=2.00",
        "labwin event=close n=2 t=20.800",
        "labdash n=1 startT=10.400 endT=10.650 startPos=0/0 endPos=2/0 dir=1/0 speed=20.00 intentM=5.00 actualM=2.00 shortM=3.00 blocked=true " +
        "blockPos=2/0 blockCollider=Wall_North blockLayer=Wall blockAtS=0.100 wallM=1.10",
        "dashint slot=0 character=Paladin clientId=0 order=dash_rmb outcome=attached delayS=0.050 window=1 serverT=10.450 firstT=10.400 " +
        "dashAgeS=0.050 windowS=0.100 wallM=1.10 charge=1 maxCharge=2",
        "dp event=cast side=server castId=s0-1 clientId=0 host=true serverT=10.450 castServerT=10.450 predict=success predReason=in_range",
        "dp event=judge side=server castId=s0-1 clientId=0 host=true serverT=10.700 castServerT=10.450 result=fail match=false mismatch=dash_shortened shortfallM=2.90",
        "dashint slot=0 character=Paladin clientId=0 order=rmb_dash outcome=rmb_not_started delayS=0.030 window=2 serverT=20.300 firstT=20.300 intCdS=0.00 endState=Hit wallM=7.00",
        "dashint slot=0 character=Paladin clientId=0 order=dash_rmb outcome=no_charge delayS=0 window=3 serverT=40.000 charge=0 maxCharge=2 nextChargeS=0.80 wallM=na",
    };

    [Test]
    public void 시도_행은_창_대시_궤적_서버_판정을_묶는다()
    {
        List<string[]> rows = QABoss116Lab.BuildAttempts(Key, SampleLines());
        Assert.AreEqual(3, rows.Count);
        string[] h = QABoss116Lab.AttemptsHeader;
        string Col(string[] r, string c) => r[System.Array.IndexOf(h, c)];

        string[] a = rows[0];
        Assert.AreEqual("attached", Col(a, "outcome"));
        Assert.AreEqual("wall", Col(a, "zone"));
        Assert.AreEqual("1", Col(a, "win_n"));
        Assert.AreEqual("0.400", Col(a, "t_from_open_s"));
        Assert.AreEqual("10.400", Col(a, "dash_start_t"));
        Assert.AreEqual("Wall_North", Col(a, "dash_block_collider"));
        Assert.AreEqual("s0-1", Col(a, "cast_id"));
        Assert.AreEqual("fail", Col(a, "result"));
        Assert.AreEqual("dash_shortened", Col(a, "mismatch"));

        string[] b = rows[1];
        Assert.AreEqual("open", Col(b, "zone"));
        Assert.AreEqual("2", Col(b, "win_n"));
        Assert.AreEqual("not_started_Hit", Col(b, "cause"));
        Assert.AreEqual(QABoss116DashPredict.NoCast, Col(b, "result"));
        Assert.AreEqual("", Col(b, "dash_start_t"), "이미 앞 시도가 묶은 대시는 다시 묶지 않는다");

        string[] c = rows[2];
        Assert.AreEqual("", Col(c, "zone"), "벽 거리를 모르면 구간도 비운다");
        Assert.AreEqual("", Col(c, "win_n"), "창 밖(닫힌 뒤 0.6초 넘음) 시도");
        Assert.AreEqual("charge_0of2_next_0.80s", Col(c, "cause"));
    }

    [Test]
    public void 창_행은_든_시도_수와_결과를_센다()
    {
        List<string[]> rows = QABoss116Lab.BuildWindows(Key, SampleLines());
        string[] h = QABoss116Lab.WindowsHeader;
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("1.500", rows[0][System.Array.IndexOf(h, "dur_s")]);
        Assert.AreEqual("Dash", rows[0][System.Array.IndexOf(h, "attack")]);
        Assert.AreEqual("1", rows[0][System.Array.IndexOf(h, "attempts")]);
        Assert.AreEqual("rmb_not_started", rows[1][System.Array.IndexOf(h, "outcomes")]);
    }

    [Test]
    public void 대시_행은_묶인_시도_번호를_적는다()
    {
        List<string[]> rows = QABoss116Lab.BuildDashes(Key, SampleLines());
        string[] h = QABoss116Lab.DashesHeader;
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("1", rows[0][System.Array.IndexOf(h, "attempt_n")]);
        Assert.AreEqual("wall", rows[0][System.Array.IndexOf(h, "zone")]);
        Assert.AreEqual("true", rows[0][System.Array.IndexOf(h, "blocked")]);
    }

    [Test]
    public void 요약은_구간별_적중률과_원인을_센다()
    {
        string csv = QABoss103Csv.Line(QABoss116Lab.AttemptsHeader) + "\n" +
                     string.Join("\n", QABoss116Lab.BuildAttempts(Key, SampleLines()).Select(QABoss103Csv.Line));
        string win = QABoss103Csv.Line(QABoss116Lab.WindowsHeader) + "\n" +
                     string.Join("\n", QABoss116Lab.BuildWindows(Key, SampleLines()).Select(QABoss103Csv.Line));
        QABoss116Lab.Summary s = QABoss116Lab.Summarize(QABoss103CsvTable.Parse(csv), QABoss103CsvTable.Parse(win));
        Assert.AreEqual(3, s.All.Attempts);
        Assert.AreEqual(1, s.All.Casts);
        Assert.AreEqual(1, s.All.Judged);
        Assert.AreEqual(0, s.All.Match);
        Assert.AreEqual(1, s.All.Shortened);
        Assert.AreEqual(1, s.ByZone["wall"].Attempts);
        Assert.AreEqual(1, s.ByZone["open"].Attempts);
        Assert.AreEqual(1, s.ByZone["unknown"].Attempts);
        Assert.AreEqual(1, s.Blockers["Wall_North"]);
        Assert.AreEqual(1, s.Causes["no_charge"]["charge_0of2_next_0.80s"]);
        Assert.AreEqual(2, s.Windows);
        Assert.AreEqual(2, s.WindowsWithAttempt);
        Assert.AreEqual(1.15, s.WindowMedianS, 1e-9);
    }

    // ── 격리 ─────────────────────────────────────────────────

    private static readonly System.Type[] LabComponents = { typeof(QABoss116LabDirector), typeof(QABoss116LabCamera) };

    private static string GuidOf(System.Type t) =>
        AssetDatabase.AssetPathToGUID(AssetDatabase.FindAssets($"t:MonoScript {t.Name}").Select(AssetDatabase.GUIDToAssetPath)
            .Single(p => Path.GetFileNameWithoutExtension(p) == t.Name));

    [Test]
    public void 랩_씬은_빌드_목록에_없다()
    {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(QABoss116Lab.ScenePath));
        CollectionAssert.DoesNotContain(EditorBuildSettings.scenes.Select(s => s.path.Replace('\\', '/')).ToArray(), QABoss116Lab.ScenePath);
    }

    [Test]
    public void 랩_컴포넌트는_랩_씬에만_있다()
    {
        string[] guids = LabComponents.Select(GuidOf).ToArray();
        Assert.IsTrue(guids.All(g => !string.IsNullOrEmpty(g)));
        bool Uses(string path) { string text = File.ReadAllText(path); return guids.Any(g => text.Contains("guid: " + g)); }
        Assert.IsTrue(Uses(QABoss116Lab.ScenePath), "검색이 실제로 잡는지 — 랩 씬에는 있어야 한다");
        string[] others = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories))
            .Select(p => p.Replace('\\', '/'))
            .Where(p => p != QABoss116Lab.ScenePath)
            .Where(Uses)
            .ToArray();
        CollectionAssert.IsEmpty(others);
    }

    [Test]
    public void 랩_배치는_창_최소화를_부르지_않는다()
    {
        string path = AssetDatabase.FindAssets("t:MonoScript QABoss116LabBatchRunner").Select(AssetDatabase.GUIDToAssetPath)
            .Single(p => Path.GetFileNameWithoutExtension(p) == "QABoss116LabBatchRunner");
        StringAssert.DoesNotContain("QAWindowMinimizer.", File.ReadAllText(path));
    }
}
#endif
