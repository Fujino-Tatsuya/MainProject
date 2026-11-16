using NUnit.Framework;

/// <summary>처치 연출 낙하 검 수명(fallingSwordLife) — 꽂힌 채 멈췄다 풀기 시계(VictoryStrikeHold).</summary>
public sealed class BossVictoryFallingSwordHoldTests
{
    const float Tol = 1e-4f;

    [Test]
    public void 수명_0이면_꺼짐_원래_그대로()
    {
        VictoryStrikeHold hold = VictoryStrikeHold.Create(2.2f, 0f);
        Assert.AreEqual(VictoryStrikeHold.Phase.Off, hold.State);
        Assert.AreEqual(VictoryStrikeHold.Action.None, hold.Step(10f, 10f));
        Assert.AreEqual(VictoryStrikeHold.Phase.Off, VictoryStrikeHold.Create(0f, 5f).State, "엔트리 길이 0 = 꺼짐");
    }

    [Test]
    public void 엔트리_길이의_0점75에서_멈추고_수명_뒤_푼다()
    {
        VictoryStrikeHold hold = VictoryStrikeHold.Create(2.2f, 3f);
        Assert.AreEqual(1.65f, hold.HoldAt, Tol, "칼 알파가 줄기(입자 수명 81%) 전");
        Assert.AreEqual(3f, hold.HoldFor, Tol);

        Assert.AreEqual(VictoryStrikeHold.Action.None, hold.Step(1.0f, 1.0f));
        Assert.AreEqual(VictoryStrikeHold.Action.None, hold.Step(0.6f, 0.6f));
        Assert.AreEqual(VictoryStrikeHold.Action.Freeze, hold.Step(0.1f, 0.1f), "1.7초 ≥ 1.65 → 멈춤");
        Assert.AreEqual(VictoryStrikeHold.Phase.Holding, hold.State);

        Assert.AreEqual(VictoryStrikeHold.Action.None, hold.Step(0f, 2.9f), "멈춘 동안은 실제 시간으로 센다(이펙트 시간 0)");
        Assert.AreEqual(VictoryStrikeHold.Action.Resume, hold.Step(0f, 0.2f), "3초 → 풀기");
        Assert.AreEqual(VictoryStrikeHold.Phase.Off, hold.State);
        Assert.AreEqual(VictoryStrikeHold.Action.None, hold.Step(1f, 1f), "푼 뒤에는 아무것도 안 함(원래 페이드로 끝)");
    }

    [Test]
    public void 재생_배율이_느리면_이펙트_시간으로_늦게_멈춘다()
    {
        VictoryStrikeHold hold = VictoryStrikeHold.Create(2.2f, 1f);
        int frames = 0;
        while (hold.Step(0.1f * 0.5f, 0.1f) != VictoryStrikeHold.Action.Freeze && frames < 1000)
            frames++;
        Assert.That(frames, Is.InRange(32, 33), "배율 0.5 → 실제 3.3초쯤(33~34번째 프레임, 부동소수 누적)에 멈춤");
    }
}
