#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>할 일 116-C5 — 대쉬 간파 예측 랩 수동 모드 순수 규칙(<see cref="QABoss116LabManual"/>·<see cref="QABoss116ManualTracker"/>).</summary>
public sealed class QABoss116LabManualTests
{
    private static readonly string[] Key = { "1", "0", "manual", "0" };

    private static QABoss116ManualFrame F(float t, bool rmb = false, bool dash = false, bool inDash = false, float elapsed = 0f, float remaining = 0f,
        bool inInterrupt = false, bool attached = false, int charge = 1, bool open = false, string state = "Idle") =>
        new QABoss116ManualFrame
        {
            T = t, ServerT = 100.0 + t, Rmb = rmb, Dash = dash, InDash = inDash, DashElapsed = elapsed, DashRemaining = remaining,
            InInterrupt = inInterrupt, Attached = attached, Charge = charge, CounterOpen = open, State = state,
        };

    /// <summary>프레임들을 차례로 넣고 끝난 시도들을 모은다.</summary>
    private static List<QABoss116ManualAttempt> Run(QABoss116ManualTracker tr, params QABoss116ManualFrame[] frames)
    {
        var done = new List<QABoss116ManualAttempt>();
        foreach (QABoss116ManualFrame f in frames)
        {
            QABoss116ManualAttempt a = tr.Step(f);
            if (a != null)
                done.Add(a);
        }
        return done;
    }

    // ── 모드 ─────────────────────────────────────────────────

    [Test]
    public void 설정_파일이_없을_때만_수동_모드()
    {
        Assert.AreEqual(QABoss116LabMode.Manual, QABoss116LabManual.ResolveMode(false, false));
        Assert.AreEqual(QABoss116LabMode.Batch, QABoss116LabManual.ResolveMode(true, true));
        Assert.AreEqual(QABoss116LabMode.Plain, QABoss116LabManual.ResolveMode(true, false), "오래됐거나 깨진 설정 파일 = 기록 없는 사람 모드(C4 그대로)");
    }

    [Test]
    public void 결과_폴더_이름()
    {
        Assert.AreEqual("116manual-20261028_1105", QABoss116LabManual.FolderName(new System.DateTime(2026, 10, 28, 11, 5, 30)));
    }

    // ── 대시 → 우클릭 ─────────────────────────────────────────

    [Test]
    public void 대시_우클릭_창_안이면_attached()
    {
        var tr = new QABoss116ManualTracker();
        List<QABoss116ManualAttempt> done = Run(tr,
            F(0.00f, dash: true, open: true),
            F(0.02f, inDash: true, elapsed: 0.02f, remaining: 0.23f, state: "Dash"),
            F(0.07f, rmb: true, inDash: true, elapsed: 0.07f, remaining: 0.18f, state: "Dash"),
            F(0.09f, inDash: true, attached: true, elapsed: 0.09f, remaining: 0.16f, state: "Dash"));
        Assert.AreEqual(1, done.Count);
        QABoss116ManualAttempt a = done[0];
        Assert.AreEqual(QABoss116Order.DashRmb, a.Order);
        Assert.AreEqual("attached", a.Outcome);
        Assert.AreEqual(0.07f, a.GapS, 1e-4f);
        Assert.AreEqual(0.07f, a.DashAgeS, 1e-4f, "대시 시작 = 첫 대시 프레임 시각 − 경과");
        Assert.AreEqual(0.25f, a.DashTotalS, 1e-4f);
        Assert.IsTrue(a.CounterOpenFirst);
        Assert.IsFalse(a.GateAtFirst);
        Assert.AreEqual("Idle>Dash", a.Seen);
        Assert.IsFalse(tr.Busy);
    }

    [Test]
    public void 대시가_안_섰으면_dash_rejected_게이트는_첫_입력()
    {
        List<QABoss116ManualAttempt> done = Run(new QABoss116ManualTracker(),
            F(0.00f, dash: true, charge: 0),
            F(0.05f, rmb: true, charge: 0));
        Assert.AreEqual("dash_rejected", done.Single().Outcome);
        Assert.IsTrue(done[0].GateAtFirst, "거부 사유(no_charge 등)는 대시를 누른 순간 값");
    }

    [Test]
    public void 대시가_끝난_뒤_우클릭이면_dash_ended_before_press()
    {
        List<QABoss116ManualAttempt> done = Run(new QABoss116ManualTracker(),
            F(0.00f, dash: true),
            F(0.02f, inDash: true, elapsed: 0.02f, remaining: 0.23f),
            F(0.30f),
            F(0.40f, rmb: true));
        QABoss116ManualAttempt a = done.Single();
        Assert.AreEqual("dash_ended_before_press", a.Outcome);
        Assert.AreEqual(0.40f, a.DashAgeS, 1e-4f);
        Assert.AreEqual("delay_beyond_dash", QABoss116Lab.Cause(a.Outcome,
            new Dictionary<string, string> { ["plannedS"] = "0.400", ["dashTotalS"] = "0.250", ["dashAgeS"] = "0.400" }));
    }

