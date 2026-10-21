using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 스텝별 고정 안전 지점(할 일 80) 순수 규칙(<see cref="TutorialSafePointRules"/>·<see cref="TutorialRecoveryTimer"/>) —
/// 감시 조건·구간 밖 판정·구간 순서(부활과 같음)·저작 기본 위치·연속 시간 타이머. 문서: Docs/tech/tutorial-director.md §12.
/// </summary>
public sealed class TutorialSafePointRulesTests
{
    // 존1 (0..10) → 복도1 (10..20, 폭 4) → 존2 (20..30). 진행 = +X.
    static TutorialAreaTracker Layout()
    {
        var t = new TutorialAreaTracker();
        t.SetArea(TutorialArea.Zone1, TutorialRect.FromMinMax(0f, -5f, 10f, 5f));
        t.SetArea(TutorialArea.Corridor1, TutorialRect.FromMinMax(10f, -2f, 20f, 2f));
        t.SetArea(TutorialArea.Zone2, TutorialRect.FromMinMax(20f, -5f, 30f, 5f));
        return t;
    }

    static int Mask(params TutorialGate[] gates)
    {
        int m = 0;
        foreach (TutorialGate g in gates) m |= 1 << (int)g;
        return m;
    }

    // ─── 감시 조건 ────────────────────────────────────────────────────────

