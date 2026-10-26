using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 메뉴·설정창 반응 규칙(경석 10-26 6차) — UiFeedbackStyle 이 붙이는 것과 ButtonLabelStateColor 상태 색.
public sealed class UiFeedbackStyleTests
{
    private GameObject _root;

    [SetUp]
    public void SetUp() => _root = new GameObject("Root", typeof(RectTransform));

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_root);

    private T Child<T>(string name, Transform parent = null) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent != null ? parent : _root.transform, false);
        return go.AddComponent<T>();
    }

    private static UiClickSound.Trigger TriggerOf(UiClickSound sound) =>
        (UiClickSound.Trigger)new SerializedObject(sound).FindProperty("trigger").enumValueIndex;

    [Test]
    public void Button_GetsMenuTint_LabelState_AndClickSound()
    {
        Image bg = Child<Image>("Btn");
        Button b = bg.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        Child<TextMeshProUGUI>("Text", b.transform);
        Child<Image>("Icon", b.transform);

        UiFeedbackStyle.Apply(_root.transform);

        Assert.AreEqual(ButtonLabelStateColor.ChosenTint, b.colors.highlightedColor);
        Assert.AreEqual(ButtonLabelStateColor.ChosenTint, b.colors.selectedColor);
        Assert.AreEqual(ButtonLabelStateColor.PressedTint, b.colors.pressedColor);
        Assert.IsTrue(b.TryGetComponent(out ButtonLabelStateColor _));
        Assert.IsTrue(b.TryGetComponent(out UiClickSound sound));
        Assert.AreEqual(UiClickSound.Trigger.ButtonClick, TriggerOf(sound));
    }

    [Test]
    public void Slider_SoundsOnPointerDown_Toggle_OnValueChanged()
    {
        Image handle = Child<Image>("Handle");
        Slider s = handle.gameObject.AddComponent<Slider>();
        s.targetGraphic = handle;
        Image tbg = Child<Image>("Toggle");
        Toggle t = tbg.gameObject.AddComponent<Toggle>();
        t.targetGraphic = tbg;

        UiFeedbackStyle.Apply(_root.transform);

        Assert.AreEqual(UiClickSound.Trigger.PointerDown, TriggerOf(s.GetComponent<UiClickSound>()));
        Assert.AreEqual(UiClickSound.Trigger.ToggleChanged, TriggerOf(t.GetComponent<UiClickSound>()));
    }

    [Test]
    public void Apply_IsIdempotent()
    {
        Image bg = Child<Image>("Btn");
        Button b = bg.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;

        UiFeedbackStyle.Apply(_root.transform);
        UiFeedbackStyle.Apply(_root.transform);

        Assert.AreEqual(1, b.GetComponents<UiClickSound>().Length);
        Assert.AreEqual(1, b.GetComponents<ButtonLabelStateColor>().Length);
    }

    [Test]
    public void Label_TurnsGrayOnHover_AndBackOnExit()
    {
        Image bg = Child<Image>("Btn");
        Button b = bg.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        TextMeshProUGUI text = Child<TextMeshProUGUI>("Text", b.transform);
        Image icon = Child<Image>("Icon", b.transform);
        UiFeedbackStyle.Apply(_root.transform);
        var state = b.GetComponent<ButtonLabelStateColor>();
        var e = new PointerEventData(null);

        state.OnPointerEnter(e);
        Assert.AreEqual(ButtonLabelStateColor.ChosenLabel, text.color);
        Assert.AreEqual(ButtonLabelStateColor.ChosenTint, icon.color);

        state.OnPointerExit(e);
        Assert.AreEqual(Color.white, text.color);
        Assert.AreEqual(Color.white, icon.color);
    }

    [Test]
    public void PinWithGraphic_DarkensTargetOnce_AndRestores()
    {
        Image bg = Child<Image>("Btn");
        Button b = bg.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        UiFeedbackStyle.Apply(_root.transform);
        var state = b.GetComponent<ButtonLabelStateColor>();

        state.PinWithGraphic(true);
        Assert.AreEqual(ButtonLabelStateColor.ChosenTint, bg.color);
        Assert.AreEqual(Color.white, b.colors.highlightedColor, "고정 중에는 hover 로 두 번 곱하지 않는다");

        state.PinWithGraphic(false);
        Assert.AreEqual(Color.white, bg.color);
        Assert.AreEqual(ButtonLabelStateColor.ChosenTint, b.colors.highlightedColor);
    }
}
