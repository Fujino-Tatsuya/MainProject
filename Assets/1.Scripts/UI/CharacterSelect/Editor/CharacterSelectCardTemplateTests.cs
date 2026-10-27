using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 가운데 카드 원본(할 일 109 후속) — 실제 패널 프리팹을 프리뷰 씬에 올려, <c>Content/Cards/Card_Template</c> 의
/// 초상화 크기·회전과 Ring_Focus 크기·색·스프라이트가 런타임 카드(가운데 포함)에 그대로 들어가는지 본다.
/// 원본은 열 때 꺼져 복제본과 겹쳐 보이지 않는다.
/// </summary>
public sealed class CharacterSelectCardTemplateTests
{
    Scene _scene;
    CharacterSelectPanel _panel;
    CharacterSelectCard _template;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPanelAuthoring.PanelPath);
        Assert.IsNotNull(prefab, CharacterSelectPanelAuthoring.PanelPath);

        _scene = EditorSceneManager.NewPreviewScene();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
        _panel = instance.GetComponent<CharacterSelectPanel>();
        _template = new SerializedObject(_panel).FindProperty("cardTemplate").objectReferenceValue as CharacterSelectCard;
        Assert.IsNotNull(_template, "프리팹 cardTemplate 이 연결돼 있어야 한다");
    }

    [TearDown]
    public void TearDown()
    {
        if (_panel != null)
            _panel.Hide(); // 정적 열림 수 되돌리기(IsEscapeClaimed)
        EditorSceneManager.ClosePreviewScene(_scene);
    }

    static RectTransform Portrait(CharacterSelectCard card) =>
        ((Image)new SerializedObject(card).FindProperty("portraitImage").objectReferenceValue).rectTransform;

    static Image Ring(CharacterSelectCard card) =>
        ((GameObject)new SerializedObject(card).FindProperty("focusRing").objectReferenceValue).GetComponent<Image>();

    List<CharacterSelectCard> Cards() =>
        (List<CharacterSelectCard>)typeof(CharacterSelectPanel).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_panel);

    CharacterSelectCard FocusedCard() => Cards()[_panel.FocusedCharacterId];

    static void AssertSameLook(CharacterSelectCard expected, Vector2 portraitSize, float portraitZ, Vector2 ringSize, Color ringColor,
        Sprite ringSprite, CharacterSelectCard actual)
    {
        RectTransform portrait = Portrait(actual);
        Assert.AreEqual(portraitSize, portrait.sizeDelta, "초상화 크기");
        Assert.AreEqual(portraitZ, portrait.localEulerAngles.z, 0.01f, "초상화 회전 z");
        Image ring = Ring(actual);
        Assert.AreEqual(ringSize, ring.rectTransform.sizeDelta, "테두리 크기");
        Assert.AreSame(ringSprite, ring.sprite, "테두리 스프라이트");
        Assert.AreEqual(ringColor.r, ring.color.r, 0.001f, "테두리 색 r");
        Assert.AreEqual(ringColor.g, ring.color.g, 0.001f, "테두리 색 g");
        Assert.AreEqual(ringColor.b, ring.color.b, 0.001f, "테두리 색 b");
        Assert.AreEqual(ringColor.a, ring.color.a, 0.001f, "테두리 알파(다 선 가운데 = 원본 알파 × 1)");
        Assert.AreNotSame(expected, actual);
    }

    [Test]
    public void Prefab_Template_IsCardPrefabInstance_AtFocusedSlot_UnderContainer()
    {
        var so = new SerializedObject(_panel);
        Assert.AreEqual(CharacterSelectPanelAuthoring.CardTemplateName, _template.name);
        Assert.AreSame(so.FindProperty("cardContainer").objectReferenceValue, _template.transform.parent, "Content/Cards 아래");
        Assert.AreEqual(CharacterSelectPanelAuthoring.CardPath, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_template.gameObject));
        Assert.AreEqual(so.FindProperty("focusedSlot").vector2Value, ((RectTransform)_template.transform).anchoredPosition, "가운데 자리");
        Assert.IsTrue(_template.gameObject.activeSelf, "프리팹을 열면 보여야 한다");
        Assert.IsTrue(Ring(_template).gameObject.activeSelf, "미리보기 = 테두리 켬");
    }

    [Test]
    public void Show_FocusedCard_CopiesPrefabTemplateLook()
    {
        RectTransform portrait = Portrait(_template);
        Image ring = Ring(_template);
        Vector2 portraitSize = portrait.sizeDelta;
        float portraitZ = portrait.localEulerAngles.z;
        Vector2 ringSize = ring.rectTransform.sizeDelta;
        Color ringColor = ring.color;
        Sprite ringSprite = ring.sprite;

        _panel.Show(0, false);

        AssertSameLook(_template, portraitSize, portraitZ, ringSize, ringColor, ringSprite, FocusedCard());
    }

    [Test]
    public void Show_EditedTemplate_AppliesToRuntimeCards_TemplateHidden()
    {
        // 사용자가 프리팹에서 고친 값 — 카드 프리팹 원래 값과 다르게
        RectTransform portrait = Portrait(_template);
        portrait.sizeDelta = portrait.sizeDelta + new Vector2(37f, 21f);
        portrait.localEulerAngles = new Vector3(0f, 0f, 12.5f);
        Image ring = Ring(_template);
        ring.rectTransform.sizeDelta = ring.rectTransform.sizeDelta + new Vector2(18f, 9f);
        ring.color = new Color(0.2f, 0.9f, 0.4f, 0.75f);
        Sprite otherSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        ring.sprite = otherSprite;

        _panel.Show(0, false);

        List<CharacterSelectCard> cards = Cards();
        var roster = (CharacterRoster)new SerializedObject(_panel).FindProperty("roster").objectReferenceValue;
        Assert.AreEqual(roster.Count, cards.Count, "카드 수 = 로스터 그대로");
        Assert.IsFalse(_template.gameObject.activeSelf, "원본은 꺼져 복제본과 겹쳐 보이지 않는다");

        CharacterSelectCard focused = FocusedCard();
        AssertSameLook(_template, portrait.sizeDelta, 12.5f, ring.rectTransform.sizeDelta, ring.color, otherSprite, focused);
        for (int i = 0; i < cards.Count; i++)
        {
            Assert.AreEqual($"Card_{i}", cards[i].name);
            Assert.AreEqual(portrait.sizeDelta, Portrait(cards[i]).sizeDelta, $"옆 카드 {i} 도 같은 원본");
        }

        Object.DestroyImmediate(otherSprite);
    }
}
