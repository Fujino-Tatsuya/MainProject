using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class DashCooldownHudTests
{
    private const string PrefabPath = "Assets/2.Prefabs/UI/CombatHUD.prefab";
    private const string SlotPath = "Layer_Combat/SkillBar/Slot_Dash";
    private const string SlotPrefabPath = "Assets/2.Prefabs/UI/Slot_.prefab";

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

    [Test]
    public void Prefab_HasExistingSkillSlotWiredAsDashAtSeventyFivePercent()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform slot = root.transform.Find(SlotPath);
            Assert.That(slot, Is.Not.Null);
            Assert.That(((RectTransform)slot).anchoredPosition.x, Is.EqualTo(300f).Within(0.0001f));
            Assert.That(slot.localScale.x, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(slot.localScale.y, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(slot.localScale.z, Is.EqualTo(0.75f).Within(0.0001f));

            // 새 슬롯을 만들지 않고 기존 스킬 슬롯 프리팹(Slot_)을 그대로 쓴다.
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(slot.gameObject);
            Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(SlotPrefabPath));
            Assert.That(slot.gameObject.activeSelf, Is.True);

            Image icon = slot.Find("Icon_mask/Icon_Dash")?.GetComponent<Image>();
            Image fill = slot.Find("CooldownFill")?.GetComponent<Image>();
            TMP_Text remaining = slot.Find("Cooldown")?.GetComponent<TMP_Text>();

            Assert.That(icon?.sprite?.name, Is.EqualTo("Common_DashIcon"));
            // Slot_ 의 Icon_mask 는 Y 180° 반전이라 아이콘이 그대로면 «« 로 뒤집혀 보인다.
            Assert.That(Quaternion.Angle(slot.rotation, icon.transform.rotation), Is.LessThan(0.01f));
            Assert.That(fill, Is.Not.Null);
            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(remaining, Is.Not.Null);

            DashCooldownHUD hud = root.GetComponentInChildren<DashCooldownHUD>(true);
            SerializedObject so = new SerializedObject(hud);
            Assert.That(so.FindProperty("cooldownFill").objectReferenceValue, Is.SameAs(fill));
            Assert.That(so.FindProperty("remainingText").objectReferenceValue, Is.SameAs(remaining));

            CombatHUD combatHud = root.GetComponent<CombatHUD>();
            Assert.That(new SerializedObject(combatHud).FindProperty("dashCooldownHUD").objectReferenceValue,
                Is.SameAs(hud));

            string[] existingSlots = { "Slot_P", "Slot_Q", "Slot_E", "Slot_R", "Slot_RMB" };
            float[] existingX = { -200f, -100f, 0f, 100f, 200f };
            for (int i = 0; i < existingSlots.Length; i++)
            {
                RectTransform existing = root.transform.Find($"Layer_Combat/SkillBar/{existingSlots[i]}") as RectTransform;
                Assert.That(existing, Is.Not.Null, existingSlots[i]);
                Assert.That(existing.anchoredPosition.x, Is.EqualTo(existingX[i]).Within(0.0001f), existingSlots[i]);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
