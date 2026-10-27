#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 88 Q3 순수 계산 — 로그 파싱·런 구간·누름↔판정 잇기·집계·스윕 구간·스위트 설정·명령 파일·리포트 표.</summary>
public sealed class QAInterruptLag88ReportTests
{
    // networking.md 'Interrupt lag measurement' 형식 + tee 접두어.
    private const string JudgeFail =
        "12:00:01.000 [Log] [InterruptLag] event=judge clientId=1 host=false skill=GunnerInterruptSkill target=GauntletBot(Clone) pressT=812.402 arriveT=812.431 hitT=812.632 lagMs=29.0 hitAfterPressMs=230.0 rttMs=58 pressWindow=open hitWindow=closed correctedWindow=open result=fail wouldSucceed=true capMs=100";

    private static string Judge(ulong client, bool host, double pressT, string result, bool would, double lag, double rtt, string target = "GauntletBot(Clone)") =>
        $"[InterruptLag] event=judge clientId={client} host={(host ? "true" : "false")} skill=S target={target} pressT={pressT.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} " +
        $"arriveT=0 hitT=0 lagMs={lag.ToString(System.Globalization.CultureInfo.InvariantCulture)} rttMs={rtt.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
        $"pressWindow=open hitWindow=open correctedWindow=open result={result} wouldSucceed={(would ? "true" : "false")} capMs=100";

    private static string Press(ulong client, bool host, string mode, double pressT, string offsetMs) =>
        $"[QA88] event=press clientId={client} host={(host ? "true" : "false")} mode={mode} kind=midboss target=G idx=0 n=30 planOffsetMs=na offsetMs={offsetMs} " +
        $"planReactMs=na reactMs=200.0 openAtPress=true pressT={pressT.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} netsimMs=35";

    private static List<QA88LogLine> Parse(params string[] lines)
    {
        var list = new List<QA88LogLine>();
        foreach (string l in lines)
            if (QAInterruptLag88Log.TryParse(l, out QA88LogLine p))
                list.Add(p);
        return list;
    }

    // ─── 파싱 ─────────────────────────────────────────────────────────

    [Test]
    public void 판정줄은_tee_접두어가_있어도_키값을_읽는다()
    {
        Assert.IsTrue(QAInterruptLag88Log.TryParse(JudgeFail, out QA88LogLine p));
        Assert.IsTrue(p.IsLag);
        Assert.AreEqual("judge", p.Event);
        Assert.AreEqual("1", p.Get("clientId"));
        Assert.AreEqual(812.402, p.GetDouble("pressT"), 1e-9);
        Assert.AreEqual(58.0, p.GetDouble("rttMs"), 1e-9);
        Assert.IsTrue(p.GetBool("wouldSucceed"));
        Assert.AreEqual("fail", p.Get("result"));
    }

    [Test]
    public void 태그나_event가_없으면_무시()
    {
        Assert.IsFalse(QAInterruptLag88Log.TryParse("[QA] 48 시나리오 시작", out _));
        Assert.IsFalse(QAInterruptLag88Log.TryParse("[QA88] 원격 지연 35ms — 클론 2개에 기록", out _));
        Assert.IsFalse(QAInterruptLag88Log.TryParse("", out _));
    }

    [Test]
    public void na_값은_NaN()
    {
        Assert.IsTrue(QAInterruptLag88Log.TryParse(Press(1, false, "human", 1.0, "na"), out QA88LogLine p));
        Assert.IsTrue(double.IsNaN(p.GetDouble("offsetMs")));
        Assert.IsTrue(double.IsNaN(p.GetDouble("없는키")));
    }

    // ─── 런 구간 ──────────────────────────────────────────────────────

