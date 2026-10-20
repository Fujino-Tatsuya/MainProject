using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class SkillKeyLabelRebindTests
{
    private const string PrefabPath = "Assets/2.Prefabs/UI/CombatHUD.prefab";
    private const string OverridesKey = "Settings.Input.BindingOverrides";

    private GameObject root;
    private bool hadSavedOverrides;
    private string savedOverrides;
    private string assetOverrides;

    [SetUp]
    public void SetUp()
    {
        // 실제 사용자 설정(PlayerPrefs)과 전역 액션 오버라이드를 떠 두고 TearDown 에서 되돌린다.
        hadSavedOverrides = PlayerPrefs.HasKey(OverridesKey);
        savedOverrides = PlayerPrefs.GetString(OverridesKey, string.Empty);
        assetOverrides = InputSystem.actions.SaveBindingOverridesAsJson();
        root = PrefabUtility.LoadPrefabContents(PrefabPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            PrefabUtility.UnloadPrefabContents(root);

        InputSystem.actions.RemoveAllBindingOverrides();
        if (!string.IsNullOrEmpty(assetOverrides))
            InputSystem.actions.LoadBindingOverridesFromJson(assetOverrides);

        if (hadSavedOverrides)
            PlayerPrefs.SetString(OverridesKey, savedOverrides);
        else
            PlayerPrefs.DeleteKey(OverridesKey);
        PlayerPrefs.Save();
    }

    [TestCase(PlayerSkillSlot.Main, "Player/SkillMain")]
    [TestCase(PlayerSkillSlot.Sub, "Player/SkillSub")]
    [TestCase(PlayerSkillSlot.Interrupt, "Player/Interrupt")]
    [TestCase(PlayerSkillSlot.Ultimate, "Player/SkillUltimate")]
    public void KeyLabel_UsesSettingsDisplayNameOfSlotAction(PlayerSkillSlot slot, string actionPath)
    {
        Assert.That(SkillCooldownHUD.ActionPath(slot), Is.EqualTo(actionPath));
        Assert.That(KeyBindingConfig.RebindableActions, Does.Contain(actionPath));
        Assert.That(SkillCooldownHUD.KeyLabel(slot), Is.EqualTo(KeyBindingConfig.DisplayName(actionPath)));
    }

    [Test]
    public void PrefabSlots_WireKeyTextToSlotKeyLabel()
    {
        SerializedProperty slots = SlotsProperty(Hud());
        Assert.That(slots.arraySize, Is.EqualTo(4));

        for (int i = 0; i < slots.arraySize; i++)
        {
            SerializedProperty widget = slots.GetArrayElementAtIndex(i);
            var keyText = widget.FindPropertyRelative("keyText").objectReferenceValue as TMP_Text;
            var background = widget.FindPropertyRelative("keyBackground").objectReferenceValue as Component;

            Assert.That(keyText, Is.Not.Null, $"slots[{i}]");
            Assert.That(keyText.name, Is.EqualTo("Key"), $"slots[{i}]");
            Assert.That(keyText.transform.parent, Is.EqualTo(background.transform), $"slots[{i}]");
        }
    }

    [Test]
    public void KeyBindingChanged_RefreshesSlotKeyText()
    {
        SkillCooldownHUD hud = Hud();
        TMP_Text mainKey = KeyText(hud, PlayerSkillSlot.Main);

        // 프리팹 미리보기 씬은 Unity 메시지를 돌리지 않는다 — OnEnable/OnDisable 을 직접 부른다.
        Invoke(hud, "OnEnable");
        try
        {
            InputAction action = InputSystem.actions.FindAction("Player/SkillMain");
            action.ApplyBindingOverride(KeyBindingConfig.KeyboardMouseBindingIndex(action), "<Keyboard>/leftShift");
            KeyBindingConfig.Save();

            Assert.That(mainKey.text, Is.EqualTo(KeyBindingConfig.DisplayName("Player/SkillMain")));
            Assert.That(mainKey.text, Is.Not.EqualTo("Q"));

            KeyBindingConfig.ResetAll();

            Assert.That(mainKey.text, Is.EqualTo("Q"));
        }
        finally
        {
            Invoke(hud, "OnDisable");
        }
    }

    private SkillCooldownHUD Hud() => root.GetComponentInChildren<SkillCooldownHUD>(true);

    private static SerializedProperty SlotsProperty(SkillCooldownHUD hud) => new SerializedObject(hud).FindProperty("slots");

    private static TMP_Text KeyText(SkillCooldownHUD hud, PlayerSkillSlot slot)
    {
        SerializedProperty slots = SlotsProperty(hud);
        for (int i = 0; i < slots.arraySize; i++)
        {
            SerializedProperty widget = slots.GetArrayElementAtIndex(i);
            if (widget.FindPropertyRelative("slot").enumValueIndex == (int)slot)
                return (TMP_Text)widget.FindPropertyRelative("keyText").objectReferenceValue;
        }

        Assert.Fail($"{slot} 슬롯이 없다.");
        return null;
    }

    private static void Invoke(SkillCooldownHUD hud, string method) =>
        typeof(SkillCooldownHUD).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
}
