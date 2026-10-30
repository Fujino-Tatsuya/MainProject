using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 125 5/5 — 특성 Strings 키 규칙(<see cref="TraitStringKeys"/>)·고정 문구(<see cref="TraitUiStrings"/>)·Export 키 수집(<see cref="TraitStringKeyCollector"/>).
/// </summary>
public class TraitStringKeysTests
{
    [Test]
    public void Keys_FollowIdRule()
    {
        Assert.AreEqual("Trait.PAL_1_Q_DAMAGE.Name", TraitStringKeys.Name("PAL_1_Q_DAMAGE"));
        Assert.AreEqual("Trait.PAL_1_Q_DAMAGE.Category", TraitStringKeys.Category("PAL_1_Q_DAMAGE"));
        Assert.AreEqual("Trait.PAL_1_Q_DAMAGE.Desc", TraitStringKeys.Description("PAL_1_Q_DAMAGE"));
        Assert.AreEqual("Trait.ASN_2_RESTRAINT.Conflict", TraitStringKeys.Conflict("ASN_2_RESTRAINT"));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("PAL 1")]
    [TestCase("PAL.1")]
    [TestCase("PAL/1")]
    [TestCase("{PAL}")]
    public void InvalidId_HasNoKey(string id)
    {
        Assert.IsFalse(TraitStringKeys.IsValidId(id));
        Assert.IsNull(TraitStringKeys.Name(id));
        Assert.IsNull(TraitStringKeys.Description(id));
    }

    [Test]
    public void AllSpecIds_AreValidKeys_AndRoundTrip()
    {
        foreach (TraitCatalog.Slot slot in TraitCatalog.Spec)
        {
            Assert.IsTrue(TraitStringKeys.IsValidId(slot.Id), slot.Id);
            foreach (string suffix in TraitStringKeys.Suffixes)
            {
                string key = TraitStringKeys.Prefix + slot.Id + suffix;
                Assert.IsTrue(TraitStringKeys.TryParse(key, out string id, out string parsedSuffix), key);
                Assert.AreEqual(slot.Id, id);
                Assert.AreEqual(suffix, parsedSuffix);
            }
        }

        Assert.IsFalse(TraitStringKeys.TryParse("UI.Trait.Help", out _, out _));
        Assert.IsFalse(TraitStringKeys.TryParse("Trait..Name", out _, out _));
    }

    [Test]
    public void MissingRow_FallsBackToInline()
    {
        Assert.AreEqual("인라인", TraitStringKeys.Resolve("Trait.ZZZ_NOT_A_TRAIT.Name", "인라인"));
        Assert.AreEqual("인라인", TraitStringKeys.Resolve(null, "인라인"));

        PaladinTraitDefinition def = TraitContentTestKit.Make<PaladinTraitDefinition>("ZZZ_NOT_A_TRAIT", "이름", "분류", "설명");
        try
        {
            Assert.AreEqual("이름", def.DisplayName);
            Assert.AreEqual("분류", def.Category);
            Assert.AreEqual("설명", def.Description);
        }
        finally
        {
            Object.DestroyImmediate(def);
        }
    }

    [Test]
    public void FixedUiStrings_AreTraitKeys_InUiStringsAll()
    {
        var all = new HashSet<string>(UiStrings.All.Select(s => s.Key));
        foreach (UiString s in TraitUiStrings.All)
        {
            StringAssert.StartsWith("UI.Trait.", s.Key);
            Assert.IsTrue(all.Contains(s.Key), s.Key + " 가 Export 목록(UiStrings.All)에 있어야 한다");
            Assert.IsFalse(string.IsNullOrEmpty(s.Ko), s.Key);
        }

        StringAssert.Contains("{traits}", TraitUiStrings.ConflictDefault.Ko);
        StringAssert.Contains("{category}", TraitUiStrings.CardCategory.Ko);
        Assert.AreEqual(TraitUiStrings.All.Count, TraitUiStrings.All.Select(s => s.Key).Distinct().Count(), "키 중복 없음");
    }

    [Test]
    public void CategoryLine_EmptyHidesLine()
    {
        Assert.AreEqual(string.Empty, TraitUiStrings.FormatCategory(""));
        Assert.AreEqual(string.Empty, TraitUiStrings.FormatCategory(null));
        Assert.AreEqual("Q 변경 · 밀치기", TraitUiStrings.FormatCategory("Q 변경 · 밀치기"));
    }

    [Test]
    public void Collector_DefinitionAndConflictKeys_SkipEmpty()
    {
        AssassinTraitDefinition def = TraitContentTestKit.Make<AssassinTraitDefinition>("ASN_2_RESTRAINT", "절제된 분노", "", "R 을 쓸 수 없습니다.");
        try
        {
            var registry = new TraitContentRegistry();
            registry.For(TutorialCharacter.Assassin).Conflict("ASN_2_RESTRAINT", "ASN_1_TRANS_E_DAMAGE", "‘{traits}’ 비활성. 선택?");
            List<StringKeyInfo> keys = TraitStringKeyCollector.Collect(new TraitDefinition[] { def, null }, registry);
            CollectionAssert.AreEqual(new[]
            {
                "Trait.ASN_2_RESTRAINT.Name",
                "Trait.ASN_2_RESTRAINT.Desc",
                "Trait.ASN_2_RESTRAINT.Conflict",
            }, keys.Select(k => k.Key).ToArray(), "빈 분류는 모으지 않는다(ko 빈 칸 = 시트 오류)");
            Assert.AreEqual("절제된 분노", keys[0].Ko);
            Assert.AreEqual("‘{traits}’ 비활성. 선택?", keys[2].Ko);
        }
        finally
        {
            Object.DestroyImmediate(def);
        }
    }

    [Test]
    public void Collector_ProjectRegistration_IncludesSpecConflictKey()
    {
        List<StringKeyInfo> keys = TraitStringKeyCollector.Collect(new TraitDefinition[0], TraitContent.Build());
        Assert.IsTrue(keys.Any(k => k.Key == "Trait.ASN_2_RESTRAINT.Conflict"));
    }
}
