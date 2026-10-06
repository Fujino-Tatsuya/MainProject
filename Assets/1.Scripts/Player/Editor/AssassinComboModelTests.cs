using NUnit.Framework;

public sealed class AssassinComboModelTests
{
    [Test]
    public void FirstAttackStartsAtStepZero()
    {
        var model = new AssassinComboModel(4, 0.8f);

        Assert.That(model.Begin(10f), Is.EqualTo(0));
    }

    [Test]
    public void CompletedStepsAdvanceAndWrapWithinWindow()
    {
        var model = new AssassinComboModel(4, 0.8f);

        for (int step = 0; step < 4; step++)
        {
            float start = step * 0.2f;
            Assert.That(model.Begin(start), Is.EqualTo(step));
            model.Complete(step, start + 0.1f);
        }

        Assert.That(model.Begin(0.8f), Is.EqualTo(0));
    }

    [Test]
    public void ReinputAfterWindowRestartsAtStepZero()
    {
        var model = new AssassinComboModel(4, 0.8f);
        model.Complete(1, 2f);

        Assert.That(model.Begin(2.81f), Is.EqualTo(0));
    }

    [Test]
    public void WindowBoundaryIsInclusive()
    {
        var model = new AssassinComboModel(4, 0.8f);
        model.Complete(2, 5f);

        Assert.That(model.Begin(5.8f), Is.EqualTo(3));
    }

    [Test]
    public void SkillResetReturnsToStepZero()
    {
        var model = new AssassinComboModel(4, 0.8f);
        model.Complete(0, 1f);

        model.Reset();

        Assert.That(model.Begin(1.1f), Is.EqualTo(0));
        Assert.That(model.HasCompletion, Is.False);
    }
}
