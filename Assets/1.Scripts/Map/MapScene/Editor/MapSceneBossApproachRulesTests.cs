using NUnit.Framework;

// 튜토리얼 존4 흐름 + 보스 진입 안내 순수 규칙(S10) — MapSceneBossApproachModel · MapSceneBossApproachDisplay.
public class MapSceneBossApproachRulesTests
{
    static MapSceneBossApproachModel InLore(double now = 0d)
    {
        var m = new MapSceneBossApproachModel();
        m.NotifyEntryGateOpen(now);
        m.NotifyPartyEntered(now);
        return m;
    }

    static MapSceneBossApproachModel InCombat(double now = 0d)
    {
        var m = InLore(now);
        m.NotifyTimerStarted(now, out _);
        return m;
    }

    // ─── Z4-0 설정 자막 ───

    [Test]
    public void Lore_NeedsBothGateAndEntry_InEitherOrder()
    {
        var a = new MapSceneBossApproachModel();
        Assert.IsFalse(a.NotifyEntryGateOpen(0d));
        Assert.IsTrue(a.NotifyPartyEntered(0d));
        Assert.AreEqual(MapSceneBossApproachPhase.Lore, a.Phase);

        var b = new MapSceneBossApproachModel();
        Assert.IsFalse(b.NotifyPartyEntered(0d));
        Assert.AreEqual(MapSceneBossApproachPhase.Waiting, b.Phase);
        Assert.IsTrue(b.NotifyEntryGateOpen(0d));
        Assert.AreEqual(MapSceneBossApproachPhase.Lore, b.Phase);
    }

    [Test]
    public void Lore_OnlyOnce()
    {
        var m = InLore();
        Assert.IsFalse(m.NotifyPartyEntered(1d));
        Assert.IsFalse(m.NotifyEntryGateOpen(1d));
    }

    // ─── Z4-1 G8 ───

    [Test]
    public void Timer_StartsAfterLoreSeconds_Once()
    {
        var m = new MapSceneBossApproachModel(loreSeconds: 10f);
        m.NotifyEntryGateOpen(100d);
        m.NotifyPartyEntered(100d);
        Assert.IsFalse(m.ShouldStartTimer(109.9d));
        Assert.IsTrue(m.ShouldStartTimer(110d));
        Assert.IsFalse(m.ShouldStartTimer(111d));
    }

    [Test]
    public void Timer_NotBeforeLore()
    {
        var m = new MapSceneBossApproachModel();
        Assert.IsFalse(m.ShouldStartTimer(1000d));
    }

    [Test]
    public void DefaultLore_IsWithinScenarioRange()
    {
        // §3.7 Z4-1 "설정 자막이 끝나면(약 8~10초)".
        Assert.That(MapSceneBossApproachModel.DefaultLoreSeconds, Is.InRange(8f, 10f));
    }

    [Test]
    public void TimerStarted_GoesCombat_ThenWakesAfterDelay_Once()
    {
        var m = InLore();
        Assert.IsTrue(m.NotifyTimerStarted(50d, out bool skipped));
        Assert.IsFalse(skipped);
        Assert.AreEqual(MapSceneBossApproachPhase.Combat, m.Phase);
        Assert.IsFalse(m.ShouldWakeNormals(52.9d));
        Assert.IsTrue(m.ShouldWakeNormals(53d));
        Assert.IsFalse(m.ShouldWakeNormals(60d));
        Assert.IsFalse(m.NotifyTimerStarted(60d, out _));
    }

    [Test]
    public void TimerStarted_BeforeLore_IsSkippedLore()
    {
        var m = new MapSceneBossApproachModel();
        Assert.IsTrue(m.NotifyTimerStarted(0d, out bool skipped));
        Assert.IsTrue(skipped);
        Assert.AreEqual(MapSceneBossApproachPhase.Combat, m.Phase);
        // 그 뒤 진입해도 설정 자막을 다시 시작하지 않는다.
        Assert.IsFalse(m.NotifyEntryGateOpen(1d));
        Assert.IsFalse(m.NotifyPartyEntered(1d));
    }

    [Test]
    public void TimerStarted_CancelsPendingLoreTimer()
    {
        var m = InLore(0d);
        m.NotifyTimerStarted(2d, out _);
        Assert.IsFalse(m.ShouldStartTimer(100d));
    }

    // ─── 패널 · 다리 · 중간보스 ───

