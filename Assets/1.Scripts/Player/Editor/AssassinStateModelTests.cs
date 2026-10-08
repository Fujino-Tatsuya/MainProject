using NUnit.Framework;

public sealed class AssassinStateModelTests
{
    private static readonly AssassinStateRules Rules = AssassinStateRules.Default;

    private static AssassinStateSnapshot WithStacks(int stacks)
    {
        var state = default(AssassinStateSnapshot);
        for (int i = 0; i < stacks; i++)
            AssassinStateModel.TryGainStack(ref state, Rules);
        return state;
    }

    private static AssassinStateSnapshot Transformed(int stacks, double now)
    {
        AssassinStateSnapshot state = WithStacks(stacks);
        Assert.That(AssassinStateModel.TryBeginTransform(ref state, now, Rules, out _), Is.True);
        return state;
    }

    [Test]
    public void StacksCapAtMaximum()
    {
        AssassinStateSnapshot state = WithStacks(4);

        Assert.That(AssassinStateModel.TryGainStack(ref state, Rules), Is.False);
        Assert.That(state.stacks, Is.EqualTo(4));
    }

    [Test]
    public void NoStackGainWhileTransformed()
    {
        AssassinStateSnapshot state = Transformed(1, 0.0);

        Assert.That(AssassinStateModel.TryGainStack(ref state, Rules), Is.False);
        Assert.That(state.stacks, Is.EqualTo(0));
    }

    [Test]
    public void CannotTransformWithZeroStacks()
    {
        var state = default(AssassinStateSnapshot);

        Assert.That(AssassinStateModel.CanBeginTransform(state), Is.False);
        Assert.That(AssassinStateModel.TryBeginTransform(ref state, 0.0, Rules, out _), Is.False);
        Assert.That(state.transformed, Is.False);
    }

    [TestCase(1, 4f)]
    [TestCase(2, 6f)]
    [TestCase(3, 8f)]
    [TestCase(4, 10f)]
    public void TransformConsumesAllStacksAndUsesDurationTable(int stacks, float expectedDuration)
    {
        AssassinStateSnapshot state = Transformed(stacks, 100.0);

        Assert.That(state.stacks, Is.EqualTo(0));
        Assert.That(state.transformed, Is.True);
        Assert.That(state.transformEndTime - state.transformStartTime, Is.EqualTo(expectedDuration).Within(0.0001));
        Assert.That(AssassinStateModel.Remaining(state, 100.0), Is.EqualTo(expectedDuration).Within(0.0001f));
    }

    [Test]
    public void TransformRemovesPreparedEnhancement()
    {
        AssassinStateSnapshot state = WithStacks(2);
        AssassinStateModel.TryPrepareEnhancement(ref state);

        AssassinStateModel.TryBeginTransform(ref state, 0.0, Rules, out bool removed);

        Assert.That(removed, Is.True);
        Assert.That(state.enhancedReady, Is.False);
    }

    [Test]
    public void TransformWithoutEnhancementReportsNothingRemoved()
    {
        AssassinStateSnapshot state = WithStacks(1);

        AssassinStateModel.TryBeginTransform(ref state, 0.0, Rules, out bool removed);

        Assert.That(removed, Is.False);
    }

    [Test]
    public void CannotTransformAgainWhileTransformed()
    {
        AssassinStateSnapshot state = Transformed(1, 0.0);
        state.stacks = 2;

        Assert.That(AssassinStateModel.CanBeginTransform(state), Is.False);
    }

    [Test]
    public void EnhancementCannotStackOrBeUsedWhileTransformed()
    {
        var state = default(AssassinStateSnapshot);

        Assert.That(AssassinStateModel.TryPrepareEnhancement(ref state), Is.True);
        Assert.That(AssassinStateModel.TryPrepareEnhancement(ref state), Is.False);

        AssassinStateSnapshot transformed = Transformed(1, 0.0);
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

    [Test]
    public void ReleaseIgnoredBeforeLockAndNotReserved()
    {
        AssassinStateSnapshot state = Transformed(4, 10.0);

        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 11.99, Rules), Is.False);
        Assert.That(state.releaseRequested, Is.False);
        Assert.That(AssassinStateModel.IsEndPending(state, 12.5), Is.False);
    }

    [Test]
    public void ReleaseAcceptedAtLockBoundary()
    {
        AssassinStateSnapshot state = Transformed(4, 10.0);

        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 12.0, Rules), Is.True);
        Assert.That(AssassinStateModel.IsEndPending(state, 12.0), Is.True);
        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 13.0, Rules), Is.False);
    }

    [Test]
    public void ExpiryMakesEndPending()
    {
        AssassinStateSnapshot state = Transformed(1, 0.0);

        Assert.That(AssassinStateModel.IsEndPending(state, 3.99), Is.False);
        Assert.That(AssassinStateModel.IsEndPending(state, 4.0), Is.True);
    }

    [Test]
    public void EndWaitsForCurrentAction()
    {
        AssassinStateSnapshot state = Transformed(1, 0.0);

        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 5.0, actionInProgress: true), Is.False);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 5.0, actionInProgress: false), Is.True);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 1.0, actionInProgress: false), Is.False);
    }

    [Test]
    public void FinishReturnsToNormalWithoutRefundingStacks()
    {
        AssassinStateSnapshot state = Transformed(3, 0.0);
        AssassinStateModel.TryRequestRelease(ref state, 2.5, Rules);

        Assert.That(AssassinStateModel.TryFinishTransform(ref state), Is.True);
        Assert.That(state.transformed, Is.False);
        Assert.That(state.releaseRequested, Is.False);
        Assert.That(state.stacks, Is.EqualTo(0));
        Assert.That(AssassinStateModel.TryFinishTransform(ref state), Is.False);
    }

    [Test]
    public void DownResetsImmediatelyEvenWhileWaitingForAttack()
    {
        AssassinStateSnapshot state = Transformed(2, 0.0);
        AssassinStateModel.TryRequestRelease(ref state, 3.0, Rules);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 3.0, actionInProgress: true), Is.False);

        AssassinDownResult result = AssassinStateModel.ResetForDown(ref state);

        Assert.That(result.WasTransformed, Is.True);
        Assert.That(result.HadEnhancement, Is.False);
        Assert.That(state.transformed, Is.False);
        Assert.That(state.releaseRequested, Is.False);
    }

    [Test]
    public void DownClearsStacksAndEnhancement()
    {
        AssassinStateSnapshot state = WithStacks(3);
        AssassinStateModel.TryPrepareEnhancement(ref state);

        AssassinDownResult result = AssassinStateModel.ResetForDown(ref state);

        Assert.That(result.HadEnhancement, Is.True);
        Assert.That(result.WasTransformed, Is.False);
        Assert.That(state.stacks, Is.EqualTo(0));
        Assert.That(state.enhancedReady, Is.False);
    }

    [Test]
    public void DownWithNothingActiveCreatesNoCooldowns()
    {
        AssassinStateSnapshot state = WithStacks(1);

        AssassinDownResult result = AssassinStateModel.ResetForDown(ref state);

        Assert.That(result.HadEnhancement, Is.False);
        Assert.That(result.WasTransformed, Is.False);
    }

    [Test]
    public void DurationTableClampsBeyondLastEntry()
    {
        var rules = new AssassinStateRules(6, new[] { 4f, 6f }, 2f);

        Assert.That(rules.DurationFor(5), Is.EqualTo(6f));
        Assert.That(rules.DurationFor(0), Is.EqualTo(0f));
    }
}
