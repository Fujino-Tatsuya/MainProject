using NUnit.Framework;

// 튜토리얼 존4 흐름 + 보스 진입 안내 순수 규칙(S10) — TutorialZone4Model · TutorialZone4Display.
public class TutorialZone4RulesTests
{
    static TutorialZone4Model InLore(double now = 0d)
    {
        var m = new TutorialZone4Model();
        m.NotifyEntryGateOpen(now);
        m.NotifyPartyEntered(now);
        return m;
    }

    static TutorialZone4Model InCombat(double now = 0d)
    {
        var m = InLore(now);
        m.NotifyTimerStarted(now, out _);
        return m;
    }

    // ─── Z4-0 설정 자막 ───

    [Test]
    public void Lore_NeedsBothGateAndEntry_InEitherOrder()
    {
        var a = new TutorialZone4Model();
        Assert.IsFalse(a.NotifyEntryGateOpen(0d));
        Assert.IsTrue(a.NotifyPartyEntered(0d));
        Assert.AreEqual(TutorialZone4Phase.Lore, a.Phase);

        var b = new TutorialZone4Model();
        Assert.IsFalse(b.NotifyPartyEntered(0d));
        Assert.AreEqual(TutorialZone4Phase.Waiting, b.Phase);
        Assert.IsTrue(b.NotifyEntryGateOpen(0d));
        Assert.AreEqual(TutorialZone4Phase.Lore, b.Phase);
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
        var m = new TutorialZone4Model(loreSeconds: 10f);
        m.NotifyEntryGateOpen(100d);
        m.NotifyPartyEntered(100d);
        Assert.IsFalse(m.ShouldStartTimer(109.9d));
        Assert.IsTrue(m.ShouldStartTimer(110d));
        Assert.IsFalse(m.ShouldStartTimer(111d));
    }

    [Test]
    public void Timer_NotBeforeLore()
    {
        var m = new TutorialZone4Model();
        Assert.IsFalse(m.ShouldStartTimer(1000d));
    }

    [Test]
    public void DefaultLore_IsWithinScenarioRange()
    {
        // §3.7 Z4-1 "설정 자막이 끝나면(약 8~10초)".
        Assert.That(TutorialZone4Model.DefaultLoreSeconds, Is.InRange(8f, 10f));
    }

    [Test]
    public void TimerStarted_GoesCombat_ThenWakesAfterDelay_Once()
    {
        var m = InLore();
        Assert.IsTrue(m.NotifyTimerStarted(50d, out bool skipped));
        Assert.IsFalse(skipped);
        Assert.AreEqual(TutorialZone4Phase.Combat, m.Phase);
        Assert.IsFalse(m.ShouldWakeNormals(52.9d));
        Assert.IsTrue(m.ShouldWakeNormals(53d));
        Assert.IsFalse(m.ShouldWakeNormals(60d));
        Assert.IsFalse(m.NotifyTimerStarted(60d, out _));
    }

    [Test]
    public void TimerStarted_BeforeLore_IsSkippedLore()
    {
        var m = new TutorialZone4Model();
        Assert.IsTrue(m.NotifyTimerStarted(0d, out bool skipped));
        Assert.IsTrue(skipped);
        Assert.AreEqual(TutorialZone4Phase.Combat, m.Phase);
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
        Assert.AreEqual(TutorialZone4Phase.Cleared, m.Phase);
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
        var m = new TutorialZone4Model(padCountCooldownSeconds: 8f);
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
        var m = new TutorialZone4Model();
        Assert.IsFalse(m.NotifyPadCountdown(true, true, 0d));
    }

    [Test]
    public void BossArrived_TipAfterDelay_Once()
    {
        var m = new TutorialZone4Model(bossTipDelaySeconds: 1f);
        Assert.IsTrue(m.NotifyBossArrived(10d));
        Assert.IsFalse(m.NotifyBossArrived(10.5d));
        Assert.IsFalse(m.ShouldShowBossTip(10.9d));
        Assert.IsTrue(m.ShouldShowBossTip(11d));
        Assert.IsFalse(m.ShouldShowBossTip(20d));
        Assert.IsFalse(m.NotifyPadCountdown(true, false, 30d));
    }

    [Test]
    public void BossArrived_DuringLore_CancelsTimerAndWake()
    {
        var m = InLore(0d);
        m.NotifyBossArrived(1d);
        Assert.IsFalse(m.ShouldStartTimer(100d));
    }

    // ─── 목표 · 강조 ───

    [Test]
    public void Objective_ByStepPanelsAndG9()
    {
        Assert.IsNull(TutorialZone4Display.ObjectiveFor(TutorialStep.Corridor3, 0, 4, false));
        Assert.IsNull(TutorialZone4Display.ObjectiveFor(TutorialStep.Zone4Lore, 0, 4, false));
        Assert.AreEqual(TutorialGuideIds.Z4PanelGoal, TutorialZone4Display.ObjectiveFor(TutorialStep.Zone4Combat, 3, 4, false));
        Assert.AreEqual(TutorialGuideIds.Z4Midboss, TutorialZone4Display.ObjectiveFor(TutorialStep.Zone4Combat, 4, 4, false));
        Assert.AreEqual(TutorialGuideIds.Z4GoBoss, TutorialZone4Display.ObjectiveFor(TutorialStep.Zone4Combat, 4, 4, true));
        Assert.AreEqual(TutorialGuideIds.Z4GoBoss, TutorialZone4Display.ObjectiveFor(TutorialStep.Corridor4, 4, 4, true));
        Assert.IsNull(TutorialZone4Display.ObjectiveFor(TutorialStep.Finished, 4, 4, true));
    }

    [Test]
    public void Highlights_ByStepPanelsAndG9()
    {
        Assert.AreEqual(TutorialZone4Highlight.None, TutorialZone4Display.HighlightsFor(TutorialStep.Corridor3, 0, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.BossTimer, TutorialZone4Display.HighlightsFor(TutorialStep.Zone4Lore, 0, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.None, TutorialZone4Display.HighlightsFor(TutorialStep.Zone4Combat, 3, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.MidbossRoute, TutorialZone4Display.HighlightsFor(TutorialStep.Zone4Combat, 4, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.MinimapBossIcon, TutorialZone4Display.HighlightsFor(TutorialStep.Zone4Combat, 4, 4, true));
        Assert.AreEqual(TutorialZone4Highlight.None, TutorialZone4Display.HighlightsFor(TutorialStep.Finished, 4, 4, true));
    }

    [Test]
    public void IsZone4Objective_OnlyOwnIds()
    {
        Assert.IsTrue(TutorialZone4Display.IsZone4Objective(TutorialGuideIds.Z4PanelGoal));
        Assert.IsTrue(TutorialZone4Display.IsZone4Objective(TutorialGuideIds.Z4Midboss));
        Assert.IsTrue(TutorialZone4Display.IsZone4Objective(TutorialGuideIds.Z4GoBoss));
        Assert.IsFalse(TutorialZone4Display.IsZone4Objective(TutorialGuideIds.Z3Boss));
        Assert.IsFalse(TutorialZone4Display.IsZone4Objective(null));
    }
}
