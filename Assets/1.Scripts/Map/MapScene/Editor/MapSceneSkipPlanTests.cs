using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 구간 스킵 계획(할 일 34) — 단축키 매핑 · 뒤로 가기 거부(처음부터 예외) · 재시작 재진입 거부 · 목적 구간 → 열 게이트·개인 체크·존 처치·자동 획득 목록.
/// 원본: Docs/tech/tutorial-director.md §10.5.
/// </summary>
public sealed class MapSceneSkipPlanTests
{
    static int Mask(params MapSceneGate[] gates)
    {
        int m = 0;
        foreach (MapSceneGate g in gates) m |= 1 << (int)g;
        return m;
    }

    static List<MapSceneSkipAction> Plan(MapSceneSkipTarget target, int opened = 0)
    {
        var list = new List<MapSceneSkipAction>();
        MapSceneSkipPlan.Build(target, opened, list);
        return list;
    }

    static List<MapSceneGate> Gates(List<MapSceneSkipAction> plan) => plan.ConvertAll(a => a.Gate);

    [Test]
    public void 단축키_숫자는_구간_순서_0은_처음부터()
    {
        var expected = new[]
        {
            MapSceneSkipTarget.Restart, MapSceneSkipTarget.Corridor1, MapSceneSkipTarget.Zone2, MapSceneSkipTarget.Corridor2,
            MapSceneSkipTarget.Zone3, MapSceneSkipTarget.Corridor3, MapSceneSkipTarget.Zone4, MapSceneSkipTarget.Corridor4,
            MapSceneSkipTarget.BossEnter, MapSceneSkipTarget.BossRoom,
        };
        for (int d = 0; d <= 9; d++)
        {
            Assert.IsTrue(MapSceneSkipPlan.TryFromDigit(d, out MapSceneSkipTarget t));
            Assert.AreEqual(expected[d], t, $"Alt+{d}");
            Assert.AreEqual(d, MapSceneSkipPlan.DigitOf(t));
        }
        Assert.IsFalse(MapSceneSkipPlan.TryFromDigit(10, out _));
        Assert.IsFalse(MapSceneSkipPlan.TryFromDigit(-1, out _));
        Assert.AreEqual(-1, MapSceneSkipPlan.DigitOf(MapSceneSkipTarget.Zone1), "존1 은 단축키가 없다(지금 구간 기준으로만)");
        Assert.AreEqual("처음부터", MapSceneSkipPlan.NameOf(MapSceneSkipTarget.Restart));
    }

    [Test]
    public void 구간과_단계_매핑()
    {
        Assert.AreEqual(MapSceneArea.Corridor1, MapSceneSkipPlan.AreaOf(MapSceneSkipTarget.Corridor1));
        Assert.AreEqual(MapSceneArea.Zone3, MapSceneSkipPlan.AreaOf(MapSceneSkipTarget.Zone3));
        Assert.AreEqual(MapSceneArea.BossEnter, MapSceneSkipPlan.AreaOf(MapSceneSkipTarget.BossEnter));
        Assert.AreEqual(MapSceneArea.BossEnter, MapSceneSkipPlan.AreaOf(MapSceneSkipTarget.BossRoom), "보스방 = 보스 진입에 내린 뒤 패드 이동");

        Assert.AreEqual(MapSceneStep.Zone3Practice, MapSceneSkipPlan.StepOf(MapSceneSkipTarget.Zone3), "존 = 첫 진입 단계");
        Assert.AreEqual(MapSceneStep.Zone4Lore, MapSceneSkipPlan.StepOf(MapSceneSkipTarget.Zone4));
        Assert.AreEqual(MapSceneStep.BossEnter, MapSceneSkipPlan.StepOf(MapSceneSkipTarget.BossRoom), "Finished 는 도착이 건다");

        Assert.AreEqual(MapSceneSkipTarget.Zone1, MapSceneSkipPlan.CurrentOf(MapSceneStep.None));
        Assert.AreEqual(MapSceneSkipTarget.Zone3, MapSceneSkipPlan.CurrentOf(MapSceneStep.Zone3Fight));
        Assert.AreEqual(MapSceneSkipTarget.Zone4, MapSceneSkipPlan.CurrentOf(MapSceneStep.Zone4Combat));
        Assert.AreEqual(MapSceneSkipTarget.BossRoom, MapSceneSkipPlan.CurrentOf(MapSceneStep.Finished));
    }

