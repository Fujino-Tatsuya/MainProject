using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 167 후속 — 특성 카드 설명·획득 아이콘 호버 툴팁 설명 TMP 가 <c>{dmg}</c> 의 sprite 태그(<see cref="SkillTooltipFormatter"/>)를
/// 그릴 sprite asset 을 갖는지 확인한다. 비어 있거나 이름이 없으면 TMP 가 노란 ? 로 그린다.
/// </summary>
public class TraitDescriptionSpriteTests
{
    private const string TraitUiPrefab = "Assets/2.Prefabs/UI/Tutorial/TraitUI.prefab";
    private static readonly Regex SpriteName = new Regex("<sprite name=\"([^\"]+)\"");

    [Test]
    public void DescriptionTexts_CanDrawDamageSprites()
    {
        Assert.IsTrue(SkillTooltipFormatter.TryFormat("{dmg}", null, new SkillTooltipDamage(10, 12, 1f, 1.2f), true,
            out string text, out bool hasDamage, out string error), error);
        Assert.IsTrue(hasDamage);
        var names = new List<string>();
        foreach (Match match in SpriteName.Matches(text))
        {
            if (!names.Contains(match.Groups[1].Value))
                names.Add(match.Groups[1].Value);
        }
        Assert.IsNotEmpty(names, $"FormatDamage 출력에 sprite 태그가 없다: {text}");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TraitUiPrefab);
        Assert.IsNotNull(prefab, TraitUiPrefab);
        TraitSelectionView view = prefab.GetComponent<TraitSelectionView>();
        TraitCardView card = prefab.GetComponentInChildren<TraitCardView>(true);
        Assert.IsNotNull(view, "TraitSelectionView");
        Assert.IsNotNull(card, "TraitCardView");

        var failures = new List<string>();
        Check("카드 설명", new SerializedObject(card).FindProperty("description").objectReferenceValue as TMP_Text, names, failures);
        Check("호버 툴팁 설명", new SerializedObject(view).FindProperty("tooltipDescription").objectReferenceValue as TMP_Text, names, failures);
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    private static void Check(string label, TMP_Text text, List<string> names, List<string> failures)
    {
        if (text == null)
        {
            failures.Add($"{label}: TMP 칸이 비어 있다");
            return;
        }

        TMP_SpriteAsset asset = text.spriteAsset;
        if (asset == null)
        {
            failures.Add($"{label}: spriteAsset 이 비어 있다");
            return;
        }

        asset.UpdateLookupTables();
        foreach (string name in names)
        {
            if (asset.GetSpriteIndexFromName(name) < 0)
                failures.Add($"{label}: {AssetDatabase.GetAssetPath(asset)} 에 sprite '{name}' 이 없다");
        }
    }
}