    [Test]
    public void 런_마커_사이만_런에_붙고_끝마커가_없으면_파일끝까지()
    {
        var spec0 = new QA88RunSpec { Index = 0, Kind = QAInterruptTargetKind.Midboss, DelayMs = 5, Mode = QAInterruptBotMode.Sweep };
        var spec1 = new QA88RunSpec { Index = 1, Kind = QAInterruptTargetKind.Midboss, DelayMs = 5, Mode = QAInterruptBotMode.Human };
        var lines = new List<string>
        {
            Judge(1, false, 1.000, "success", true, 10, 20), // 런 밖
            QAInterruptLag88Log.FormatRunBegin(spec0, "host", false),
            Judge(1, false, 2.000, "success", true, 10, 20),
            "[Log] 관계없는 줄",
            QAInterruptLag88Log.FormatRunEnd(0, "host", "done", 0, 0, 0, 0, ""),
            Judge(1, false, 3.000, "fail", false, 10, 20), // 런 밖
            QAInterruptLag88Log.FormatRunBegin(spec1, "host", false),
            Judge(1, false, 4.000, "fail", true, 10, 20),
        };
        Dictionary<int, List<QA88LogLine>> runs = QAInterruptLag88Log.SplitByRun(lines);
        Assert.AreEqual(2, runs.Count);
        Assert.AreEqual(3, runs[0].Count, "begin + judge + end");
        Assert.AreEqual(2.0, runs[0][1].GetDouble("pressT"), 1e-9);
        Assert.AreEqual(2, runs[1].Count, "begin + judge(끝 마커 없음)");
        Assert.AreEqual(4.0, runs[1][1].GetDouble("pressT"), 1e-9);
    }

    [Test]
    public void run_end_줄의_비고_값은_공백없이_한토큰()
    {
        string end = QAInterruptLag88Log.FormatRunEnd(3, "client-mppm1a", "done", 30, 31, 1, 2, "Warning/Softlock:이동 지시 중 6s");
        Assert.IsTrue(QAInterruptLag88Log.TryParse(end, out QA88LogLine p));
        Assert.AreEqual(3, p.GetInt("run"));
        Assert.AreEqual(30, p.GetInt("attempts"));
        Assert.AreEqual(2, p.GetInt("findings"));
        Assert.AreEqual("Warning/Softlock:이동_지시_중_6s", p.Get("findingTop"));
    }

    // ─── 집계 ─────────────────────────────────────────────────────────

    [Test]
    public void 원격_집계_성공_보정성공_구제_손실_RTT_지연()
    {
        List<QA88LogLine> lines = Parse(
            Judge(1, false, 10.000, "success", true, 30, 60),
            Judge(1, false, 11.000, "fail", true, 40, 80),   // 구제
            Judge(1, false, 12.000, "fail", false, 50, 100),
            Judge(1, false, 13.000, "success", false, 20, 40), // 손실
            Press(1, false, "human", 10.000, "na"),
            Press(1, false, "human", 11.000, "na"),
            Press(1, false, "human", 12.000, "na"),
            Press(1, false, "human", 13.000, "na"));
        List<QA88GroupStats> groups = QAInterruptLag88Aggregate.Aggregate(lines);
        Assert.AreEqual(1, groups.Count, "호스트 줄이 없으면 remote 만");
        QA88GroupStats g = groups[0];
        Assert.AreEqual("remote", g.Group);
        Assert.AreEqual(4, g.Judges);
        Assert.AreEqual(4, g.Presses);
        Assert.AreEqual(4, g.Matched);
        Assert.AreEqual(2, g.Success);
        Assert.AreEqual(2, g.WouldSucceed);
        Assert.AreEqual(1, g.Rescued);
        Assert.AreEqual(1, g.Lost);
        Assert.AreEqual(4, g.WithTarget);
        Assert.AreEqual(70.0, g.RttAvgMs, 1e-9);
        Assert.AreEqual(100.0, g.RttMaxMs, 1e-9);
        Assert.AreEqual(35.0, g.LagAvgMs, 1e-9);
        Assert.AreEqual(50.0, g.LagMaxMs, 1e-9);
        Assert.AreEqual(0, g.Buckets.Count, "B 모드는 구간 없음");
    }