    [Test]
    public void 뒤로_가기는_거부_같은_구간은_허용()
    {
        Assert.AreEqual(MapSceneSkipVerdict.Ok, MapSceneSkipPlan.Check(MapSceneStep.Zone1, MapSceneSkipTarget.Zone1));
        Assert.AreEqual(MapSceneSkipVerdict.Backward, MapSceneSkipPlan.Check(MapSceneStep.Corridor1, MapSceneSkipTarget.Zone1));
        Assert.AreEqual(MapSceneSkipVerdict.Backward, MapSceneSkipPlan.Check(MapSceneStep.Zone3Fight, MapSceneSkipTarget.Corridor2));
        Assert.AreEqual(MapSceneSkipVerdict.Ok, MapSceneSkipPlan.Check(MapSceneStep.Zone3Fight, MapSceneSkipTarget.Zone3));
        Assert.AreEqual(MapSceneSkipVerdict.Ok, MapSceneSkipPlan.Check(MapSceneStep.Zone2, MapSceneSkipTarget.BossRoom));
        Assert.AreEqual(MapSceneSkipVerdict.Finished, MapSceneSkipPlan.Check(MapSceneStep.Finished, MapSceneSkipTarget.BossRoom));
    }

    [Test]
    public void 처음부터는_뒤로_가기_예외_어느_구간에서든_허용()
    {
        MapSceneStep[] steps =
        {
            MapSceneStep.None, MapSceneStep.Zone1, MapSceneStep.Corridor1, MapSceneStep.Zone3Fight,
            MapSceneStep.Zone4Combat, MapSceneStep.BossEnter, MapSceneStep.Finished,
        };
        foreach (MapSceneStep step in steps)
            Assert.AreEqual(MapSceneSkipVerdict.Ok, MapSceneSkipPlan.Check(step, MapSceneSkipTarget.Restart), $"단계 {step}");
    }

    [Test]
    public void 재시작_진행_중엔_재시작도_스킵도_거부()
    {
        Assert.AreEqual(MapSceneSkipVerdict.RestartInProgress, MapSceneSkipPlan.Check(MapSceneStep.Zone2, MapSceneSkipTarget.Restart, restartInProgress: true));
        Assert.AreEqual(MapSceneSkipVerdict.RestartInProgress, MapSceneSkipPlan.Check(MapSceneStep.Zone2, MapSceneSkipTarget.BossRoom, restartInProgress: true));
        Assert.AreEqual(MapSceneSkipVerdict.RestartInProgress, MapSceneSkipPlan.Check(MapSceneStep.Finished, MapSceneSkipTarget.Restart, restartInProgress: true),
                        "재진입 거부가 Finished 판정보다 먼저");
    }

