using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

// 할 일 168 — 문구 키워드 스타일(heal 등)이 TMP 기본 시트에도 있어 툴팁 밖 TMP(특성 카드 등)에서도 태그가 색으로 풀린다.
// 171 후속 — 기본 시트 heal 만 의도적으로 다르다: 같은 색 + 검은 아웃라인 머티리얼 프리셋(밝은 특성 카드 위 초록 가독성).
// 전투 HUD 스킬 툴팁 시트(SkillTooltipStyles)는 그대로.
public sealed class TmpDefaultStyleSheetTests
{
    private const string TooltipStylePath = "Assets/2.Prefabs/UI/SkillTooltipGenerated/SkillTooltipStyles.asset";
    private const string FontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";
    private const string HealOutlineName = "SeoulAlrimTTF-Medium SDF - Heal Outline";
    private static readonly string[] KeywordStyles = { "shield", "stun", "overheat", "slow" };

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
    public void DefaultSheet_Heal_IsTooltipColorPlusOutlinePreset()
    {
        TMP_Style tooltip = AssetDatabase.LoadAssetAtPath<TMP_StyleSheet>(TooltipStylePath).GetStyle("heal");
        TMP_Style heal = TMP_Settings.defaultStyleSheet.GetStyle("heal");
        Assert.AreEqual("<color=#72D572>", tooltip.styleOpeningDefinition, "툴팁 시트 heal 은 색만(불변)");
        Assert.AreEqual("</color>", tooltip.styleClosingDefinition);
        Assert.AreEqual($"<color=#72D572><material=\"{HealOutlineName}\">", heal.styleOpeningDefinition);
        Assert.AreEqual("</material></color>", heal.styleClosingDefinition);
    }

    [Test]
    public void HealOutlinePreset_LoadsFromTmpPath_SameAtlasWithOutline()
    {
        // <material="이름"> 은 Resources/{TMP 기본 폰트 경로}/이름 으로 찾는다 — git 쪽 Assets/Fonts/SeoulAlrim/Resources/Fonts & Materials/.
        var preset = Resources.Load<Material>(TMP_Settings.defaultFontAssetPath + HealOutlineName);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Assert.IsNotNull(preset, HealOutlineName);
        Assert.IsNotNull(font, FontPath);
        Assert.AreSame(font.atlasTexture, preset.GetTexture(ShaderUtilities.ID_MainTex), "폰트와 같은 아틀라스");
        Assert.AreEqual(font.material.shader, preset.shader);
        Assert.IsTrue(preset.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), "아웃라인 켬");
        Assert.AreEqual((Color32)Color.black, (Color32)preset.GetColor(ShaderUtilities.ID_OutlineColor));
        Assert.Greater(preset.GetFloat(ShaderUtilities.ID_OutlineWidth), 0f);
    }

    [Test]
    public void TraitDescription_HealTag_RendersAsColor()
    {
        go = new GameObject("TmpStyleTest");
        var text = go.AddComponent<TextMeshPro>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
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
        Assert.AreEqual(HealOutlineName, text.textInfo.characterInfo[index].material.name, "회 = 아웃라인 프리셋");
        Assert.AreNotEqual(HealOutlineName, text.textInfo.characterInfo[index + 2].material.name, "닫는 태그 뒤는 원래 머티리얼");
    }
}
