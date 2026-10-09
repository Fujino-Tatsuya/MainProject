using NUnit.Framework;

/// <summary>
/// 보스 제한시간의 순수 계산(<see cref="BossTimerManager.RemainingAt"/>·<see cref="BossTimerManager.IsExpiredAt"/>·
/// <see cref="BossTimerManager.SelectNow"/>) — 시간 도메인이 GameNow 라 슬로우만큼 덜 줄어드는지(PLAN-interrupt-slowmo D16).
/// 시각·길이는 2 진수로 딱 떨어지는 값을 쓴다(<see cref="SlowMotionTimelineTests"/> 와 같은 이유).
/// </summary>
public sealed class BossTimerClockTests
{
    const double Eps = 1e-9;

    // 배율 0.5, 진입 0.125, 유지 0.25, 복귀 0.25 → 정상 종료 = 0.625s.
    static SlowMotionProfile P() =>
        new SlowMotionProfile
        {
            scale = 0.5f,
            enterDuration = 0.125f,
            holdDuration = 0.25f,
            exitDuration = 0.25f,
            failExitDuration = 0.125f,
        };

    [Test]
    public void 남은_시간은_만료_시각과의_차이()
    {
        Assert.AreEqual(4.0, BossTimerManager.RemainingAt(12.0, 8.0), Eps);
    }

    [Test]
    public void 만료_뒤_남은_시간은_0()
    {
        Assert.AreEqual(0.0, BossTimerManager.RemainingAt(8.0, 12.0), Eps);
    }

    [Test]
    public void 만료_시각_0_은_비활성()
    {
        Assert.AreEqual(0.0, BossTimerManager.RemainingAt(0.0, -4.0), Eps, "남은 시간");
        Assert.IsFalse(BossTimerManager.IsExpiredAt(0.0, 8.0), "만료 아님");
    }

    [Test]
    public void 만료는_만료_시각에_도달한_순간부터()
    {
        Assert.IsFalse(BossTimerManager.IsExpiredAt(8.0, 7.5));
        Assert.IsTrue(BossTimerManager.IsExpiredAt(8.0, 8.0));
        Assert.IsTrue(BossTimerManager.IsExpiredAt(8.0, 9.0));
    }

    [Test]
    public void 세션_시계가_돌면_GameNow_아니면_ServerTime()
    {
        Assert.AreEqual(6.0, BossTimerManager.SelectNow(true, 6.0, 8.0), Eps, "세션 시계");
        Assert.AreEqual(8.0, BossTimerManager.SelectNow(false, 6.0, 8.0), Eps, "폴백");
    }

    [Test]
    public void 슬로우만큼_제한시간이_덜_줄어든다()
    {
        // NetworkClock.GameNow = UnslowedGameNow − LostTimeUntil(t) 와 같은 식으로 GameNow 를 만든다.
        var session = new SlowMotionSession();
        const double start = 8.0;
        const double expiresAt = start + 4.0;
        session.Apply(SlowMotionEvent.CreateStart(1, start, 1, P()));

        double t = start + 1.0; // 슬로우가 끝난 뒤
        double lost = session.LostTimeUntil(t);
        double gameNow = t - lost;

        Assert.Greater(lost, 0.0, "슬로우로 잃은 시간이 있어야 한다");
        Assert.AreEqual(3.0, BossTimerManager.RemainingAt(expiresAt, t), Eps, "실시간 기준(옛 동작)");
        Assert.AreEqual(3.0 + lost, BossTimerManager.RemainingAt(expiresAt, gameNow), Eps, "GameNow 기준 = 잃은 시간만큼 더 남는다");
        Assert.IsFalse(BossTimerManager.IsExpiredAt(expiresAt, expiresAt - lost), "실시간으로 만료 시각이어도 GameNow 로는 아직");
    }
}
