using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

// 할 일 168 — 문구 키워드 스타일(heal 등)이 TMP 기본 시트에도 있어 툴팁 밖 TMP(특성 카드 등)에서도 태그가 색으로 풀린다.
public sealed class TmpDefaultStyleSheetTests
{
    private const string TooltipStylePath = "Assets/2.Prefabs/UI/SkillTooltipGenerated/SkillTooltipStyles.asset";
    private static readonly string[] KeywordStyles = { "heal", "shield", "stun", "overheat", "slow" };

    private GameObject go;

    [TearDown]
    public void TearDown()
    {
        if (go != null)
            Object.DestroyImmediate(go);
    }

    [Test]
    public void DefaultSheet_HasKeywordStyles_SameAsTooltipSheet()
    {
        TMP_StyleSheet defaultSheet = TMP_Settings.defaultStyleSheet;
        TMP_StyleSheet tooltipSheet = AssetDatabase.LoadAssetAtPath<TMP_StyleSheet>(TooltipStylePath);
        Assert.IsNotNull(defaultSheet, "TMP Settings 기본 스타일 시트");
        Assert.IsNotNull(tooltipSheet, TooltipStylePath);

        foreach (string name in KeywordStyles)
        {
            TMP_Style expected = tooltipSheet.GetStyle(name);
            TMP_Style actual = defaultSheet.GetStyle(name);
            Assert.IsNotNull(expected, $"툴팁 시트 {name}");
            Assert.IsNotNull(actual, $"기본 시트 {name}");
            Assert.AreEqual(expected.styleOpeningDefinition, actual.styleOpeningDefinition, name);
            Assert.AreEqual(expected.styleClosingDefinition, actual.styleClosingDefinition, name);
        }
    }

    [Test]
    public void TraitDescription_HealTag_RendersAsColor()
    {
        go = new GameObject("TmpStyleTest");
        var text = go.AddComponent<TextMeshPro>();
        text.text = "가장 낮은 아군 한 명을 <style=heal>회복</style>합니다.";
        text.ForceMeshUpdate(true, true);

        string parsed = text.GetParsedText();
        StringAssert.DoesNotContain("<style", parsed);
        StringAssert.DoesNotContain("</style>", parsed);

        int index = parsed.IndexOf('회');
        Assert.GreaterOrEqual(index, 0, parsed);
        Assert.IsTrue(ColorUtility.TryParseHtmlString("#72D572", out Color heal));
        Assert.AreEqual((Color32)heal, text.textInfo.characterInfo[index].color, "회");
        Assert.AreEqual((Color32)heal, text.textInfo.characterInfo[index + 1].color, "복");
        Assert.AreNotEqual((Color32)heal, text.textInfo.characterInfo[index + 2].color, "닫는 태그 뒤는 원래 색");
    }
}
