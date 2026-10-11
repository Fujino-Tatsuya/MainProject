using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 특성 복도 순수 규칙(S8) — 게이트·체크·창 표시 매핑 · 복도당 라운드 1번 · 라운드→복도 역매핑 · 진행 중 복도 · 목표 장면.
/// 원본: Docs/design/tutorial-scenario.md §3.2·§3.4·§3.6·§3.8·§4.
/// </summary>
public sealed class TutorialCorridorModelTests
{
    static TutorialPlayerProgress Progress(params TutorialCheck[] checks)
    {
        var p = new TutorialPlayerProgress { ClientId = 0, Connected = true };
        foreach (TutorialCheck c in checks) p.CheckMask |= 1 << (int)c;
        return p;
    }

    // ─── 매핑 ──────────────────────────────────────────────────────────

    [Test]
    public void 복도_게이트_체크_단계_매핑()
    {
        Assert.AreEqual(TutorialGate.G1_Corridor1Entry, TutorialCorridorRules.EntryGate(1));
        Assert.AreEqual(TutorialGate.G3_Corridor2Entry, TutorialCorridorRules.EntryGate(2));
        Assert.AreEqual(TutorialGate.G6_Corridor3Entry, TutorialCorridorRules.EntryGate(3));
        Assert.AreEqual(TutorialGate.G9_Corridor4Entry, TutorialCorridorRules.EntryGate(4));

        Assert.AreEqual(TutorialGate.G2_Zone2Entry, TutorialCorridorRules.ExitGate(1));
        Assert.AreEqual(TutorialGate.G4_Zone3Entry, TutorialCorridorRules.ExitGate(2));
        Assert.AreEqual(TutorialGate.G7_Zone4Entry, TutorialCorridorRules.ExitGate(3));
        Assert.AreEqual(TutorialGate.G10_BossEnterAndPad, TutorialCorridorRules.ExitGate(4));

        for (int c = 1; c <= 4; c++)
        {
            // 출구 게이트가 요구하는 개인 체크 = 이 복도 체크(디렉터 모델과 같은 정의).
            Assert.AreEqual(1 << (int)TutorialCorridorRules.CheckOf(c),
                TutorialFlowModel.RequiredMaskOf(TutorialCorridorRules.ExitGate(c)), $"복도{c}");
            Assert.AreEqual(c, TutorialCorridorRules.CorridorOfExitGate(TutorialCorridorRules.ExitGate(c)));
            Assert.AreEqual(c, TutorialCorridorRules.CorridorOfEntryGate(TutorialCorridorRules.EntryGate(c)));
        }

        Assert.AreEqual(TutorialStep.Corridor3, TutorialCorridorRules.StepOf(3));
        Assert.AreEqual(0, TutorialCorridorRules.CorridorOfExitGate(TutorialGate.G5_SpinnerRealMode));
        Assert.AreEqual(0, TutorialCorridorRules.CorridorOfEntryGate(TutorialGate.G8_TimerStart));
    }

    [Test]
    public void 창_표시는_복도1_설명_복도4_한줄_나머지_없음()
    {
        Assert.AreEqual(TutorialTraitRoundDisplay.Intro, TutorialCorridorRules.DisplayOf(1));
        Assert.AreEqual(TutorialTraitRoundDisplay.None, TutorialCorridorRules.DisplayOf(2));
        Assert.AreEqual(TutorialTraitRoundDisplay.None, TutorialCorridorRules.DisplayOf(3));
        Assert.AreEqual(TutorialTraitRoundDisplay.LastNotice, TutorialCorridorRules.DisplayOf(4));
    }

    // ─── 라운드 1회 ─────────────────────────────────────────────────────