    [Test]
    public void 잇기는_같은_클라_허용오차_안_가장_가까운_판정만()
    {
        List<QA88LogLine> lines = Parse(
            Judge(1, false, 20.000, "success", true, 30, 60),
            Judge(2, false, 30.000, "success", true, 30, 60),
            Press(1, false, "sweep", 20.010, "-10.0"),  // 0.010 차 → 이어짐
            Press(1, false, "sweep", 25.000, "0.0"),    // 판정 없음
            Press(1, false, "sweep", 30.000, "0.0"));   // 같은 시각이지만 다른 클라 판정
        QA88GroupStats g = QAInterruptLag88Aggregate.Aggregate(lines)[0];
        Assert.AreEqual(3, g.Presses);
        Assert.AreEqual(2, g.Judges);
        Assert.AreEqual(1, g.Matched);
    }

    [Test]
    public void 판정_하나는_누름_하나에만_이어진다()
    {
        List<QA88LogLine> lines = Parse(
            Judge(1, false, 40.000, "success", true, 30, 60),
            Press(1, false, "sweep", 40.000, "0.0"),
            Press(1, false, "sweep", 40.005, "5.0"));
        Assert.AreEqual(1, QAInterruptLag88Aggregate.Aggregate(lines)[0].Matched);
    }

    [Test]
    public void 스윕_구간은_25ms_내림()
    {
        Assert.AreEqual(-150.0, QAInterruptLag88Aggregate.BucketLo(-150.0), 1e-9);
        Assert.AreEqual(-150.0, QAInterruptLag88Aggregate.BucketLo(-126.0), 1e-9);
        Assert.AreEqual(-25.0, QAInterruptLag88Aggregate.BucketLo(-0.1), 1e-9);
        Assert.AreEqual(0.0, QAInterruptLag88Aggregate.BucketLo(0.0), 1e-9);
        Assert.AreEqual(25.0, QAInterruptLag88Aggregate.BucketLo(49.9), 1e-9);
        Assert.AreEqual(50.0, QAInterruptLag88Aggregate.BucketLo(50.0), 1e-9);
    }

    [Test]
    public void A_모드_구간별_성공과_보정성공()
    {
        List<QA88LogLine> lines = Parse(
            Judge(1, false, 1.000, "success", true, 30, 60),
            Judge(1, false, 2.000, "success", true, 30, 60),
            Judge(1, false, 3.000, "fail", true, 30, 60),
            Judge(1, false, 4.000, "fail", false, 30, 60),
            Press(1, false, "sweep", 1.000, "-140.0"),
            Press(1, false, "sweep", 2.000, "-130.5"),
            Press(1, false, "sweep", 3.000, "-10.0"),
            Press(1, false, "sweep", 4.000, "30.0"),
            Press(1, false, "sweep", 5.000, "-145.0")); // 판정 없음 → 구간에 안 들어감
        List<QA88Bucket> b = QAInterruptLag88Aggregate.Aggregate(lines)[0].Buckets;
        Assert.AreEqual(3, b.Count);
        Assert.AreEqual(-150.0, b[0].LoMs, 1e-9);
        Assert.AreEqual(2, b[0].N);
        Assert.AreEqual(2, b[0].Success);
        Assert.AreEqual(2, b[0].WouldSucceed);
        Assert.AreEqual(-25.0, b[1].LoMs, 1e-9);
        Assert.AreEqual(0, b[1].Success);
        Assert.AreEqual(1, b[1].WouldSucceed);
        Assert.AreEqual(25.0, b[2].LoMs, 1e-9);
        Assert.AreEqual(0, b[2].WouldSucceed);
    }

