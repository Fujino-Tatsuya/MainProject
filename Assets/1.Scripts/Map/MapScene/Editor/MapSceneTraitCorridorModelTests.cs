using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 특성 복도 순수 규칙(S8) — 게이트·체크·창 표시 매핑 · 복도당 라운드 1번 · 라운드→복도 역매핑 · 진행 중 복도 · 목표 장면.
/// 원본: Docs/design/tutorial-scenario.md §3.2·§3.4·§3.6·§3.8·§4.
/// </summary>
public sealed class MapSceneTraitCorridorModelTests
{
    static MapScenePlayerProgress Progress(params MapSceneCheck[] checks)
    {
        var p = new MapScenePlayerProgress { ClientId = 0, Connected = true };
        foreach (MapSceneCheck c in checks) p.CheckMask |= 1 << (int)c;
        return p;
    }

    // ─── 매핑 ──────────────────────────────────────────────────────────

    [Test]
    public void 복도_게이트_체크_단계_매핑()
    {
        Assert.AreEqual(MapSceneGate.G1_Corridor1Entry, MapSceneTraitCorridorRules.EntryGate(1));
        Assert.AreEqual(MapSceneGate.G3_Corridor2Entry, MapSceneTraitCorridorRules.EntryGate(2));
        Assert.AreEqual(MapSceneGate.G6_Corridor3Entry, MapSceneTraitCorridorRules.EntryGate(3));
        Assert.AreEqual(MapSceneGate.G9_Corridor4Entry, MapSceneTraitCorridorRules.EntryGate(4));

        Assert.AreEqual(MapSceneGate.G2_Zone2Entry, MapSceneTraitCorridorRules.ExitGate(1));
        Assert.AreEqual(MapSceneGate.G4_Zone3Entry, MapSceneTraitCorridorRules.ExitGate(2));
        Assert.AreEqual(MapSceneGate.G7_Zone4Entry, MapSceneTraitCorridorRules.ExitGate(3));
        Assert.AreEqual(MapSceneGate.G10_BossEnterAndPad, MapSceneTraitCorridorRules.ExitGate(4));

        for (int c = 1; c <= 4; c++)
        {
            // 출구 게이트가 요구하는 개인 체크 = 이 복도 체크(디렉터 모델과 같은 정의).
            Assert.AreEqual(1 << (int)MapSceneTraitCorridorRules.CheckOf(c),
                MapSceneFlowModel.RequiredMaskOf(MapSceneTraitCorridorRules.ExitGate(c)), $"복도{c}");
            Assert.AreEqual(c, MapSceneTraitCorridorRules.CorridorOfExitGate(MapSceneTraitCorridorRules.ExitGate(c)));
            Assert.AreEqual(c, MapSceneTraitCorridorRules.CorridorOfEntryGate(MapSceneTraitCorridorRules.EntryGate(c)));
        }

        Assert.AreEqual(MapSceneStep.Corridor3, MapSceneTraitCorridorRules.StepOf(3));
        Assert.AreEqual(0, MapSceneTraitCorridorRules.CorridorOfExitGate(MapSceneGate.G5_SpinnerRealMode));
        Assert.AreEqual(0, MapSceneTraitCorridorRules.CorridorOfEntryGate(MapSceneGate.G8_TimerStart));
    }

    [Test]
    public void 창_표시는_복도1_설명_복도4_한줄_나머지_없음()
    {
        Assert.AreEqual(TraitRoundDisplay.Intro, MapSceneTraitCorridorRules.DisplayOf(1));
        Assert.AreEqual(TraitRoundDisplay.None, MapSceneTraitCorridorRules.DisplayOf(2));
        Assert.AreEqual(TraitRoundDisplay.None, MapSceneTraitCorridorRules.DisplayOf(3));
        Assert.AreEqual(TraitRoundDisplay.LastNotice, MapSceneTraitCorridorRules.DisplayOf(4));
    }

    // ─── 라운드 1회 ─────────────────────────────────────────────────────

