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

    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(4, 3)] // 기존 송전기(3인 이상 4개)와 다르다 — 인원과 같게, 최대 3
    public void RaidPylonCount_EqualsPartyUpToThree(int players, int expected)
    {
        Assert.That(BossRaidRules.RaidPylonCount(players), Is.EqualTo(expected));
    }

    /// ② 실패 방전 — 부순 탑이 선 칸만 빠지고 나머지 48칸은 전부 위험.
    [Test]
    public void DischargeMask_ExcludesOnlySafeTiles()
    {
        var safe = new List<UnityEngine.Vector2Int> { new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(6, 6) };
        ulong mask = BossRaidRules.DischargeMask(safe);
        Assert.That(BossTileGrid.Count(mask), Is.EqualTo(BossTileGrid.TileCount - 2));
        Assert.That(BossTileGrid.Has(mask, 0, 0), Is.False);
        Assert.That(BossTileGrid.Has(mask, 6, 6), Is.False);
        Assert.That(BossTileGrid.Has(mask, 3, 3), Is.True);
    }

    [Test]
    public void DischargeMask_NoSafeTile_IsWholeRoom_AndOutOfRangeIgnored()
    {
        Assert.That(BossRaidRules.DischargeMask(new List<UnityEngine.Vector2Int>()), Is.EqualTo(BossTileGrid.AllMask));
        var bad = new List<UnityEngine.Vector2Int> { new UnityEngine.Vector2Int(-1, 2), new UnityEngine.Vector2Int(7, 0) };
        Assert.That(BossRaidRules.DischargeMask(bad), Is.EqualTo(BossTileGrid.AllMask));
    }

    /// ④ 피해만으로도 게이지를 비울 수 있다 — 목표 피해 합을 넣으면 정확히 0.
    [Test]
    public void SelfDestructGauge_DamageAloneCanEmptyIt()
    {
        float g = 100f;
        g = BossRaidRules.SelfDestructGaugeAfterDamage(g, 150, 300);
        Assert.That(g, Is.EqualTo(50f).Within(0.001f));
        g = BossRaidRules.SelfDestructGaugeAfterDamage(g, 150, 300);
        Assert.That(g, Is.EqualTo(0f));
        Assert.That(BossRaidRules.SelfDestructGaugeAfterDamage(0f, 999, 300), Is.EqualTo(0f), "0 아래로 내려가지 않는다");
    }

    [Test]
    public void SelfDestructGauge_ZeroDamageOrTotal_Unchanged()
    {
        Assert.That(BossRaidRules.SelfDestructGaugeAfterDamage(70f, 0, 300), Is.EqualTo(70f));
        Assert.That(BossRaidRules.SelfDestructGaugeAfterDamage(70f, 50, 0), Is.EqualTo(70f));
    }

    /// ③ 혼자면 구출(잡기) 대신 점프(→ 점프·점프·점프 콤보, 경석 10-20). 예전 = 간파 가능 돌진.
    [TestCase(0, BossAttackId.Jump)]
    [TestCase(1, BossAttackId.Jump)]
    [TestCase(2, BossAttackId.Grab)]
    [TestCase(3, BossAttackId.Grab)]
    public void RescueAttack_SoloIsJump_PartyIsGrab(int alive, BossAttackId expected)
    {
        Assert.That(BossRaidRules.RescueAttackFor(alive), Is.EqualTo(expected));
    }

    [Test]
    public void HitCap_SmallHitsAndDisabledCap_Unchanged()
    {
        Assert.That(BossRaidRules.CapHitDamage(45, 220, 0.9f), Is.EqualTo(45));
        Assert.That(BossRaidRules.CapHitDamage(9999, 220, 0f), Is.EqualTo(9999));
    }
}