    [Test]
    public void Panel_FirstOnlyOnce()
    {
        var m = InCombat();
        m.NotifyPanelActivated(out bool first);
        Assert.IsTrue(first);
        m.NotifyPanelActivated(out first);
        Assert.IsFalse(first);
    }

    [Test]
    public void Bridge_OnlyOnce()
    {
        var m = InCombat();
        Assert.IsTrue(m.NotifyBridgeOpened());
        Assert.IsFalse(m.NotifyBridgeOpened());
    }

    [Test]
    public void CounterRemind_OnlyAfterBridge_Once_NotAfterClear()
    {
        var m = InCombat();
        Assert.IsFalse(m.NotifyMidbossWindowOpened());
        m.NotifyBridgeOpened();
        Assert.IsTrue(m.NotifyMidbossWindowOpened());
        Assert.IsFalse(m.NotifyMidbossWindowOpened());

        var c = InCombat();
        c.NotifyBridgeOpened();
        c.NotifyCleared();
        Assert.IsFalse(c.NotifyMidbossWindowOpened());
    }

    [Test]
    public void Cleared_Once()
    {
        var m = InCombat();
        Assert.IsTrue(m.NotifyCleared());
        Assert.AreEqual(MapSceneBossApproachPhase.Cleared, m.Phase);
        Assert.IsFalse(m.NotifyCleared());
    }

    // ─── 만료 · 패드 · 도착 ───

    [Test]
    public void TimerExpired_Once_NotAfterArrival()
    {
        var m = InCombat();
        Assert.IsTrue(m.NotifyTimerExpired());
        Assert.IsFalse(m.NotifyTimerExpired());

        var a = InCombat();
        a.NotifyBossArrived(0d);
        Assert.IsFalse(a.NotifyTimerExpired());
    }

    [Test]
    public void PadCount_OnRisingEdge_WithCooldown()
    {
        var m = new MapSceneBossApproachModel(padCountCooldownSeconds: 8f);
        Assert.IsFalse(m.NotifyPadCountdown(false, false, 0d));
        Assert.IsTrue(m.NotifyPadCountdown(true, false, 1d));
        Assert.IsFalse(m.NotifyPadCountdown(true, false, 2d));   // 진행 중 — 다시 안 띄움
        m.NotifyPadCountdown(false, false, 3d);                  // 발판 이탈 → 취소
        Assert.IsFalse(m.NotifyPadCountdown(true, false, 4d));   // 간격 안 재시작
        m.NotifyPadCountdown(false, false, 5d);
        Assert.IsTrue(m.NotifyPadCountdown(true, false, 9d));    // 간격 지남
    }

    [Test]
    public void PadCount_NotForForcedMove()
    {
        var m = new MapSceneBossApproachModel();
        Assert.IsFalse(m.NotifyPadCountdown(true, true, 0d));
    }

    [Test]
    public void BossArrived_TipAfterDelay_Once()
    {
        var m = new MapSceneBossApproachModel(bossTipDelaySeconds: 1f);
        Assert.IsTrue(m.NotifyBossArrived(10d));
        Assert.IsFalse(m.NotifyBossArrived(10.5d));
        Assert.IsFalse(m.ShouldShowBossTip(10.9d));
        Assert.IsTrue(m.ShouldShowBossTip(11d));
        Assert.IsFalse(m.ShouldShowBossTip(20d));
        Assert.IsFalse(m.NotifyPadCountdown(true, false, 30d));
    }

    [Test]
    public void BossRetry_ReleasesArrival_PadAndTipAgain()
    {
        var m = new MapSceneBossApproachModel(padCountCooldownSeconds: 0f, bossTipDelaySeconds: 1f);
        Assert.IsFalse(m.NotifyBossRetry());
        m.NotifyBossArrived(10d);
        Assert.IsTrue(m.NotifyBossRetry());
        Assert.IsFalse(m.NotifyBossRetry());
        Assert.IsFalse(m.ShouldShowBossTip(20d));
        Assert.IsTrue(m.NotifyPadCountdown(true, false, 30d));
        Assert.IsTrue(m.NotifyBossArrived(40d));
        Assert.IsTrue(m.ShouldShowBossTip(41d));
    }

    [Test]
    public void BossArrived_DuringLore_CancelsTimerAndWake()
    {
        var m = InLore(0d);
        m.NotifyBossArrived(1d);
        Assert.IsFalse(m.ShouldStartTimer(100d));
    }

    // ─── 보스 진입 목표 · 강조 (교육 목표·강조는 Legacy TutorialZone4EducationTests — 할 일 107) ───

