using NUnit.Framework;

/// <summary>머리 위 특성 게이지 판정 고정 — 암살자 최소 변신량·변신, 거너 단계·과열·깜빡임, 가붕이 쿨 진행률·충전.</summary>
public sealed class OverheadTraitGaugeRulesTests
{
    [Test]
    public void Normalize_ClampsAndGuardsZeroMax()
    {
        Assert.That(OverheadTraitGaugeRules.Normalize(50f, 100f), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(OverheadTraitGaugeRules.Normalize(150f, 100f), Is.EqualTo(1f));
        Assert.That(OverheadTraitGaugeRules.Normalize(-5f, 100f), Is.EqualTo(0f));
        Assert.That(OverheadTraitGaugeRules.Normalize(10f, 0f), Is.EqualTo(0f));
    }

    [TestCase(0f, AssassinRageTone.Building)]
    [TestCase(49.9f, AssassinRageTone.Building)]
    [TestCase(50f, AssassinRageTone.Ready)]
    [TestCase(100f, AssassinRageTone.Ready)]
    public void AssassinTone_SplitsAtMinTransformRage(float rage, AssassinRageTone expected)
    {
        Assert.That(OverheadTraitGaugeRules.AssassinTone(rage, 50f, false), Is.EqualTo(expected));
    }

    [TestCase(0f)]
    [TestCase(30f)]
    [TestCase(100f)]
    public void AssassinTone_TransformedWinsRegardlessOfRage(float rage)
    {
        Assert.That(OverheadTraitGaugeRules.AssassinTone(rage, 50f, true), Is.EqualTo(AssassinRageTone.Transformed));
    }

    [TestCase(0, false, GunnerHeatTone.Cool)]
    [TestCase(1, false, GunnerHeatTone.Warm)]
    [TestCase(2, false, GunnerHeatTone.Warm)]
    [TestCase(3, false, GunnerHeatTone.Hot)]
    [TestCase(0, true, GunnerHeatTone.Hot)]
    [TestCase(3, true, GunnerHeatTone.Hot)]
    public void GunnerTone_ByStageAndOverheat(int stage, bool overheated, GunnerHeatTone expected)
    {
        Assert.That(OverheadTraitGaugeRules.GunnerTone(stage, overheated), Is.EqualTo(expected));
    }

    [Test]
    public void IsBlinkOn_OnlyFirstHalfOfPeriodWhileOverheated()
    {
        Assert.That(OverheadTraitGaugeRules.IsBlinkOn(true, 0.1f, 0.4f), Is.True);
        Assert.That(OverheadTraitGaugeRules.IsBlinkOn(true, 0.3f, 0.4f), Is.False);
        Assert.That(OverheadTraitGaugeRules.IsBlinkOn(true, 0.5f, 0.4f), Is.True, "다음 주기 앞 절반");
        Assert.That(OverheadTraitGaugeRules.IsBlinkOn(false, 0.1f, 0.4f), Is.False);
        Assert.That(OverheadTraitGaugeRules.IsBlinkOn(true, 0.1f, 0f), Is.False);
    }

    [Test]
    public void PassiveTone_ChargedIsCharged()
    {
        Assert.That(OverheadTraitGaugeRules.PassiveTone(true), Is.EqualTo(FirstMeleePassiveTone.Charged));
        Assert.That(OverheadTraitGaugeRules.PassiveTone(false), Is.EqualTo(FirstMeleePassiveTone.Charging));
    }

    [Test]
    public void PassiveFill_ChargedIsFull()
    {
        Assert.That(OverheadTraitGaugeRules.FirstMeleePassiveFill(true, 5f, 6f), Is.EqualTo(1f));
    }

    [TestCase(6f, 0f)]
    [TestCase(3f, 0.5f)]
    [TestCase(0f, 1f)]
    [TestCase(10f, 0f)]
    public void PassiveFill_IsCooldownProgress(float remaining, float expected)
    {
        Assert.That(OverheadTraitGaugeRules.FirstMeleePassiveFill(false, remaining, 6f), Is.EqualTo(expected).Within(1e-5f));
    }

    [Test]
    public void PassiveFill_ZeroCooldownIsFull()
    {
        Assert.That(OverheadTraitGaugeRules.FirstMeleePassiveFill(false, 0f, 0f), Is.EqualTo(1f));
    }
}
