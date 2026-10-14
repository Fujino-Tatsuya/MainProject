using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 23호 레이드 변형의 순수 규칙(PLAN-boss-raid.md W1).
/// · 구간 예약 — 한 방에 임계 여러 개를 넘어도 기믹이 빠지지 않는다(예전 bool 은 앞의 것을 잃었다).
/// · 인원 HP — 1인 기준 × 배수, 표가 비면 기존 동작.
/// · 피해 상한 — 가득 찬 체력에서 한 방에 죽지 않는다(팀장 10-14).
/// </summary>
public sealed class BossRaidRulesTests
{
    static readonly BossPhaseEntry[] RaidPhases =
    {
        new BossPhaseEntry { hpThreshold = 0.75f, sequence = BossPhaseSequence.RaidDroneCharge },
        new BossPhaseEntry { hpThreshold = 0.45f, sequence = BossPhaseSequence.RaidRescue },
        new BossPhaseEntry { hpThreshold = 0.20f, sequence = BossPhaseSequence.RaidSelfDestruct },
    };

    static List<BossPhaseSequence> Collect(BossPhaseEntry[] phases, int from, int to)
    {
        var result = new List<BossPhaseSequence>();
        BossRaidRules.CollectEnteredSequences(phases, from, to, result);
        return result;
    }

    [Test]
    public void OnePhaseStep_QueuesThatPhaseOnly()
    {
        Assert.That(Collect(RaidPhases, 0, 1), Is.EqualTo(new[] { BossPhaseSequence.RaidDroneCharge }));
        Assert.That(Collect(RaidPhases, 1, 2), Is.EqualTo(new[] { BossPhaseSequence.RaidRescue }));
    }

    /// 핵심 회귀 — 75% 와 45% 를 한 방에 넘기면 둘 다 순서대로 나와야 한다.
    [Test]
    public void SkippingThresholds_QueuesEveryPassedPhaseInOrder()
    {
        Assert.That(Collect(RaidPhases, 0, 3), Is.EqualTo(new[]
        {
            BossPhaseSequence.RaidDroneCharge, BossPhaseSequence.RaidRescue, BossPhaseSequence.RaidSelfDestruct,
        }));
    }

    [Test]
    public void NoneSequence_IsNotQueued()
    {
        var phases = new[]
        {
            new BossPhaseEntry { hpThreshold = 0.66f, sequence = BossPhaseSequence.None },
            new BossPhaseEntry { hpThreshold = 0.33f, sequence = BossPhaseSequence.ChargeSequence },
        };
        Assert.That(Collect(phases, 0, 2), Is.EqualTo(new[] { BossPhaseSequence.ChargeSequence }));
    }

    [Test]
    public void ToBeyondTable_IsClamped()
    {
        Assert.That(Collect(RaidPhases, 2, 99), Is.EqualTo(new[] { BossPhaseSequence.RaidSelfDestruct }));
        Assert.That(Collect(RaidPhases, 3, 3), Is.Empty);
    }

    [TestCase(1, 2400)]
    [TestCase(2, 5760)]
    [TestCase(3, 10080)]
    [TestCase(4, 10080)] // 표보다 많으면 마지막 배수
    public void PartyHp_ScalesFromSoloBase(int party, int expected)
    {
        Assert.That(BossRaidRules.PartyScaledMaxHp(2400, new[] { 1f, 2.4f, 4.2f }, party), Is.EqualTo(expected));
    }

    [Test]
    public void PartyHp_EmptyTableOrUnknownParty_KeepsBase()
    {
        Assert.That(BossRaidRules.PartyScaledMaxHp(10000, new float[0], 3), Is.EqualTo(10000));
        Assert.That(BossRaidRules.PartyScaledMaxHp(10000, null, 3), Is.EqualTo(10000));
        Assert.That(BossRaidRules.PartyScaledMaxHp(2400, new[] { 1f, 2.4f, 4.2f }, 0), Is.EqualTo(2400));
    }

    [Test]
    public void HitCap_FullHpNeverDiesInOneHit()
    {
        const int maxHp = 220;
        int capped = BossRaidRules.CapHitDamage(9999, maxHp, 0.9f);
        Assert.That(capped, Is.EqualTo(198));
        Assert.That(maxHp - capped, Is.GreaterThan(0));
    }

    [Test]
    public void HitCap_SmallHitsAndDisabledCap_Unchanged()
    {
        Assert.That(BossRaidRules.CapHitDamage(45, 220, 0.9f), Is.EqualTo(45));
        Assert.That(BossRaidRules.CapHitDamage(9999, 220, 0f), Is.EqualTo(9999));
    }
}
