#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 116 — 대쉬 간파 예측 QA(봇 굴림·판 파일 → dash_predict.csv 묶기·요약·리포트 섹션·창 기록 질의·클론 pid·설정).</summary>
public sealed class QABoss116DashPredictTests
{
    private static readonly string[] Key = { "1", "0", "Normal", "103" };

    private const string ServerCast =
        "dp event=cast side=server castId=s1-1 clientId=1 host=false serverT=50.000 castServerT=50.000 skill=AssassinInterruptSkill order=dash_rmb " +
        "predEndPos=0.00,4.00 predResolveInS=0.300 target=TwentyThreeBoss targetInRange=true targetOpen=true winRemainS=0.500 winSrc=history " +
        "distM=1.00 angleDeg=5 predict=success predReason=success prepS=0.300 prepSrc=observed";
    private const string ServerJudge =
        "dp event=judge side=server castId=s1-1 clientId=1 host=false serverT=50.300 castServerT=50.000 skill=AssassinInterruptSkill order=dash_rmb " +
        "predict=success predReason=success result=no_target match=false mismatch=dash_shortened resolveInS=0.310 predResolveInS=0.300 timeErrS=0.010 " +
        "posErrM=2.00 shortfallM=2.00 winCloseAfterCastS=0.400 targetOpenAtResolve=false targetMoveM=0.10 targetYawDeg=2.0 rttMs=30";
    private const string OwnerCast =
        "dp event=cast side=owner castId=o1-7 clientId=1 host=false serverT=49.980 castServerT=49.980 skill=AssassinInterruptSkill order=dash_rmb " +
        "predict=fail predReason=window_closed_now";
    private const string OwnerHandOff =
        "dp event=handoff side=owner castId=o1-7 clientId=1 host=false serverT=50.200 castServerT=49.980 posErrM=0.50";
    private const string BotLine =
        "dashint slot=1 character=Assassin clientId=1 order=dash_rmb outcome=attached delayS=0.040 window=3 serverT=49.990";
    private const string BotNoCast =
        "dashint slot=2 character=Gunner clientId=2 order=rmb_dash outcome=rmb_not_started delayS=0.050 window=4 serverT=80.000";

    [Test]
    public void Plan_PctZero_NeverUses_AndAlwaysRollsThree()
    {
        var a = new System.Random(5);
        var b = new System.Random(5);
        for (int i = 0; i < 50; i++)
        {
            Assert.AreEqual(QABoss116Order.None, QABoss116DashPlan.Roll(a, 0, out double d));
            Assert.That(d, Is.InRange(QABoss116DashPlan.DelayMin, QABoss116DashPlan.DelayMax));
            b.NextDouble(); b.NextDouble(); b.NextDouble();
        }
        Assert.AreEqual(b.NextDouble(), a.NextDouble(), "사용 여부와 무관하게 창마다 3번 굴린다");
    }

    [Test]
    public void Plan_Pct100_UsesBothOrders()
    {
        var rng = new System.Random(116);
        int dashRmb = 0, rmbDash = 0;
        for (int i = 0; i < 200; i++)
        {
            QABoss116Order o = QABoss116DashPlan.Roll(rng, 100, out _);
            Assert.AreNotEqual(QABoss116Order.None, o);
            if (o == QABoss116Order.DashRmb) dashRmb++; else rmbDash++;
        }
        Assert.Greater(dashRmb, 60);
        Assert.Greater(rmbDash, 60);
    }

    [Test]
    public void ToPartLine_StripsTag()
    {
        Assert.AreEqual("dp event=cast side=server", QABoss116DashPredict.ToPartLine("[DashPredict] event=cast side=server"));
        Assert.IsNull(QABoss116DashPredict.ToPartLine("[QA103] event=bot_rule"));
    }

    [Test]
    public void BuildRows_JoinsServerOwnerAndBot()
    {
        List<string[]> rows = QABoss116DashPredict.BuildRows(Key,
            new[] { BotLine, OwnerCast, ServerCast, OwnerHandOff, ServerJudge, BotNoCast, "counter slot=1 cast=true", "" });
        Assert.AreEqual(2, rows.Count);
        string[] h = QABoss116DashPredict.Header;
        string Get(string[] r, string col) => r[System.Array.IndexOf(h, col)];

        string[] r0 = rows[0];
        Assert.AreEqual("1", Get(r0, "run"));
        Assert.AreEqual("Assassin", Get(r0, "character"));
        Assert.AreEqual("dash_rmb", Get(r0, "order"));
        Assert.AreEqual("attached", Get(r0, "bot_outcome"));
        Assert.AreEqual("success", Get(r0, "predict"));
        Assert.AreEqual("no_target", Get(r0, "result"));
        Assert.AreEqual("false", Get(r0, "match"));
        Assert.AreEqual("dash_shortened", Get(r0, "mismatch"));
        Assert.AreEqual("o1-7", Get(r0, "owner_cast_id"));
        Assert.AreEqual("true", Get(r0, "owner_match"), "오너 실패 예측 = 실제 대상 없음");
        Assert.AreEqual("0.50", Get(r0, "owner_dash_end_pos_err_m"));
        Assert.AreEqual("-0.020", Get(r0, "owner_cast_dt_s"));

        string[] r1 = rows[1];
        Assert.AreEqual(QABoss116DashPredict.NoCast, Get(r1, "result"));
        Assert.AreEqual("Gunner", Get(r1, "character"));
        Assert.AreEqual("rmb_dash", Get(r1, "order"));
    }