    [Test]
    public void 라운드는_복도당_한번만_열고_늦은_도착자는_참가()
    {
        var r = new TutorialCorridorRounds();
        Assert.AreEqual(TutorialCorridorRoundDecision.Start, r.Decide(1, false, false));
        r.MarkStarted(1, 1);
        // 두 번째 도착자·왕복 — 새로 열지 않고 같은 라운드에 참가(이미 참가자면 특성 디렉터가 무시).
        Assert.AreEqual(TutorialCorridorRoundDecision.Join, r.Decide(1, false, false));
        Assert.IsTrue(r.IsStarted(1));
        // 출구가 열린 뒤·만료 뒤 도착은 창 없음(§3.2).
        Assert.AreEqual(TutorialCorridorRoundDecision.AlreadyHandled, r.Decide(1, true, false));
        Assert.AreEqual(TutorialCorridorRoundDecision.AlreadyHandled, r.Decide(1, false, true));
        // 다른 복도는 따로.
        Assert.AreEqual(TutorialCorridorRoundDecision.Start, r.Decide(2, false, false));
    }

    [Test]
    public void 건너뛴_복도는_늦은_도착자도_참가하지_않는다()
    {
        var r = new TutorialCorridorRounds();
        r.MarkHandled(2, 0);   // 출구가 이미 열림·만료로 건너뜀
        Assert.AreEqual(TutorialCorridorRoundDecision.AlreadyHandled, r.Decide(2, false, false));
        Assert.IsFalse(r.IsStarted(2));

        // 특성 디렉터 없이 연 복도(라운드 0)도 참가 판정은 Join — 흐름이 도착자마다 바로 체크한다.
        r.MarkStarted(3, 0);
        Assert.AreEqual(TutorialCorridorRoundDecision.Join, r.Decide(3, false, false));
        Assert.AreEqual(0, r.RoundOf(3));
    }

    [Test]
    public void 출구가_열린_뒤_낙오자나_타이머_만료면_열지_않는다()
    {
        var r = new TutorialCorridorRounds();
        Assert.AreEqual(TutorialCorridorRoundDecision.GateAlreadyOpen, r.Decide(2, true, false));
        Assert.AreEqual(TutorialCorridorRoundDecision.TimerExpired, r.Decide(4, false, true));
        Assert.AreEqual(TutorialCorridorRoundDecision.Invalid, r.Decide(0, false, false));
        Assert.AreEqual(TutorialCorridorRoundDecision.Invalid, r.Decide(5, false, false));
    }

    [Test]
    public void 라운드_번호로_복도를_찾는다_건너뛴_복도는_라운드_없음()
    {
        var r = new TutorialCorridorRounds();
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
        var open = new HashSet<TutorialGate>();
        System.Func<TutorialGate, bool> isOpen = g => open.Contains(g);
        Assert.AreEqual(0, TutorialCorridorRules.ActiveCorridor(isOpen));

        open.Add(TutorialGate.G1_Corridor1Entry);
        Assert.AreEqual(1, TutorialCorridorRules.ActiveCorridor(isOpen));
        open.Add(TutorialGate.G2_Zone2Entry);
        Assert.AreEqual(0, TutorialCorridorRules.ActiveCorridor(isOpen), "존2 진행 중");
        open.Add(TutorialGate.G3_Corridor2Entry);
        Assert.AreEqual(2, TutorialCorridorRules.ActiveCorridor(isOpen));

        foreach (TutorialGate g in new[] { TutorialGate.G4_Zone3Entry, TutorialGate.G5_SpinnerRealMode, TutorialGate.G6_Corridor3Entry,
                     TutorialGate.G7_Zone4Entry, TutorialGate.G8_TimerStart, TutorialGate.G9_Corridor4Entry })
            open.Add(g);
        Assert.AreEqual(4, TutorialCorridorRules.ActiveCorridor(isOpen));
        open.Add(TutorialGate.G10_BossEnterAndPad);
        Assert.AreEqual(0, TutorialCorridorRules.ActiveCorridor(isOpen));
        Assert.AreEqual(0, TutorialCorridorRules.ActiveCorridor(null));
    }

    // ─── 목표 장면 ──────────────────────────────────────────────────────

    [Test]
    public void 목표_창이_떠_있으면_고르기()
    {
        TutorialCorridorView v = TutorialCorridorProgress.Resolve(1, true, Progress(), false, TutorialGate.G1_Corridor1Entry);
        Assert.AreEqual(TutorialCorridorTask.Pick, v.Task);
        Assert.IsTrue(v.OwnsObjective);
        Assert.IsFalse(v.ShowWait);
    }

