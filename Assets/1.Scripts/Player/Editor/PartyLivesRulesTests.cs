using System.Collections.Generic;
using NUnit.Framework;

/// <summary>파티 공용 목숨 풀(할 일 30 (1)) — 사망 목적지·차감·재충전·전멸 판정. 원본: Docs/tech/party-lives-respawn.md §2.</summary>
public sealed class PartyLivesRulesTests
{
    static PartyMemberLife M(PlayerLifeState s, bool free = false) => new PartyMemberLife(s, free);

    [Test]
    public void DeathDestination_SoulWhileLivesRemain_PermanentDeadAtZero()
    {
        Assert.AreEqual(PlayerLifeState.Soul, PartyLivesRules.ResolveDeathDestination(1, keepsSoul: false));
        Assert.AreEqual(PlayerLifeState.PermanentDead, PartyLivesRules.ResolveDeathDestination(0, keepsSoul: false));
    }

    [Test]
    public void DeathDestination_TutorialFreeRevive_SoulEvenAtZero()
    {
        Assert.AreEqual(PlayerLifeState.Soul, PartyLivesRules.ResolveDeathDestination(0, keepsSoul: true));
    }

    [Test]
    public void Consume_SharedPoolDropsByOnePerRevive_StopsAtZero()
    {
        int pool = PartyLivesRules.Refill(PartyLivesRules.DefaultCapacity);
        Assert.AreEqual(5, pool);

        // 서로 다른 플레이어의 부활도 같은 풀에서 뺀다.
        for (int expected = 4; expected >= 0; expected--)
        {
            Assert.IsTrue(PartyLivesRules.TryConsume(pool, out pool));
            Assert.AreEqual(expected, pool);
        }

        Assert.IsFalse(PartyLivesRules.TryConsume(pool, out int after));
        Assert.AreEqual(0, after);
    }

    [Test]
    public void CanRevive_ChargedNeedsLives_FreeIgnoresPool()
    {
        Assert.IsTrue(PartyLivesRules.CanRevive(1, consumeLife: true));
        Assert.IsFalse(PartyLivesRules.CanRevive(0, consumeLife: true));
        Assert.IsTrue(PartyLivesRules.CanRevive(0, consumeLife: false));
    }

    [Test]
    public void Refill_RestoresCapacity_NegativeClampsToZero()
    {
        Assert.AreEqual(5, PartyLivesRules.Refill(5));
        Assert.AreEqual(0, PartyLivesRules.Refill(-2));
        Assert.AreEqual(0, PartyLivesRules.ClampLives(-1));
    }

    [Test]
    public void Wipe_NoMembers_IsNotWipe()
    {
        Assert.IsFalse(PartyLivesRules.IsPartyWiped(0, new List<PartyMemberLife>()));
        Assert.IsFalse(PartyLivesRules.IsPartyWiped(0, null));
    }

    [Test]
    public void Wipe_AllPermanentDead_IsWipe()
    {
        var m = new List<PartyMemberLife> { M(PlayerLifeState.PermanentDead), M(PlayerLifeState.PermanentDead) };
        Assert.IsTrue(PartyLivesRules.IsPartyWiped(0, m));
    }

    [Test]
    public void Wipe_AnyoneAliveOrDying_IsNotWipe()
    {
        Assert.IsFalse(PartyLivesRules.IsPartyWiped(0, new List<PartyMemberLife>
            { M(PlayerLifeState.PermanentDead), M(PlayerLifeState.Alive) }));
        Assert.IsFalse(PartyLivesRules.IsPartyWiped(0, new List<PartyMemberLife>
            { M(PlayerLifeState.PermanentDead), M(PlayerLifeState.DeadPresentation) }));
    }

    [Test]
    public void Wipe_SoulWithLivesLeft_IsNotWipe()
    {
        var m = new List<PartyMemberLife> { M(PlayerLifeState.Soul), M(PlayerLifeState.PermanentDead) };
        Assert.IsFalse(PartyLivesRules.IsPartyWiped(1, m));
    }

    [Test]
    public void Wipe_SoulStrandedAtZeroLives_IsWipe()
    {
        // 목숨 1 일 때 둘이 Soul → 한 명이 부활해 0 → 그 사람도 죽어 PermanentDead. 남은 Soul 은 부활할 수 없다.
        var m = new List<PartyMemberLife> { M(PlayerLifeState.Soul), M(PlayerLifeState.PermanentDead) };
        Assert.IsTrue(PartyLivesRules.IsPartyWiped(0, m));
    }

    [Test]
    public void Wipe_FreeReviveSoulAtZeroLives_IsNotWipe()
    {
        // 튜토리얼 G11 전 사망 — 차감 없는 자동 부활 대기는 풀과 무관하게 살아난다.
        var m = new List<PartyMemberLife> { M(PlayerLifeState.Soul, free: true), M(PlayerLifeState.PermanentDead) };
        Assert.IsFalse(PartyLivesRules.IsPartyWiped(0, m));
    }
}