    [Test]
    public void Objective_GoBossOnlyAfterG9()
    {
        Assert.IsNull(MapSceneBossApproachDisplay.ObjectiveFor(MapSceneStep.Corridor3, false));
        Assert.IsNull(MapSceneBossApproachDisplay.ObjectiveFor(MapSceneStep.Zone4Lore, false));
        Assert.IsNull(MapSceneBossApproachDisplay.ObjectiveFor(MapSceneStep.Zone4Combat, false), "패널·중간보스 목표는 교육(Legacy)");
        Assert.AreEqual(TutorialGuideIds.Z4GoBoss, MapSceneBossApproachDisplay.ObjectiveFor(MapSceneStep.Zone4Combat, true));
        Assert.AreEqual(TutorialGuideIds.Z4GoBoss, MapSceneBossApproachDisplay.ObjectiveFor(MapSceneStep.Corridor4, true));
        Assert.IsNull(MapSceneBossApproachDisplay.ObjectiveFor(MapSceneStep.Finished, true));
    }

    [Test]
    public void MinimapBossIcon_AfterG9UntilArrival()
    {
        Assert.IsFalse(MapSceneBossApproachDisplay.ShowsMinimapBossIcon(MapSceneStep.Zone4Combat, false));
        Assert.IsTrue(MapSceneBossApproachDisplay.ShowsMinimapBossIcon(MapSceneStep.Zone4Combat, true));
        Assert.IsTrue(MapSceneBossApproachDisplay.ShowsMinimapBossIcon(MapSceneStep.BossEnter, true));
        Assert.IsFalse(MapSceneBossApproachDisplay.ShowsMinimapBossIcon(MapSceneStep.Finished, true));
    }

    [Test]
    public void IsZone4Objective_OnlyOwnIds()
    {
        Assert.IsTrue(MapSceneBossApproachDisplay.IsZone4Objective(TutorialGuideIds.Z4GoBoss));
        Assert.IsFalse(MapSceneBossApproachDisplay.IsZone4Objective(TutorialGuideIds.Z4PanelGoal), "교육 목표는 Legacy 몫");
        Assert.IsFalse(MapSceneBossApproachDisplay.IsZone4Objective(TutorialGuideIds.Z3Boss));
        Assert.IsFalse(MapSceneBossApproachDisplay.IsZone4Objective(null));
    }

    // ─── 복도 흐름과 목표 순서 (S12) ────────────────────────────────────────

    [Test]
    public void 보스방으로_목표는_복도4_고르기_대기_발판_목표를_덮지_않는다()
    {
        string goBoss = TutorialGuideIds.Z4GoBoss;
        Assert.IsTrue(MapSceneBossApproachDisplay.YieldsToCorridor(goBoss, MapSceneTraitCorridorFlow.PickId));
        Assert.IsTrue(MapSceneBossApproachDisplay.YieldsToCorridor(goBoss, MapSceneTraitCorridorFlow.WaitId));
        Assert.IsTrue(MapSceneBossApproachDisplay.YieldsToCorridor(goBoss, MapSceneTraitCorridorFlow.GoPadId));
        Assert.IsFalse(MapSceneBossApproachDisplay.YieldsToCorridor(goBoss, MapSceneTraitCorridorFlow.NextId), "낡은 복도3 목표는 덮는다");
        Assert.IsFalse(MapSceneBossApproachDisplay.YieldsToCorridor(goBoss, TutorialGuideIds.Z4Midboss));
    }

    [Test]
    public void 존4_안에선_남은_다음_구역으로_목표를_지운다()
    {
        Assert.IsTrue(MapSceneBossApproachDisplay.ShouldClearStaleCorridorObjective(MapSceneStep.Zone4Lore, MapSceneTraitCorridorFlow.NextId));
        Assert.IsTrue(MapSceneBossApproachDisplay.ShouldClearStaleCorridorObjective(MapSceneStep.Zone4Combat, MapSceneTraitCorridorFlow.NextId),
            "107 — 소개 연출 판은 설정 단계가 바로 지나간다");
        Assert.IsFalse(MapSceneBossApproachDisplay.ShouldClearStaleCorridorObjective(MapSceneStep.Corridor3, MapSceneTraitCorridorFlow.NextId), "복도3 안에선 그대로");
        Assert.IsFalse(MapSceneBossApproachDisplay.ShouldClearStaleCorridorObjective(MapSceneStep.Zone4Lore, MapSceneTraitCorridorFlow.WaitId));
    }
}

