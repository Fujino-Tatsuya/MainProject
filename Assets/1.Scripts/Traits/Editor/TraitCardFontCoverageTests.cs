using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 167 — 특성 카드 글자 칸(이름 = 서울알림 Heavy, 분류·설명 = Medium)의 글꼴 에셋이 프로젝트 특성 문구의 글자를
/// 폴백 없이 모두 갖는지 확인한다. 실패하면 메시지의 글자를 그 글꼴의 Static 아틀라스에 추가해 다시 굽는다.
/// </summary>
public class TraitCardFontCoverageTests
{
    private const string TraitUiPrefab = "Assets/2.Prefabs/UI/Tutorial/TraitUI.prefab";
    private static readonly Regex Tag = new Regex("<[^>]*>");

    [Test]
    public void CardTexts_HaveAllGlyphsInCardFonts()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TraitUiPrefab);
        Assert.IsNotNull(prefab, TraitUiPrefab);
        TraitCardView card = prefab.GetComponentInChildren<TraitCardView>(true);
        Assert.IsNotNull(card, "TraitCardView");
        var view = new SerializedObject(card);

        var names = new StringBuilder();
        var categories = new StringBuilder();
        var descriptions = new StringBuilder();
        foreach (string guid in AssetDatabase.FindAssets("t:TraitDefinition"))
        {
            var trait = AssetDatabase.LoadAssetAtPath<TraitDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (trait == null)
                continue;
            names.Append(trait.DisplayName);
            categories.Append(TraitUiStrings.FormatCategory(trait.Category));
            if (TraitTooltip.FormatDescription(trait, null, false, out string text, out _))
                descriptions.Append(Tag.Replace(text, string.Empty));
        }

        var failures = new List<string>();
        Check(view, "displayName", names.ToString(), failures);
        Check(view, "category", categories.ToString(), failures);
        Check(view, "description", descriptions.ToString(), failures);
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    private static void Check(SerializedObject view, string field, string text, List<string> failures)
    {
        var label = view.FindProperty(field).objectReferenceValue as TMP_Text;
        if (label == null || label.font == null)
        {
            failures.Add($"{field}: 글자 칸 또는 글꼴이 비어 있다");
            return;
        }

        var missing = new List<char>();
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c) && !missing.Contains(c) && !label.font.HasCharacter(c, false, false))
                missing.Add(c);
        }

        if (missing.Count > 0)
            failures.Add($"{field} ({AssetDatabase.GetAssetPath(label.font)}) 누락 {missing.Count}자: {new string(missing.ToArray())}");
    }
}
