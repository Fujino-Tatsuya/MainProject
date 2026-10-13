using System.Collections.Generic;
using NUnit.Framework;

/// <summary>전멸 리스폰(할 일 30 (2)) — 결과/리스폰 분기·리스폰 가능 조건·지점 선택(패드 제외·간격·폴백)·재충전. 원본: Docs/tech/party-lives-respawn.md §3.</summary>
public sealed class PartyWipeRespawnRulesTests
{
    static PartyMemberLife M(PlayerLifeState s) => new PartyMemberLife(s, false);

    [Test]
    public void Decide_NotWiped_None()
    {
        Assert.AreEqual(PartyWipeOutcome.None, PartyWipeRespawnRules.Decide(false, true));
        Assert.AreEqual(PartyWipeOutcome.None, PartyWipeRespawnRules.Decide(false, false));
    }

    [Test]
    public void Decide_WipedWithRespawn_RespawnNotResult()
    {
        Assert.AreEqual(PartyWipeOutcome.Respawn, PartyWipeRespawnRules.Decide(true, true));
        Assert.AreEqual(PartyWipeOutcome.Result, PartyWipeRespawnRules.Decide(true, false));
    }

    [Test]
    public void CanRespawn_TutorialNeedsG10_ProceduralNeedsPad()
    {
        Assert.IsTrue(PartyWipeRespawnRules.CanRespawn(true, bossEnterGateOpen: true, hasBossPad: true, bossDefeated: false));
        Assert.IsFalse(PartyWipeRespawnRules.CanRespawn(true, bossEnterGateOpen: false, hasBossPad: true, bossDefeated: false));
        Assert.IsTrue(PartyWipeRespawnRules.CanRespawn(false, bossEnterGateOpen: false, hasBossPad: true, bossDefeated: false));
        Assert.IsFalse(PartyWipeRespawnRules.CanRespawn(false, bossEnterGateOpen: true, hasBossPad: false, bossDefeated: false));
    }

    [Test]
    public void CanRespawn_BossAlreadyDefeated_NeverRespawn()
    {
        // 보스가 쓰러진 뒤(결과 전환 예약) 전멸은 클리어 — 결과 화면.
        Assert.IsFalse(PartyWipeRespawnRules.CanRespawn(true, true, true, bossDefeated: true));
        Assert.IsFalse(PartyWipeRespawnRules.CanRespawn(false, false, true, bossDefeated: true));
    }

    [Test]
    public void WipeAtZeroLives_RefillRestoresFive_PartyNoLongerWiped()
    {
        var wiped = new List<PartyMemberLife> { M(PlayerLifeState.PermanentDead), M(PlayerLifeState.Soul) };
        Assert.IsTrue(PartyLivesRules.IsPartyWiped(0, wiped));
        Assert.AreEqual(PartyWipeOutcome.Respawn,
            PartyWipeRespawnRules.Decide(PartyLivesRules.IsPartyWiped(0, wiped), PartyWipeRespawnRules.CanRespawn(true, true, true, false)));

        int lives = PartyLivesRules.Refill(PartyLivesRules.DefaultCapacity);
        Assert.AreEqual(5, lives);
        var respawned = new List<PartyMemberLife> { M(PlayerLifeState.Alive), M(PlayerLifeState.Alive) };
        Assert.IsFalse(PartyLivesRules.IsPartyWiped(lives, respawned));
    }

    [Test]
    public void SelectSpread_SkipsPadAndMargin()
    {
        var c = new List<(float x, float z)> { (0f, 0f), (1.5f, 0f), (10f, 0f), (14f, 0f) };
        var pads = new List<TutorialPadArea> { TutorialPadArea.Box(-1f, -1f, 2f, 1f) };   // (0,0)·(1.5,0) 은 패드 안
        var chosen = new List<int>();
        Assert.IsTrue(PartyWipeRespawnRules.TrySelectSpread(c, 2, 0f, 0f, 2f, pads, 1f, chosen));
        CollectionAssert.AreEqual(new[] { 2, 3 }, chosen);
    }

    [Test]
    public void SelectSpread_PadMarginExcludesNearPoint()
    {
        var c = new List<(float x, float z)> { (2.5f, 0f), (6f, 0f) };
        var pads = new List<TutorialPadArea> { TutorialPadArea.Box(-2f, -2f, 2f, 2f) };
        var chosen = new List<int>();
        Assert.IsTrue(PartyWipeRespawnRules.TrySelectSpread(c, 1, 0f, 0f, 2f, pads, 1f, chosen));
        CollectionAssert.AreEqual(new[] { 1 }, chosen);   // 2.5 는 패드 + 1m 안
    }

    [Test]
    public void SelectSpread_KeepsSpacing_NearestToReferenceFirst()
    {
        var c = new List<(float x, float z)> { (0f, 0f), (1f, 0f), (2f, 0f), (3f, 0f), (4f, 0f) };
        var chosen = new List<int>();
        Assert.IsTrue(PartyWipeRespawnRules.TrySelectSpread(c, 3, 0f, 0f, 2f, null, 1f, chosen));
        CollectionAssert.AreEqual(new[] { 0, 2, 4 }, chosen);
    }

    [Test]
    public void SelectSpread_NotEnoughSpacedPoints_RelaxesThenReuses()
    {
        var c = new List<(float x, float z)> { (0f, 0f), (0.5f, 0f) };
        var chosen = new List<int>();
        Assert.IsTrue(PartyWipeRespawnRules.TrySelectSpread(c, 3, 0f, 0f, 2f, null, 1f, chosen));
        Assert.AreEqual(3, chosen.Count);
        CollectionAssert.AreEqual(new[] { 0, 1, 0 }, chosen);   // 간격 무시로 1 을 더하고, 그래도 모자라면 처음부터 돌려 쓴다
    }

    [Test]
    public void SelectSpread_AllOnPadOrEmpty_Fails()
    {
        var chosen = new List<int>();
        var pads = new List<TutorialPadArea> { TutorialPadArea.Circle(0f, 0f, 5f) };
        Assert.IsFalse(PartyWipeRespawnRules.TrySelectSpread(new List<(float x, float z)> { (1f, 1f) }, 2, 0f, 0f, 2f, pads, 1f, chosen));
        Assert.AreEqual(0, chosen.Count);
        Assert.IsFalse(PartyWipeRespawnRules.TrySelectSpread(new List<(float x, float z)>(), 1, 0f, 0f, 2f, null, 1f, chosen));
    }

    [Test]
    public void RingCandidates_AllOutsidePadMargin()
    {
        foreach (TutorialPadArea pad in new[] { TutorialPadArea.Box(-3f, -3f, 3f, 3f), TutorialPadArea.Circle(10f, 5f, 2f) })
        {
            var ring = new List<(float x, float z)>();
            PartyWipeRespawnRules.RingCandidates(pad, 1f, 1.5f, 1f, ring);
            Assert.Greater(ring.Count, 8);
            foreach ((float x, float z) in ring)
                Assert.IsFalse(pad.Contains(x, z, 1f), $"{pad} 둘레 점 ({x}, {z}) 이 패드 + 1m 안");
        }
    }
}
