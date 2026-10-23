using NUnit.Framework;

/// <summary>쿨 시계 적분(할 일 91 T2 · PLAN-91 §3.3) — Now += Δ원천 × 배율.</summary>
public sealed class CooldownClockTests
{
    [Test]
    public void FirstSample_OnlySetsBaseline()
    {
        var clock = new CooldownClock();
        clock.AdvanceTo(100.0, 1f);
        Assert.That(clock.Now, Is.Zero, "첫 표본 이전 시간은 쌓지 않는다");

        clock.AdvanceTo(101.5, 1f);
        Assert.That(clock.Now, Is.EqualTo(1.5).Within(1e-9));
    }

    [Test]
    public void RateOne_FollowsSourceExactly()
    {
        var clock = new CooldownClock();
        clock.AdvanceTo(0.0, 1f);
        for (int i = 1; i <= 60; i++)
            clock.AdvanceTo(i / 60.0, 1f);

        Assert.That(clock.Now, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void RateChangeMidway_OnlyAffectsLaterInterval()
    {
        var clock = new CooldownClock();
        clock.AdvanceTo(0.0, 1f);
        clock.AdvanceTo(2.0, 1f);  // 2초 × 1
        clock.AdvanceTo(5.0, 2f);  // 3초 × 2
        clock.AdvanceTo(6.0, 1f);  // 1초 × 1

        Assert.That(clock.Now, Is.EqualTo(2.0 + 6.0 + 1.0).Within(1e-9));
    }

    [Test]
    public void RateZeroOrInvalid_Freezes_AndDoesNotCatchUpLater()
    {
        var clock = new CooldownClock();
        clock.AdvanceTo(0.0, 1f);
        clock.AdvanceTo(1.0, 1f);
        clock.AdvanceTo(4.0, 0f);
        clock.AdvanceTo(5.0, float.NaN);
        clock.AdvanceTo(6.0, -1f);
        Assert.That(clock.Now, Is.EqualTo(1.0).Within(1e-9), "배율 0·NaN·음수 구간은 멈춘다");

        clock.AdvanceTo(7.0, 1f);
        Assert.That(clock.Now, Is.EqualTo(2.0).Within(1e-9), "멈춘 구간을 나중에 몰아 쌓지 않는다");
    }

    [Test]
    public void SlowMotionDelta_IsScaledBySource()
    {
        // 슬로우 모션 = 원천(Time.deltaTime 합)이 느리게 흐른다. 실시간 1초 동안 원천 0.25초.
        var clock = new CooldownClock();
        clock.AdvanceTo(10.0, 1f);
        for (int i = 1; i <= 60; i++)
            clock.AdvanceTo(10.0 + i * (0.25 / 60.0), 2f);

        Assert.That(clock.Now, Is.EqualTo(0.5).Within(1e-9), "원천 0.25초 × 배율 2");
    }

    [Test]
    public void SourceGoingBackwards_Rebases()
    {
        var clock = new CooldownClock();
        clock.AdvanceTo(50.0, 1f);
        clock.AdvanceTo(52.0, 1f);
        clock.AdvanceTo(1.0, 1f);   // 원천 재시작
        Assert.That(clock.Now, Is.EqualTo(2.0).Within(1e-9));

        clock.AdvanceTo(1.5, 1f);
        Assert.That(clock.Now, Is.EqualTo(2.5).Within(1e-9));
    }

    [Test]
    public void Rebase_SwitchesSourceWithoutJump()
    {
        var clock = new CooldownClock();
        clock.AdvanceTo(3.0, 1f);
        clock.AdvanceTo(4.0, 1f);
        clock.Rebase(1000.0);       // 원천 교체(다른 원점)
        clock.AdvanceTo(1000.5, 1f);

        Assert.That(clock.Now, Is.EqualTo(1.5).Within(1e-9));
    }

    [Test]
    public void SkillLedgerOnClock_CooldownEndsFasterWithRateTwo()
    {
        // 장부는 시계 시각만 본다 — 배율 2 구간에서는 실시간 절반에 끝난다.
        var clock = new CooldownClock();
        var ledger = new PlayerSkillCooldownLedger(1);
        clock.AdvanceTo(0.0, 1f);
        ledger.Start(0, (float)clock.Now, 10f);

        clock.AdvanceTo(2.0, 1f);
        Assert.That(ledger.GetRemaining(0, (float)clock.Now), Is.EqualTo(8f).Within(1e-5f));

        clock.AdvanceTo(6.0, 2f);
        Assert.That(ledger.IsReady(0, (float)clock.Now), Is.True, "2 + 4×2 = 10");

        // 서버가 보낸 남은 초는 받는 쪽 시계 기준으로 그대로 적는다(SetRemaining).
        ledger.SetRemaining(0, (float)clock.Now, 3f);
        clock.AdvanceTo(7.0, 2f);
        Assert.That(ledger.GetRemaining(0, (float)clock.Now), Is.EqualTo(1f).Within(1e-5f));
    }
}