    [Test]
    public void 목표_골랐으면_대기_대기줄은_이_복도_출구_차례일_때만()
    {
        TutorialPlayerProgress p = Progress(TutorialCheck.TraitCorridor2);
        TutorialCorridorView v = TutorialCorridorProgress.Resolve(2, false, p, true, TutorialGate.G4_Zone3Entry);
        Assert.AreEqual(TutorialCorridorTask.Wait, v.Task);
        Assert.IsTrue(v.ShowWait);

        v = TutorialCorridorProgress.Resolve(2, false, p, true, TutorialGate.G2_Zone2Entry);
        Assert.IsFalse(v.ShowWait);
        v = TutorialCorridorProgress.Resolve(2, false, p, false, TutorialGate.G4_Zone3Entry);
        Assert.IsFalse(v.ShowWait);
    }

    [Test]
    public void 목표_라운드_전이나_복도_밖이면_건드리지_않는다()
    {
        Assert.IsFalse(TutorialCorridorProgress.Resolve(3, false, Progress(), true, TutorialGate.G7_Zone4Entry).OwnsObjective);
        Assert.IsFalse(TutorialCorridorProgress.Resolve(0, true, Progress(TutorialCheck.TraitCorridor1), true, TutorialGate.G2_Zone2Entry).OwnsObjective);
        // 앞 복도 체크는 이 복도와 무관.
        Assert.IsFalse(TutorialCorridorProgress.Resolve(2, false, Progress(TutorialCheck.TraitCorridor1), false, TutorialGate.G4_Zone3Entry).OwnsObjective);
    }

    // ─── 출구 목표와 존 첫 진입 목표 순서 (S12) ──────────────────────────────

    [Test]
    public void 출구_목표는_다음_존_단계가_보이기_전에만_쓴다()
    {
        Assert.IsTrue(TutorialCorridorRules.ShouldShowExitObjective(1, TutorialStep.Corridor1));
        Assert.IsFalse(TutorialCorridorRules.ShouldShowExitObjective(1, TutorialStep.Zone2), "같은 틱에 존2 단계가 먼저 반영 — 존2 목표를 덮지 않는다");
        Assert.IsTrue(TutorialCorridorRules.ShouldShowExitObjective(2, TutorialStep.Corridor2));
        Assert.IsFalse(TutorialCorridorRules.ShouldShowExitObjective(2, TutorialStep.Zone3Practice));
        Assert.IsTrue(TutorialCorridorRules.ShouldShowExitObjective(3, TutorialStep.Corridor3));
        Assert.IsFalse(TutorialCorridorRules.ShouldShowExitObjective(3, TutorialStep.Zone4Lore));
        // 복도4 는 G10 다음 프레임에 BossEnter 가 걸린다 — 발판 목표는 보스방 도착 전까지 쓴다.
        Assert.IsTrue(TutorialCorridorRules.ShouldShowExitObjective(4, TutorialStep.BossEnter));
        Assert.IsFalse(TutorialCorridorRules.ShouldShowExitObjective(4, TutorialStep.Finished));
        Assert.IsFalse(TutorialCorridorRules.ShouldShowExitObjective(0, TutorialStep.Zone1));
    }

    [Test]
    public void 복도_목표_id_판별()
    {
        Assert.IsTrue(TutorialCorridorRules.IsCorridorObjective(TutorialCorridorFlow.PickId));
        Assert.IsTrue(TutorialCorridorRules.IsCorridorObjective(TutorialCorridorFlow.WaitId));
        Assert.IsTrue(TutorialCorridorRules.IsCorridorObjective(TutorialCorridorFlow.NextId));
        Assert.IsTrue(TutorialCorridorRules.IsCorridorObjective(TutorialCorridorFlow.GoPadId));
        Assert.IsFalse(TutorialCorridorRules.IsCorridorObjective(TutorialGuideIds.Z4GoBoss));
        Assert.IsFalse(TutorialCorridorRules.IsCorridorObjective(null));
    }
}
