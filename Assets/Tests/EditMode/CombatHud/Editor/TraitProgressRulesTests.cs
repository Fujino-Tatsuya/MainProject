using NUnit.Framework;

public sealed class TraitProgressRulesTests
{
    static int Mask(params MapSceneGate[] gates)
    {
        int mask = 0;
        foreach (MapSceneGate g in gates)
            mask |= 1 << (int)g;
        return mask;
    }

    const int AllGates = (1 << MapSceneFlowModel.GateCount) - 1;

    // ── 레벨 ──

    [TestCase(0, 1)]
    [TestCase(1, 2)]
    [TestCase(4, 5)]
    [TestCase(-1, 1)]
    public void Level_IsAcquiredPlusOne(int acquired, int expected)
    {
        Assert.That(TraitProgressRules.Level(true, acquired), Is.EqualTo(expected));
    }

    [Test]
    public void Level_OutsideTutorial_IsOne()
    {
        Assert.That(TraitProgressRules.Level(false, 3), Is.EqualTo(1));
    }

    // ── XP 바 ──

    [Test]
    public void XpFill_OutsideTutorial_IsZero()
    {
        Assert.That(TraitProgressRules.XpFill(false, 0, AllGates), Is.EqualTo(0f));
        Assert.That(TraitProgressRules.XpFill(false, 4, AllGates), Is.EqualTo(0f));
    }

    [Test]
    public void XpFill_ZeroTraits_UsesCorridor1Segment()
    {
        Assert.That(TraitProgressRules.XpFill(true, 0, 0), Is.EqualTo(0f));
        Assert.That(TraitProgressRules.XpFill(true, 0, Mask(MapSceneGate.G1_Corridor1Entry)), Is.EqualTo(1f));
        // 다음 구간 게이트는 지금 구간에 섞이지 않는다.
        Assert.That(TraitProgressRules.XpFill(true, 0, Mask(MapSceneGate.G2_Zone2Entry)), Is.EqualTo(0f));
    }

    [Test]
    public void XpFill_AfterChoosingTrait_RestartsFromZeroInNextSegment()
    {
        int g1Open = Mask(MapSceneGate.G1_Corridor1Entry);
        Assert.That(TraitProgressRules.XpFill(true, 1, g1Open), Is.EqualTo(0f));
    }

    [Test]
    public void XpFill_PartiallyOpenSegment_IsRatio()
    {
        int mask = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry);
        Assert.That(TraitProgressRules.XpFill(true, 1, mask), Is.EqualTo(0.5f));

        mask |= Mask(MapSceneGate.G3_Corridor2Entry, MapSceneGate.G4_Zone3Entry);
        Assert.That(TraitProgressRules.XpFill(true, 2, mask), Is.EqualTo(1f / 3f).Within(1e-6));

        mask |= Mask(MapSceneGate.G5_SpinnerRealMode, MapSceneGate.G6_Corridor3Entry, MapSceneGate.G7_Zone4Entry, MapSceneGate.G8_TimerStart);
        Assert.That(TraitProgressRules.XpFill(true, 3, mask), Is.EqualTo(2f / 3f).Within(1e-6));
    }

    [Test]
    public void XpFill_SegmentBoundaries_FullAtLastGateOfSegment()
    {
        Assert.That(TraitProgressRules.XpFill(true, 1, Mask(MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry)), Is.EqualTo(1f));
        Assert.That(TraitProgressRules.XpFill(true, 2,
            Mask(MapSceneGate.G4_Zone3Entry, MapSceneGate.G5_SpinnerRealMode, MapSceneGate.G6_Corridor3Entry)), Is.EqualTo(1f));
        Assert.That(TraitProgressRules.XpFill(true, 3,
            Mask(MapSceneGate.G7_Zone4Entry, MapSceneGate.G8_TimerStart, MapSceneGate.G9_Corridor4Entry)), Is.EqualTo(1f));
        // G10 이후 게이트는 어느 구간에도 없다.
        Assert.That(TraitProgressRules.XpFill(true, 3,
            Mask(MapSceneGate.G10_BossEnterAndPad, MapSceneGate.G11_BossRoom)), Is.EqualTo(0f));
    }

    [TestCase(4)]
    [TestCase(7)]
    public void XpFill_AllTraitsAcquired_IsFull(int acquired)
    {
        Assert.That(TraitProgressRules.XpFill(true, acquired, 0), Is.EqualTo(1f));
    }

    [Test]
    public void SegmentCount_IsFourCorridors()
    {
        Assert.That(TraitProgressRules.SegmentCount, Is.EqualTo(4));
    }

    // ── 상단 칸 ──

    [Test]
    public void CountsForSlot_OnlyCooldownEffectsOnMatchingSlot()
    {
        // 할 일 125 3/5: 옛 효과 enum 대신 효과 클래스의 AffectsCooldownOf(쿨 차감·쿨 배율)
        var refundQ = new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.AttackLanded), 1f, new[] { PlayerSkillSlot.Main });
        Assert.That(refundQ.AffectsCooldownOf(PlayerSkillSlot.Main), Is.True);
        Assert.That(refundQ.AffectsCooldownOf(PlayerSkillSlot.Sub), Is.False);
        Assert.That(new TraitCooldownEffect(-0.15f, new[] { PlayerSkillSlot.Sub }).AffectsCooldownOf(PlayerSkillSlot.Sub), Is.True);
        Assert.That(new TraitDamageEffect(0.2f).AffectsCooldownOf(PlayerSkillSlot.Main), Is.False);
    }

    [TestCase(-1, 0)]
    [TestCase(0, 0)]
    [TestCase(2, 2)]
    [TestCase(4, 4)]
    [TestCase(6, 4)]
    public void ClampUpgradeStage_ClampsToFour(int count, int expected)
    {
        Assert.That(TraitProgressRules.ClampUpgradeStage(count), Is.EqualTo(expected));
    }
}
