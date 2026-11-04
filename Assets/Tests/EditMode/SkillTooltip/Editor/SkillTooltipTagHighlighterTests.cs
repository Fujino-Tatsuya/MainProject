using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 할 일 208 — 설명문 태그 이름 칩 색 칠하기(SkillTooltipTagHighlighter). 러너 = 데이터 테이블 EditMode 테스트.
public sealed class SkillTooltipTagHighlighterTests
{
    private const string AssetPath = "Assets/2.Prefabs/UI/SkillTooltipGenerated/SkillTooltipTagStyles.asset";
    private const string Red = "#FF0000";
    private const string Blue = "#0000FF";
    private const string Green = "#00FF00";

    private static readonly SkillTooltipTagHighlighter.Term[] Terms =
    {
        new SkillTooltipTagHighlighter.Term("저지", Red),
        new SkillTooltipTagHighlighter.Term("저지불가", Blue),
        new SkillTooltipTagHighlighter.Term("넉백", Green),
        new SkillTooltipTagHighlighter.Term("Heal", Red),
        new SkillTooltipTagHighlighter.Term("Overheat", Blue),
    };

    private static string Hl(string text) => SkillTooltipTagHighlighter.Highlight(text, Terms);

    [Test]
    public void PlainWord_IsWrapped()
    {
        Assert.That(Hl("적을 넉백시킵니다."), Is.EqualTo("적을 <color=#00FF00>넉백</color>시킵니다."));
    }

    [Test]
    public void LongerWord_WinsOverPrefix()
    {
        Assert.That(Hl("저지불가 상태"), Is.EqualTo("<color=#0000FF>저지불가</color> 상태"));
        Assert.That(Hl("저지 당함"), Is.EqualTo("<color=#FF0000>저지</color> 당함"));
    }

    [Test]
    public void InsideExistingColorOrStyle_NotWrapped()
    {
        Assert.That(Hl("<style=heal>넉백</style> 넉백"),
            Is.EqualTo("<style=heal>넉백</style> <color=#00FF00>넉백</color>"));
        Assert.That(Hl("<color=#FFFFFF>넉백 12</color>"), Is.EqualTo("<color=#FFFFFF>넉백 12</color>"));
    }

    [Test]
    public void TagMarkupAndPlaceholders_Untouched()
    {
        Assert.That(Hl("<sprite name=\"넉백\">"), Is.EqualTo("<sprite name=\"넉백\">"));
        Assert.That(Hl("{넉백} 넉백"), Is.EqualTo("{넉백} <color=#00FF00>넉백</color>"));
        Assert.That(Hl("<b>넉백</b>"), Is.EqualTo("<b><color=#00FF00>넉백</color></b>"));
    }

    [Test]
    public void AlreadyHighlighted_IsIdempotent()
    {
        string once = Hl("넉백 저지불가 Overheat");
        Assert.That(Hl(once), Is.EqualTo(once));
    }

    [Test]
    public void Ascii_MatchesWholeWordOnly_CaseInsensitive()
    {
        Assert.That(Hl("Healing heals"), Is.EqualTo("Healing heals"));
        Assert.That(Hl("you overheat."), Is.EqualTo("you <color=#0000FF>overheat</color>."));
        Assert.That(Hl("Heal 5"), Is.EqualTo("<color=#FF0000>Heal</color> 5"));
    }

    [Test]
    public void NumbersAndEmpty_Unchanged()
    {
        Assert.That(Hl("120 (35%)"), Is.EqualTo("120 (35%)"));
        Assert.That(Hl(""), Is.EqualTo(""));
        Assert.That(Hl(null), Is.Null);
        Assert.That(SkillTooltipTagHighlighter.Highlight("넉백", (SkillTooltipTagStyles)null), Is.EqualTo("넉백"));
    }

    [Test]
    public void ProjectStyles_CoverAllTags_WithChipColors()
    {
        var styles = AssetDatabase.LoadAssetAtPath<SkillTooltipTagStyles>(AssetPath);
        Assert.That(styles, Is.Not.Null, AssetPath);
        List<SkillTooltipTagHighlighter.Term> terms = SkillTooltipTagHighlighter.TermsFrom(styles);
        Assert.That(terms.Count, Is.EqualTo(System.Enum.GetValues(typeof(SkillTooltipTag)).Length));

        styles.TryGetColors(SkillTooltipTag.Knockback, out _, out Color32 text);
        string expected = "#" + ColorUtility.ToHtmlStringRGB(text);
        string knockback = SkillTooltipTags.Name(SkillTooltipTag.Knockback).Text;
        Assert.That(SkillTooltipTagHighlighter.Highlight(knockback, styles),
            Is.EqualTo($"<color={expected}>{knockback}</color>"));
    }
}
