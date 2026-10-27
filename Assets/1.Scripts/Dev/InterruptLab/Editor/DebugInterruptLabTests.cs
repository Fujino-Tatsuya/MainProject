using NUnit.Framework;

/// <summary>
/// 간파 제작용 디버그 씬(할 일 115 C5)의 순수 규칙 — 배속 합성(<see cref="GlobalTimeScale"/>)과
/// 허수아비 회복 시간(<see cref="DebugDummyInterruptRecovery.SplitRecovery"/> → <see cref="TrainingDummyInterruptCycle"/>).
/// 길이는 2 진수로 딱 떨어지는 값을 쓴다.
/// </summary>
public sealed class DebugInterruptLabTests
{
    const float Eps = 1e-5f;

    // ─── 배속 합성 ─────────────────────────────────────────────────────

    [Test]
    public void 기본_배속_1이면_슬로우_배율_그대로()
    {
        Assert.AreEqual(1f, GlobalTimeScale.ComposeScale(1f, 1f), Eps);
        Assert.AreEqual(0.25f, GlobalTimeScale.ComposeScale(0.25f, 1f), Eps);
    }

    [Test]
    public void 최종_배율은_슬로우_곱하기_기본_배속()
    {
        Assert.AreEqual(0.125f, GlobalTimeScale.ComposeScale(0.25f, 0.5f), Eps);
        Assert.AreEqual(0.5f, GlobalTimeScale.ComposeScale(1f, 0.5f), Eps); // 슬로우 없을 때도 적용
        Assert.AreEqual(0.5f, GlobalTimeScale.ComposeScale(0.25f, 2f), Eps);
    }

    [Test]
    public void 기본_배속은_0이_되지_않는다()
    {
        Assert.Greater(GlobalTimeScale.ComposeScale(1f, 0f), 0f);
    }

    [Test]
    public void 슬로우도_기본_배속도_없으면_레이어는_쉰다()
    {
        Assert.IsFalse(GlobalTimeScale.IsLayerActive(false, 1f)); // 지금 동작과 같다 — timeScale 에 쓰지 않음
        Assert.IsTrue(GlobalTimeScale.IsLayerActive(true, 1f));
        Assert.IsTrue(GlobalTimeScale.IsLayerActive(false, 0.5f));
        Assert.IsTrue(GlobalTimeScale.IsLayerActive(true, 0.5f));
    }

    [Test]
    public void 디버그_배속은_범위로_자르고_프리셋은_범위_안()
    {
        Assert.AreEqual(DebugTimeScale.MinScale, DebugTimeScale.ClampScale(0f), Eps);
        Assert.AreEqual(DebugTimeScale.MaxScale, DebugTimeScale.ClampScale(10f), Eps);
        foreach (float preset in DebugTimeScale.Presets)
            Assert.AreEqual(preset, DebugTimeScale.ClampScale(preset), Eps);
    }

    // ─── 허수아비 회복 ─────────────────────────────────────────────────

    static TrainingDummyInterruptCycle CycleFor(float recovery)
    {
        (float vulnerable, float idle) = DebugDummyInterruptRecovery.SplitRecovery(recovery);
        return new TrainingDummyInterruptCycle(vulnerable, idle);
    }

    [Test]
    public void 회복_시간_합은_입력과_같다()
    {
        (float vulnerable, float idle) = DebugDummyInterruptRecovery.SplitRecovery(2f);
        Assert.AreEqual(2f, vulnerable + idle, Eps);
    }

    [Test]
    public void 성공_후_회복_시간_전에는_인터럽트_불가()
    {
        var c = CycleFor(2f);
        Assert.IsTrue(c.TryInterrupt());
        c.Tick(1.5f);
        Assert.IsFalse(c.IsInterruptible);
        Assert.IsFalse(c.TryInterrupt());
    }

    [Test]
    public void 회복_시간이_지나면_인터럽트_가능으로_복귀()
    {
        var c = CycleFor(2f);
        c.TryInterrupt();
        c.Tick(1.5f);
        c.Tick(0.5f);
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 복귀한_인터럽트_가능_상태는_시간_제한_없이_유지()
    {
        var c = CycleFor(2f);
        c.TryInterrupt();
        c.Tick(2f);
        for (int i = 0; i < 100; i++)
            c.Tick(64f);
        Assert.IsTrue(c.IsInterruptible);
        Assert.IsTrue(c.TryInterrupt()); // 다시 성공한다
    }

    [Test]
    public void 회복_시간_0이면_다음_틱에_바로_복귀()
    {
        var c = CycleFor(0f);
        c.TryInterrupt();
        c.Tick(0f);
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 음수_회복_시간은_0으로_본다()
    {
        (float vulnerable, float idle) = DebugDummyInterruptRecovery.SplitRecovery(-1f);
        Assert.AreEqual(0f, vulnerable, Eps);
        Assert.AreEqual(0f, idle, Eps);
    }
}
