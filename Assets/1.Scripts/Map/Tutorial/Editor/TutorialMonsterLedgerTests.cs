using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 존 장부(<see cref="TutorialMonsterLedger"/>) — 존 배정·클리어 판정(전멸/핵심 처치)·필터,
/// 그리고 존 클리어가 차례보다 먼저 올 때의 게이트 보류(<see cref="TutorialFlowModel.RequestGate"/>).
/// 원본: Docs/design/tutorial-scenario.md §2.2·§4 · Docs/tech/tutorial-director.md §8.
/// </summary>
public sealed class TutorialMonsterLedgerTests
{
    static TutorialMonsterLedger NewLedger()
    {
        var l = new TutorialMonsterLedger();
        l.SetClearRule(TutorialArea.Zone2, TutorialZoneClearRule.AllDead);
        l.SetClearRule(TutorialArea.Zone3, TutorialZoneClearRule.KeyDead);
        l.SetClearRule(TutorialArea.Zone4, TutorialZoneClearRule.KeyDead);
        return l;
    }

    static List<TutorialArea> RecordCleared(TutorialMonsterLedger l)
    {
        var log = new List<TutorialArea>();
        l.ZoneCleared += z => log.Add(z);
        return log;
    }

    // ─── 존 배정 ────────────────────────────────────────────────────────

    [Test]
    public void 위치가_든_존_사각형으로_배정한다()
    {
        var areas = new TutorialAreaTracker();
        areas.SetArea(TutorialArea.Zone1, new TutorialRect(30f, -200f, 10f, 10f));
        areas.SetArea(TutorialArea.Zone2, new TutorialRect(0f, -200f, 10f, 10f));
        areas.SetArea(TutorialArea.Zone4, new TutorialRect(10f, -110f, 20f, 20f));
        areas.SetArea(TutorialArea.Corridor1, TutorialRect.FromMinMax(10f, -202f, 20f, -198f));

        Assert.AreEqual(TutorialArea.Zone2, TutorialMonsterLedger.ClassifyZone(areas, 3f, -195f));
        Assert.AreEqual(TutorialArea.Zone4, TutorialMonsterLedger.ClassifyZone(areas, 25f, -95f));
        // 복도·존 밖은 배정하지 않는다(복도 사각형은 존이 아니다).
        Assert.AreEqual(TutorialArea.Zone1, TutorialMonsterLedger.ClassifyZone(areas, 21f, -200f));
        Assert.AreEqual(TutorialArea.None, TutorialMonsterLedger.ClassifyZone(areas, 15f, -200f));
        Assert.AreEqual(TutorialArea.None, TutorialMonsterLedger.ClassifyZone(areas, -500f, 0f));
        Assert.AreEqual(TutorialArea.None, TutorialMonsterLedger.ClassifyZone(null, 0f, -200f));
    }

    [Test]
    public void 회전된_존_사각형도_로컬_범위로_판정한다()
    {
        var areas = new TutorialAreaTracker();
        // 20×40 을 90° 돌리면 월드 X 가 긴 쪽이다.
        areas.SetArea(TutorialArea.Zone3, new TutorialRect(0f, 0f, 10f, 20f, 90f));
        Assert.AreEqual(TutorialArea.Zone3, TutorialMonsterLedger.ClassifyZone(areas, 18f, 0f));
        Assert.AreEqual(TutorialArea.None, TutorialMonsterLedger.ClassifyZone(areas, 0f, 18f));
    }

    [Test]
    public void 존_없음과_중복_id_는_등록하지_않는다()
    {
        var l = NewLedger();
        Assert.IsFalse(l.Add(1, TutorialArea.None, false));
        Assert.IsTrue(l.Add(1, TutorialArea.Zone2, false));
        Assert.IsFalse(l.Add(1, TutorialArea.Zone4, true));
        Assert.AreEqual(1, l.Count(TutorialArea.Zone2));
        Assert.AreEqual(0, l.Count(TutorialArea.Zone4));
    }

