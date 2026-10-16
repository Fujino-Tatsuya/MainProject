using NUnit.Framework;

/// <summary>머리 위 이름 → 상태이상 이름 판정 고정(할 일 50) — 대상 CC 6종 + 붙잡힘, 우선순위 하나만, 버프 무시, 사망·Soul 은 이름.</summary>
public sealed class OverheadStatusLabelRulesTests
{
    static OverheadStatusLabel SelectAlive(StatusEffectType active, bool restrained = false)
        => OverheadStatusLabelRules.Select(active, restrained, true, PlayerLifeState.Alive);

    [Test]
    public void NoEffect_ShowsName()
    {
        Assert.That(SelectAlive(StatusEffectType.None), Is.EqualTo(OverheadStatusLabel.None));
    }

    [TestCase(StatusEffectType.Airborne, OverheadStatusLabel.Airborne)]
    [TestCase(StatusEffectType.Stunned, OverheadStatusLabel.Stunned)]
    [TestCase(StatusEffectType.Rooted, OverheadStatusLabel.Rooted)]
    [TestCase(StatusEffectType.Silenced, OverheadStatusLabel.Silenced)]
    [TestCase(StatusEffectType.Debilitated, OverheadStatusLabel.Debilitated)]
    [TestCase(StatusEffectType.Slowed, OverheadStatusLabel.Slowed)]
    public void SingleCrowdControl_ShowsIt(StatusEffectType active, OverheadStatusLabel expected)
    {
        Assert.That(SelectAlive(active), Is.EqualTo(expected));
        Assert.That(OverheadStatusLabelRules.ToStatusEffect(expected), Is.EqualTo(active));
    }

    [Test]
    public void Restrained_ShowsGrabbed()
    {
        Assert.That(SelectAlive(StatusEffectType.None, restrained: true), Is.EqualTo(OverheadStatusLabel.Grabbed));
        Assert.That(OverheadStatusLabelRules.ToStatusEffect(OverheadStatusLabel.Grabbed), Is.EqualTo(StatusEffectType.None));
    }

    [Test]
    public void Overlap_PicksHighestPriority()
    {
        Assert.That(SelectAlive(StatusEffectType.Airborne | StatusEffectType.Stunned), Is.EqualTo(OverheadStatusLabel.Airborne));
        Assert.That(SelectAlive(StatusEffectType.Silenced | StatusEffectType.Rooted), Is.EqualTo(OverheadStatusLabel.Rooted));
        Assert.That(SelectAlive(StatusEffectType.Stunned, restrained: true), Is.EqualTo(OverheadStatusLabel.Grabbed));
        Assert.That(SelectAlive(StatusEffectType.Slowed | StatusEffectType.Debilitated), Is.EqualTo(OverheadStatusLabel.Debilitated));
        // 버프가 섞여도 CC 는 그대로 고른다.
        Assert.That(SelectAlive(StatusEffectType.SuperArmor | StatusEffectType.Slowed), Is.EqualTo(OverheadStatusLabel.Slowed));
    }

    [Test]
    public void BuffsAndStatModifiersOnly_ShowsName()
    {
        const StatusEffectType nonCc =
            StatusEffectType.SuperArmor | StatusEffectType.MoveSpeedModifier | StatusEffectType.AttackDamageModifier |
            StatusEffectType.AttackSpeedModifier | StatusEffectType.DefenseModifier | StatusEffectType.MaxHpModifier |
            StatusEffectType.PassiveCharge | StatusEffectType.Focus;

        Assert.That(SelectAlive(nonCc), Is.EqualTo(OverheadStatusLabel.None));
    }

    [TestCase(PlayerLifeState.DeadPresentation)]
    [TestCase(PlayerLifeState.Soul)]
    [TestCase(PlayerLifeState.PermanentDead)]
    public void NotAlive_ShowsName_EvenIfGrabbed(PlayerLifeState state)
    {
        Assert.That(OverheadStatusLabelRules.Select(StatusEffectType.Stunned, true, true, state),
            Is.EqualTo(OverheadStatusLabel.None));
    }

    [Test]
    public void WithoutLifeCycle_TreatsAsAlive()
    {
        Assert.That(OverheadStatusLabelRules.Select(StatusEffectType.Stunned, false, false, PlayerLifeState.Soul),
            Is.EqualTo(OverheadStatusLabel.Stunned));
    }

    [Test]
    public void PrioritySet_MatchesBlockedCrowdControl()
    {
        StatusEffectType union = StatusEffectType.None;
        foreach (StatusEffectType type in OverheadStatusLabelRules.CrowdControlPriority)
        {
            Assert.That(union & type, Is.EqualTo(StatusEffectType.None), $"{type} 가 우선순위에 두 번 있다");
            union |= type;
        }

        Assert.That(union, Is.EqualTo(StatusEffectImmunityPolicy.BlockedCrowdControl));
    }
}
