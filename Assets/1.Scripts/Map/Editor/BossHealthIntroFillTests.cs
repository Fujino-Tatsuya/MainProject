using NUnit.Framework;

public sealed class BossHealthIntroFillTests
{
    private const float Start = 0.5f, Lead = 0.4f, Fill = 0.9f, Fade = 0.3f;

    [Test]
    public void Inactive_ReturnsActual()
    {
        var fill = new BossHealthIntroFill();
        BossHealthIntroFill.Frame f = fill.Evaluate(0.7f, 0.1f, Start, Lead, Fill, Fade);
        Assert.IsFalse(f.Active);
        Assert.AreEqual(0.7f, f.Red, 1e-5f);
        Assert.AreEqual(0.7f, f.White, 1e-5f);
    }

    [Test]
    public void Begin_BothStartAtHalf()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        BossHealthIntroFill.Frame f = fill.Evaluate(1f, 0f, Start, Lead, Fill, Fade);
        Assert.IsTrue(f.Active);
        Assert.AreEqual(0.5f, f.Red, 1e-5f);
        Assert.AreEqual(0.5f, f.White, 1e-5f);
        Assert.AreEqual(1f, f.WhiteAlpha, 1e-5f);
    }

    [Test]
    public void WhiteLeadsRed_ThenFades_ThenEnds()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        BossHealthIntroFill.Frame mid = fill.Evaluate(1f, 0.3f, Start, Lead, Fill, Fade);
        Assert.Greater(mid.White, mid.Red);                       // 흰색이 앞서 달린다
        BossHealthIntroFill.Frame lead = fill.Evaluate(1f, 0.1f, Start, Lead, Fill, Fade);   // 0.4초 = 흰색 도착
        Assert.AreEqual(1f, lead.White, 1e-4f);
        Assert.Less(lead.Red, 1f);
        BossHealthIntroFill.Frame full = fill.Evaluate(1f, 0.5f, Start, Lead, Fill, Fade);   // 0.9초 = 빨강 도착
        Assert.AreEqual(1f, full.Red, 1e-4f);
        Assert.AreEqual(1f, full.WhiteAlpha, 1e-4f);
        BossHealthIntroFill.Frame fading = fill.Evaluate(1f, 0.15f, Start, Lead, Fill, Fade);
        Assert.AreEqual(0.5f, fading.WhiteAlpha, 1e-3f);          // 사그라드는 중
        BossHealthIntroFill.Frame done = fill.Evaluate(1f, 0.2f, Start, Lead, Fill, Fade);
        Assert.IsFalse(done.Active);
        Assert.IsFalse(fill.Active);
    }

    [Test]
    public void LeadGap_WhiteAheadFromFirstFrame()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        BossHealthIntroFill.Frame first = fill.Evaluate(1f, 0f, Start, Lead, Fill, Fade, leadStartGap: 0.08f);
        Assert.IsTrue(first.Active);
        Assert.AreEqual(0.5f, first.Red, 1e-5f);
        Assert.AreEqual(0.58f, first.White, 1e-5f);              // 첫 프레임부터 흰 구간이 앞서 있다
        BossHealthIntroFill.Frame next = fill.Evaluate(1f, 1f / 60f, Start, Lead, Fill, Fade, leadStartGap: 0.08f);
        Assert.Greater(next.Red, 0.5f);                          // 멈춤 없이 바로 차오른다(경석 Play 6차)
        Assert.Greater(next.White, 0.58f);
    }

    [Test]
    public void LeadGap_ClampedToActual()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        BossHealthIntroFill.Frame f = fill.Evaluate(0.52f, 0f, Start, Lead, Fill, Fade, leadStartGap: 0.08f);
        Assert.AreEqual(0.52f, f.White, 1e-5f);                  // 실제 HP 너머로 앞서지 않는다
    }

    [Test]
    public void ActualBelowStart_NeverGoesDown()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        Assert.AreEqual(0.1f, fill.Evaluate(0.1f, 0f, Start, Lead, Fill, Fade).Red, 1e-5f);
        Assert.AreEqual(0.1f, fill.Evaluate(0.1f, 0.3f, Start, Lead, Fill, Fade).White, 1e-5f);
    }

    [Test]
    public void Cancel_StopsImmediately()
    {
        var fill = new BossHealthIntroFill();
        fill.Begin();
        fill.Cancel();
        Assert.IsFalse(fill.Evaluate(0.9f, 0.01f, Start, Lead, Fill, Fade).Active);
    }
}