    [Test]
    public void ShouldWatch_OnlyWhileTutorialRuns()
    {
        Assert.IsTrue(TutorialSafePointRules.ShouldWatch(true, TutorialStep.Zone2, false));
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(false, TutorialStep.Zone2, false), "튜토리얼 아님");
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(true, TutorialStep.None, false), "활성 전");
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(true, TutorialStep.Finished, false), "보스방 도착 뒤 = 일반 규칙");
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(true, TutorialStep.BossEnter, true), "보스방 이동(카운트다운·도착 대기) 중");
    }

    // ─── 구간 밖 ──────────────────────────────────────────────────────────

    [Test]
    public void IsOutsideAll_InsideAnyAreaOrMargin_IsFalse()
    {
        TutorialAreaTracker t = Layout();
        Assert.IsFalse(TutorialSafePointRules.IsOutsideAll(t, 5f, 0f, 1f), "존1 안");
        Assert.IsFalse(TutorialSafePointRules.IsOutsideAll(t, 15f, 1.5f, 1f), "복도1 안");
        Assert.IsFalse(TutorialSafePointRules.IsOutsideAll(t, 15f, 2.8f, 1f), "복도 벽 너머 0.8m — 여유 안");
        Assert.IsTrue(TutorialSafePointRules.IsOutsideAll(t, 15f, 3.5f, 1f), "복도 옆 1.5m 밖 — 맵 밖");
        Assert.IsTrue(TutorialSafePointRules.IsOutsideAll(t, -3f, 0f, 1f), "존1 뒤 바깥");
    }

    [Test]
    public void IsOutsideAll_NoAreas_NeverJudges()
    {
        Assert.IsFalse(TutorialSafePointRules.IsOutsideAll(null, 999f, 999f, 0f), "구간 계산 실패");
        Assert.IsFalse(TutorialSafePointRules.IsOutsideAll(new TutorialAreaTracker(), 999f, 999f, 0f), "구간 0개");
    }

    // ─── 구간 순서 = 자동 부활 ───────────────────────────────────────────

    [Test]
    public void AreaOrder_MatchesRevive_StepAreaDownToFrontmost()
    {
        int mask = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone2, TutorialArea.Corridor1, TutorialArea.Zone1 },
            TutorialSafePointRules.AreaOrder(TutorialStep.Zone2, mask, 0), "단계 = 존2, 뒤 배리어 없음");

        int passed = Mask(TutorialGate.G1_Corridor1Entry);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone2, TutorialArea.Corridor1 },
            TutorialSafePointRules.AreaOrder(TutorialStep.Zone2, mask, passed), "G1 을 지난 사람은 존1 로 안 돌아간다");

        CollectionAssert.AreEqual(TutorialRevivePointRules.AreaOrder(TutorialStep.Corridor1, mask, passed),
            TutorialSafePointRules.AreaOrder(TutorialStep.Corridor1, mask, passed));
    }

    // ─── 저작 기본 위치 ──────────────────────────────────────────────────

    [Test]
    public void DefaultAnchor_Zone1Center_OthersInsideEntryBarrier()
    {
        TutorialAreaTracker t = Layout();
        var specs = new List<TutorialBarrierSpec>
        {
            new TutorialBarrierSpec(TutorialGate.G1_Corridor1Entry, 1, true, TutorialRect.FromMinMax(10f, -2.3f, 10.4f, 2.3f), true, 1f),
            new TutorialBarrierSpec(TutorialGate.G2_Zone2Entry, 1, false, TutorialRect.FromMinMax(19.6f, -2.3f, 20f, 2.3f), true, 1f),
        };

        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Zone1, t, specs, 2.5f, out float x, out float z));
        Assert.AreEqual(5f, x, 1e-4);
        Assert.AreEqual(0f, z, 1e-4);

        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Corridor1, t, specs, 1.5f, out x, out z));
        Assert.AreEqual(11.9f, x, 1e-4, "입구 배리어 앞면 10.4 + 1.5");
        Assert.AreEqual(0f, z, 1e-4);

        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Zone2, t, specs, 2.5f, out x, out z));
        Assert.AreEqual(22.5f, x, 1e-4, "복도1 출구 배리어 앞면 20 + 2.5");

        Assert.IsFalse(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Zone3, t, specs, 2.5f, out _, out _), "구간 없음");
    }

    [Test]
    public void DefaultAnchor_NegativeForward_GoesTowardMinus()
    {
        var t = new TutorialAreaTracker();
        t.SetArea(TutorialArea.Corridor2, TutorialRect.FromMinMax(-2f, -190f, 2f, -180f));
        var specs = new List<TutorialBarrierSpec>
        {
            // 복도2 입구 — 진행 = −Z (z 큰 쪽에서 작은 쪽으로)
            new TutorialBarrierSpec(TutorialGate.G3_Corridor2Entry, 2, true, TutorialRect.FromMinMax(-2.3f, -180.4f, 2.3f, -180f), false, -1f),
        };
        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Corridor2, t, specs, 1.5f, out float x, out float z));
        Assert.AreEqual(0f, x, 1e-4);
        Assert.AreEqual(-181.9f, z, 1e-4, "앞면 −180.4 에서 −Z 로 1.5");
    }

    // ─── 연속 시간 타이머 ────────────────────────────────────────────────

    [Test]
    public void Timer_FiresAfterContinuousThreshold_ThenRearms()
    {
        var timer = new TutorialRecoveryTimer();
        Assert.IsFalse(timer.Tick(true, 0.0, 1.5f));
        Assert.IsFalse(timer.Tick(true, 1.0, 1.5f));
        Assert.IsTrue(timer.Tick(true, 1.5, 1.5f), "1.5초 연속");
        Assert.IsFalse(timer.Tick(true, 2.0, 1.5f), "알린 뒤 다시 잰다");
        Assert.IsTrue(timer.Tick(true, 3.0, 1.5f), "복귀 실패로 그대로면 다음 1.5초 뒤 다시");
    }

    [Test]
    public void Timer_BreakResets()
    {
        var timer = new TutorialRecoveryTimer();
        timer.Tick(true, 0.0, 1.5f);
        Assert.IsFalse(timer.Tick(false, 1.0, 1.5f));
        Assert.IsFalse(timer.IsRunning);
        Assert.IsFalse(timer.Tick(true, 1.2, 1.5f), "끊긴 뒤 처음부터");
        Assert.IsFalse(timer.Tick(true, 2.6, 1.5f));
        Assert.IsTrue(timer.Tick(true, 2.7, 1.5f));

        timer.Reset();
        Assert.IsFalse(timer.IsRunning);
    }
}
