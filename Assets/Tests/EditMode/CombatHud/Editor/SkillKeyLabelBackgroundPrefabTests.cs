using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class SkillKeyLabelBackgroundPrefabTests
{
    private const string PrefabPath = "Assets/2.Prefabs/UI/CombatHUD.prefab";
    private const string SkillBarPath = "Layer_Combat/SkillBar";
    private static readonly string[] SlotNames = { "Slot_P", "Slot_Q", "Slot_E", "Slot_RMB", "Slot_R" };

    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        root = PrefabUtility.LoadPrefabContents(PrefabPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            PrefabUtility.UnloadPrefabContents(root);
    }

    [Test]
    public void KeyBackground_IsSlicedNonRaycastImage()
    {
        foreach (string slotName in SlotNames)
        {
            Image background = FindBackground(slotName);

            Assert.That(background.type, Is.EqualTo(Image.Type.Sliced), slotName);
            Assert.That(background.raycastTarget, Is.False, slotName);
            Assert.That(background.sprite, Is.Not.Null, slotName);
        }
    }

    [Test]
    public void KeyBackground_WidensForLongerKeyText()
    {
        foreach (string slotName in SlotNames)
        {
            Image background = FindBackground(slotName);
            TMP_Text key = background.transform.Find("Key").GetComponent<TMP_Text>();
            var rect = (RectTransform)background.transform;

            float shortWidth = LayoutWidth(rect, key, "Q");
            float longWidth = LayoutWidth(rect, key, "Shift");

            Assert.That(longWidth, Is.GreaterThan(shortWidth), slotName);
        }
    }

    [Test]
    public void SkillCooldownHud_ExcludesKeyBackgroundFromBlockedTint()
    {
        var hud = root.GetComponentInChildren<SkillCooldownHUD>(true);
        SerializedProperty slots = new SerializedObject(hud).FindProperty("slots");

        for (int i = 0; i < slots.arraySize; i++)
        {
            Object background = slots.GetArrayElementAtIndex(i).FindPropertyRelative("keyBackground").objectReferenceValue;
            Assert.That(background, Is.Not.Null, $"slots[{i}]");
            Assert.That(((Component)background).name, Is.EqualTo("KeyBg"), $"slots[{i}]");
        }
    }

    private Image FindBackground(string slotName)
    {
        Transform background = root.transform.Find($"{SkillBarPath}/{slotName}/KeyBg");
        Assert.That(background, Is.Not.Null, slotName);
        return background.GetComponent<Image>();
    }

    private static float LayoutWidth(RectTransform rect, TMP_Text key, string text)
    {
        key.text = text;
        key.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        return rect.rect.width;
    }
}
