using NUnit.Framework;

public sealed class AssassinStateModelTests
{
    // 최대 100 · 최소 변신량 40 · 충전 기본 3 / Q 5 / 강타 15 · 변신 중 10/초 · 해제 제한 2초
    private static readonly AssassinStateRules Rules = AssassinStateRules.Default;

    private static AssassinStateSnapshot WithRage(float rage) => new AssassinStateSnapshot { rage = rage };

    private static AssassinStateSnapshot Transformed(float rage, double now)
    {
        AssassinStateSnapshot state = WithRage(rage);
        Assert.That(AssassinStateModel.TryBeginTransform(ref state, now, Rules, out _), Is.True);
        return state;
    }

    // ── 충전 ──

    [TestCase(AssassinRageSource.BasicAttack, 3f)]
    [TestCase(AssassinRageSource.DashStrike, 5f)]
    [TestCase(AssassinRageSource.EnhancedStrike, 15f)]
    public void GainAddsAmountPerSource(AssassinRageSource source, float expected)
    {
        var state = default(AssassinStateSnapshot);

        Assert.That(AssassinStateModel.TryGainRage(ref state, source, Rules), Is.True);
        Assert.That(state.rage, Is.EqualTo(expected));
    }

    [Test]
    public void GainBeyondMaximumIsDiscarded()
    {
        AssassinStateSnapshot state = WithRage(95f);

        Assert.That(AssassinStateModel.TryGainRage(ref state, AssassinRageSource.EnhancedStrike, Rules), Is.True);
        Assert.That(state.rage, Is.EqualTo(100f));

        Assert.That(AssassinStateModel.TryGainRage(ref state, AssassinRageSource.BasicAttack, Rules), Is.False);
        Assert.That(state.rage, Is.EqualTo(100f));
    }

    [Test]
    public void NoGainWhileTransformed()
    {
        AssassinStateSnapshot state = Transformed(40f, 0.0);
        float before = state.rage;

        Assert.That(AssassinStateModel.TryGainRage(ref state, AssassinRageSource.EnhancedStrike, Rules), Is.False);
        Assert.That(state.rage, Is.EqualTo(before));
        Assert.That(AssassinStateModel.Rage(state, 1.0, Rules), Is.EqualTo(30f).Within(0.0001f));
    }

    [Test]
    public void NoIdleDecayInNormalState()
    {
        AssassinStateSnapshot state = WithRage(25f);

        Assert.That(AssassinStateModel.Rage(state, 1000.0, Rules), Is.EqualTo(25f));
    }

    [Test]
    public void ThirteenBasicHitsPlusOneReachMinimumWithoutFloatDrift()
    {
        var state = default(AssassinStateSnapshot);
        for (int i = 0; i < 13; i++)
            AssassinStateModel.TryGainRage(ref state, AssassinRageSource.BasicAttack, Rules);

        Assert.That(AssassinStateModel.CanBeginTransform(state, Rules), Is.False); // 39
        AssassinStateModel.TryGainRage(ref state, AssassinRageSource.BasicAttack, Rules);
        Assert.That(AssassinStateModel.CanBeginTransform(state, Rules), Is.True); // 42
    }

    // ── 변신 시작 ──

    [Test]
    public void CannotTransformBelowMinimum()
    {
        AssassinStateSnapshot state = WithRage(39.9f);

        Assert.That(AssassinStateModel.CanBeginTransform(state, Rules), Is.False);
        Assert.That(AssassinStateModel.TryBeginTransform(ref state, 0.0, Rules, out _), Is.False);
        Assert.That(state.transformed, Is.False);
        Assert.That(state.rage, Is.EqualTo(39.9f));
    }

    [Test]
    public void CannotTransformWithZeroGaugeEvenIfMinimumIsZero()
    {
        var rules = new AssassinStateRules(100f, 0f, 3f, 5f, 15f, 10f, 2f);
        var state = default(AssassinStateSnapshot);

        Assert.That(AssassinStateModel.CanBeginTransform(state, rules), Is.False);
    }

    [TestCase(40f, 4f)]
    [TestCase(73f, 7.3f)]
    [TestCase(100f, 10f)]
    public void TransformUsesWholeGaugeAsFuel(float rage, float expectedDuration)
    {
        AssassinStateSnapshot state = Transformed(rage, 100.0);

        Assert.That(state.transformed, Is.True);
        Assert.That(AssassinStateModel.Rage(state, 100.0, Rules), Is.EqualTo(rage).Within(0.0001f));
        Assert.That(state.transformEndTime - state.transformStartTime, Is.EqualTo(expectedDuration).Within(0.0001));
        Assert.That(AssassinStateModel.Remaining(state, 100.0), Is.EqualTo(expectedDuration).Within(0.0001f));
    }

