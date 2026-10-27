using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 할 일 114 후속 — 태그 칩 색 표(SkillTooltipTagStyles): 색 문자열 파싱·조회·표 검사·데이터 테이블 검증·프로젝트 에셋. 러너 = 데이터 테이블 EditMode 테스트.
public sealed class SkillTooltipTagStylesTests
{
    private const string AssetPath = "Assets/2.Prefabs/UI/SkillTooltipGenerated/SkillTooltipTagStyles.asset";
    private readonly List<ScriptableObject> created = new List<ScriptableObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (ScriptableObject so in created)
            UnityEngine.Object.DestroyImmediate(so);
        created.Clear();
    }

    [TestCase("#2A6E36", 0x2A, 0x6E, 0x36, 0xFF)]
    [TestCase("#2a6e36cc", 0x2A, 0x6E, 0x36, 0xCC)]
    [TestCase("  #FFFFFF  ", 0xFF, 0xFF, 0xFF, 0xFF)]
    public void HexColor_Valid_Parses(string text, int r, int g, int b, int a)
    {
        Assert.That(HexColor.TryParse(text, out Color32 color, out string error), Is.True, error);
        Assert.That(color, Is.EqualTo(new Color32((byte)r, (byte)g, (byte)b, (byte)a)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("2A6E36")]   // # 없음
    [TestCase("#FFF")]     // 축약
    [TestCase("#12345")]
    [TestCase("#1234567")]
    [TestCase("#GG0000")]
    [TestCase("red")]
    public void HexColor_Invalid_IsError(string text)
    {
        Assert.That(HexColor.TryParse(text, out _, out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void HexColor_Format_RoundTrips()
    {
        Assert.That(HexColor.Format(new Color32(0x2A, 0x6E, 0x36, 0xFF)), Is.EqualTo("#2A6E36"));
        Assert.That(HexColor.Format(new Color32(0x2A, 0x6E, 0x36, 0x80)), Is.EqualTo("#2A6E3680"));
    }

    [Test]
    public void DefaultStyles_CoverEveryTag_NoErrorsNoWarnings_AndDistinctBackgrounds()
    {
        SkillTooltipTagStyle[] styles = SkillTooltipTagStyles.DefaultStyles();
        var errors = new List<string>();
        var warnings = new List<string>();

        SkillTooltipTagStyles.Validate(styles, SkillTooltipTagStyles.DefaultBackgroundHex, SkillTooltipTagStyles.DefaultTextHex, errors, warnings);

        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(warnings, Is.Empty, string.Join("\n", warnings));
        Assert.That(styles.Select(s => s.Background.ToUpperInvariant()).Distinct().Count(), Is.EqualTo(styles.Length));
    }

    // 반투명 배경은 툴팁 패널 색 위에 합성한 색으로 잰다. 테두리 = 글자색이라 글자 대비가 곧 테두리 대비다.
    [Test]
    public void DefaultStyles_TextIsReadableOnBackground()
    {
        foreach (SkillTooltipTagStyle style in SkillTooltipTagStyles.DefaultStyles())
            AssertReadable(style.Background, style.Text, style.TagId);
        AssertReadable(SkillTooltipTagStyles.DefaultBackgroundHex, SkillTooltipTagStyles.DefaultTextHex, "기본색");
    }

    [Test]
    public void ProjectAsset_TextIsReadableOnBackground()
    {
        var styles = AssetDatabase.LoadAssetAtPath<SkillTooltipTagStyles>(AssetPath);
        Assert.That(styles, Is.Not.Null, AssetPath);
        foreach (SkillTooltipTagStyle style in styles.Entries)
            AssertReadable(style.Background, style.Text, style.TagId);
        AssertReadable(styles.DefaultBackground, styles.DefaultText, "기본색");
    }

    [Test]
    public void OverPanel_CompositesAlpha()
    {
        Color32 panel = new Color32(0x10, 0x20, 0x30, 0xFF);
        Assert.That(OverPanel(new Color32(0xFF, 0x00, 0x00, 0xFF), panel), Is.EqualTo(new Color32(0xFF, 0x00, 0x00, 0xFF)));
        Assert.That(OverPanel(new Color32(0xFF, 0x00, 0x00, 0x00), panel), Is.EqualTo(panel));
        Assert.That(OverPanel(new Color32(0xFF, 0xFF, 0xFF, 0x80), new Color32(0x00, 0x00, 0x00, 0xFF)).r, Is.EqualTo(0x80));
    }

    private static void AssertReadable(string background, string text, string label)
    {
        Assert.That(HexColor.TryParse(SkillTooltipTagStyles.TooltipPanelHex, out Color32 panel, out _), Is.True);
        Assert.That(HexColor.TryParse(background, out Color32 bg, out _), Is.True, label);
        Assert.That(HexColor.TryParse(text, out Color32 fg, out _), Is.True, label);
        Assert.That(ContrastRatio(OverPanel(bg, panel), fg), Is.GreaterThanOrEqualTo(4.5), label); // WCAG AA 본문
    }

    private static Color32 OverPanel(Color32 color, Color32 panel)
    {
        float a = color.a / 255f;
        byte Mix(byte c, byte p) => (byte)Mathf.RoundToInt(c * a + p * (1f - a));
        return new Color32(Mix(color.r, panel.r), Mix(color.g, panel.g), Mix(color.b, panel.b), 0xFF);
    }

    [Test]
    public void TryGetColors_Listed_ReturnsRow_Unlisted_ReturnsDefault()
    {
        SkillTooltipTagStyles styles = Make("Styles");
        styles.SetEntries("#101010", "#F0F0F0", new[] { new SkillTooltipTagStyle("stun", "#7E6B10", "#FFF2A8") });

        Assert.That(styles.TryGetColors(SkillTooltipTag.Stun, out Color32 bg, out Color32 fg), Is.True);
        Assert.That(bg, Is.EqualTo(new Color32(0x7E, 0x6B, 0x10, 0xFF)));
        Assert.That(fg, Is.EqualTo(new Color32(0xFF, 0xF2, 0xA8, 0xFF)));

        Assert.That(styles.TryGetColors(SkillTooltipTag.Heal, out bg, out fg), Is.False);
        Assert.That(bg, Is.EqualTo(new Color32(0x10, 0x10, 0x10, 0xFF)));
        Assert.That(fg, Is.EqualTo(new Color32(0xF0, 0xF0, 0xF0, 0xFF)));
    }

    [Test]
    public void TryGetColors_BadColorString_FallsBackToDefault()
    {
        SkillTooltipTagStyles styles = Make("Styles");
        styles.SetEntries("nope", "#F0F0F0", new[] { new SkillTooltipTagStyle("Stun", "#XYZ", "#FFF2A8") });

        Assert.That(styles.TryGetColors(SkillTooltipTag.Stun, out Color32 bg, out _), Is.True);
        Assert.That(bg, Is.EqualTo(SkillTooltipTagStyles.FallbackBackground));
    }

    [Test]
    public void Validate_UnknownId_Duplicate_BadColor_AreErrors_MissingTag_IsWarning()
    {
        var styles = new[]
        {
            new SkillTooltipTagStyle("Stun", "#7E6B10", "#FFF2A8"),
            new SkillTooltipTagStyle("STUN", "#000000", "#FFFFFF"),
            new SkillTooltipTagStyle("Nope", "#000000", "#FFFFFF"),
            new SkillTooltipTagStyle("Heal", "green", "#FFFFFF"),
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        SkillTooltipTagStyles.Validate(styles, "#3A3F4A", "#E3E6EB", errors, warnings);

        Assert.That(errors.Any(e => e.Contains("entries[1].tagId") && e.Contains("entries[0]")), Is.True, string.Join("\n", errors));
        Assert.That(errors.Any(e => e.Contains("entries[2].tagId") && e.Contains("Nope")), Is.True, string.Join("\n", errors));
        Assert.That(errors.Any(e => e.Contains("entries[3].background")), Is.True, string.Join("\n", errors));
        Assert.That(warnings.Any(w => w.Contains("'Knockback'")), Is.True, string.Join("\n", warnings));
        Assert.That(warnings.Any(w => w.Contains("'Stun'") || w.Contains("'Heal'")), Is.False, string.Join("\n", warnings));
    }

    [Test]
    public void TableBind_ValidColorsAndId_Applies()
    {
        SkillTooltipTagStyles styles = MakeFull("SkillTooltipTagStyles");
        styles.TryGetColors(SkillTooltipTag.Stun, out _, out _); // 캐시를 먼저 만든다 — 적용 뒤 새 값을 읽어야 한다
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = DataTableApplier.Bind(new[]
        {
            Entry("entries[1].background", "#112233"),
            Entry("entries[1].text", "#AABBCCDD"),
        }, new TestLookup(styles), issues);

        Assert.That(issues.HasErrors, Is.False, string.Join("\n", issues.Items));
        DataTableApplier.ApplyInMemory(writes);
        Assert.That(styles.TryGetColors(SkillTooltipTag.Stun, out Color32 bg, out Color32 fg), Is.True);
        Assert.That(bg, Is.EqualTo(new Color32(0x11, 0x22, 0x33, 0xFF)));
        Assert.That(fg, Is.EqualTo(new Color32(0xAA, 0xBB, 0xCC, 0xDD)));
    }

    [TestCase("entries[0].background", "#12345")]
    [TestCase("entries[0].text", "")]
    [TestCase("defaultBackground", "blue")]
    [TestCase("entries[0].tagId", "Nope")]
    [TestCase("entries[0].tagId", "")]
    public void TableBind_BadCell_IsError(string field, string value)
    {
        SkillTooltipTagStyles styles = MakeFull("SkillTooltipTagStyles");
        var issues = new DataTableIssues();

        List<DataTableWrite> writes = DataTableApplier.Bind(new[] { Entry(field, value) }, new TestLookup(styles), issues);

        Assert.That(issues.HasErrors, Is.True, string.Join("\n", issues.Items));
        Assert.That(writes, Is.Empty);
    }

    [Test]
    public void TableBind_DuplicateId_IsError_MissingTag_IsWarning()
    {
        SkillTooltipTagStyles styles = MakeFull("SkillTooltipTagStyles");
        var issues = new DataTableIssues();

        // entries[0] = Knockback → Stun 으로 바꾸면 Stun 두 줄 + Knockback 줄 없음.
        DataTableApplier.Bind(new[] { Entry("entries[0].tagId", "Stun") }, new TestLookup(styles), issues);

        string all = string.Join("\n", issues.Items);
        Assert.That(issues.Items.Any(i => i.Kind == DataTableIssueKind.Error && i.Message.Contains("Stun")), Is.True, all);
        Assert.That(issues.Items.Any(i => i.Kind == DataTableIssueKind.Warning && i.Message.Contains("'Knockback'")), Is.True, all);
    }

    [Test]
    public void ProjectAsset_CoversEveryTag_WithoutErrors()
    {
        var styles = AssetDatabase.LoadAssetAtPath<SkillTooltipTagStyles>(AssetPath);
        Assert.That(styles, Is.Not.Null, AssetPath + " 없음 — Tools/UI/스킬 툴팁 레이아웃만 갱신");
        var errors = new List<string>();
        var warnings = new List<string>();

        SkillTooltipTagStyles.Validate(styles.Entries, styles.DefaultBackground, styles.DefaultText, errors, warnings);

        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(warnings, Is.Empty, string.Join("\n", warnings));
    }

    [Test]
    public void SheetAttribute_IsUiSheet()
    {
        var sheet = (DataTableSheetAttribute)Attribute.GetCustomAttribute(typeof(SkillTooltipTagStyles), typeof(DataTableSheetAttribute));
        Assert.That(sheet, Is.Not.Null);
        Assert.That(sheet.Sheet, Is.EqualTo("UI"));
    }

    private SkillTooltipTagStyles Make(string name)
    {
        var styles = ScriptableObject.CreateInstance<SkillTooltipTagStyles>();
        styles.name = name;
        created.Add(styles);
        return styles;
    }

    private SkillTooltipTagStyles MakeFull(string name)
    {
        SkillTooltipTagStyles styles = Make(name);
        styles.SetEntries(SkillTooltipTagStyles.DefaultBackgroundHex, SkillTooltipTagStyles.DefaultTextHex,
            SkillTooltipTagStyles.DefaultStyles());
        return styles;
    }

    private static DataTableEntry Entry(string field, string value) =>
        new DataTableEntry(nameof(SkillTooltipTagStyles), "SkillTooltipTagStyles", field, value,
            $"T.xlsx › {nameof(SkillTooltipTagStyles)}!{field}");

    private static double ContrastRatio(Color32 a, Color32 b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color32 c)
    {
        double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.r) + 0.7152 * Channel(c.g) + 0.0722 * Channel(c.b);
    }
}