    [Test]
    public void BuildRows_OwnerTooFar_NotJoined_JudgeMissing()
    {
        string farOwner = OwnerCast.Replace("castServerT=49.980", "castServerT=40.000");
        List<string[]> rows = QABoss116DashPredict.BuildRows(Key, new[] { ServerCast, farOwner });
        Assert.AreEqual(1, rows.Count);
        string[] h = QABoss116DashPredict.Header;
        Assert.AreEqual("", rows[0][System.Array.IndexOf(h, "owner_cast_id")]);
        Assert.AreEqual("missing", rows[0][System.Array.IndexOf(h, "result")]);
        Assert.AreEqual("Assassin", rows[0][System.Array.IndexOf(h, "character")], "봇 줄 없으면 스킬 이름으로");
    }

    private static string Csv(IEnumerable<string[]> rows)
    {
        var sb = new System.Text.StringBuilder(QABoss103Csv.Line(QABoss116DashPredict.Header)).Append("\r\n");
        foreach (string[] r in rows)
            sb.Append(QABoss103Csv.Line(r)).Append("\r\n");
        return sb.ToString();
    }

    private static string FakeCsv()
    {
        var lines = new List<string> { BotLine, OwnerCast, ServerCast, OwnerHandOff, ServerJudge, BotNoCast };
        // 적중 2건(성공 예측 → 성공 · 실패 예측 → 실패), 중단 1건
        lines.Add(ServerCast.Replace("s1-1", "s0-2").Replace("clientId=1", "clientId=0").Replace("castServerT=50.000", "castServerT=60.000").Replace("order=dash_rmb", "order=rmb_dash"));
        lines.Add(ServerJudge.Replace("s1-1", "s0-2").Replace("clientId=1", "clientId=0").Replace("result=no_target match=false mismatch=dash_shortened", "result=success match=true mismatch=none"));
        lines.Add(ServerCast.Replace("s1-1", "s2-3").Replace("clientId=1", "clientId=2").Replace("castServerT=50.000", "castServerT=70.000").Replace("predict=success predReason=success", "predict=fail predReason=no_target_in_range").Replace("AssassinInterruptSkill", "GunnerInterruptSkill"));
        lines.Add(ServerJudge.Replace("s1-1", "s2-3").Replace("clientId=1", "clientId=2").Replace("result=no_target match=false mismatch=dash_shortened", "result=no_target match=true mismatch=none"));
        lines.Add(ServerCast.Replace("s1-1", "s0-4").Replace("clientId=1", "clientId=0").Replace("castServerT=50.000", "castServerT=90.000").Replace("AssassinInterruptSkill", "PaladinInterruptSkill"));
        lines.Add(ServerJudge.Replace("s1-1", "s0-4").Replace("clientId=1", "clientId=0").Replace("result=no_target match=false mismatch=dash_shortened", "result=aborted match=false mismatch=aborted"));
        return Csv(QABoss116DashPredict.BuildRows(Key, lines));
    }

    [Test]
    public void Summarize_AccuracyAndBuckets()
    {
        QABoss116DashPredict.Summary s = QABoss116DashPredict.Summarize(QABoss103CsvTable.Parse(FakeCsv()));
        Assert.AreEqual(5, s.All.Rows);
        Assert.AreEqual(3, s.All.Judged);
        Assert.AreEqual(2, s.All.Match);
        Assert.AreEqual(1, s.All.PredSuccessActualFail);
        Assert.AreEqual(0, s.All.PredFailActualSuccess);
        Assert.AreEqual(1, s.All.Aborted);
        Assert.AreEqual(1, s.All.NoCast);
        Assert.AreEqual(2.0 / 3.0, s.All.Accuracy, 1e-9);
        Assert.AreEqual(1, s.All.OwnerJudged);
        Assert.IsTrue(s.ByOrder.ContainsKey("dash_rmb") && s.ByOrder.ContainsKey("rmb_dash"));
        Assert.IsTrue(s.ByCharacter.ContainsKey("Gunner"));
        List<KeyValuePair<string, int>> top = s.TopMismatch();
        Assert.AreEqual(2, top.Count);
        Assert.AreEqual("aborted", top[0].Key, "같은 건수면 이름순");
        Assert.AreEqual("dash_shortened", top[1].Key);
    }

