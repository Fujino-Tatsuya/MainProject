using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 존 장부(<see cref="MapSceneMonsterLedger"/>) — 존 배정·클리어 판정(전멸/핵심 처치)·필터,
/// 그리고 존 클리어가 차례보다 먼저 올 때의 게이트 보류(<see cref="MapSceneFlowModel.RequestGate"/>).
/// 원본: Docs/design/tutorial-scenario.md §2.2·§4 · Docs/tech/tutorial-director.md §8.
/// </summary>
public sealed class MapSceneMonsterLedgerTests
{
    static MapSceneMonsterLedger NewLedger()
    {
        var l = new MapSceneMonsterLedger();
        l.SetClearRule(MapSceneArea.Zone2, MapSceneZoneClearRule.AllDead);
        l.SetClearRule(MapSceneArea.Zone3, MapSceneZoneClearRule.KeyDead);
        l.SetClearRule(MapSceneArea.Zone4, MapSceneZoneClearRule.KeyDead);
        return l;
    }

    static List<MapSceneArea> RecordCleared(MapSceneMonsterLedger l)
    {
        var log = new List<MapSceneArea>();
        l.ZoneCleared += z => log.Add(z);
        return log;
    }

    // ─── 존 배정 ────────────────────────────────────────────────────────

    [Test]
    public void 위치가_든_존_사각형으로_배정한다()
    {
        var areas = new MapSceneAreaTracker();
        areas.SetArea(MapSceneArea.Zone1, new MapSceneRect(30f, -200f, 10f, 10f));
        areas.SetArea(MapSceneArea.Zone2, new MapSceneRect(0f, -200f, 10f, 10f));
        areas.SetArea(MapSceneArea.Zone4, new MapSceneRect(10f, -110f, 20f, 20f));
        areas.SetArea(MapSceneArea.Corridor1, MapSceneRect.FromMinMax(10f, -202f, 20f, -198f));

        Assert.AreEqual(MapSceneArea.Zone2, MapSceneMonsterLedger.ClassifyZone(areas, 3f, -195f));
        Assert.AreEqual(MapSceneArea.Zone4, MapSceneMonsterLedger.ClassifyZone(areas, 25f, -95f));
        // 복도·존 밖은 배정하지 않는다(복도 사각형은 존이 아니다).
        Assert.AreEqual(MapSceneArea.Zone1, MapSceneMonsterLedger.ClassifyZone(areas, 21f, -200f));
        Assert.AreEqual(MapSceneArea.None, MapSceneMonsterLedger.ClassifyZone(areas, 15f, -200f));
        Assert.AreEqual(MapSceneArea.None, MapSceneMonsterLedger.ClassifyZone(areas, -500f, 0f));
        Assert.AreEqual(MapSceneArea.None, MapSceneMonsterLedger.ClassifyZone(null, 0f, -200f));
    }

    [Test]
    public void 회전된_존_사각형도_로컬_범위로_판정한다()
    {
        var areas = new MapSceneAreaTracker();
        // 20×40 을 90° 돌리면 월드 X 가 긴 쪽이다.
        areas.SetArea(MapSceneArea.Zone3, new MapSceneRect(0f, 0f, 10f, 20f, 90f));
        Assert.AreEqual(MapSceneArea.Zone3, MapSceneMonsterLedger.ClassifyZone(areas, 18f, 0f));
        Assert.AreEqual(MapSceneArea.None, MapSceneMonsterLedger.ClassifyZone(areas, 0f, 18f));
    }

    [Test]
    public void 존_없음과_중복_id_는_등록하지_않는다()
    {
        var l = NewLedger();
        Assert.IsFalse(l.Add(1, MapSceneArea.None, false));
        Assert.IsTrue(l.Add(1, MapSceneArea.Zone2, false));
        Assert.IsFalse(l.Add(1, MapSceneArea.Zone4, true));
        Assert.AreEqual(1, l.Count(MapSceneArea.Zone2));
        Assert.AreEqual(0, l.Count(MapSceneArea.Zone4));
    }

    // ─── 존2 = 전멸 ─────────────────────────────────────────────────────

    [Test]
    public void 존2_는_마지막_한_마리가_죽어야_클리어된다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        for (ulong i = 0; i < 9; i++) l.Add(i, MapSceneArea.Zone2, false);