    [Test]
    public void TransformRemovesPreparedEnhancement()
    {
        AssassinStateSnapshot state = WithRage(50f);
        AssassinStateModel.TryPrepareEnhancement(ref state);

        AssassinStateModel.TryBeginTransform(ref state, 0.0, Rules, out bool removed);

        Assert.That(removed, Is.True);
        Assert.That(state.enhancedReady, Is.False);
    }

    [Test]
    public void TransformWithoutEnhancementReportsNothingRemoved()
    {
        AssassinStateSnapshot state = WithRage(40f);

        AssassinStateModel.TryBeginTransform(ref state, 0.0, Rules, out bool removed);

        Assert.That(removed, Is.False);
    }

    [Test]
    public void CannotTransformAgainWhileTransformed()
    {
        AssassinStateSnapshot state = Transformed(100f, 0.0);

        Assert.That(AssassinStateModel.CanBeginTransform(state, Rules), Is.False);
    }

    // ── 감소·만료 ──

    [Test]
    public void GaugeDecaysPerSecondWhileTransformedAndStopsAtZero()
    {
        AssassinStateSnapshot state = Transformed(50f, 10.0);

        Assert.That(AssassinStateModel.Rage(state, 12.5, Rules), Is.EqualTo(25f).Within(0.0001f));
        Assert.That(AssassinStateModel.Rage(state, 15.0, Rules), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(AssassinStateModel.Rage(state, 30.0, Rules), Is.EqualTo(0f));
    }

    [Test]
    public void ZeroGaugeMakesEndPending()
    {
        AssassinStateSnapshot state = Transformed(40f, 0.0);

        Assert.That(AssassinStateModel.IsEndPending(state, 3.99), Is.False);
        Assert.That(AssassinStateModel.IsEndPending(state, 4.0), Is.True);
        Assert.That(AssassinStateModel.Rage(state, 4.0, Rules), Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void EndWaitsForCurrentAction()
    {
        AssassinStateSnapshot state = Transformed(40f, 0.0);

        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 5.0, actionInProgress: true), Is.False);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 5.0, actionInProgress: false), Is.True);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 1.0, actionInProgress: false), Is.False);
    }

    [Test]
    public void ExpiryFinishLeavesZeroGauge()
    {
        AssassinStateSnapshot state = Transformed(40f, 0.0);

        Assert.That(AssassinStateModel.TryFinishTransform(ref state, 5.0, Rules), Is.True);
        Assert.That(state.transformed, Is.False);
        Assert.That(state.rage, Is.EqualTo(0f));
        Assert.That(AssassinStateModel.CanBeginTransform(state, Rules), Is.False);
    }

    // ── 수동 해제 ──

    [Test]
    public void ReleaseIgnoredBeforeLockAndNotReserved()
    {
        AssassinStateSnapshot state = Transformed(100f, 10.0);

        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 11.99, Rules), Is.False);
        Assert.That(state.releaseRequested, Is.False);
        Assert.That(AssassinStateModel.IsEndPending(state, 12.5), Is.False);
    }

    [Test]
    public void ReleaseAcceptedAtLockBoundary()
    {
        AssassinStateSnapshot state = Transformed(100f, 10.0);

        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 12.0, Rules), Is.True);
        Assert.That(AssassinStateModel.IsEndPending(state, 12.0), Is.True);
        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 13.0, Rules), Is.False);
    }

    [Test]
    public void ManualReleasePreservesRemainingGaugeAndKeepsDecayingWhilePending()
    {
        AssassinStateSnapshot state = Transformed(100f, 0.0);
        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 3.0, Rules), Is.True);

        // 현재 공격 완료(1.5초 뒤)까지 기다리는 동안에도 감소한다 — 100 − 4.5 × 10 = 55.
        Assert.That(AssassinStateModel.Rage(state, 4.5, Rules), Is.EqualTo(55f).Within(0.0001f));
        Assert.That(AssassinStateModel.TryFinishTransform(ref state, 4.5, Rules), Is.True);

        Assert.That(state.transformed, Is.False);
        Assert.That(state.releaseRequested, Is.False);
        Assert.That(state.rage, Is.EqualTo(55f).Within(0.0001f));
        // 일반 상태로 돌아오면 더 줄지 않는다. 최소량 이상이라 R 쿨만 끝나면 다시 변신할 수 있다.
        Assert.That(AssassinStateModel.Rage(state, 100.0, Rules), Is.EqualTo(55f).Within(0.0001f));
        Assert.That(AssassinStateModel.CanBeginTransform(state, Rules), Is.True);
        Assert.That(AssassinStateModel.TryFinishTransform(ref state, 5.0, Rules), Is.False);
    }

    [Test]
    public void ManualReleaseWithLittleLeftCannotRetransform()
    {
        AssassinStateSnapshot state = Transformed(50f, 0.0);
        AssassinStateModel.TryRequestRelease(ref state, 2.0, Rules);
        AssassinStateModel.TryFinishTransform(ref state, 2.0, Rules);

        Assert.That(state.rage, Is.EqualTo(30f).Within(0.0001f));
        Assert.That(AssassinStateModel.CanBeginTransform(state, Rules), Is.False);
        Assert.That(AssassinStateModel.TryGainRage(ref state, AssassinRageSource.BasicAttack, Rules), Is.True);
    }

    // ── 강화 ──

    [Test]
    public void EnhancementCannotStackOrBeUsedWhileTransformed()
    {
        var state = default(AssassinStateSnapshot);

        Assert.That(AssassinStateModel.TryPrepareEnhancement(ref state), Is.True);
        Assert.That(AssassinStateModel.TryPrepareEnhancement(ref state), Is.False);

        AssassinStateSnapshot transformed = Transformed(40f, 0.0);
        Assert.That(AssassinStateModel.CanPrepareEnhancement(transformed), Is.False);
    }

    [Test]
    public void ConsumeEnhancementOnlyWhenReady()
    {
        var state = default(AssassinStateSnapshot);
        Assert.That(AssassinStateModel.TryConsumeEnhancement(ref state), Is.False);

        AssassinStateModel.TryPrepareEnhancement(ref state);
        Assert.That(AssassinStateModel.TryConsumeEnhancement(ref state), Is.True);
        Assert.That(state.enhancedReady, Is.False);
    }

    // ── 쓰러짐·사망 ──

    [Test]
    public void DownResetsImmediatelyEvenWhileWaitingForAttack()
    {
        AssassinStateSnapshot state = Transformed(80f, 0.0);
        AssassinStateModel.TryRequestRelease(ref state, 3.0, Rules);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 3.0, actionInProgress: true), Is.False);

        AssassinDownResult result = AssassinStateModel.ResetForDown(ref state);

        Assert.That(result.WasTransformed, Is.True);
        Assert.That(result.HadEnhancement, Is.False);
        Assert.That(state.transformed, Is.False);
        Assert.That(state.releaseRequested, Is.False);
        Assert.That(state.rage, Is.EqualTo(0f));
    }

    [Test]
    public void DownClearsGaugeAndEnhancement()
    {
        AssassinStateSnapshot state = WithRage(70f);
        AssassinStateModel.TryPrepareEnhancement(ref state);

        AssassinDownResult result = AssassinStateModel.ResetForDown(ref state);

        Assert.That(result.HadEnhancement, Is.True);
        Assert.That(result.WasTransformed, Is.False);
        Assert.That(state.rage, Is.EqualTo(0f));
        Assert.That(state.enhancedReady, Is.False);
    }

    [Test]
    public void DownWithNothingActiveCreatesNoCooldowns()
    {
        AssassinStateSnapshot state = WithRage(10f);

        AssassinDownResult result = AssassinStateModel.ResetForDown(ref state);

        Assert.That(result.HadEnhancement, Is.False);
        Assert.That(result.WasTransformed, Is.False);
    }

    // ── 규칙 ──

    [Test]
    public void RulesClampMinimumToMaximumAndDecayAboveZero()
    {
        var rules = new AssassinStateRules(50f, 80f, 3f, 5f, 15f, 0f, 2f);

        Assert.That(rules.MinTransformRage, Is.EqualTo(50f));
        Assert.That(rules.TransformDecayPerSecond, Is.GreaterThan(0f));
        Assert.That(rules.DurationFor(0f), Is.EqualTo(0f));
    }

    [Test]
    public void DashLedgerGrantsRageOncePerUse()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryClaimRageReward(landedRewardTarget: false), Is.False);
        Assert.That(ledger.TryClaimRageReward(landedRewardTarget: true), Is.True);
        Assert.That(ledger.TryClaimRageReward(landedRewardTarget: true), Is.False);
        // 쿨 차감 장부와 별개다.
        Assert.That(ledger.TryClaimCooldownReward(landedRewardTarget: true), Is.True);

        ledger.Begin();
        Assert.That(ledger.TryClaimRageReward(landedRewardTarget: true), Is.True);
    }
}
