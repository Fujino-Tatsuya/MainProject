using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 할 일 114 — 스킬 툴팁 태그 칸(tooltip.tags) 파싱·검증·화면 이름 키. 러너 = 데이터 테이블 EditMode 테스트.
public sealed class SkillTooltipTagsTests
{
    private readonly List<ScriptableObject> created = new List<ScriptableObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (ScriptableObject so in created)
            UnityEngine.Object.DestroyImmediate(so);
        created.Clear();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Parse_Empty_IsNoTags(string ids)
    {
        var tags = new List<SkillTooltipTag> { SkillTooltipTag.Stun };

        Assert.That(SkillTooltipTags.TryParse(ids, tags, out string error), Is.True, error);
        Assert.That(tags, Is.Empty);
    }

    [Test]
    public void Parse_CommaSeparated_TrimsAndIgnoresCase_KeepsOrder()
    {
        var tags = new List<SkillTooltipTag>();

        Assert.That(SkillTooltipTags.TryParse(" stun ,Knockback,  HEAL", tags, out string error), Is.True, error);
        Assert.That(tags, Is.EqualTo(new[] { SkillTooltipTag.Stun, SkillTooltipTag.Knockback, SkillTooltipTag.Heal }));
    }

    [TestCase("Knockback,Nope", "Nope")]
    [TestCase("1", "'1'")]                     // enum 숫자 값은 ID 가 아니다
    [TestCase("Knockback,,Stun", "빈 칸")]
    [TestCase("Stun, stun", "두 번")]
    public void Parse_Invalid_IsErrorButKeepsKnownTags(string ids, string expected)
    {
        var tags = new List<SkillTooltipTag>();

        Assert.That(SkillTooltipTags.TryParse(ids, tags, out string error), Is.False);
        Assert.That(error, Does.Contain(expected));
    }

    [Test]
    public void Parse_UnknownId_KeepsKnownTags()
    {
        var tags = new List<SkillTooltipTag>();

        SkillTooltipTags.TryParse("Knockback, Nope, Stun", tags, out _);

        Assert.That(tags, Is.EqualTo(new[] { SkillTooltipTag.Knockback, SkillTooltipTag.Stun }));
    }

    [Test]
    public void Names_EveryTag_HasKeyAndKoAndIsExported()
    {
        foreach (SkillTooltipTag tag in (SkillTooltipTag[])Enum.GetValues(typeof(SkillTooltipTag)))
        {
            UiString name = SkillTooltipTags.Name(tag);
            Assert.That(name.Key, Is.EqualTo("UI.SkillTooltip.Tag." + tag));
            Assert.That(name.Ko, Is.Not.Empty.And.Not.EqualTo(tag.ToString()), tag + " ko 이름 누락");
            Assert.That(UiStrings.All.Count(s => s.Key == name.Key), Is.EqualTo(1), name.Key);
        }
    }

    [Test]
    public void Name_NoRow_FallsBackToKo_RowWins()
    {
        var table = ScriptableObject.CreateInstance<StringTable>();
        bool hadPref = PlayerPrefs.HasKey(UserLanguageConfig.PrefsKey);
        string savedPref = PlayerPrefs.GetString(UserLanguageConfig.PrefsKey, string.Empty);
        try
        {
            table.SetEntries(new StringTable.Entry[0]);
            Loc.SetTable(table);
            Loc.Current = GameLanguage.Ko;
            Assert.That(SkillTooltipTags.Name(SkillTooltipTag.Knockback).Text, Is.EqualTo("넉백"));

            table.SetEntries(new[] { new StringTable.Entry("UI.SkillTooltip.Tag.Knockback", "넉백", "Knockback") });
            Loc.Current = GameLanguage.En;
            Assert.That(SkillTooltipTags.Name(SkillTooltipTag.Knockback).Text, Is.EqualTo("Knockback"));
        }
        finally
        {
            Loc.SetTable(null);
            UnityEngine.Object.DestroyImmediate(table);
            if (hadPref)
                PlayerPrefs.SetString(UserLanguageConfig.PrefsKey, savedPref);
            else
                PlayerPrefs.DeleteKey(UserLanguageConfig.PrefsKey);
            PlayerPrefs.Save();
            UserLanguageConfig.ResetCache();
        }
    }

    [Test]
    public void TooltipText_TagIds_DefaultIsEmpty()
    {
        Assert.That(default(SkillTooltipText).TagIds, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TableBind_KnownTags_Applies()
    {
        SkillTooltipTagsTestData data = Make("A");
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = DataTableApplier.Bind(new[] { Entry("A", "Knockback, Stun") }, new TestLookup(data), issues);

        Assert.That(issues.HasErrors, Is.False, string.Join("\n", issues.Items));
        DataTableApplier.ApplyInMemory(writes);
        Assert.That(data.tooltip.TagIds, Is.EqualTo("Knockback, Stun"));
    }

    [Test]
    public void TableBind_EmptyCell_IsNoTags()
    {
        SkillTooltipTagsTestData data = Make("A");
        var issues = new DataTableIssues();

        DataTableApplier.Bind(new[] { Entry("A", "") }, new TestLookup(data), issues);

        Assert.That(issues.HasErrors, Is.False, string.Join("\n", issues.Items));
    }

    [Test]
    public void TableBind_UnknownTag_IsError()
    {
        SkillTooltipTagsTestData data = Make("A");
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = DataTableApplier.Bind(new[] { Entry("A", "Knockback,Nope") }, new TestLookup(data), issues);

        Assert.That(issues.HasErrors, Is.True);
        Assert.That(string.Join("\n", issues.Items), Does.Contain("Nope"));
        Assert.That(writes, Is.Empty);
    }

    private SkillTooltipTagsTestData Make(string name)
    {
        var data = ScriptableObject.CreateInstance<SkillTooltipTagsTestData>();
        data.name = name;
        created.Add(data);
        return data;
    }

    private static DataTableEntry Entry(string asset, string value) =>
        new DataTableEntry(nameof(SkillTooltipTagsTestData), asset, "tooltip.tags", value,
            $"T.xlsx › {nameof(SkillTooltipTagsTestData)}!{asset}.tooltip.tags");
}
