using NUnit.Framework;

/// <summary>
/// 존1 순수 규칙(S7) — 이동 보고 상한 검증 · 이동 누적(순간이동 제외) · 패시브 툴팁 1초 유지 · 착지 1초 · 목표 패널 순서·대기 줄.
/// 원본: Docs/design/tutorial-scenario.md §3.0·§3.1.
/// </summary>
public sealed class TutorialZone1ModelTests
{
    static TutorialPlayerProgress Progress(params TutorialCheck[] checks)
    {
        var p = new TutorialPlayerProgress { ClientId = 0, Connected = true };
        foreach (TutorialCheck c in checks) p.CheckMask |= 1 << (int)c;
        return p;
    }

    static readonly TutorialCheck[] All = TutorialZone1Progress.ChecklistOrder;

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
    public void 목표는_이동_대시_기본공격_패시브_Q_E_우클릭_R_복도_순서()
    {
        TutorialZone1Task[] expected =
        {
            TutorialZone1Task.Move, TutorialZone1Task.Dash, TutorialZone1Task.Attack, TutorialZone1Task.Passive,
            TutorialZone1Task.SkillQ, TutorialZone1Task.SkillE, TutorialZone1Task.Interrupt, TutorialZone1Task.SkillR,
            TutorialZone1Task.Go,
        };
        var done = new System.Collections.Generic.List<TutorialCheck>();
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], TutorialZone1Progress.Resolve(Progress(done.ToArray()), false).Task, $"{i}단계");
            if (i < All.Length) done.Add(All[i]);
        }
    }

    [Test]
    public void 순서를_건너_먼저_쓴_항목은_체크된_채_건너뛴다()
    {
        // R·Q 를 먼저 썼어도 이동이 남았으면 목표는 이동, 체크는 그대로. 다시 시키지 않는다.
        TutorialZone1View v = TutorialZone1Progress.Resolve(Progress(TutorialCheck.SkillR, TutorialCheck.SkillQ), false);
        Assert.AreEqual(TutorialZone1Task.Move, v.Task);
        Assert.IsTrue(v.Has(TutorialCheck.SkillR));
        Assert.IsTrue(v.Has(TutorialCheck.SkillQ));

        // 앞 넷 + Q 완료 → E, R 은 이미 했으니 E → 우클릭 → Go.
        TutorialPlayerProgress p = Progress(TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.BasicAttack,
                                            TutorialCheck.PassiveTooltip, TutorialCheck.SkillQ, TutorialCheck.SkillR);
        Assert.AreEqual(TutorialZone1Task.SkillE, TutorialZone1Progress.Resolve(p, false).Task);
        p.CheckMask |= 1 << (int)TutorialCheck.SkillE;
        Assert.AreEqual(TutorialZone1Task.Interrupt, TutorialZone1Progress.Resolve(p, false).Task);
        p.CheckMask |= 1 << (int)TutorialCheck.SkillInterrupt;
        Assert.AreEqual(TutorialZone1Task.Go, TutorialZone1Progress.Resolve(p, false).Task, "R 은 먼저 써 둠");
    }

    [Test]
    public void 패시브를_마쳤어도_호버가_안_끝났으면_패시브에_머문다()
    {
        TutorialPlayerProgress all = Progress(All);
        TutorialZone1View v = TutorialZone1Progress.Resolve(all, passiveInfoPending: true);
        Assert.AreEqual(TutorialZone1Task.Passive, v.Task);
        Assert.IsTrue(v.PassiveDone);
        Assert.IsFalse(v.HighlightPassiveSlot, "끝났으면 P 칸 펄스는 끈다");
    }

    [Test]
    public void P칸_강조는_패시브_차례에만()
    {
        Assert.IsFalse(TutorialZone1Progress.Resolve(Progress(TutorialCheck.Move, TutorialCheck.Dash), false).HighlightPassiveSlot);
        Assert.IsTrue(TutorialZone1Progress.Resolve(
            Progress(TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.BasicAttack), false).HighlightPassiveSlot);
    }

    [Test]
    public void 패시브는_이동_대시_기본공격을_마친_뒤에만_센다()
    {
        Assert.IsFalse(TutorialZone1Progress.IsPassiveTurn(Progress(TutorialCheck.Move, TutorialCheck.Dash)));
        Assert.IsTrue(TutorialZone1Progress.IsPassiveTurn(Progress(TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.BasicAttack)));
    }

    [Test]
    public void 대기_줄은_G1_차례이고_내가_여덟_항목을_끝냈을_때만()
    {
        TutorialPlayerProgress done = Progress(All);
        TutorialPlayerProgress notDone = Progress(TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.BasicAttack, TutorialCheck.PassiveTooltip);

        Assert.IsTrue(TutorialZone1Progress.ShowsWait(true, TutorialGate.G1_Corridor1Entry, done));
        Assert.IsFalse(TutorialZone1Progress.ShowsWait(true, TutorialGate.G1_Corridor1Entry, notDone), "스킬 미사용");
        Assert.IsFalse(TutorialZone1Progress.ShowsWait(false, TutorialGate.G1_Corridor1Entry, done));
        Assert.IsFalse(TutorialZone1Progress.ShowsWait(true, TutorialGate.G2_Zone2Entry, done));
    }

    // ─── 디렉터 규칙과 맞물림 — 존1 체크 8개 = G1 ──────────────────────────────

    [Test]
    public void 존1_체크리스트_여덟_줄이_G1_개인_마스크와_같다()
    {
        int listed = 0;
        foreach (TutorialCheck c in All) listed |= 1 << (int)c;
        Assert.AreEqual(TutorialFlowModel.RequiredMaskOf(TutorialGate.G1_Corridor1Entry), listed);
        Assert.AreEqual(TutorialZone1Task.Go, TutorialZone1Progress.Resolve(Progress(All), false).Task);
    }

    // ─── 공격·스킬 사용 판정 ────────────────────────────────────────────────

    [Test]
    public void 스킬_슬롯마다_다른_체크_항목이다()
    {
        Assert.IsTrue(TutorialZone1SkillRules.TryGetSkillCheck(PlayerSkillSlot.Main, out TutorialCheck q));
        Assert.IsTrue(TutorialZone1SkillRules.TryGetSkillCheck(PlayerSkillSlot.Sub, out TutorialCheck e));
        Assert.IsTrue(TutorialZone1SkillRules.TryGetSkillCheck(PlayerSkillSlot.Interrupt, out TutorialCheck rmb));
        Assert.IsTrue(TutorialZone1SkillRules.TryGetSkillCheck(PlayerSkillSlot.Ultimate, out TutorialCheck r));
        CollectionAssert.AreEqual(
            new[] { TutorialCheck.SkillQ, TutorialCheck.SkillE, TutorialCheck.SkillInterrupt, TutorialCheck.SkillR },
            new[] { q, e, rmb, r });
    }

    [Test]
    public void 발사형_스킬은_승인으로_세지_않는다()
    {
        Assert.IsTrue(TutorialZone1SkillRules.CountsOnApproval(PlayerSkillSlot.Main, countsOnFire: false), "일반 Q = 승인");
        Assert.IsFalse(TutorialZone1SkillRules.CountsOnApproval(PlayerSkillSlot.Main, countsOnFire: true), "거너 Q = 발사 때");
        Assert.IsTrue(TutorialZone1SkillRules.CountsOnApproval(PlayerSkillSlot.Ultimate, countsOnFire: false), "R = 대상 확정 뒤 승인");
    }

    // ─── 허수아비 정리 (S12) ────────────────────────────────────────────────

    [Test]
    public void 허수아비는_G1_뒤_파티가_존1_을_떠나면_거둔다()
    {
        Assert.IsFalse(TutorialZone1Rules.ShouldDespawnDummies(false, false, TutorialStep.Zone1), "G1 전엔 연습 중");
        Assert.IsFalse(TutorialZone1Rules.ShouldDespawnDummies(true, true, TutorialStep.Corridor1), "아직 존1 에 남은 사람 눈앞에서 지우지 않는다");
        Assert.IsTrue(TutorialZone1Rules.ShouldDespawnDummies(true, false, TutorialStep.Corridor1));
        Assert.IsTrue(TutorialZone1Rules.ShouldDespawnDummies(true, true, TutorialStep.Zone2), "파티가 존2 로 넘어갔으면 낙오자가 있어도 거둔다");
    }
}
