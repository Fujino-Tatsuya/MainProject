using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 구간 스킵 계획(할 일 34) — 단축키 매핑 · 뒤로 가기 거부 · 목적 구간 → 열 게이트·개인 체크·존 처치·자동 획득 목록.
/// 원본: Docs/tech/tutorial-director.md §10.5.
/// </summary>
public sealed class TutorialSkipPlanTests
{
    static int Mask(params TutorialGate[] gates)
    {
        int m = 0;
        foreach (TutorialGate g in gates) m |= 1 << (int)g;
        return m;
    }

    static List<TutorialSkipAction> Plan(TutorialSkipTarget target, int opened = 0)
    {
        var list = new List<TutorialSkipAction>();
        TutorialSkipPlan.Build(target, opened, list);
        return list;
    }

    static List<TutorialGate> Gates(List<TutorialSkipAction> plan) => plan.ConvertAll(a => a.Gate);

    [Test]
    public void 단축키_숫자는_구간_순서_0은_존1()
    {
        var expected = new[]
        {
            TutorialSkipTarget.Zone1, TutorialSkipTarget.Corridor1, TutorialSkipTarget.Zone2, TutorialSkipTarget.Corridor2,
            TutorialSkipTarget.Zone3, TutorialSkipTarget.Corridor3, TutorialSkipTarget.Zone4, TutorialSkipTarget.Corridor4,
            TutorialSkipTarget.BossEnter, TutorialSkipTarget.BossRoom,
        };
        for (int d = 0; d <= 9; d++)
        {
            Assert.IsTrue(TutorialSkipPlan.TryFromDigit(d, out TutorialSkipTarget t));
            Assert.AreEqual(expected[d], t, $"Alt+{d}");
            Assert.AreEqual(d, TutorialSkipPlan.DigitOf(t));
        }
        Assert.IsFalse(TutorialSkipPlan.TryFromDigit(10, out _));
        Assert.IsFalse(TutorialSkipPlan.TryFromDigit(-1, out _));
    }

    [Test]
    public void 구간과_단계_매핑()
    {
        Assert.AreEqual(TutorialArea.Corridor1, TutorialSkipPlan.AreaOf(TutorialSkipTarget.Corridor1));
        Assert.AreEqual(TutorialArea.Zone3, TutorialSkipPlan.AreaOf(TutorialSkipTarget.Zone3));
        Assert.AreEqual(TutorialArea.BossEnter, TutorialSkipPlan.AreaOf(TutorialSkipTarget.BossEnter));
        Assert.AreEqual(TutorialArea.BossEnter, TutorialSkipPlan.AreaOf(TutorialSkipTarget.BossRoom), "보스방 = 보스 진입에 내린 뒤 패드 이동");

        Assert.AreEqual(TutorialStep.Zone3Practice, TutorialSkipPlan.StepOf(TutorialSkipTarget.Zone3), "존 = 첫 진입 단계");
        Assert.AreEqual(TutorialStep.Zone4Lore, TutorialSkipPlan.StepOf(TutorialSkipTarget.Zone4));
        Assert.AreEqual(TutorialStep.BossEnter, TutorialSkipPlan.StepOf(TutorialSkipTarget.BossRoom), "Finished 는 도착이 건다");

        Assert.AreEqual(TutorialSkipTarget.Zone1, TutorialSkipPlan.CurrentOf(TutorialStep.None));
        Assert.AreEqual(TutorialSkipTarget.Zone3, TutorialSkipPlan.CurrentOf(TutorialStep.Zone3Fight));
        Assert.AreEqual(TutorialSkipTarget.Zone4, TutorialSkipPlan.CurrentOf(TutorialStep.Zone4Combat));
        Assert.AreEqual(TutorialSkipTarget.BossRoom, TutorialSkipPlan.CurrentOf(TutorialStep.Finished));
    }

    [Test]
    public void 뒤로_가기는_거부_같은_구간은_허용()
    {
        Assert.AreEqual(TutorialSkipVerdict.Ok, TutorialSkipPlan.Check(TutorialStep.Zone1, TutorialSkipTarget.Zone1), "Alt+0 은 존1 에서만 의미");
        Assert.AreEqual(TutorialSkipVerdict.Backward, TutorialSkipPlan.Check(TutorialStep.Corridor1, TutorialSkipTarget.Zone1));
        Assert.AreEqual(TutorialSkipVerdict.Backward, TutorialSkipPlan.Check(TutorialStep.Zone3Fight, TutorialSkipTarget.Corridor2));
        Assert.AreEqual(TutorialSkipVerdict.Ok, TutorialSkipPlan.Check(TutorialStep.Zone3Fight, TutorialSkipTarget.Zone3));
        Assert.AreEqual(TutorialSkipVerdict.Ok, TutorialSkipPlan.Check(TutorialStep.Zone2, TutorialSkipTarget.BossRoom));
        Assert.AreEqual(TutorialSkipVerdict.Finished, TutorialSkipPlan.Check(TutorialStep.Finished, TutorialSkipTarget.BossRoom));
    }