        for (ulong i = 0; i < 8; i++) l.MarkDead(i);
        CollectionAssert.IsEmpty(cleared);
        Assert.AreEqual(1, l.Count(MapSceneArea.Zone2, MapSceneMonsterFilter.All, true));

        l.MarkDead(8);
        CollectionAssert.AreEqual(new[] { MapSceneArea.Zone2 }, cleared);
        Assert.IsTrue(l.IsZoneCleared(MapSceneArea.Zone2));
    }

    [Test]
    public void 같은_사망과_모르는_id_는_무시하고_클리어는_한_번만()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        int deaths = 0;
        l.MemberDied += (_, __, ___) => deaths++;
        l.Add(1, MapSceneArea.Zone2, false);

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
        l.Add(1, MapSceneArea.Zone2, false);
        l.Add(2, MapSceneArea.Zone4, false);
        l.Add(3, MapSceneArea.Zone4, true);

        l.MarkDead(2);
        CollectionAssert.IsEmpty(cleared);
    }

    // ─── 존3·존4 = 핵심(중간보스급) 처치 ─────────────────────────────────

    [Test]
    public void 존4_는_일반_몹이_남아도_중간보스를_잡으면_클리어된다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        for (ulong i = 0; i < 30; i++) l.Add(i, MapSceneArea.Zone4, false);
        l.Add(100, MapSceneArea.Zone4, true);

        for (ulong i = 0; i < 30; i++) l.MarkDead(i);
        CollectionAssert.IsEmpty(cleared);   // 일반 전멸로는 안 열린다(D4)

        l.MarkDead(100);
        CollectionAssert.AreEqual(new[] { MapSceneArea.Zone4 }, cleared);
    }

    [Test]
    public void 존3_스피너봇_처치로_클리어된다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        l.Add(7, MapSceneArea.Zone3, true);
        l.MarkDead(7);
        CollectionAssert.AreEqual(new[] { MapSceneArea.Zone3 }, cleared);
    }

    [Test]
    public void 핵심_몬스터가_없는_존은_전멸로_판정한다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        l.Add(1, MapSceneArea.Zone3, false);
        l.Add(2, MapSceneArea.Zone3, false);

        l.MarkDead(1);
        CollectionAssert.IsEmpty(cleared);
        l.MarkDead(2);
        CollectionAssert.AreEqual(new[] { MapSceneArea.Zone3 }, cleared);
    }

    [Test]
    public void 규칙이_없는_존은_클리어되지_않는다()
    {
        var l = NewLedger();
        var cleared = RecordCleared(l);
        l.Add(1, MapSceneArea.Zone1, false);
        l.MarkDead(1);
        CollectionAssert.IsEmpty(cleared);
        Assert.IsFalse(l.MeetsClearRule(MapSceneArea.Zone2));   // 0마리 존 = 클리어 아님
    }

    // ─── 필터 ───────────────────────────────────────────────────────────

    [Test]
    public void 존4_기상_대상은_중간보스를_뺄_수_있다()
    {
        var l = NewLedger();
        l.Add(1, MapSceneArea.Zone4, false);
        l.Add(2, MapSceneArea.Zone4, true);
        l.Add(3, MapSceneArea.Zone4, false);
        l.Add(4, MapSceneArea.Zone2, false);
        l.MarkDead(3);

        var ids = new List<ulong>();
        l.GetAlive(MapSceneArea.Zone4, MapSceneMonsterFilter.NormalOnly, ids);
        CollectionAssert.AreEqual(new ulong[] { 1 }, ids);
        l.GetAlive(MapSceneArea.Zone4, MapSceneMonsterFilter.KeyOnly, ids);
        CollectionAssert.AreEqual(new ulong[] { 2 }, ids);
        l.GetAlive(MapSceneArea.Zone4, MapSceneMonsterFilter.All, ids);
        CollectionAssert.AreEqual(new ulong[] { 1, 2 }, ids);

        Assert.AreEqual(3, l.Count(MapSceneArea.Zone4));
        Assert.AreEqual(2, l.Count(MapSceneArea.Zone4, MapSceneMonsterFilter.NormalOnly));
        Assert.AreEqual(1, l.Count(MapSceneArea.Zone4, MapSceneMonsterFilter.NormalOnly, true));
    }

    // ─── 게이트 보류(존 클리어가 차례보다 먼저) ─────────────────────────────

    static MapSceneFlowModel ModelWithPlayer()
    {
        var m = new MapSceneFlowModel(60d);
        m.AddPlayer(0);
        return m;
    }

    static void OpenUpTo(MapSceneFlowModel m, MapSceneGate lastInclusive)
    {
        for (int i = 0; i <= (int)lastInclusive; i++) m.TryOpenGate((MapSceneGate)i);
    }

    [Test]
    public void 차례면_즉시_연다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, MapSceneGate.G2_Zone2Entry);
        Assert.IsTrue(m.RequestGate(MapSceneGate.G3_Corridor2Entry));
        Assert.IsTrue(m.IsGateOpen(MapSceneGate.G3_Corridor2Entry));
        Assert.AreEqual(0, m.RequestedGateMask);
    }

    [Test]
    public void 차례가_아니면_보류했다가_앞_게이트가_열리면_연다()
    {
        var m = ModelWithPlayer();
        var opened = new List<MapSceneGate>();
        m.GateOpened += (g, _) => opened.Add(g);

        // 존2 를 G2 전에 정리했다(디버그 기상 등) → G3 보류.
        Assert.IsFalse(m.RequestGate(MapSceneGate.G3_Corridor2Entry));
        Assert.IsTrue(m.IsGateRequested(MapSceneGate.G3_Corridor2Entry));
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G3_Corridor2Entry));

        // G1(개인)·G2(개인)가 차례로 열리는 같은 Evaluate 에서 G3 도 연다.
        for (int bit = 0; bit < 32; bit++)   // G1 조건 항목 전부(v8 여덟 항목 — 107 뒤 맵씬은 G1 을 바로 연다)
            if ((MapSceneFlowModel.G1Mask & (1 << bit)) != 0) m.ReportCheck(0, (MapSceneCheck)bit);
        m.ReportCheck(0, MapSceneCheck.TraitCorridor1);
        m.Evaluate(2d);

        CollectionAssert.AreEqual(new[]
        {
            MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry,
        }, opened);
        Assert.AreEqual(0, m.RequestedGateMask);
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G4_Zone3Entry));   // 보류는 그 게이트 하나만
    }

    [Test]
    public void G9_보류는_G8_수동_개방_뒤_Evaluate_에서_열린다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, MapSceneGate.G7_Zone4Entry);

        Assert.IsFalse(m.RequestGate(MapSceneGate.G9_Corridor4Entry));   // 타이머 시작(G8) 전 중간보스 처치
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G9_Corridor4Entry));

        m.TryOpenGate(MapSceneGate.G8_TimerStart);
        m.Evaluate(2d);
        Assert.IsTrue(m.IsGateOpen(MapSceneGate.G9_Corridor4Entry));
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G10_BossEnterAndPad));
    }

    [Test]
    public void G6_보류는_G5_가_열리는_순간_같이_열린다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, MapSceneGate.G4_Zone3Entry);

        m.RequestGate(MapSceneGate.G6_Corridor3Entry);
        m.AddCounter(MapSceneCounter.Zone3Interrupts, 3);
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G5_SpinnerRealMode), "v8 — 연계 3회만으로는 G5 아님(마지막 회복 대기)");
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G6_Corridor3Entry));

        m.RequestGate(MapSceneGate.G5_SpinnerRealMode);   // 존3 흐름 — 마지막 회복 끝
        m.Evaluate(2d);
        Assert.IsTrue(m.IsGateOpen(MapSceneGate.G5_SpinnerRealMode));
        Assert.IsTrue(m.IsGateOpen(MapSceneGate.G6_Corridor3Entry));
    }

    [Test]
    public void 이미_열린_게이트_요청은_무시한다()
    {
        var m = ModelWithPlayer();
        OpenUpTo(m, MapSceneGate.G3_Corridor2Entry);
        Assert.IsFalse(m.RequestGate(MapSceneGate.G3_Corridor2Entry));
        Assert.AreEqual(0, m.RequestedGateMask);
    }
}
