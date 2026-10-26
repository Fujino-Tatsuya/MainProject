using System.Collections.Generic;
using NUnit.Framework;

/// <summary>집결 상태 UI 표시 대상 고르기 — 할 일 86 T2c(서 있는 구역 우선, 없으면 가장 가까운 활성 구역).</summary>
public sealed class PartyGatherDisplayRulesTests
{
    static PartyGatherDisplayCandidate Zone(bool showable, bool inside, float distanceSqr) =>
        new PartyGatherDisplayCandidate(showable, inside, distanceSqr);

    [Test]
    public void 서_있는_구역이_가까운_구역보다_우선한다()
    {
        var candidates = new List<PartyGatherDisplayCandidate>
        {
            Zone(true, false, 1f),
            Zone(true, true, 9f),
        };

        Assert.That(PartyGatherDisplayRules.Pick(candidates, true), Is.EqualTo(1));
    }

    [Test]
    public void 아무_구역에도_없으면_가장_가까운_활성_구역()
    {
        var candidates = new List<PartyGatherDisplayCandidate>
        {
            Zone(true, false, 25f),
            Zone(false, false, 1f),
            Zone(true, false, 4f),
            Zone(true, false, 16f),
        };

        Assert.That(PartyGatherDisplayRules.Pick(candidates, true), Is.EqualTo(2));
    }

    [Test]
    public void 비활성_확정_구역은_안에_있어도_고르지_않는다()
    {
        var candidates = new List<PartyGatherDisplayCandidate>
        {
            Zone(false, true, 0f),
            Zone(true, false, 9f),
        };

        Assert.That(PartyGatherDisplayRules.Pick(candidates, true), Is.EqualTo(1));
    }

    [Test]
    public void 거리가_같으면_앞쪽_구역()
    {
        var candidates = new List<PartyGatherDisplayCandidate>
        {
            Zone(true, false, 4f),
            Zone(true, false, 4f),
        };

        Assert.That(PartyGatherDisplayRules.Pick(candidates, true), Is.EqualTo(0));
    }

    [Test]
    public void 로컬_위치를_모르면_첫_활성_구역()
    {
        var candidates = new List<PartyGatherDisplayCandidate>
        {
            Zone(false, false, 0f),
            Zone(true, false, 0f),
            Zone(true, false, 0f),
        };

        Assert.That(PartyGatherDisplayRules.Pick(candidates, false), Is.EqualTo(1));
    }

    [Test]
    public void 활성_구역이_없으면_None()
    {
        var candidates = new List<PartyGatherDisplayCandidate> { Zone(false, false, 1f) };

        Assert.That(PartyGatherDisplayRules.Pick(candidates, true), Is.EqualTo(PartyGatherDisplayRules.None));
        Assert.That(PartyGatherDisplayRules.Pick(new List<PartyGatherDisplayCandidate>(), false), Is.EqualTo(PartyGatherDisplayRules.None));
    }
}
