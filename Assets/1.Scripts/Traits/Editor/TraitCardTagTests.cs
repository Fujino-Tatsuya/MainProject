using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 할 일 212 — 특성 카드 태그 칩. 정의 에셋의 태그 칸(<see cref="TraitDefinition.TagIds"/>, xlsx Traits_&lt;캐릭터&gt; <c>tags</c> 행)이
/// 태그 ID 문법에 맞는지, TraitUI.prefab 카드 원본에 칩 줄·칩 원본·색 표가 이어져 있는지 확인한다.
/// </summary>
public class TraitCardTagTests
{
    private const string TraitUiPrefab = "Assets/2.Prefabs/UI/Tutorial/TraitUI.prefab";

    [Test]
    public void ProjectTraits_TagIds_ParseWithoutErrors()
    {
        var failures = new List<string>();
        int tagged = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:TraitDefinition"))
        {
            var trait = AssetDatabase.LoadAssetAtPath<TraitDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (trait == null)
                continue;

            var tags = new List<SkillTooltipTag>();
            if (!SkillTooltipTags.TryParse(trait.TagIds, tags, out string error))
                failures.Add($"{trait.name}: {error}");
            if (tags.Count > 0)
                tagged++;
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
        Assert.Greater(tagged, 0, "태그가 붙은 특성이 하나도 없다 — 정의 에셋 tags 칸이 비었다");
    }

    [Test]
    public void TraitUiCard_HasTagRowAndChipTemplate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TraitUiPrefab);
        Assert.IsNotNull(prefab, TraitUiPrefab);
        TraitCardView card = prefab.GetComponentInChildren<TraitCardView>(true);
        Assert.IsNotNull(card, "TraitCardView");

        var so = new SerializedObject(card);
        var row = so.FindProperty("tagRow").objectReferenceValue as RectTransform;
        var chip = so.FindProperty("tagChipTemplate").objectReferenceValue as RectTransform;
        Assert.IsNotNull(row, "tagRow");
        Assert.IsNotNull(chip, "tagChipTemplate");
        Assert.IsNotNull(so.FindProperty("tagStyles").objectReferenceValue as SkillTooltipTagStyles, "tagStyles(칩 색 표)");
        Assert.AreSame(row, chip.parent, "칩 원본은 칩 줄 안에 둔다(복제가 같은 줄에 놓인다)");
        Assert.IsFalse(chip.gameObject.activeSelf, "칩 원본은 꺼 둔다");
        Assert.IsTrue(row.IsChildOf(card.transform), "칩 줄은 카드 안");
        Assert.IsNotNull(chip.GetComponent<Image>(), "칩 배경 Image");
        Assert.IsNotNull(chip.Find(SkillTooltipView.TagChipRingName)?.GetComponent<Image>(), "칩 테두리 Ring Image");
        Assert.IsNotNull(chip.GetComponentInChildren<TMP_Text>(true), "칩 글자 TMP");
        Assert.Greater(chip.rect.height, 0f, "칩 높이 = 줄 높이");
    }
}