    // ─── 존2 = 전멸 ─────────────────────────────────────────────────────

    [Test]
    public void 존2_는_마지막_한_마리가_죽어야_클리어된다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        for (ulong i = 0; i < 9; i++) l.Add(i, TutorialArea.Zone2, false);

        for (ulong i = 0; i < 8; i++) l.MarkDead(i);
        CollectionAssert.IsEmpty(cleared);
        Assert.AreEqual(1, l.Count(TutorialArea.Zone2, TutorialMonsterFilter.All, true));

        l.MarkDead(8);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone2 }, cleared);
        Assert.IsTrue(l.IsZoneCleared(TutorialArea.Zone2));
    }

    [Test]
    public void 같은_사망과_모르는_id_는_무시하고_클리어는_한_번만()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        int deaths = 0;
        l.MemberDied += (_, __, ___) => deaths++;
        l.Add(1, TutorialArea.Zone2, false);

        Assert.IsTrue(l.MarkDead(1));
        Assert.IsFalse(l.MarkDead(1));
        Assert.IsFalse(l.MarkDead(99));
        Assert.AreEqual(1, deaths);
        Assert.AreEqual(1, cleared.Count);
    }

    [Test]
    public void 다른_존_사망은_존2_를_클리어하지_않는다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        l.Add(1, TutorialArea.Zone2, false);
        l.Add(2, TutorialArea.Zone4, false);
        l.Add(3, TutorialArea.Zone4, true);

        l.MarkDead(2);
        CollectionAssert.IsEmpty(cleared);
    }

    // ─── 존3·존4 = 핵심(중간보스급) 처치 ─────────────────────────────────

    [Test]
    public void 존4_는_일반_몹이_남아도_중간보스를_잡으면_클리어된다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        for (ulong i = 0; i < 30; i++) l.Add(i, TutorialArea.Zone4, false);
        l.Add(100, TutorialArea.Zone4, true);

        for (ulong i = 0; i < 30; i++) l.MarkDead(i);
        CollectionAssert.IsEmpty(cleared);   // 일반 전멸로는 안 열린다(D4)

        l.MarkDead(100);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone4 }, cleared);
    }

    [Test]
    public void 존3_스피너봇_처치로_클리어된다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        l.Add(7, TutorialArea.Zone3, true);
        l.MarkDead(7);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone3 }, cleared);
    }

    [Test]
    public void 핵심_몬스터가_없는_존은_전멸로_판정한다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        l.Add(1, TutorialArea.Zone3, false);
        l.Add(2, TutorialArea.Zone3, false);

        l.MarkDead(1);
        CollectionAssert.IsEmpty(cleared);
        l.MarkDead(2);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone3 }, cleared);
    }

    [Test]
    public void 규칙이_없는_존은_클리어되지_않는다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        l.Add(1, TutorialArea.Zone1, false);
        l.MarkDead(1);
        CollectionAssert.IsEmpty(cleared);
        Assert.IsFalse(l.MeetsClearRule(TutorialArea.Zone2));   // 0마리 존 = 클리어 아님
    }

    // ─── 필터 ───────────────────────────────────────────────────────────

    [Test]
    public void 존4_기상_대상은_중간보스를_뺄_수_있다()
    {
        var l = NewLedger();
        l.Add(1, TutorialArea.Zone4, false);
        l.Add(2, TutorialArea.Zone4, true);
        l.Add(3, TutorialArea.Zone4, false);
        l.Add(4, TutorialArea.Zone2, false);
        l.MarkDead(3);

        var ids = new List<ulong>();
        l.GetAlive(TutorialArea.Zone4, TutorialMonsterFilter.NormalOnly, ids);
        CollectionAssert.AreEqual(new ulong[] { 1 }, ids);
        l.GetAlive(TutorialArea.Zone4, TutorialMonsterFilter.KeyOnly, ids);
        CollectionAssert.AreEqual(new ulong[] { 2 }, ids);
        l.GetAlive(TutorialArea.Zone4, TutorialMonsterFilter.All, ids);
        CollectionAssert.AreEqual(new ulong[] { 1, 2 }, ids);

        Assert.AreEqual(3, l.Count(TutorialArea.Zone4));
        Assert.AreEqual(2, l.Count(TutorialArea.Zone4, TutorialMonsterFilter.NormalOnly));
        Assert.AreEqual(1, l.Count(TutorialArea.Zone4, TutorialMonsterFilter.NormalOnly, true));
    }

    // ─── 게이트 보류(존 클리어가 차례보다 먼저) ─────────────────────────────

    static TutorialFlowModel ModelWithPlayer()
    {
        var m = new TutorialFlowModel(60d);
        m.AddPlayer(0);
        return m;
    }

    static void OpenUpTo(TutorialFlowModel m, TutorialGate lastInclusive)
    {
        for (int i = 0; i <= (int)lastInclusive; i++) m.TryOpenGate((TutorialGate)i);
    }

    [Test]
    public void 차례면_즉시_연다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, TutorialGate.G2_Zone2Entry);
        Assert.IsTrue(m.RequestGate(TutorialGate.G3_Corridor2Entry));
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G3_Corridor2Entry));
        Assert.AreEqual(0, m.RequestedGateMask);
    }

    [Test]
    public void 차례가_아니면_보류했다가_앞_게이트가_열리면_연다()
    {
        var m = ModelWithPlayer();
        var opened = new List<TutorialGate>();
        m.GateOpened += (g, _) => opened.Add(g);

        // 존2 를 G2 전에 정리했다(디버그 기상 등) → G3 보류.
        Assert.IsFalse(m.RequestGate(TutorialGate.G3_Corridor2Entry));
        Assert.IsTrue(m.IsGateRequested(TutorialGate.G3_Corridor2Entry));
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G3_Corridor2Entry));

        // G1(개인)·G2(개인)가 차례로 열리는 같은 Evaluate 에서 G3 도 연다.
        foreach (TutorialCheck c in TutorialZone1Progress.ChecklistOrder) m.ReportCheck(0, c);   // v8 G1 여덟 항목
        m.ReportCheck(0, TutorialCheck.TraitCorridor1);
        m.Evaluate(2d);

        CollectionAssert.AreEqual(new[]
        {
            TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry, TutorialGate.G3_Corridor2Entry,
        }, opened);
        Assert.AreEqual(0, m.RequestedGateMask);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G4_Zone3Entry));   // 보류는 그 게이트 하나만
    }

    [Test]
    public void G9_보류는_G8_수동_개방_뒤_Evaluate_에서_열린다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, TutorialGate.G7_Zone4Entry);

        Assert.IsFalse(m.RequestGate(TutorialGate.G9_Corridor4Entry));   // 타이머 시작(G8) 전 중간보스 처치
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G9_Corridor4Entry));

        m.TryOpenGate(TutorialGate.G8_TimerStart);
        m.Evaluate(2d);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G9_Corridor4Entry));
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G10_BossEnterAndPad));
    }

    [Test]
    public void G6_보류는_G5_카운터_도달과_같은_Evaluate_에서_열린다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, TutorialGate.G4_Zone3Entry);

        m.RequestGate(TutorialGate.G6_Corridor3Entry);
        m.AddCounter(TutorialCounter.Zone3Interrupts, 3);
        m.Evaluate(1d);

        Assert.IsTrue(m.IsGateOpen(TutorialGate.G5_SpinnerRealMode));
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G6_Corridor3Entry));
    }

    [Test]
    public void 이미_열린_게이트_요청은_무시한다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, TutorialGate.G3_Corridor2Entry);
        Assert.IsFalse(m.RequestGate(TutorialGate.G3_Corridor2Entry));
        Assert.AreEqual(0, m.RequestedGateMask);
    }
}
