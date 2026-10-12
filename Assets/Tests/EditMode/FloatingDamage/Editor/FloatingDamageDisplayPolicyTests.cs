using NUnit.Framework;

public sealed class FloatingDamageDisplayPolicyTests
{
    const float TeammateScale = 0.75f;
    const float TeammateAlpha = 200f / 255f;

    [TestCase(FloatingDamageDisplayFilter.AllDamage, true, true, 1f, 1f)]
    [TestCase(FloatingDamageDisplayFilter.AllDamage, false, true, 1f, 1f)]
    [TestCase(FloatingDamageDisplayFilter.OwnDealtOnly, true, true, 1f, 1f)]
    [TestCase(FloatingDamageDisplayFilter.OwnDealtOnly, false, false, 1f, 1f)]
    [TestCase(FloatingDamageDisplayFilter.AllWithOwnEmphasis, true, true, 1f, 1f)]
    [TestCase(FloatingDamageDisplayFilter.AllWithOwnEmphasis, false, true, TeammateScale, TeammateAlpha)]
    public void Evaluate_ReturnsExpectedDisplayAndStyle(
        FloatingDamageDisplayFilter filter,
        bool fromLocalPlayer,
        bool expectedDisplay,
        float expectedScale,
        float expectedAlpha)
    {
        FloatingDamageDisplayDecision decision = FloatingDamageDisplayPolicy.Evaluate(
            filter, fromLocalPlayer, TeammateScale, TeammateAlpha);

        Assert.That(decision.shouldDisplay, Is.EqualTo(expectedDisplay));
        Assert.That(decision.scaleMultiplier, Is.EqualTo(expectedScale).Within(0.0001f));
        Assert.That(decision.alphaMultiplier, Is.EqualTo(expectedAlpha).Within(0.0001f));
    }
}
