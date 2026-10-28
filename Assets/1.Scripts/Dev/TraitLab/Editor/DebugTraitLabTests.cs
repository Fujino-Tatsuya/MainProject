using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// 특성 디버그 랩(할 일 135)의 순수 규칙 — <see cref="TraitLabModel"/>.
/// 넣기/빼기 토글·차수당 하나(같은 차수 교체)·규칙 무시·전부 빼기·캐릭터 변경 시 초기화·차수별 랜덤·2차 상충 경고.
/// </summary>
public sealed class DebugTraitLabTests
{
    static TraitLabModel ModelFor(TutorialCharacter character)
    {
        var model = new TraitLabModel();
        model.SetCharacter(character);
        return model;
    }

    // ─── 후보 ─────────────────────────────────────────────────────────

    [TestCase(TutorialCharacter.Paladin)]
    [TestCase(TutorialCharacter.Gunner)]
    [TestCase(TutorialCharacter.Assassin)]
    public void 캐릭터마다_12개_차수별_3개(TutorialCharacter character)
    {
        TraitLabModel model = ModelFor(character);
        Assert.AreEqual(TraitCatalog.TierCount * TraitCatalog.CandidatesPerTier, model.Slots.Count);
        for (int tier = 1; tier <= TraitCatalog.TierCount; tier++)
            Assert.AreEqual(TraitCatalog.CandidatesPerTier, model.Slots.Count(s => s.Tier == tier));
        Assert.IsTrue(model.Slots.All(s => s.Character == character));
    }

    [Test]
    public void 캐릭터_모름이면_후보_없음()
    {
        var model = new TraitLabModel();
        Assert.AreEqual(TutorialCharacter.None, model.Character);
        CollectionAssert.IsEmpty(model.Slots);
        Assert.IsFalse(model.Toggle("PAL_1_Q_DAMAGE"));
    }

    // ─── 토글 ─────────────────────────────────────────────────────────

