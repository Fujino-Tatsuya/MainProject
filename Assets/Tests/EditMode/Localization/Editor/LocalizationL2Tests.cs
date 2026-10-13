using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 로컬라이징 L2 — [DataTableText] 자동 키·Export 수집·제외·폴백·자리표시자 규칙 공용. 러너 = 데이터 테이블 EditMode 테스트.

public sealed class LocKeyTests
{
    [Test]
    public void ForField_JoinsOwnerAndPath()
    {
        Assert.That(LocKey.ForField("FirstMeleeMainSkillData", "tooltip.description"),
            Is.EqualTo("FirstMeleeMainSkillData/tooltip.description"));
    }

    [TestCase(null, "tooltip.description")]
    [TestCase("", "tooltip.description")]
    [TestCase("Data", null)]
    [TestCase("Data", "")]
    public void ForField_EmptyPart_IsNull(string owner, string path)
    {
        Assert.That(LocKey.ForField(owner, path), Is.Null);
    }

    [Test]
    public void OwnerId_ScriptableObject_IsAssetName()
    {
        var data = ScriptableObject.CreateInstance<LocalizedTextTestData>();
        data.name = "SkillA";
        try
        {
            Assert.That(LocKey.OwnerId(data), Is.EqualTo("SkillA"));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void OwnerId_Component_IsTypeName_NotObjectName()
    {
        var go = new GameObject("Player_Paladin(Clone)");
        try
        {
            var component = go.AddComponent<DataTableTestComponent>();
            Assert.That(LocKey.OwnerId(component), Is.EqualTo(nameof(DataTableTestComponent)));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void OwnerId_Null_IsNull()
    {
        Assert.That(LocKey.OwnerId(null), Is.Null);
        Assert.That(LocKey.ForField((Object)null, "tooltip"), Is.Null);
    }
}

public sealed class DataTableTextKeysTests
{
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            Object.DestroyImmediate(o);
        created.Clear();
    }

    private LocalizedTextTestData Make(string name, string displayName = "검격", string description = "피해 {dmg}")
    {
        var data = ScriptableObject.CreateInstance<LocalizedTextTestData>();
        data.name = name;
        var so = new SerializedObject(data);
        so.FindProperty("tooltip.displayName").stringValue = displayName;
        so.FindProperty("tooltip.description").stringValue = description;
        so.ApplyModifiedPropertiesWithoutUndo();
        created.Add(data);
        return data;
    }

    [Test]
    public void Collect_KeysKoAndDescription()
    {
        LocalizedTextTestData data = Make("SkillA");
        List<StringKeyInfo> keys = DataTableTextKeys.Collect(new[] { ("SkillA", (Object)data) }, new List<string>());

        Assert.That(keys.Select(k => k.Key), Is.EqualTo(new[]
        {
            "SkillA/title", "SkillA/tooltip.displayName", "SkillA/tooltip.description",
        }));
        StringKeyInfo description = keys.Single(k => k.Key == "SkillA/tooltip.description");
        Assert.That(description.Ko, Is.EqualTo("피해 {dmg}"));
        Assert.That(description.Description, Is.EqualTo("SkillA · tooltip.description"));
    }

    [Test]
    public void Collect_Excludes_LocalizeFalse_EmptyAndNonTableText()
    {
        LocalizedTextTestData data = Make("SkillA");
        List<StringKeyInfo> keys = DataTableTextKeys.Collect(new[] { ("SkillA", (Object)data) }, new List<string>());

        Assert.That(keys.Select(k => k.Key), Has.None.EqualTo("SkillA/codeId"), "[DataTableText(Localize = false)]");
        Assert.That(keys.Select(k => k.Key), Has.None.EqualTo("SkillA/emptyText"), "빈 값 — ko 빈 칸은 시트 오류");
        Assert.That(keys.Select(k => k.Key), Has.None.EqualTo("SkillA/tooltip.subtitle"), "빈 부제");
        Assert.That(keys.Select(k => k.Key), Has.None.EqualTo("SkillA/plain"), "[DataTableText] 아닌 문자열");
    }

    [Test]
    public void Collect_DuplicateKey_FirstWins_WithWarning()
    {
        LocalizedTextTestData first = Make("Same", "첫째");
        LocalizedTextTestData second = Make("Same", "둘째");
        var warnings = new List<string>();

        List<StringKeyInfo> keys = DataTableTextKeys.Collect(
            new[] { ("Same", (Object)first), ("Same", (Object)second) }, warnings);

        Assert.That(keys.Single(k => k.Key == "Same/tooltip.displayName").Ko, Is.EqualTo("첫째"));
        Assert.That(warnings, Is.Not.Empty);
    }

    [Test]
    public void SoundEntry_KeyAndDescription_AreNotLocalized()
    {
        FieldInfo key = typeof(SoundEntry).GetField("key", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo description = typeof(SoundEntry).GetField("description", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(DataTableFields.IsTableText(key), Is.True, "수치 시트 문자열 칸으로는 남는다");
        Assert.That(DataTableFields.IsLocalizedText(key), Is.False);
        Assert.That(DataTableFields.IsLocalizedText(description), Is.False);
    }

    [Test]
    public void SkillTooltipFields_AreLocalized()
    {
        foreach (string name in new[] { "displayName", "subtitle", "description" })
        {
            FieldInfo field = typeof(SkillTooltipText).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(DataTableFields.IsLocalizedText(field), Is.True, name);
        }
    }

    [Test]
    public void CollectedKeys_LandInStringsSheet_AsNewRows()
    {
        LocalizedTextTestData data = Make("SkillA");
        List<StringKeyInfo> keys = DataTableTextKeys.Collect(new[] { ("SkillA", (Object)data) }, new List<string>());

        XlsxWriteSheet sheet = StringsSheet.Strings.BuildSheet(null, keys, new List<string>());

        Assert.That(sheet.Rows[2], Is.EqualTo(new object[] { "SkillA/tooltip.displayName", "SkillA · tooltip.displayName", "검격", null }));
    }
}

public sealed class CharacterRosterStringKeysTests
{
    [Test]
    public void NameKeyFor_UsesPrefabName()
    {
        Assert.That(CharacterRoster.Entry.NameKeyFor("Player_Paladin"), Is.EqualTo("Character.Player_Paladin.Name"));
        Assert.That(CharacterRoster.Entry.NameKeyFor(null), Is.Null);
        Assert.That(CharacterRoster.Entry.NameKeyFor(""), Is.Null);
    }

    [Test]
    public void Collect_SkipsMissingPrefabAndEmptyName()
    {
        var roster = ScriptableObject.CreateInstance<CharacterRoster>();
        roster.name = "Roster";
        var paladin = new GameObject("Player_Paladin");
        try
        {
            var so = new SerializedObject(roster);
            SerializedProperty entries = so.FindProperty("entries");
            entries.arraySize = 3;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("displayName").stringValue = "팔라딘";
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("playerPrefab").objectReferenceValue = paladin;
            entries.GetArrayElementAtIndex(1).FindPropertyRelative("displayName").stringValue = "프리팹 없음";
            entries.GetArrayElementAtIndex(2).FindPropertyRelative("displayName").stringValue = "";
            entries.GetArrayElementAtIndex(2).FindPropertyRelative("playerPrefab").objectReferenceValue = paladin;
            so.ApplyModifiedPropertiesWithoutUndo();

            List<StringKeyInfo> keys = CharacterRosterStringKeys.Collect(roster);

            Assert.That(keys.Count, Is.EqualTo(1));
            Assert.That(keys[0].Key, Is.EqualTo("Character.Player_Paladin.Name"));
            Assert.That(keys[0].Ko, Is.EqualTo("팔라딘"));
            Assert.That(keys[0].Description, Is.EqualTo("Roster · entries[0].displayName"));
        }
        finally
        {
            Object.DestroyImmediate(paladin);
            Object.DestroyImmediate(roster);
        }
    }
}

public sealed class LocTextRuleTests
{
    private sealed class Source
    {
        public float cooldownTime = 8f;
    }

    [TestCase("{a} {b:%}", null)]
    [TestCase("닫힘 }", "여는 '{'가 없는 '}'")]
    [TestCase("값 {a", "닫히지 않은 '{'")]
    [TestCase("값 {a{b}", "닫히지 않은 '{'")]
    [TestCase("빈 {}", "빈 자리표시자")]
    [TestCase("빈 { }", "빈 자리표시자")]
    public void BraceError(string text, string expected)
    {
        string error = LocText.BraceError(text);
        if (expected == null)
            Assert.That(error, Is.Null);
        else
            Assert.That(error, Does.Contain(expected));
    }

    [Test]
    public void SamePlaceholders_IgnoresOrderAndRepeat_FormatIsPartOfName()
    {
        Assert.That(LocText.SamePlaceholders("{a} {b} {a}", "{b} then {a}"), Is.True);
        Assert.That(LocText.SamePlaceholders("{cooldownTime}초", "{cooldownTime}s"), Is.True);
        Assert.That(LocText.SamePlaceholders("{healPercent:%}", "{healPercent}"), Is.False);
        Assert.That(LocText.SamePlaceholders("{dmg}", "no damage"), Is.False);
    }

    // 같은 문법 오류를 시트 검증과 툴팁 포매터가 똑같이 막는다(규칙 하나 — LocText).
    [TestCase("값 {cooldownTime")]
    [TestCase("값 }")]
    [TestCase("빈 {}")]
    [TestCase("<style=stun>기절")]
    [TestCase("기절</style>")]
    [TestCase("<style=>기절</style>")]
    public void SyntaxErrors_RejectedByValidatorAndFormatter(string text)
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { new StringRow("A", "", text, "", "A2", "C2", "D2") }, issues);
        Assert.That(issues.HasErrors, Is.True, "StringTableValidator");

        Assert.That(SkillTooltipFormatter.TryValidate(text, new Source(), out string error), Is.False, "SkillTooltipFormatter");
        Assert.That(error, Is.EqualTo(LocText.StyleTagError(text) ?? LocText.BraceError(text)), "같은 이유");
    }

    [Test]
    public void ValidTooltipText_PassesBoth()
    {
        const string text = "<style=stun>기절</style> {cooldownTime}초";
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { new StringRow("A", "", text, "<style=stun>Stun</style> {cooldownTime}s", "A2", "C2", "D2") }, issues);

        Assert.That(issues.Items, Is.Empty);
        Assert.That(SkillTooltipFormatter.TryValidate(text, new Source(), out string error), Is.True, error);
    }

    [Test]
    public void Formatter_KeepsLiteralTextAroundPlaceholders()
    {
        Assert.That(SkillTooltipFormatter.TryFormat("앞 {cooldownTime} 중간 {cooldownTime:0.0} 끝", new Source(), default, false,
            out string text, out _, out string error), Is.True, error);
        Assert.That(text, Is.EqualTo("앞 8 중간 8.0 끝"));
    }
}