    [Test]
    public void 호스트_줄이_있으면_host_그룹을_따로()
    {
        List<QA88LogLine> lines = Parse(
            Judge(0, true, 1.000, "success", true, 0, 0),
            Press(0, true, "human", 1.000, "na"),
            Judge(1, false, 2.000, "fail", true, 60, 120),
            Press(1, false, "human", 2.000, "na"));
        List<QA88GroupStats> groups = QAInterruptLag88Aggregate.Aggregate(lines);
        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual("remote", groups[0].Group);
        Assert.AreEqual(1, groups[0].Rescued);
        Assert.AreEqual("host", groups[1].Group);
        Assert.AreEqual(1, groups[1].Success);
        Assert.AreEqual(1, groups[1].Matched);
        Assert.AreEqual(0.0, groups[1].RttMaxMs, 1e-9);
    }

    [Test]
    public void 대상_none_은_대상있음에서_빠진다()
    {
        List<QA88LogLine> lines = Parse(Judge(1, false, 1.0, "fail", false, 10, 20, "none"));
        Assert.AreEqual(0, QAInterruptLag88Aggregate.Aggregate(lines)[0].WithTarget);
    }

    // ─── 스위트 설정·명령 파일 ───────────────────────────────────────

    [Test]
    public void 전체_스위트는_16런_대상_지연_모드_순()
    {
        QA88SuiteConfig cfg = QA88SuiteConfig.FullSuite();
        Assert.AreEqual(16, cfg.Runs.Count);
        Assert.AreEqual(30, cfg.Attempts);
        Assert.AreEqual(QAInterruptTargetKind.Midboss, cfg.Runs[0].Kind);
        Assert.AreEqual(5, cfg.Runs[0].DelayMs);
        Assert.AreEqual(QAInterruptBotMode.Sweep, cfg.Runs[0].Mode);
        Assert.AreEqual(QAInterruptBotMode.Human, cfg.Runs[1].Mode);
        Assert.AreEqual(35, cfg.Runs[2].DelayMs);
        Assert.AreEqual(QAInterruptTargetKind.TwentyThree, cfg.Runs[8].Kind);
        Assert.AreEqual(120, cfg.Runs[15].DelayMs);
        for (int i = 0; i < cfg.Runs.Count; i++)
            Assert.AreEqual(i, cfg.Runs[i].Index);
    }

    [Test]
    public void 스위트_설정_왕복()
    {
        QA88SuiteConfig cfg = QA88SuiteConfig.FullSuite(10);
        cfg.RunTimeoutSec = 1200;
        cfg.HostBot = true;
        Assert.IsTrue(QA88SuiteConfig.TryParse(cfg.Format(), out QA88SuiteConfig back));
        Assert.AreEqual(10, back.Attempts);
        Assert.AreEqual(1200, back.RunTimeoutSec);
        Assert.IsTrue(back.HostBot);
        Assert.AreEqual(16, back.Runs.Count);
        for (int i = 0; i < 16; i++)
        {
            Assert.AreEqual(cfg.Runs[i].Kind, back.Runs[i].Kind);
            Assert.AreEqual(cfg.Runs[i].DelayMs, back.Runs[i].DelayMs);
            Assert.AreEqual(cfg.Runs[i].Mode, back.Runs[i].Mode);
        }
    }

    [Test]
    public void 스위트_설정_틀린_런은_건너뛰고_런이_없으면_false()
    {
        Assert.IsTrue(QA88SuiteConfig.TryParse("runs=midboss:35:human,boss:5:sweep,23:x:sweep,23:80:fast,23:80:sweep", out QA88SuiteConfig cfg));
        Assert.AreEqual(2, cfg.Runs.Count);
        Assert.AreEqual(1, cfg.Runs[1].Index);
        Assert.AreEqual(QAInterruptTargetKind.TwentyThree, cfg.Runs[1].Kind);
        Assert.IsFalse(QA88SuiteConfig.TryParse("attempts=30\nruns=", out _));
        Assert.IsFalse(QA88SuiteConfig.TryParse("", out _));
    }

