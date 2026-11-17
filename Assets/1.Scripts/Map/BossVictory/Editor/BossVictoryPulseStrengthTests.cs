using NUnit.Framework;

/// <summary>가로 찢김 클립별 강도 배율(11-17) — <see cref="BossVictoryClip.pulseStrength"/>.</summary>
public class BossVictoryPulseStrengthTests
{
    const float Tolerance = 1e-4f;

    static BossVictoryClip Flat(float start, float length, float strength)
    {
        BossVictoryClip c = BossVictoryClip.Make(start, length);   // 페이드 없음·세기 1
        c.pulseStrength = strength;
        return c;
    }

    [Test]
    public void 강도_0이하는_1_옛_데이터_그대로()
    {
        Assert.AreEqual(1f, Flat(0f, 1f, 0f).PulseStrength, Tolerance);
        Assert.AreEqual(2.5f, Flat(0f, 1f, 2.5f).PulseStrength, Tolerance);
    }

    [Test]
    public void 가로_찢김은_클립마다_강도를_곱하고_1을_넘을_수_있다()
    {
        var tl = new BossVictoryTimeline();
        var tear = tl.Clips(BossVictoryTrack.Tear);
        tear.Clear();
        tear.Add(Flat(0f, 1f, 0f));     // 옛 데이터 = ×1
        tear.Add(Flat(2f, 1f, 3f));     // ×3
        tear.Add(Flat(4f, 1f, 0.5f));   // ×0.5
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.Tear, 0.5f), Tolerance);
        Assert.AreEqual(3f, tl.Evaluate(BossVictoryTrack.Tear, 2.5f), Tolerance);
        Assert.AreEqual(0.5f, tl.Evaluate(BossVictoryTrack.Tear, 4.5f), Tolerance);
    }

    [Test]
    public void 끌림은_강도를_안_쓴다()
    {
        var tl = new BossVictoryTimeline();
        var smear = tl.Clips(BossVictoryTrack.Smear);
        smear.Clear();
        smear.Add(Flat(0f, 1f, 3f));
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.Smear, 0.5f), Tolerance);
        Assert.IsFalse(BossVictoryTimeline.HasPulseStrength(BossVictoryTrack.Smear));
        Assert.IsTrue(BossVictoryTimeline.HasPulseStrength(BossVictoryTrack.Tear));
    }
}
