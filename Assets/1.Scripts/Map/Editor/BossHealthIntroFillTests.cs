using NUnit.Framework;

public sealed class BossHealthIntroFillTests
{
    [Test]
    public void Inactive_ReturnsActual()
    {
        var fill = new BossHealthIntroFill();
        Assert.AreEqual(0.7f, fill.Evaluate(0.7f, 0.1f, 0.25f, 0.8f), 1e-5f);
        Assert.IsFalse(fill.Active);
    }

    [Test]
    public void Begin_StartsAtQuarter_RisesFastThenEndsAtActual()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        float first = fill.Evaluate(1f, 0f, 0.25f, 0.8f);
        Assert.AreEqual(0.25f, first, 1e-5f);
        float mid = fill.Evaluate(1f, 0.4f, 0.25f, 0.8f);   // 절반 시간에 감속 곡선 → 남은 양의 7/8 가 찼다
        Assert.AreEqual(0.25f + 0.75f * 0.875f, mid, 1e-4f);
        Assert.AreEqual(1f, fill.Evaluate(1f, 0.4f, 0.25f, 0.8f), 1e-5f);
        Assert.IsFalse(fill.Active);
    }

    [Test]
    public void ActualBelowStart_NeverGoesDown()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        Assert.AreEqual(0.1f, fill.Evaluate(0.1f, 0f, 0.25f, 0.8f), 1e-5f);
        Assert.AreEqual(0.1f, fill.Evaluate(0.1f, 0.3f, 0.25f, 0.8f), 1e-5f);
    }

    [Test]
    public void Cancel_StopsImmediately()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        fill.Cancel();
        Assert.AreEqual(0.9f, fill.Evaluate(0.9f, 0.01f, 0.25f, 0.8f), 1e-5f);
    }
}