    [Test]
    public void 창_밖_우클릭은_대시_끝나고_간파면_skill_after_dash()
    {
        List<QABoss116ManualAttempt> done = Run(new QABoss116ManualTracker(),
            F(0.00f, dash: true),
            F(0.02f, inDash: true, elapsed: 0.02f, remaining: 0.23f),
            F(0.18f, rmb: true, inDash: true, elapsed: 0.18f, remaining: 0.07f),
            F(0.26f, inInterrupt: true, state: "Skill:PaladinInterruptSkill"));
        QABoss116ManualAttempt a = done.Single();
        Assert.AreEqual("skill_after_dash", a.Outcome);
        Assert.AreEqual("press_after_window", QABoss116Lab.Cause(a.Outcome,
            new Dictionary<string, string> { ["dashAgeS"] = "0.180", ["windowS"] = "0.100" }));
    }

    [Test]
    public void 대시만_누르면_시도가_아니다()
    {
        var tr = new QABoss116ManualTracker();
        List<QABoss116ManualAttempt> done = Run(tr,
            F(0.00f, dash: true),
            F(0.02f, inDash: true, elapsed: 0.02f, remaining: 0.23f),
            F(0.30f),
            F(0.70f));
        CollectionAssert.IsEmpty(done);
        Assert.IsFalse(tr.Busy, $"짝 입력 상한 {QABoss116ManualTracker.PairSeconds}s 뒤 비운다");
    }

    [Test]
    public void 같은_프레임_대시_우클릭은_대시_우클릭_순서()
    {
        List<QABoss116ManualAttempt> done = Run(new QABoss116ManualTracker(),
            F(0.00f, dash: true, rmb: true),
            F(0.02f, inDash: true, attached: true, elapsed: 0.02f, remaining: 0.23f));
        QABoss116ManualAttempt a = done.Single();
        Assert.AreEqual(QABoss116Order.DashRmb, a.Order);
        Assert.IsTrue(a.SameFrame);
        Assert.AreEqual(0f, a.GapS, 1e-6f);
        Assert.AreEqual("attached", a.Outcome);
    }

    // ── 우클릭 → 대시 ─────────────────────────────────────────

    [Test]
    public void 우클릭_대시_간파_중_대시면_attached()
    {
        List<QABoss116ManualAttempt> done = Run(new QABoss116ManualTracker(),
            F(0.00f, rmb: true, open: true),
            F(0.02f, inInterrupt: true, open: true),
            F(0.06f, dash: true, inInterrupt: true, open: false),
            F(0.08f, inDash: true, attached: true, elapsed: 0.02f, remaining: 0.23f));
        QABoss116ManualAttempt a = done.Single();
        Assert.AreEqual(QABoss116Order.RmbDash, a.Order);
        Assert.AreEqual("attached", a.Outcome);
        Assert.IsTrue(a.CounterOpenFirst);
        Assert.IsFalse(a.CounterOpenSecond);
        Assert.AreEqual("press_at_0.06s_of_0.10s", QABoss116Lab.Cause("attached",
            new Dictionary<string, string> { ["dashAgeS"] = "na", ["gapS"] = "0.060", ["windowS"] = "0.100" }), "대시 경과가 없으면 입력 간격");
    }

    [Test]
    public void 우클릭_대시_실패_사유()
    {
        Assert.AreEqual("no_charge", Run(new QABoss116ManualTracker(),
            F(0.00f, rmb: true), F(0.02f, inInterrupt: true), F(0.06f, dash: true, inInterrupt: true, charge: 0)).Single().Outcome);
        Assert.AreEqual("rmb_not_started", Run(new QABoss116ManualTracker(),
            F(0.00f, rmb: true), F(0.02f), F(0.06f, dash: true)).Single().Outcome);
        Assert.AreEqual("interrupt_ended_before_dash", Run(new QABoss116ManualTracker(),
            F(0.00f, rmb: true), F(0.02f, inInterrupt: true), F(0.30f, dash: true)).Single().Outcome);
        Assert.AreEqual("dash_without_transfer", Run(new QABoss116ManualTracker(),
            F(0.00f, rmb: true), F(0.02f, inInterrupt: true), F(0.06f, dash: true, inInterrupt: true),
            F(0.08f, inDash: true, elapsed: 0.02f, remaining: 0.23f), F(0.35f)).Single().Outcome);
        Assert.AreEqual("no_dash", Run(new QABoss116ManualTracker(),
            F(0.00f, rmb: true), F(0.02f, inInterrupt: true), F(0.06f, dash: true, inInterrupt: true),
            F(0.50f, inInterrupt: true), F(1.10f)).Single().Outcome);
    }