    [Test]
    public void 토글은_넣고_다시_누르면_뺀다()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Paladin);
        Assert.IsTrue(model.Toggle("PAL_1_Q_DAMAGE"));
        Assert.IsTrue(model.IsOwned("PAL_1_Q_DAMAGE"));
        Assert.IsTrue(model.Toggle("PAL_1_Q_DAMAGE"));
        Assert.IsFalse(model.IsOwned("PAL_1_Q_DAMAGE"));
        CollectionAssert.IsEmpty(model.Owned);
    }

    [Test]
    public void 다른_캐릭터_특성은_넣지_않는다()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Paladin);
        Assert.IsFalse(model.Toggle("GUN_1_BASIC_DAMAGE"));
        Assert.IsFalse(model.Toggle("NOT_A_TRAIT"));
        CollectionAssert.IsEmpty(model.Owned);
    }

    [Test]
    public void 보유는_차수_순서로_정렬된다()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Paladin);
        model.Toggle("PAL_4_E_PUSH");
        model.Toggle("PAL_2_DASH");
        model.Toggle("PAL_1_E_COOLDOWN");
        CollectionAssert.AreEqual(new[] { "PAL_1_E_COOLDOWN", "PAL_2_DASH", "PAL_4_E_PUSH" }, model.Owned);
    }

    // ─── 차수당 하나 ───────────────────────────────────────────────────

    [Test]
    public void 같은_2차를_넣으면_기존_2차를_빼고_새_것을_넣는다()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Assassin);
        model.Toggle("ASN_1_Q_COOLDOWN");
        model.Toggle("ASN_2_RELEASE");
        model.Toggle("ASN_2_RETURN");
        CollectionAssert.AreEqual(new[] { "ASN_1_Q_COOLDOWN", "ASN_2_RETURN" }, model.Owned);
    }

    [Test]
    public void 차수당_하나는_모든_차수에_걸린다()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Gunner);
        model.Toggle("GUN_1_BASIC_DAMAGE");
        model.Toggle("GUN_1_WIDTH");
        model.Toggle("GUN_3_PIERCE");
        model.Toggle("GUN_3_VOLLEY");
        CollectionAssert.AreEqual(new[] { "GUN_1_WIDTH", "GUN_3_VOLLEY" }, model.Owned);
    }

    [Test]
    public void 규칙_무시면_같은_차수_동시_보유()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Assassin);
        model.IgnoreRules = true;
        model.Toggle("ASN_2_RETURN");
        model.Toggle("ASN_2_RELEASE");
        model.Toggle("ASN_2_RESTRAINT");
        CollectionAssert.AreEqual(new[] { "ASN_2_RELEASE", "ASN_2_RESTRAINT", "ASN_2_RETURN" }, model.Owned);

        // 규칙을 다시 켜면 다음 넣기부터 그 차수를 하나로 줄인다
        model.IgnoreRules = false;
        model.Toggle("ASN_2_RELEASE"); // 빼기
        model.Toggle("ASN_2_RELEASE"); // 넣기 — 나머지 2차를 뺀다
        CollectionAssert.AreEqual(new[] { "ASN_2_RELEASE" }, model.Owned);
    }

    // ─── 전부 빼기·캐릭터 변경·동기화 ─────────────────────────────────

    [Test]
    public void 전부_빼기()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Paladin);
        Assert.IsFalse(model.ClearAll());
        model.Toggle("PAL_1_Q_DAMAGE");
        model.Toggle("PAL_3_Q_STUN");
        Assert.IsTrue(model.ClearAll());
        CollectionAssert.IsEmpty(model.Owned);
    }

    [Test]
    public void 캐릭터가_바뀌면_목록이_바뀌고_보유는_초기화()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Paladin);
        model.Toggle("PAL_1_Q_DAMAGE");
        Assert.IsFalse(model.SetCharacter(TutorialCharacter.Paladin)); // 같은 캐릭터 = 그대로
        Assert.IsTrue(model.IsOwned("PAL_1_Q_DAMAGE"));

        Assert.IsTrue(model.SetCharacter(TutorialCharacter.Gunner));
        CollectionAssert.IsEmpty(model.Owned);
        Assert.IsTrue(model.Slots.All(s => s.Character == TutorialCharacter.Gunner));
    }

    [Test]
    public void 서버_목록_동기화는_다른_캐릭터_ID_와_중복을_버리고_정렬()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Paladin);
        model.Sync(new[] { "PAL_3_Q_STUN", "GUN_1_CHARGE", "PAL_1_Q_DAMAGE", "PAL_3_Q_STUN", null });
        CollectionAssert.AreEqual(new[] { "PAL_1_Q_DAMAGE", "PAL_3_Q_STUN" }, model.Owned);
        model.Sync(null);
        CollectionAssert.IsEmpty(model.Owned);
    }

    // ─── 차수별 랜덤 ───────────────────────────────────────────────────

    [Test]
    public void 차수별_랜덤은_차수마다_하나씩_새로_채운다()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Paladin);
        model.IgnoreRules = true;
        model.Toggle("PAL_1_Q_DAMAGE");
        model.Toggle("PAL_1_E_COOLDOWN");

        var picks = new Queue<int>(new[] { 2, 0, 1, 2 });
        Assert.AreEqual(4, model.RandomPerTier(n => picks.Dequeue()));
        CollectionAssert.AreEqual(
            new[] { "PAL_1_PASSIVE_HEAL", "PAL_2_DASH", "PAL_3_E_SHIELD", "PAL_4_HEAL_SHIELD" }, model.Owned);
    }

    [Test]
    public void 차수별_랜덤은_범위_밖_값이면_첫_칸()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Gunner);
        model.RandomPerTier(n => n + 5);
        CollectionAssert.AreEqual(
            new[] { "GUN_1_BASIC_DAMAGE", "GUN_2_OVERDRIVE", "GUN_3_Q_PUSH", "GUN_4_R_FOCUS" }, model.Owned);
    }

    [Test]
    public void 차수별_랜덤_결과는_모두_다른_차수()
    {
        var rng = new System.Random(135);
        TraitLabModel model = ModelFor(TutorialCharacter.Assassin);
        for (int run = 0; run < 20; run++)
        {
            model.RandomPerTier(rng.Next);
            int[] tiers = model.Owned.Select(id => TraitCatalog.TryGetSlot(id, out TraitCatalog.Slot s) ? s.Tier : 0).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, tiers);
        }
    }

    // ─── 2차 상충 경고 ─────────────────────────────────────────────────

    [Test]
    public void 상충_경고는_실게임_등록부_규칙을_쓴다()
    {
        TraitLabModel model = ModelFor(TutorialCharacter.Assassin);
        var result = new List<(string chosen, string disabled)>();

        model.Toggle("ASN_2_RESTRAINT");
        model.GetConflicts(TraitContent.Registry, result);
        CollectionAssert.IsEmpty(result); // 효과를 잃을 보유 특성이 없다

        model.Toggle("ASN_1_TRANS_E_DAMAGE");
        model.GetConflicts(TraitContent.Registry, result);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("ASN_2_RESTRAINT", "ASN_1_TRANS_E_DAMAGE"), result[0]);
        // 상충은 막지 않는다 — 실게임 확인창도 [확인]이면 그대로 허용
        Assert.IsTrue(model.IsOwned("ASN_2_RESTRAINT") && model.IsOwned("ASN_1_TRANS_E_DAMAGE"));

        model.GetConflicts(null, result);
        CollectionAssert.IsEmpty(result);
    }
}
