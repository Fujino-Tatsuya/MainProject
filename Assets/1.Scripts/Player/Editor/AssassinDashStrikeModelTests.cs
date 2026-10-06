using NUnit.Framework;

public sealed class AssassinDashStrikeModelTests
{
    // ── 경로 진행 ──

    [Test]
    public void TraveledGrowsWithElapsedTimeAtDashSpeed()
    {
        Assert.That(AssassinDashStrikeRules.TraveledAt(0.1f, 20f, 4f), Is.EqualTo(2f).Within(1e-5f));
    }

    [Test]
    public void TraveledStopsAtCap()
    {
        Assert.That(AssassinDashStrikeRules.TraveledAt(1f, 20f, 4f), Is.EqualTo(4f));
        Assert.That(AssassinDashStrikeRules.TraveledAt(1f, 20f, 1.3f), Is.EqualTo(1.3f));
    }

    [Test]
    public void TraveledIsZeroBeforeStartOrWithoutSpeed()
    {
        Assert.That(AssassinDashStrikeRules.TraveledAt(-0.1f, 20f, 4f), Is.EqualTo(0f));
        Assert.That(AssassinDashStrikeRules.TraveledAt(0.5f, 0f, 4f), Is.EqualTo(0f));
    }

    // ── 오너 보고 거리 검증 ──

    [Test]
    public void ReportedDistanceWithinRangeIsKept()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(1.7f, 4f, out float validated), Is.True);
        Assert.That(validated, Is.EqualTo(1.7f));
    }

    [Test]
    public void ReportedDistanceIsClampedToMaxDistance()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(9f, 4f, out float validated), Is.True);
        Assert.That(validated, Is.EqualTo(4f));
    }

    [Test]
    public void NegativeReportedDistanceBecomesZero()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(-2f, 4f, out float validated), Is.True);
        Assert.That(validated, Is.EqualTo(0f));
    }

    [Test]
    public void NonFiniteReportedDistanceIsRejected()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(float.NaN, 4f, out _), Is.False);
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(float.PositiveInfinity, 4f, out _), Is.False);
    }

    // ── 대상당 1회 ──

    [Test]
    public void SameTargetIsDamagedOncePerUse()
    {
        var ledger = new AssassinDashHitLedger();
        var boss = new object();
        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(boss), Is.True);
        Assert.That(ledger.TryRegisterTarget(boss), Is.False);
        Assert.That(ledger.DamagedCount, Is.EqualTo(1));
    }

    [Test]
    public void DifferentTargetsAreEachDamagedOnce()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(new object()), Is.True);
        Assert.That(ledger.TryRegisterTarget(new object()), Is.True);
        Assert.That(ledger.DamagedCount, Is.EqualTo(2));
    }

    [Test]
    public void NextUseCanDamageSameTargetAgain()
    {
        var ledger = new AssassinDashHitLedger();
        var monster = new object();
        ledger.Begin();
        ledger.TryRegisterTarget(monster);

        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(monster), Is.True);
    }

    [Test]
    public void NullTargetIsIgnored()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(null), Is.False);
    }

    // ── 변신 Q 쿨 차감 1회 ──

    [Test]
    public void CooldownRewardIsClaimedOncePerUse()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryClaimCooldownReward(true), Is.True);
        Assert.That(ledger.TryClaimCooldownReward(true), Is.False);
    }

    [Test]
    public void CrateOnlyHitDoesNotConsumeCooldownReward()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryClaimCooldownReward(false), Is.False);
        Assert.That(ledger.CooldownRewardClaimed, Is.False);
        Assert.That(ledger.TryClaimCooldownReward(true), Is.True);
    }

    [Test]
    public void NextUseCanClaimCooldownRewardAgain()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();
        ledger.TryClaimCooldownReward(true);

        ledger.Begin();

        Assert.That(ledger.TryClaimCooldownReward(true), Is.True);
    }

    [Test]
    public void ReductionWithLedgerFloorsAtZero()
    {
        // §7.3 예: 남은 2.8초 → 1.3초, 하한 0 — 차감은 A2 장부가 하고 여기선 1회 규칙과 함께 확인한다.
        var cooldowns = new PlayerSkillCooldownLedger(1);
        cooldowns.Start(0, 0f, 3f);

        cooldowns.Reduce(0, 0.2f, 1.5f);
        Assert.That(cooldowns.GetRemaining(0, 0.2f), Is.EqualTo(1.3f).Within(1e-5f));

        cooldowns.Reduce(0, 0.2f, 1.5f);
        Assert.That(cooldowns.GetRemaining(0, 0.2f), Is.EqualTo(0f));
    }

    // 10-06 은희: 준비 0.1 → 돌진(5m/60m/s) → 종료 0.1. 벽 조기 종료로 상한이 줄면 그만큼 일찍 끝난다.
    [Test]
    public void Phases_PrepareDashEnd_FinishTime()
    {
        float full = 0.1f + 5f / 60f + 0.1f;
        Assert.That(AssassinDashStrikeRules.IsFinished(full - 0.01f, 0.1f, 5f, 60f, 0.1f), Is.False);
        Assert.That(AssassinDashStrikeRules.IsFinished(full + 0.001f, 0.1f, 5f, 60f, 0.1f), Is.True);

        float walled = 0.1f + 1.2f / 60f + 0.1f;
        Assert.That(AssassinDashStrikeRules.IsFinished(walled + 0.001f, 0.1f, 1.2f, 60f, 0.1f), Is.True);
    }
}
