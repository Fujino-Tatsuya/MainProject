using NUnit.Framework;

/// <summary>스킬 단위 쿨 장부·슬롯 교체 순수 모델(PLAN-assassin.md A2).</summary>
public sealed class PlayerSkillCooldownLedgerTests
{
    [Test]
    public void SkillsHaveIndependentCooldowns_AndInactiveCooldownKeepsDecreasing()
    {
        var ledger = new PlayerSkillCooldownLedger(2);
        ledger.Start(0, 10f, 5f);
        ledger.Start(1, 10f, 12f);

        Assert.That(ledger.GetRemaining(0, 12f), Is.EqualTo(3f));
        Assert.That(ledger.GetRemaining(1, 12f), Is.EqualTo(10f));
        Assert.That(ledger.GetRemaining(0, 16f), Is.Zero);
        Assert.That(ledger.GetRemaining(1, 16f), Is.EqualTo(6f));
    }

    [Test]
    public void SlotSwap_DoesNotReplaceInactiveSkillCooldown()
    {
        var ledger = new PlayerSkillCooldownLedger(2);
        var bindings = new PlayerSkillSlotBindingModel(1 << (int)PlayerSkillSlot.Main);
        ledger.Start(0, 0f, 5f);
        ledger.Start(1, 0f, 9f);

        Assert.That(bindings.Request(PlayerSkillSlot.Main, true, defer: false), Is.True);
        Assert.That(ledger.GetRemaining(0, 3f), Is.EqualTo(2f), "비활성 기본 Q 장부 유지");
        Assert.That(ledger.GetRemaining(1, 3f), Is.EqualTo(6f), "활성 대체 Q 장부 유지");
    }

    [Test]
    public void Reduce_ClampsRemainingAtZero()
    {
        var ledger = new PlayerSkillCooldownLedger(1);
        ledger.Start(0, 2f, 3f);

        ledger.Reduce(0, 3f, 10f);

        Assert.That(ledger.GetRemaining(0, 3f), Is.Zero);
        Assert.That(ledger.IsReady(0, 3f), Is.True);
    }

    [Test]
    public void OverrideDuringSkill_DefersUntilSkillEnds()
    {
        var bindings = new PlayerSkillSlotBindingModel(1 << (int)PlayerSkillSlot.Sub);

        Assert.That(bindings.Request(PlayerSkillSlot.Sub, true, defer: true), Is.False);
        Assert.That(bindings.ActiveMask, Is.Zero);
        Assert.That(bindings.HasPending, Is.True);
        Assert.That(bindings.ApplyPending(), Is.True);
        Assert.That(bindings.ActiveMask, Is.EqualTo(1 << (int)PlayerSkillSlot.Sub));
    }

    [Test]
    public void MissingAlternate_PreservesBaseBinding()
    {
        var bindings = new PlayerSkillSlotBindingModel(0);

        Assert.That(bindings.Request(PlayerSkillSlot.Main, true, defer: false), Is.False);
        Assert.That(bindings.ActiveMask, Is.Zero);
        Assert.That(bindings.HasPending, Is.False);
    }
}
