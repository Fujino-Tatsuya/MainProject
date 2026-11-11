using NUnit.Framework;

/// <summary>존 대시 벨트 켜고 끄기(할 일 257) — 패널 전부 활성이면 켜고, 건틀렛 봇 처치(G9) 뒤엔 영원히 끈다.</summary>
public sealed class ZoneDashBeltRuleTests
{
    [Test]
    public void 처음엔_꺼져_있다()
    {
        Assert.That(new ZoneDashBeltRule().Evaluate(0, 4, false), Is.False);
    }

    [TestCase(0b0001)]
    [TestCase(0b0111)]
    [TestCase(0b1011)]
    [TestCase(0b1110)]
    public void 패널이_하나라도_꺼져_있으면_꺼짐(int mask)
    {
        Assert.That(new ZoneDashBeltRule().Evaluate(mask, 4, false), Is.False);
    }

    [Test]
    public void 패널_4개가_모두_활성이면_켠다()
    {
        Assert.That(new ZoneDashBeltRule().Evaluate(0b1111, 4, false), Is.True);
    }

    [Test]
    public void 패널_수_밖의_비트는_무시한다()
    {
        Assert.That(new ZoneDashBeltRule().Evaluate(0b11111, 4, false), Is.True);
        Assert.That(new ZoneDashBeltRule().Evaluate(0b10111, 4, false), Is.False);
    }

    [Test]
    public void 패널이_없으면_켜지_않는다()
    {
        Assert.That(new ZoneDashBeltRule().Evaluate(0b1111, 0, false), Is.False);
    }

    [Test]
    public void 처치하면_끈다()
    {
        var rule = new ZoneDashBeltRule();
        Assert.That(rule.Evaluate(0b1111, 4, false), Is.True);
        Assert.That(rule.Evaluate(0b1111, 4, true), Is.False);
        Assert.That(rule.Defeated, Is.True);
    }

    [Test]
    public void 처치_뒤에는_신호가_사라져도_다시_켜지지_않는다()
    {
        var rule = new ZoneDashBeltRule();
        rule.Evaluate(0b1111, 4, true);
        // 디렉터가 먼저 사라지는 등 G9 를 더 못 읽어도 래치가 유지된다.
        Assert.That(rule.Evaluate(0b1111, 4, false), Is.False);
    }

    [Test]
    public void 패널보다_처치를_먼저_보면_패널이_다_켜져도_꺼짐()
    {
        var rule = new ZoneDashBeltRule();
        rule.Evaluate(0b0011, 4, true);   // 스킵 등으로 G9 가 먼저 열린 경우
        Assert.That(rule.Evaluate(0b1111, 4, false), Is.False);
    }

    [Test]
    public void 늦게_들어온_참가자는_복제값만으로_같은_결과()
    {
        // 패널 다 켜짐 + 아직 처치 전 → 켬 / 패널 다 켜짐 + 이미 G9 열림 → 끔
        Assert.That(new ZoneDashBeltRule().Evaluate(0b1111, 4, false), Is.True);
        Assert.That(new ZoneDashBeltRule().Evaluate(0b1111, 4, true), Is.False);
    }
}