    [Test]
    public void Report_DashPredictSection_RendersWhenCsvPresent()
    {
        QABoss103ReportInput input = QABoss103ReportFake.Input();
        string without = QABoss103Report.Build(input);
        StringAssert.DoesNotContain("116 대쉬 간파 예측 적중률", without);

        input.DashPredictCsv = FakeCsv();
        input.BatchText += "\nsvn_rev=543\ndash_interrupt_pct=60\nminimize_windows=1\n";
        string html = QABoss103Report.Build(input);
        StringAssert.Contains("116 대쉬 간파 예측 적중률", html);
        StringAssert.Contains("66.7%", html);
        StringAssert.Contains("대시 → 우클릭", html);
        StringAssert.Contains("우클릭 → 대시", html);
        StringAssert.Contains("대시가 막혀 짧아짐", html);
        StringAssert.Contains("SVN 리비전(작업 사본)", html);
        StringAssert.Contains("r543", html);
        StringAssert.Contains("대쉬 간파 비율", html);
    }

    [Test]
    public void WindowHistory_PredictQueries()
    {
        var h = new InterruptWindowHistory();
        h.Record(1.0, true);
        h.Record(1.5, false);   // 0.5
        h.Record(3.0, true);
        h.Record(3.7, false);   // 0.7
        h.Record(5.0, true);
        h.Record(5.6, false);   // 0.6
        h.Record(8.0, true);

        Assert.IsTrue(h.TryGetOpenSince(8.2, out double since));
        Assert.AreEqual(8.0, since, 1e-9);
        Assert.IsFalse(h.TryGetOpenSince(4.0, out _), "4.0 = 3.7 에 닫힌 뒤");
        Assert.IsTrue(h.TryGetFirstCloseAfter(3.2, out double close));
        Assert.AreEqual(3.7, close, 1e-9);
        Assert.IsFalse(h.TryGetFirstCloseAfter(8.1, out _));
        Assert.IsTrue(h.TryGetTypicalOpenDuration(out double typical));
        Assert.AreEqual(0.6, typical, 1e-9, "0.5·0.6·0.7 중앙값");
        Assert.IsFalse(new InterruptWindowHistory().TryGetTypicalOpenDuration(out _));
    }

    [Test]
    public void Config_DashIntRoundTrip()
    {
        var cfg = new QABoss103Config { Run = 3, DashInterruptPct = 60, OutDir = "C:/QA/116-x" };
        Assert.IsTrue(QABoss103Config.TryParse(cfg.Format(), out QABoss103Config back));
        Assert.AreEqual(60, back.DashInterruptPct);
        Assert.AreEqual("C:/QA/116-x", back.OutDir);
        StringAssert.DoesNotContain("dashint", new QABoss103Config().Format(), "0 이면 기존 형식 그대로");
        Assert.IsTrue(QABoss103Config.TryParse("dashint=250", out QABoss103Config clamp));
        Assert.AreEqual(100, clamp.DashInterruptPct);
    }

    [Test]
    public void BatchStamp_Accepts116()
    {
        Assert.AreEqual("20261027_2100", QABoss103Batch.BatchStamp("116-20261027_2100"));
        Assert.AreEqual("116-20261027_2100", QABoss103Batch.BatchFolderName(new System.DateTime(2026, 10, 27, 21, 0, 0), QABoss103Batch.Prefix116));
    }

    [Test]
    public void ClonePids_FilterByProjectVpFolder()
    {
        string output =
            "100|\"C:\\UnityEditors\\6000.3.16f1\\Editor\\Unity.exe\" -projectpath C:\\UnityProject\\MainProject-Worktree3\r\n" +
            "200|\"C:\\UnityEditors\\Unity.exe\" -projectPath \"C:\\UnityProject\\MainProject-Worktree3\\Library\\VP\\mppm1\" -name Player2\r\n" +
            "300|Unity.exe -projectPath C:/UnityProject/MainProject-Worktree2/Library/VP/mppm1\r\n" +
            "400|Unity.exe -projectPath c:/unityproject/mainproject-worktree3/library/vp/mppm2\r\n" +
            "garbage line\r\n";
        List<int> pids = QAWindowMinimizer.ParseClonePids(output, "C:\\UnityProject\\MainProject-Worktree3");
        CollectionAssert.AreEqual(new[] { 200, 400 }, pids);
        Assert.IsEmpty(QAWindowMinimizer.ParseClonePids("", "C:/x"));
    }
}
#endif