    [Test]
    public void 명령_파일_왕복_스위트_폴더_공백_허용()
    {
        var cmd = new QA88Command
        {
            Seq = 7, State = "go", Run = 3, Kind = QAInterruptTargetKind.TwentyThree, Mode = QAInterruptBotMode.Human,
            Attempts = 12, Seed = 91, Press = true, SuiteDir = @"D:\QA Logs\88-20261022_120000",
        };
        Assert.IsTrue(QA88Command.TryParse(cmd.Format(), out QA88Command back));
        Assert.AreEqual(7, back.Seq);
        Assert.AreEqual("go", back.State);
        Assert.AreEqual(3, back.Run);
        Assert.AreEqual(QAInterruptTargetKind.TwentyThree, back.Kind);
        Assert.AreEqual(QAInterruptBotMode.Human, back.Mode);
        Assert.AreEqual(12, back.Attempts);
        Assert.AreEqual(91, back.Seed);
        Assert.IsTrue(back.Press);
        Assert.AreEqual(cmd.SuiteDir, back.SuiteDir);
        Assert.IsFalse(QA88Command.TryParse("state=go run=1", out _), "seq 없으면 무시");
    }

    // ─── 리포트 ───────────────────────────────────────────────────────

    [Test]
    public void 리포트는_런표_스윕구간표_미실행_건너뜀_로그목록()
    {
        QA88SuiteConfig cfg = QA88SuiteConfig.FullSuite();
        var header = new QA88ReportHeader { Created = "2026-10-22 12:00:00", Status = "진행 중 2/16", UnityVersion = "6000.3.16f1", Attempts = 30, RunTimeoutSec = 600, Pressers = "원격 client-mppm1 1명" };
        header.Notes.Add("판 유지: 대상 보스 체력 채움");
        header.LogFiles.Add("logs/host.log");

        var r0 = new QA88RunResult { Spec = cfg.Runs[0] };
        r0.Groups = QAInterruptLag88Aggregate.Aggregate(Parse(
            Judge(1, false, 1.000, "success", true, 30, 60),
            Judge(1, false, 2.000, "fail", true, 40, 80),
            Press(1, false, "sweep", 1.000, "-140.0"),
            Press(1, false, "sweep", 2.000, "10.0")));
        r0.Notes.Add("탐지기(host) 1건: Warning/Softlock:x|y");
        var r1 = new QA88RunResult { Spec = cfg.Runs[1], Skipped = true };
        r1.Notes.Add("원격 지연 5ms 적용 미확인");

        string md = QAInterruptLag88ReportBuilder.Build(header, cfg.Runs, new List<QA88RunResult> { r0, r1 });
        StringAssert.Contains("PASS/FAIL 판정 없음", md);
        StringAssert.Contains("판 유지: 대상 보스 체력 채움", md);
        StringAssert.Contains("| 1 | 중간보스 | 5 | A 스윕 | remote | 2 | 2 | 2 | 2 | 1 | 50.0% | 2 | 100.0% | 1 | 0 | 70.0/80.0 | 35.0/40.0 | 탐지기(host) 1건: Warning/Softlock:x\\|y |", md);
        StringAssert.Contains("| 2 | 중간보스 | 5 | B 사람형 | - |", md);
        StringAssert.Contains("건너뜀: 원격 지연 5ms 적용 미확인", md);
        StringAssert.Contains("| 3 | 중간보스 | 35 | A 스윕 | - |", md);
        StringAssert.Contains("미실행", md);
        StringAssert.Contains("### #1 중간보스 · 5ms · A 스윕 · remote", md);
        StringAssert.Contains("| -150 ~ -125 | 1 | 1 | 100.0% | 1 | 100.0% |", md);
        StringAssert.Contains("| 0 ~ 25 | 1 | 0 | 0.0% | 1 | 100.0% |", md);
        StringAssert.Contains("[logs/host.log](logs/host.log)", md);
    }

    [Test]
    public void 비율은_분모_0이면_대시()
    {
        Assert.AreEqual("-", QAInterruptLag88ReportBuilder.Pct(0, 0));
        Assert.AreEqual("33.3%", QAInterruptLag88ReportBuilder.Pct(1, 3));
        Assert.AreEqual("-", QAInterruptLag88ReportBuilder.Ms(double.NaN));
    }
}
#endif
