using NUnit.Framework;

public sealed class SkillTooltipFormatterTests
{
    private sealed class Source
    {
        public float cooldownTime = 8f;
        public float healPercent = 0.15f;
        public Nested nested = new Nested { duration = 2.5f };
    }

    private sealed class Nested
    {
        public float duration;
    }

    [Test]
    public void Format_ReplacesFieldsPercentAndNestedPath()
    {
        bool ok = SkillTooltipFormatter.TryFormat(
            "재사용 {cooldownTime}초 / 회복 {healPercent:%} / {nested.duration:0.0}초",
            new Source(), default, false,
            out string text, out bool hasDamage, out string error);

        Assert.That(ok, Is.True, error);
        Assert.That(text, Is.EqualTo("재사용 8초 / 회복 15% / 2.5초"));
        Assert.That(hasDamage, Is.False);
    }

    [Test]
    public void Format_DamageSwitchesToCalculationWhenShiftHeld()
    {
        var damage = new SkillTooltipDamage(120, 360, 0.6f, 2.7f);

        Assert.That(SkillTooltipFormatter.TryFormat("피해 {dmg}", new Source(), damage, false,
            out string compact, out bool hasDamage, out string compactError), Is.True, compactError);
        Assert.That(SkillTooltipFormatter.TryFormat("피해 {dmg}", new Source(), damage, true,
            out string detailed, out _, out string detailedError), Is.True, detailedError);

        Assert.That(hasDamage, Is.True);
        Assert.That(compact, Does.Contain("120~360"));
        Assert.That(compact, Does.Not.Contain(" = (")); // <color=#…> 태그에도 '=' 가 있어 계산식 표기로 검사
        Assert.That(detailed, Does.Contain(" = ("));
        Assert.That(detailed, Does.Contain("120~360"));
        Assert.That(detailed, Does.Contain("60%~270%"));
    }

    [TestCase("값 {missing}", "문자 3")]
    [TestCase("값 {cooldownTime", "닫히지 않은")]
    [TestCase("<style=stun>기절", "닫히지 않은 <style>")]
    public void Validate_ReportsErrorPosition(string template, string expected)
    {
        bool ok = SkillTooltipFormatter.TryValidate(template, new Source(), out string error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain(expected));
    }

    [Test]
    public void Damage_LinearAndScaledSnapshotMatchGameplayRoundingOrder()
    {
        SkillTooltipDamage linear = SkillTooltipDamage.FromLinear(130f, 1.8f, 10);
        SkillTooltipDamage scaled = SkillTooltipDamage.FromScaledSnapshot(130f, 1f, 10, 0.5f, 2.25f);

        Assert.That(linear.Minimum, Is.EqualTo(244));
        Assert.That(linear.Maximum, Is.EqualTo(244));
        Assert.That(scaled.Minimum, Is.EqualTo(70));
        Assert.That(scaled.Maximum, Is.EqualTo(315));
    }
}
