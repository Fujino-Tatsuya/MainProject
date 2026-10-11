using NUnit.Framework;

/// <summary>
/// 존1 순수 규칙(S7) — 이동 보고 상한 검증 · 이동 누적(순간이동 제외) · 패시브 툴팁 1초 유지 · 착지 1초 · 목표 패널 순서·대기 줄.
/// 원본: Docs/design/tutorial-scenario.md §3.0·§3.1.
/// </summary>
public sealed class TutorialZone1ModelTests
{
    static TutorialPlayerProgress Progress(int hits = 0, params TutorialCheck[] checks)
    {
        var p = new TutorialPlayerProgress { ClientId = 0, Connected = true, AttackHits = hits };
        foreach (TutorialCheck c in checks) p.CheckMask |= 1 << (int)c;
        return p;
    }

    // ─── 이동 보고 상한 ─────────────────────────────────────────────────

    [Test]
    public void 이동_보고_목표_이상이고_가능한_거리면_받는다()
    {
        Assert.IsTrue(TutorialZone1Rules.IsMoveReportPlausible(5f, 1d));
        Assert.IsTrue(TutorialZone1Rules.IsMoveReportPlausible(5.4f, 10d));
    }

    [Test]
    public void 이동_보고_목표_미만은_거부()
    {
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(4.99f, 10d));
    }

    [Test]
    public void 이동_보고_경과시간으로_갈_수_없는_거리는_거부()
    {
        // 30 m/s × 0.05초 + 여유 3m = 4.5m < 5m
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(5f, 0.05d));
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(1000f, 2d));
        // 경계: 30 × 2 + 3 = 63
        Assert.IsTrue(TutorialZone1Rules.IsMoveReportPlausible(63f, 2d));
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(63.1f, 2d));
    }

    [Test]
    public void 이동_보고_비정상_값은_거부()
    {
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(float.NaN, 10d));
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(float.PositiveInfinity, 10d));
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(5f, -1d));
        Assert.IsFalse(TutorialZone1Rules.IsMoveReportPlausible(5f, double.NaN));
    }

    // ─── 이동 누적 ──────────────────────────────────────────────────────

    [Test]
    public void 이동_누적_첫_샘플은_기준점이고_이후_수평거리를_더한다()
    {
        var m = new TutorialMoveAccumulator();
        Assert.AreEqual(0f, m.Sample(0f, 0f, 0.1f));
        m.Sample(1f, 0f, 0.1f);
        m.Sample(1f, 1f, 0.1f);
        Assert.AreEqual(2f, m.TotalMeters, 1e-4f);
    }

    [Test]
    public void 이동_누적_순간이동은_버린다()
    {
        var m = new TutorialMoveAccumulator(maxSpeed: 30f, stepSlackMeters: 0.25f);
        m.Sample(0f, 0f, 0.016f);
        Assert.AreEqual(0f, m.Sample(20f, 0f, 0.016f), "한 프레임 20m = 순간이동");
        m.Sample(20.3f, 0f, 0.016f);   // 새 기준점에서 0.3m — 정상
        Assert.AreEqual(0.3f, m.TotalMeters, 1e-4f);
    }

    [Test]
    public void 이동_누적_끊으면_다음_샘플은_기준점()
    {
        var m = new TutorialMoveAccumulator();
        m.Sample(0f, 0f, 0.1f);
        m.Sample(1f, 0f, 0.1f);
        m.Break();
        Assert.AreEqual(0f, m.Sample(1.5f, 0f, 0.1f));
        Assert.AreEqual(1f, m.TotalMeters, 1e-4f);
    }

    // ─── 패시브 툴팁 1초 유지 ─────────────────────────────────────────────

    [Test]
    public void 툴팁_1초_유지하면_한번만_완료()
    {
        var h = new TutorialTooltipHold(1d);
        h.Show(10d);
        Assert.IsFalse(h.Tick(10.9d));
        Assert.IsTrue(h.Tick(11d));
        Assert.IsTrue(h.IsComplete);
        Assert.IsFalse(h.Tick(12d), "완료는 한 번만");
    }

    [Test]
    public void 툴팁_스치기만_하면_완료되지_않고_다시_처음부터()
    {
        var h = new TutorialTooltipHold(1d);
        h.Show(0d);
        h.Hide();
        Assert.IsFalse(h.Tick(5d), "닫힌 뒤에는 세지 않는다");
        h.Show(5d);
        Assert.IsFalse(h.Tick(5.5d));
        Assert.IsTrue(h.Tick(6d));
    }

    [Test]
    public void 툴팁_이미_떠_있으면_다시_보여도_시작시각은_그대로()
    {
        var h = new TutorialTooltipHold(1d);
        h.Show(0d);
        h.Show(0.8d);
        Assert.IsTrue(h.Tick(1d));
    }

    // ─── 착지 1초 ────────────────────────────────────────────────────────

    [Test]
    public void 착지_낙하중에는_세지_않고_연속_1초에_한번()
    {
        var t = new TutorialLandingTimer(1f);
        Assert.IsFalse(t.Tick(false, 5f), "낙하 중");
        Assert.IsFalse(t.Tick(true, 0.6f));
        Assert.IsFalse(t.Tick(false, 0.1f), "튕김 — 처음부터");
        Assert.IsFalse(t.Tick(true, 0.6f));
        Assert.IsTrue(t.Tick(true, 0.4f));
        Assert.IsFalse(t.Tick(true, 5f), "한 번만");
    }

    // ─── 목표 패널 ──────────────────────────────────────────────────────

    [Test]
    public void 목표는_이동_대시_평타_패시브_복도_순서()
    {
        Assert.AreEqual(TutorialZone1Task.Move, TutorialZone1Progress.Resolve(Progress(), false).Task);
        Assert.AreEqual(TutorialZone1Task.Dash, TutorialZone1Progress.Resolve(Progress(0, TutorialCheck.Move), false).Task);
        Assert.AreEqual(TutorialZone1Task.Attack,
            TutorialZone1Progress.Resolve(Progress(2, TutorialCheck.Move, TutorialCheck.Dash), false).Task);
        Assert.AreEqual(TutorialZone1Task.Passive,
            TutorialZone1Progress.Resolve(Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit), false).Task);
        Assert.AreEqual(TutorialZone1Task.Go,
            TutorialZone1Progress.Resolve(Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit, TutorialCheck.PassiveTooltip), false).Task);
    }

    [Test]
    public void 순서를_건너_먼저_끝낸_항목도_체크로_보인다()
    {
        // 대시·평타를 먼저 끝냈어도 이동이 남았으면 목표는 이동, 체크는 그대로.
        TutorialZone1View v = TutorialZone1Progress.Resolve(Progress(5, TutorialCheck.Dash, TutorialCheck.AttackHit), false);
        Assert.AreEqual(TutorialZone1Task.Move, v.Task);
        Assert.IsTrue(v.DashDone);
        Assert.IsTrue(v.AttackDone);
        Assert.AreEqual(5, v.AttackHits);
    }

    [Test]
    public void 패시브를_마쳤어도_호버가_안_끝났으면_패시브에_머문다()
    {
        TutorialPlayerProgress all = Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit, TutorialCheck.PassiveTooltip);
        TutorialZone1View v = TutorialZone1Progress.Resolve(all, passiveInfoPending: true);
        Assert.AreEqual(TutorialZone1Task.Passive, v.Task);
        Assert.IsTrue(v.PassiveDone);
        Assert.IsFalse(v.HighlightPassiveSlot, "끝났으면 P 칸 펄스는 끈다");
    }

    [Test]
    public void P칸_강조는_패시브_차례에만()
    {
        Assert.IsFalse(TutorialZone1Progress.Resolve(Progress(3, TutorialCheck.Move, TutorialCheck.Dash), false).HighlightPassiveSlot);
        Assert.IsTrue(TutorialZone1Progress.Resolve(
            Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit), false).HighlightPassiveSlot);
    }

    [Test]
    public void 적중수는_0에서_5사이로_보인다()
    {
        Assert.AreEqual(5, TutorialZone1Progress.Resolve(Progress(9), false).AttackHits);
        Assert.AreEqual(0, TutorialZone1Progress.Resolve(Progress(-3), false).AttackHits);
    }

    [Test]
    public void 패시브는_이동_대시_평타를_마친_뒤에만_센다()
    {
        Assert.IsFalse(TutorialZone1Progress.IsPassiveTurn(Progress(0, TutorialCheck.Move, TutorialCheck.Dash)));
        Assert.IsTrue(TutorialZone1Progress.IsPassiveTurn(Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit)));
    }

    [Test]
    public void 대기_줄은_G1_차례이고_내가_끝냈을_때만()
    {
        TutorialPlayerProgress done = Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit, TutorialCheck.PassiveTooltip);
        TutorialPlayerProgress notDone = Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit);

        Assert.IsTrue(TutorialZone1Progress.ShowsWait(true, TutorialGate.G1_Corridor1Entry, done));
        Assert.IsFalse(TutorialZone1Progress.ShowsWait(true, TutorialGate.G1_Corridor1Entry, notDone));
        Assert.IsFalse(TutorialZone1Progress.ShowsWait(false, TutorialGate.G1_Corridor1Entry, done));
        Assert.IsFalse(TutorialZone1Progress.ShowsWait(true, TutorialGate.G2_Zone2Entry, done));
    }

    // ─── 디렉터 규칙과 맞물림 — 존1 체크 4개 = G1 ──────────────────────────────

    [Test]
    public void 존1_체크_넷이_G1_개인_마스크와_같다()
    {
        TutorialPlayerProgress done = Progress(5, TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.AttackHit, TutorialCheck.PassiveTooltip);
        int mask = TutorialFlowModel.RequiredMaskOf(TutorialGate.G1_Corridor1Entry);
        Assert.AreEqual(mask, done.CheckMask & mask);
        Assert.AreEqual(TutorialZone1Task.Go, TutorialZone1Progress.Resolve(done, false).Task);
    }
}