    [Test]
    public void 라운드는_복도당_한번만_열고_늦은_도착자는_참가()
    {
        var r = new MapSceneTraitCorridorRounds();
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.Start, r.Decide(1, false, false));
        r.MarkStarted(1, 1);
        // 두 번째 도착자·왕복 — 새로 열지 않고 같은 라운드에 참가(이미 참가자면 특성 디렉터가 무시).
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.Join, r.Decide(1, false, false));
        Assert.IsTrue(r.IsStarted(1));
        // 출구가 열린 뒤·만료 뒤 도착은 창 없음(§3.2).
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.AlreadyHandled, r.Decide(1, true, false));
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.AlreadyHandled, r.Decide(1, false, true));
        // 다른 복도는 따로.
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.Start, r.Decide(2, false, false));
    }

    [Test]
    public void 건너뛴_복도는_늦은_도착자도_참가하지_않는다()
    {
        var r = new MapSceneTraitCorridorRounds();
        r.MarkHandled(2, 0);   // 출구가 이미 열림·만료로 건너뜀
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.AlreadyHandled, r.Decide(2, false, false));
        Assert.IsFalse(r.IsStarted(2));

        // 특성 디렉터 없이 연 복도(라운드 0)도 참가 판정은 Join — 흐름이 도착자마다 바로 체크한다.
        r.MarkStarted(3, 0);
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.Join, r.Decide(3, false, false));
        Assert.AreEqual(0, r.RoundOf(3));
    }

    [Test]
    public void 출구가_열린_뒤_낙오자나_타이머_만료면_열지_않는다()
    {
        var r = new MapSceneTraitCorridorRounds();
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.GateAlreadyOpen, r.Decide(2, true, false));
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.TimerExpired, r.Decide(4, false, true));
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.Invalid, r.Decide(0, false, false));
        Assert.AreEqual(MapSceneTraitCorridorRoundDecision.Invalid, r.Decide(5, false, false));
    }

    [Test]
    public void 라운드_번호로_복도를_찾는다_건너뛴_복도는_라운드_없음()
    {
        var r = new MapSceneTraitCorridorRounds();
        r.MarkStarted(1, 1);
        r.MarkHandled(2, 0);   // 건너뜀
        r.MarkStarted(3, 2);
        Assert.AreEqual(1, r.CorridorOfRound(1));
        Assert.AreEqual(3, r.CorridorOfRound(2));
        Assert.AreEqual(0, r.CorridorOfRound(0));
        Assert.AreEqual(0, r.CorridorOfRound(9));
        Assert.AreEqual(0, r.RoundOf(2));
        Assert.IsTrue(r.IsHandled(2));
        Assert.IsFalse(r.IsHandled(4));
    }

    // ─── 진행 중 복도 ───────────────────────────────────────────────────

    [Test]
    public void 진행_중_복도는_입구_열림_출구_닫힘()
    {
        var open = new HashSet<MapSceneGate>();
        System.Func<MapSceneGate, bool> isOpen = g => open.Contains(g);
        Assert.AreEqual(0, MapSceneTraitCorridorRules.ActiveCorridor(isOpen));

        open.Add(MapSceneGate.G1_Corridor1Entry);
        Assert.AreEqual(1, MapSceneTraitCorridorRules.ActiveCorridor(isOpen));
        open.Add(MapSceneGate.G2_Zone2Entry);
        Assert.AreEqual(0, MapSceneTraitCorridorRules.ActiveCorridor(isOpen), "존2 진행 중");
        open.Add(MapSceneGate.G3_Corridor2Entry);
        Assert.AreEqual(2, MapSceneTraitCorridorRules.ActiveCorridor(isOpen));

        foreach (MapSceneGate g in new[] { MapSceneGate.G4_Zone3Entry, MapSceneGate.G5_SpinnerRealMode, MapSceneGate.G6_Corridor3Entry,
                     MapSceneGate.G7_Zone4Entry, MapSceneGate.G8_TimerStart, MapSceneGate.G9_Corridor4Entry })
            open.Add(g);
        Assert.AreEqual(4, MapSceneTraitCorridorRules.ActiveCorridor(isOpen));
        open.Add(MapSceneGate.G10_BossEnterAndPad);
        Assert.AreEqual(0, MapSceneTraitCorridorRules.ActiveCorridor(isOpen));
        Assert.AreEqual(0, MapSceneTraitCorridorRules.ActiveCorridor(null));
    }

    // ─── 목표 장면 ──────────────────────────────────────────────────────

    [Test]
    public void 목표_창이_떠_있으면_고르기()
    {
        MapSceneTraitCorridorView v = MapSceneTraitCorridorProgress.Resolve(1, true, Progress(), false, MapSceneGate.G1_Corridor1Entry);
        Assert.AreEqual(MapSceneTraitCorridorTask.Pick, v.Task);
        Assert.IsTrue(v.OwnsObjective);
        Assert.IsFalse(v.ShowWait);
    }

    [Test]
    public void 목표_골랐으면_대기_대기줄은_이_복도_출구_차례일_때만()
    {
        MapScenePlayerProgress p = Progress(MapSceneCheck.TraitCorridor2);
        MapSceneTraitCorridorView v = MapSceneTraitCorridorProgress.Resolve(2, false, p, true, MapSceneGate.G4_Zone3Entry);
        Assert.AreEqual(MapSceneTraitCorridorTask.Wait, v.Task);
        Assert.IsTrue(v.ShowWait);

        v = MapSceneTraitCorridorProgress.Resolve(2, false, p, true, MapSceneGate.G2_Zone2Entry);
        Assert.IsFalse(v.ShowWait);
        v = MapSceneTraitCorridorProgress.Resolve(2, false, p, false, MapSceneGate.G4_Zone3Entry);
        Assert.IsFalse(v.ShowWait);
    }

    [Test]
    public void 목표_라운드_전이나_복도_밖이면_건드리지_않는다()
    {
        Assert.IsFalse(MapSceneTraitCorridorProgress.Resolve(3, false, Progress(), true, MapSceneGate.G7_Zone4Entry).OwnsObjective);
        Assert.IsFalse(MapSceneTraitCorridorProgress.Resolve(0, true, Progress(MapSceneCheck.TraitCorridor1), true, MapSceneGate.G2_Zone2Entry).OwnsObjective);
        // 앞 복도 체크는 이 복도와 무관.
        Assert.IsFalse(MapSceneTraitCorridorProgress.Resolve(2, false, Progress(MapSceneCheck.TraitCorridor1), false, MapSceneGate.G4_Zone3Entry).OwnsObjective);
    }

    // ─── 출구 목표와 존 첫 진입 목표 순서 (S12) ──────────────────────────────

    [Test]
    public void 출구_목표는_다음_존_단계가_보이기_전에만_쓴다()
    {
        Assert.IsTrue(MapSceneTraitCorridorRules.ShouldShowExitObjective(1, MapSceneStep.Corridor1));
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowExitObjective(1, MapSceneStep.Zone2), "같은 틱에 존2 단계가 먼저 반영 — 존2 목표를 덮지 않는다");
        Assert.IsTrue(MapSceneTraitCorridorRules.ShouldShowExitObjective(2, MapSceneStep.Corridor2));
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowExitObjective(2, MapSceneStep.Zone3Practice));
        Assert.IsTrue(MapSceneTraitCorridorRules.ShouldShowExitObjective(3, MapSceneStep.Corridor3));
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowExitObjective(3, MapSceneStep.Zone4Lore));
        // 복도4 는 G10 다음 프레임에 BossEnter 가 걸린다 — 발판 목표는 보스방 도착 전까지 쓴다.
        Assert.IsTrue(MapSceneTraitCorridorRules.ShouldShowExitObjective(4, MapSceneStep.BossEnter));
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowExitObjective(4, MapSceneStep.Finished));
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowExitObjective(0, MapSceneStep.Zone1));
    }

    [Test]
    public void 복도_목표_id_판별()
    {
        Assert.IsTrue(MapSceneTraitCorridorRules.IsCorridorObjective(MapSceneTraitCorridorFlow.PickId));
        Assert.IsTrue(MapSceneTraitCorridorRules.IsCorridorObjective(MapSceneTraitCorridorFlow.WaitId));
        Assert.IsTrue(MapSceneTraitCorridorRules.IsCorridorObjective(MapSceneTraitCorridorFlow.NextId));
        Assert.IsTrue(MapSceneTraitCorridorRules.IsCorridorObjective(MapSceneTraitCorridorFlow.GoPadId));
        Assert.IsFalse(MapSceneTraitCorridorRules.IsCorridorObjective(TutorialGuideIds.Z4GoBoss));
        Assert.IsFalse(MapSceneTraitCorridorRules.IsCorridorObjective(null));
    }

    [Test]
    public void 특성_게이지는_진행_중_복도_출구를_첫_완료자_뒤_기다릴_때만_보인다()
    {
        for (int c = 1; c <= MapSceneTraitCorridorRules.CorridorCount; c++)
            Assert.IsTrue(MapSceneTraitCorridorRules.ShouldShowTraitDeadline(c, true, MapSceneTraitCorridorRules.ExitGate(c), 42f), $"복도{c}");

        Assert.IsTrue(MapSceneTraitCorridorRules.ShouldShowTraitDeadline(2, true, MapSceneGate.G4_Zone3Entry, 0f), "0초(만료 직전)도 보인다");
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowTraitDeadline(2, true, MapSceneGate.G4_Zone3Entry, -1f), "첫 완료자 전");
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowTraitDeadline(2, false, MapSceneGate.G4_Zone3Entry, 30f), "대기 없음(솔로 즉시 완료 등)");
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowTraitDeadline(2, true, MapSceneGate.G2_Zone2Entry, 30f), "다른 복도 출구");
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowTraitDeadline(1, true, MapSceneGate.G1_Corridor1Entry, 30f), "복도 출구가 아닌 개인 게이트(G1)");
        Assert.IsFalse(MapSceneTraitCorridorRules.ShouldShowTraitDeadline(0, true, MapSceneGate.G2_Zone2Entry, 30f), "복도 밖");
    }
}