    [Test]
    public void 존1_목적은_할_일이_없다()
    {
        Assert.AreEqual(0, Plan(TutorialSkipTarget.Zone1).Count);
        Assert.IsFalse(TutorialSkipPlan.TryGetRequiredGate(TutorialSkipTarget.Zone1, out _));
    }

    [Test]
    public void 복도1_목적은_G1_개인_체크만_복도1_특성은_건드리지_않는다()
    {
        List<TutorialSkipAction> plan = Plan(TutorialSkipTarget.Corridor1);
        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual(TutorialSkipActionKind.CompleteZone1Checks, plan[0].Kind);
        Assert.AreEqual(TutorialGate.G1_Corridor1Entry, plan[0].Gate);
    }

    [Test]
    public void 처음부터_존3_목적_계획()
    {
        List<TutorialSkipAction> plan = Plan(TutorialSkipTarget.Zone3);
        CollectionAssert.AreEqual(new[]
        {
            TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry, TutorialGate.G3_Corridor2Entry, TutorialGate.G4_Zone3Entry,
        }, Gates(plan));
        Assert.AreEqual(TutorialSkipActionKind.AutoChooseTraits, plan[1].Kind);
        Assert.AreEqual(1, plan[1].Corridor);
        Assert.AreEqual(TutorialSkipActionKind.ClearZone, plan[2].Kind);
        Assert.AreEqual(TutorialArea.Zone2, plan[2].Zone);
        Assert.AreEqual(2, plan[3].Corridor);
    }

    [Test]
    public void 복도3_목적은_스피너봇_연습_완료와_존3_처치를_포함()
    {
        List<TutorialSkipAction> plan = Plan(TutorialSkipTarget.Corridor3, Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry,
                                                                                   TutorialGate.G3_Corridor2Entry, TutorialGate.G4_Zone3Entry));
        CollectionAssert.AreEqual(new[] { TutorialGate.G5_SpinnerRealMode, TutorialGate.G6_Corridor3Entry }, Gates(plan));
        Assert.AreEqual(TutorialSkipActionKind.CompleteSpinnerPractice, plan[0].Kind);
        Assert.AreEqual(TutorialSkipActionKind.ClearZone, plan[1].Kind);
        Assert.AreEqual(TutorialArea.Zone3, plan[1].Zone);
    }

    [Test]
    public void 복도4_목적은_타이머_시작_뒤_존4_처치()
    {
        List<TutorialSkipAction> plan = Plan(TutorialSkipTarget.Corridor4);
        Assert.AreEqual(9, plan.Count, "G1~G9");
        Assert.AreEqual(TutorialSkipActionKind.StartBossTimer, plan[7].Kind);
        Assert.AreEqual(TutorialGate.G8_TimerStart, plan[7].Gate);
        Assert.AreEqual(TutorialSkipActionKind.ClearZone, plan[8].Kind);
        Assert.AreEqual(TutorialArea.Zone4, plan[8].Zone);
    }

    [Test]
    public void 보스_진입과_보스방은_G10_까지_같은_계획_G11_은_없다()
    {
        List<TutorialGate> enter = Gates(Plan(TutorialSkipTarget.BossEnter));
        List<TutorialGate> room = Gates(Plan(TutorialSkipTarget.BossRoom));
        CollectionAssert.AreEqual(enter, room);
        Assert.AreEqual(10, room.Count);
        Assert.AreEqual(TutorialGate.G10_BossEnterAndPad, room[9]);
        CollectionAssert.DoesNotContain(room, TutorialGate.G11_BossRoom, "G11 은 도착이 연다");
    }

    [Test]
    public void 이미_열린_게이트는_건너뛴다()
    {
        int opened = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry, TutorialGate.G3_Corridor2Entry);
        List<TutorialSkipAction> plan = Plan(TutorialSkipTarget.Zone4, opened);
        CollectionAssert.AreEqual(new[]
        {
            TutorialGate.G4_Zone3Entry, TutorialGate.G5_SpinnerRealMode, TutorialGate.G6_Corridor3Entry, TutorialGate.G7_Zone4Entry,
        }, Gates(plan));

        Assert.AreEqual(0, Plan(TutorialSkipTarget.Zone2, Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry)).Count, "같은 구간 = 이동만");
    }

    [Test]
    public void 자동_획득_복도_목록()
    {
        var corridors = new List<int>();
        TutorialSkipPlan.AutoChosenCorridors(Plan(TutorialSkipTarget.BossEnter), corridors);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, corridors);

        TutorialSkipPlan.AutoChosenCorridors(Plan(TutorialSkipTarget.Corridor4), corridors);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, corridors, "목적 복도(4) 특성은 도착 뒤 정상 라운드");

        TutorialSkipPlan.AutoChosenCorridors(Plan(TutorialSkipTarget.Corridor2), corridors);
        CollectionAssert.AreEqual(new[] { 1 }, corridors);
    }

    [Test]
    public void 게이트_로그_이름()
    {
        Assert.AreEqual("없음", TutorialSkipPlan.DescribeGates(0));
        Assert.AreEqual("G1·G2·G10", TutorialSkipPlan.DescribeGates(Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry,
                                                                            TutorialGate.G10_BossEnterAndPad)));
    }
}