    [Test]
    public void 처음부터는_계획이_없다_재로드가_대신한다()
    {
        Assert.AreEqual(0, Plan(MapSceneSkipTarget.Restart).Count);
        Assert.AreEqual(0, Plan(MapSceneSkipTarget.Restart, Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry)).Count);
        Assert.IsFalse(MapSceneSkipPlan.TryGetRequiredGate(MapSceneSkipTarget.Restart, out _));
        Assert.AreEqual(MapSceneArea.Zone1, MapSceneSkipPlan.AreaOf(MapSceneSkipTarget.Restart));
        Assert.AreEqual(MapSceneStep.Zone1, MapSceneSkipPlan.StepOf(MapSceneSkipTarget.Restart));
    }

    [Test]
    public void 존1_목적은_할_일이_없다()
    {
        Assert.AreEqual(0, Plan(MapSceneSkipTarget.Zone1).Count);
        Assert.IsFalse(MapSceneSkipPlan.TryGetRequiredGate(MapSceneSkipTarget.Zone1, out _));
    }

    [Test]
    public void 복도1_목적은_G1_개인_체크만_복도1_특성은_건드리지_않는다()
    {
        List<MapSceneSkipAction> plan = Plan(MapSceneSkipTarget.Corridor1);
        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual(MapSceneSkipActionKind.CompleteZone1Checks, plan[0].Kind);
        Assert.AreEqual(MapSceneGate.G1_Corridor1Entry, plan[0].Gate);
    }

    [Test]
    public void 처음부터_존3_목적_계획()
    {
        List<MapSceneSkipAction> plan = Plan(MapSceneSkipTarget.Zone3);
        CollectionAssert.AreEqual(new[]
        {
            MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry, MapSceneGate.G4_Zone3Entry,
        }, Gates(plan));
        Assert.AreEqual(MapSceneSkipActionKind.AutoChooseTraits, plan[1].Kind);
        Assert.AreEqual(1, plan[1].Corridor);
        Assert.AreEqual(MapSceneSkipActionKind.ClearZone, plan[2].Kind);
        Assert.AreEqual(MapSceneArea.Zone2, plan[2].Zone);
        Assert.AreEqual(2, plan[3].Corridor);
    }

    [Test]
    public void 복도3_목적은_스피너봇_연습_완료와_존3_처치를_포함()
    {
        List<MapSceneSkipAction> plan = Plan(MapSceneSkipTarget.Corridor3, Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry,
                                                                                   MapSceneGate.G3_Corridor2Entry, MapSceneGate.G4_Zone3Entry));
        CollectionAssert.AreEqual(new[] { MapSceneGate.G5_SpinnerRealMode, MapSceneGate.G6_Corridor3Entry }, Gates(plan));
        Assert.AreEqual(MapSceneSkipActionKind.CompleteSpinnerPractice, plan[0].Kind);
        Assert.AreEqual(MapSceneSkipActionKind.ClearZone, plan[1].Kind);
        Assert.AreEqual(MapSceneArea.Zone3, plan[1].Zone);
    }

    [Test]
    public void 복도4_목적은_타이머_시작_뒤_존4_처치()
    {
        List<MapSceneSkipAction> plan = Plan(MapSceneSkipTarget.Corridor4);
        Assert.AreEqual(9, plan.Count, "G1~G9");
        Assert.AreEqual(MapSceneSkipActionKind.StartBossTimer, plan[7].Kind);
        Assert.AreEqual(MapSceneGate.G8_TimerStart, plan[7].Gate);
        Assert.AreEqual(MapSceneSkipActionKind.ClearZone, plan[8].Kind);
        Assert.AreEqual(MapSceneArea.Zone4, plan[8].Zone);
    }

    [Test]
    public void 보스_진입과_보스방은_G10_까지_같은_계획_G11_은_없다()
    {
        List<MapSceneGate> enter = Gates(Plan(MapSceneSkipTarget.BossEnter));
        List<MapSceneGate> room = Gates(Plan(MapSceneSkipTarget.BossRoom));
        CollectionAssert.AreEqual(enter, room);
        Assert.AreEqual(10, room.Count);
        Assert.AreEqual(MapSceneGate.G10_BossEnterAndPad, room[9]);
        CollectionAssert.DoesNotContain(room, MapSceneGate.G11_BossRoom, "G11 은 도착이 연다");
    }

    [Test]
    public void 이미_열린_게이트는_건너뛴다()
    {
        int opened = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry);
        List<MapSceneSkipAction> plan = Plan(MapSceneSkipTarget.Zone4, opened);
        CollectionAssert.AreEqual(new[]
        {
            MapSceneGate.G4_Zone3Entry, MapSceneGate.G5_SpinnerRealMode, MapSceneGate.G6_Corridor3Entry, MapSceneGate.G7_Zone4Entry,
        }, Gates(plan));

        Assert.AreEqual(0, Plan(MapSceneSkipTarget.Zone2, Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry)).Count, "같은 구간 = 이동만");
    }

    [Test]
    public void 자동_획득_복도_목록()
    {
        var corridors = new List<int>();
        MapSceneSkipPlan.AutoChosenCorridors(Plan(MapSceneSkipTarget.BossEnter), corridors);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, corridors);

        MapSceneSkipPlan.AutoChosenCorridors(Plan(MapSceneSkipTarget.Corridor4), corridors);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, corridors, "목적 복도(4) 특성은 도착 뒤 정상 라운드");

        MapSceneSkipPlan.AutoChosenCorridors(Plan(MapSceneSkipTarget.Corridor2), corridors);
        CollectionAssert.AreEqual(new[] { 1 }, corridors);
    }

    [Test]
    public void 게이트_로그_이름()
    {
        Assert.AreEqual("없음", MapSceneSkipPlan.DescribeGates(0));
        Assert.AreEqual("G1·G2·G10", MapSceneSkipPlan.DescribeGates(Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry,
                                                                            MapSceneGate.G10_BossEnterAndPad)));
    }
}
