using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class DashCooldownHudTests
{
    private const string PrefabPath = "Assets/2.Prefabs/UI/CombatHUD.prefab";
    private const string SlotPath = "Layer_Combat/SkillBar/Slot_Dash";
    private const string CooldownOutlineMaterialPath = "Assets/2.Prefabs/UI/LiberationSans SDF - Cooldown Outline.mat";

    [Test]
    public void RemainingAndChargeFormatting_FollowsDashHudRules()
    {
        Assert.That(DashCooldownHUD.FormatRemaining(0f), Is.Empty);
        Assert.That(DashCooldownHUD.FormatRemaining(0.24f), Is.EqualTo("0.2"));
        Assert.That(DashCooldownHUD.FormatRemaining(1.01f), Is.EqualTo("2"));
        Assert.That(DashCooldownHUD.ShouldShowCharge(1), Is.False);
        Assert.That(DashCooldownHUD.ShouldShowCharge(2), Is.True);
        Assert.That(DashCooldownHUD.FormatCharge(3, 2), Is.EqualTo("2/2"));
    }


    // 배치·크기·스프라이트는 사용자가 CombatHUD 에서 조정한다 — 여기서는 쿨타임 오버레이 배선만 고정한다.
    [Test]
    public void Prefab_DashSlotUsesIconMaskCooldownFill()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform slot = root.transform.Find(SlotPath);
            Assert.That(slot, Is.Not.Null);

            Image fill = AssertIconMaskCooldownFill(slot, "Icon_mask/Icon_Dash", out _);
            TMP_Text remaining = slot.Find("Cooldown")?.GetComponent<TMP_Text>();
            Assert.That(remaining, Is.Not.Null);

            DashCooldownHUD hud = root.GetComponentInChildren<DashCooldownHUD>(true);
            SerializedObject so = new SerializedObject(hud);
            Assert.That(so.FindProperty("cooldownFill").objectReferenceValue, Is.SameAs(fill));
            Assert.That(so.FindProperty("remainingText").objectReferenceValue, Is.SameAs(remaining));

            CombatHUD combatHud = root.GetComponent<CombatHUD>();
            Assert.That(new SerializedObject(combatHud).FindProperty("dashCooldownHUD").objectReferenceValue,
                Is.SameAs(hud));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void Prefab_SkillAndPassiveSlotsUseIconMaskCooldownFill()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform bar = root.transform.Find("Layer_Combat/SkillBar");
            SerializedProperty slots = new SerializedObject(bar.GetComponent<SkillCooldownHUD>()).FindProperty("slots");
            (string slot, string icon)[] skillSlots =
            {
                ("Slot_Q", "Icon_mask/Icon_Q"), ("Slot_E", "Icon_mask/Icon_E"),
                ("Slot_RMB", "Icon_mask/Icon_RMB"), ("Slot_R", "Icon_mask/Icon_Ult"),
            };
            Assert.That(slots.arraySize, Is.EqualTo(skillSlots.Length));

            for (int i = 0; i < skillSlots.Length; i++)
            {
                Image fill = AssertIconMaskCooldownFill(bar.Find(skillSlots[i].slot), skillSlots[i].icon, out Image overlayIcon);
                SerializedProperty widget = slots.GetArrayElementAtIndex(i);
                Assert.That(widget.FindPropertyRelative("cooldownFill").objectReferenceValue, Is.SameAs(fill), skillSlots[i].slot);
                Assert.That(widget.FindPropertyRelative("cooldownIcon").objectReferenceValue, Is.SameAs(overlayIcon), skillSlots[i].slot);
            }

            SerializedObject passive = new SerializedObject(bar.GetComponent<PassiveHUD>());
            Image passiveFill = AssertIconMaskCooldownFill(bar.Find("Slot_Passive"), "Icon_mask/Icon_Passive", out Image passiveIcon);
            Assert.That(passive.FindProperty("cooldownFill").objectReferenceValue, Is.SameAs(passiveFill));
            Assert.That(passive.FindProperty("cooldownIcon").objectReferenceValue, Is.SameAs(passiveIcon));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void Prefab_CooldownNumbersUseBlackOutlineMaterial()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform bar = root.transform.Find("Layer_Combat/SkillBar");
            int count = 0;
            foreach (Transform slot in bar)
            {
                TMP_Text text = slot.Find("Cooldown")?.GetComponent<TMP_Text>();
                if (text == null)
                    continue;

                Material mat = text.fontSharedMaterial;
                Assert.That(AssetDatabase.GetAssetPath(mat), Is.EqualTo(CooldownOutlineMaterialPath), slot.name);
                Assert.That(mat.IsKeywordEnabled("OUTLINE_ON"), Is.True, slot.name);
                Assert.That(mat.GetColor("_OutlineColor"), Is.EqualTo(Color.black), slot.name);
                Assert.That(mat.GetTexture("_MainTex"), Is.SameAs(text.font.atlasTexture), slot.name);
                count++;
            }
            Assert.That(count, Is.EqualTo(6));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Slot_Dash 와 같은 구조: 옛 CooldownFill 대신 Filled 마스크 <c>Icon_mask_CooldownFill</c> 아래 아이콘 사본.
    /// 사본은 원래 아이콘과 같은 방향·크기여야 겹쳐 보인다(반전 슬롯은 부모·자식 스케일 곱으로 비교).
    /// </summary>
    private static Image AssertIconMaskCooldownFill(Transform slot, string iconPath, out Image overlayIcon)
    {
        Assert.That(slot, Is.Not.Null);
        Assert.That(slot.Find("CooldownFill"), Is.Null, $"{slot.name}: 옛 CooldownFill 이 남아 있다");

        Transform overlay = slot.Find("Icon_mask_CooldownFill");
        Assert.That(overlay, Is.Not.Null, slot.name);
        Assert.That(overlay.GetComponent<Mask>(), Is.Not.Null, slot.name);
        Image fill = overlay.GetComponent<Image>();
        Assert.That(fill.type, Is.EqualTo(Image.Type.Filled), slot.name);

        Transform icon = slot.Find(iconPath);
        Assert.That(icon, Is.Not.Null, $"{slot.name}/{iconPath}");
        Assert.That(overlay.childCount, Is.EqualTo(1), slot.name);
        Transform copy = overlay.GetChild(0);
        overlayIcon = copy.GetComponent<Image>();
        Assert.That(overlayIcon, Is.Not.Null, slot.name);

        Assert.That(Vector3.Distance(copy.lossyScale, icon.lossyScale), Is.LessThan(0.0001f), $"{slot.name}: 사본 크기·반전");
        Assert.That(Quaternion.Angle(copy.rotation, icon.rotation), Is.LessThan(0.01f), $"{slot.name}: 사본 회전");
        Assert.That(((RectTransform)copy).sizeDelta, Is.EqualTo(((RectTransform)icon).sizeDelta), slot.name);
        return fill;
    }
}