    [Test]
    public void 결과를_보는_중_새_입력이면_앞_시도를_끝내고_새로_시작()
    {
        var tr = new QABoss116ManualTracker();
        List<QABoss116ManualAttempt> done = Run(tr,
            F(0.00f, dash: true),
            F(0.02f, inDash: true, elapsed: 0.02f, remaining: 0.23f),
            F(0.05f, rmb: true, inDash: true, elapsed: 0.05f, remaining: 0.20f),
            F(0.10f, rmb: true, inDash: true, elapsed: 0.10f, remaining: 0.15f));
        Assert.AreEqual("no_attach", done.Single().Outcome);
        Assert.IsTrue(tr.TookFirst);
        Assert.IsTrue(tr.Busy, "새 우클릭이 다음 시도의 첫 입력");
    }

    [Test]
    public void 정지하면_보던_시도만_끝내고_짝_기다리던_것은_버린다()
    {
        var tr = new QABoss116ManualTracker();
        Run(tr, F(0.00f, dash: true), F(0.02f, inDash: true, elapsed: 0.02f, remaining: 0.23f), F(0.05f, rmb: true, inDash: true, elapsed: 0.05f));
        Assert.AreEqual("no_attach", tr.Flush(F(0.06f)).Outcome);

        var waiting = new QABoss116ManualTracker();
        Run(waiting, F(0.00f, rmb: true));
        Assert.IsNull(waiting.Flush(F(0.05f)));
    }

    // ── 줄 → C4 집계 ──────────────────────────────────────────

    [Test]
    public void 사람_시도_줄은_C4_집계에_그대로_들어간다()
    {
        var tr = new QABoss116ManualTracker();
        QABoss116ManualAttempt rejected = Run(tr, F(0.00f, dash: true, charge: 0, open: true), F(0.05f, rmb: true, charge: 0, open: true)).Single();
        var gate = new List<string> { "charge", "0", "maxCharge", "2", "nextChargeS", "0.80", "canMove", "true", "grounded", "true", "dashOn", "true",
            "inputSrc", "true", "windowS", "0.100" };
        var lines = new List<string>
        {
            "labwin event=open n=1 t=99.500 attack=Dash",
            QABoss116LabManual.FormatAttempt(rejected, "Paladin", 0, 1, "Idle", "", gate),
            "labwin event=close n=1 t=101.000",
        };
        List<string[]> rows = QABoss116Lab.BuildAttempts(Key, lines);
        string[] h = QABoss116Lab.AttemptsHeader;
        string Col(string c) => rows.Single()[System.Array.IndexOf(h, c)];
        Assert.AreEqual("human", Col("source"));
        Assert.AreEqual("dash_rmb", Col("order"));
        Assert.AreEqual("dash_rejected", Col("outcome"));
        Assert.AreEqual("no_charge", Col("cause"));
        Assert.AreEqual("1", Col("win_n"));
        Assert.AreEqual("true", Col("counter_open_first"));
        Assert.AreEqual("0.050", Col("gap_s"));
        Assert.AreEqual(QABoss116DashPredict.NoCast, Col("result"));

        string csv = QABoss103Csv.Line(h) + "\n" + string.Join("\n", rows.Select(QABoss103Csv.Line));
        QABoss116Lab.Summary s = QABoss116Lab.Summarize(QABoss103CsvTable.Parse(csv), null);
        Assert.AreEqual(1, s.ByCounter["in"].Attempts);
        string line = QABoss116LabManual.SummaryLine(s, "C:/UnityProject/QA/116manual-x");
        StringAssert.Contains("시도 1", line);
        StringAssert.Contains("dash_rejected 1", line);
        StringAssert.Contains("카운터 창 안 1/밖 0", line);
        StringAssert.Contains("대시 끊긴 이유 없음", line, "대시가 안 선 시도라 끊긴 이유가 없다(116-C6)");
        StringAssert.Contains("116manual-x", line);
    }

    [Test]
    public void 봇_시도_줄은_출처가_bot()
    {
        List<string[]> rows = QABoss116Lab.BuildAttempts(Key, new[] { "dashint slot=0 clientId=0 order=dash_rmb outcome=no_charge serverT=1.000" });
        Assert.AreEqual("bot", rows.Single()[System.Array.IndexOf(QABoss116Lab.AttemptsHeader, "source")]);
    }
}
#endif
