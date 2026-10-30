using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 UI 수정(할 일 162) — 실제 패널 프리팹을 프리뷰 씬에 올려 본다.
/// 이름판(포커스 초상화·고정 영어 이름) · 카드 이름 영어 ·셰브론 hover 링 7px·본체 알파 · 가운데 선택 테두리 = card_selected(카드 뒤, 흰 띠만) · 기본/코어 버튼 → "미구현" 팝업(X 로 팝업만 닫힘, 팝업 뒤 확정·이동 막힘).
/// ESC 는 키보드를 흉내 내지 않고 순수 규칙(CharacterSelectPanelRulesTests)에서 본다.
/// </summary>
public sealed class CharacterSelectUiPolishTests
{
    Scene _scene;
    GameObject _instance;
    CharacterSelectPanel _panel;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPanelAuthoring.PanelPath);
        Assert.IsNotNull(prefab, CharacterSelectPanelAuthoring.PanelPath);

        _scene = EditorSceneManager.NewPreviewScene();
        _instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
        _panel = _instance.GetComponent<CharacterSelectPanel>();
        // EditMode 에서는 Awake 가 돌지 않는다 — 버튼 리스너 연결을 직접 부른다(CharacterSelectConfirmButtonTests 와 같다)
        typeof(CharacterSelectPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_panel, null);
    }

    [TearDown]
    public void TearDown()
    {
        if (_panel != null)
            _panel.Hide(); // 정적 열림 수 되돌리기(IsEscapeClaimed)
        EditorSceneManager.ClosePreviewScene(_scene);
    }

    static readonly string[] EnglishNames = { "Paladin", "Gunner", "Assassin" };

    [Test]
    public void Nameplate_OnInPrefab_FollowsFocus_PortraitAndEnglishName()
    {
        Transform plate = _instance.transform.Find("Content/Nameplate");
        Assert.IsNotNull(plate);
        Assert.IsTrue(plate.gameObject.activeSelf, "이름판 다시 켬");

        var so = new SerializedObject(_panel);
        var portrait = so.FindProperty("nameplatePortrait").objectReferenceValue as Image;
        var name = so.FindProperty("nameplateName").objectReferenceValue as TMP_Text;
        var roster = so.FindProperty("roster").objectReferenceValue as CharacterRoster;
        Assert.IsNotNull(portrait, "nameplatePortrait 연결");
        Assert.IsNotNull(name, "nameplateName 연결");
        Assert.AreEqual(EnglishNames.Length, roster.Count, "로스터 3명(팔라딘·거너·어쌔신 순)");

        _panel.Show(0, false);
        for (int step = 0; step <= EnglishNames.Length; step++)
        {
            int focus = step % EnglishNames.Length;
            Assert.AreEqual(focus, _panel.FocusedCharacterId);
            Assert.AreSame(roster.Entries[focus].Portrait, portrait.sprite, $"{focus}번 초상화");
            Assert.IsTrue(portrait.enabled);
            Assert.AreEqual(EnglishNames[focus], name.text, $"{focus}번 이름 = 고정 영어");
            _panel.MoveFocus(1);
        }
    }

    [Test]
    public void Cards_ShowEnglishNames()
    {
        _panel.Show(0, false);
        var cards = (System.Collections.Generic.List<CharacterSelectCard>)typeof(CharacterSelectPanel)
            .GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_panel);
        Assert.AreEqual(EnglishNames.Length, cards.Count);
        for (int i = 0; i < cards.Count; i++)
        {
            var nameText = new SerializedObject(cards[i]).FindProperty("nameText").objectReferenceValue as TMP_Text;
            Assert.AreEqual(EnglishNames[cards[i].Index], nameText.text, $"{i}번 카드 이름 = 고정 영어");
        }
    }

    [Test]
    public void CharacterName_EnglishTable_FallsBackToDisplayName()
    {
        Assert.AreEqual(string.Empty, CharacterSelectUiStrings.CharacterName(null));

        var unknown = new CharacterRoster.Entry();
        typeof(CharacterRoster.Entry).GetField("displayName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(unknown, "새캐릭터");
        Assert.AreEqual(unknown.DisplayName, CharacterSelectUiStrings.CharacterName(unknown), "표에 없으면 로스터 DisplayName");
        Assert.AreEqual("새캐릭터", CharacterSelectUiStrings.CharacterName(unknown));
    }

    [Test]
    public void Chevron_HoverTint_DimsBodyOnly_OutlineStaysOpaque()
    {
        var button = new SerializedObject(_panel).FindProperty("confirmButton").objectReferenceValue as Button;
        Assert.IsNotNull(button);
        Assert.AreEqual(Selectable.Transition.ColorTint, button.transition);
        Assert.AreEqual(CharacterSelectPanelAuthoring.ChevronHoverAlpha, button.colors.highlightedColor.a, 0.001f, "hover 동안 본체 알파");
        Assert.AreEqual(CharacterSelectPanelAuthoring.ChevronHoverFadeSeconds, button.colors.fadeDuration, 0.001f, "전이 시간");
        Assert.AreEqual(1f, button.colors.normalColor.a, 0.001f, "평소 알파 1");

        var outline = button.GetComponent<UiHoverOutline>().Outline;
        Assert.AreNotSame(button.targetGraphic, outline, "Color Tint 대상은 셰브론 본체 Image 뿐");
        Assert.AreEqual(1f, outline.color.a, 0.001f, "흰 아웃라인 알파 1 유지");
    }

    const string SelectedCardPath = "Assets/50.Art/UI/char_select/card_selected.png";
    const string CardGapPath = "Assets/50.Art/UI/char_select/card_selected_gap.png";

    [Test]
    public void FocusRing_CardPrefabAndTemplate_UseCardSelected_NativeSize_BehindCard()
    {
        Sprite selected = AssetDatabase.LoadAssetAtPath<Sprite>(SelectedCardPath);
        Assert.IsNotNull(selected, SelectedCardPath);

        var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPanelAuthoring.CardPath);
        var cardRing = cardPrefab.transform.Find("Ring_Focus").GetComponent<Image>();
        var template = _instance.transform.Find($"Content/Cards/{CharacterSelectPanelAuthoring.CardTemplateName}/Ring_Focus").GetComponent<Image>();
        foreach (Image ring in new[] { cardRing, template })
        {
            Assert.AreSame(selected, ring.sprite, $"{ring.name} 스프라이트 = card_selected");
            Assert.AreEqual(Image.Type.Simple, ring.type, "9-slice 아님");
            Assert.AreEqual(Color.white, ring.color, "색을 곱하지 않는다 — 흰 띠 그대로");
            var rect = (RectTransform)ring.transform;
            Assert.AreEqual(selected.rect.size, rect.sizeDelta, "원본 크기 1:1(654×519)");
            Assert.AreEqual(Vector2.zero, rect.anchoredPosition, "카드 가운데");
            Assert.AreEqual(((RectTransform)rect.parent).sizeDelta, rect.sizeDelta, "카드 크기와 같다");
            Assert.Less(rect.GetSiblingIndex(), rect.parent.Find("Focused").GetSiblingIndex(),
                "카드 뒤 — 회색 채움이 초록 채움·초상화·이름을 가리지 않는다");
        }
    }

    [Test]
    public void FocusRing_CardSelected_OnlyWhiteBandShowsAroundCardGap()
    {
        // card_selected 는 card_selected_gap(가운데 정렬) 과 검은 선이 겹친다 — 카드 뒤에 깔면 gap 밖으로 흰 띠만 보인다.
        var selected = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var gap = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Assert.IsTrue(selected.LoadImage(System.IO.File.ReadAllBytes(SelectedCardPath)));
            Assert.IsTrue(gap.LoadImage(System.IO.File.ReadAllBytes(CardGapPath)));
            Color32[] s = selected.GetPixels32(), g = gap.GetPixels32();
            int ox = (selected.width - gap.width) / 2, oy = (selected.height - gap.height) / 2;
            int visible = 0, dark = 0;
            for (int y = 0; y < selected.height; y++)
            for (int x = 0; x < selected.width; x++)
            {
                Color32 c = s[y * selected.width + x];
                if (c.a < 128)
                    continue;
                int gx = x - ox, gy = y - oy;
                if (gx >= 0 && gy >= 0 && gx < gap.width && gy < gap.height && g[gy * gap.width + gx].a >= 128)
                    continue;
                visible++;
                if (c.r < 100)
                    dark++;
            }
            Assert.Greater(visible, 10000, "흰 띠가 보인다");
            Assert.AreEqual(0, dark, "gap 밖으로 검은 선·회색 채움이 새지 않는다");
        }
        finally
        {
            Object.DestroyImmediate(selected);
            Object.DestroyImmediate(gap);
        }
    }

    Button Field(string name) => new SerializedObject(_panel).FindProperty(name).objectReferenceValue as Button;

    GameObject Popup => new SerializedObject(_panel).FindProperty("infoPopup").objectReferenceValue as GameObject;

    [Test]
    public void InfoTabs_Wired_WithTabArt_DrawnAboveCards()
    {
        Button basic = Field("basicInfoButton"), core = Field("coreInfoButton");
        Assert.IsNotNull(basic, "basicInfoButton 연결");
        Assert.IsNotNull(core, "coreInfoButton 연결");
        StringAssert.EndsWith("tab_gibon_body.png", AssetDatabase.GetAssetPath(((Image)basic.targetGraphic).sprite));
        StringAssert.EndsWith("tab_core_body.png", AssetDatabase.GetAssetPath(((Image)core.targetGraphic).sprite));
        Assert.AreEqual(Navigation.Mode.None, basic.navigation.mode, "클릭 뒤 선택이 남지 않게");
        Assert.AreEqual(Navigation.Mode.None, core.navigation.mode);

        var basicRect = (RectTransform)basic.transform;
        var coreRect = (RectTransform)core.transform;
        Assert.Greater(basicRect.anchoredPosition.y, coreRect.anchoredPosition.y, "기본이 위");
        Assert.Greater(basicRect.anchoredPosition.x, coreRect.anchoredPosition.x, "계단식 — 기본이 오른쪽");

        Transform cards = _instance.transform.Find("Content/Cards");
        Assert.Greater(basic.transform.parent.GetSiblingIndex(), cards.GetSiblingIndex(), "버튼이 카드 위(위 카드 클릭 영역보다 먼저)");
        Assert.Less(_instance.transform.Find("Content/InfoTabs_Backing").GetSiblingIndex(), cards.GetSiblingIndex(), "검은 판은 카드 밑");
    }

    [Test]
    public void InfoPopup_Prefab_OffByDefault_TopMost_BlocksClicks_Text()
    {
        GameObject popup = Popup;
        Assert.IsNotNull(popup, "infoPopup 연결");
        Assert.IsFalse(popup.activeSelf, "기본 꺼짐");
        Assert.AreEqual(popup.transform.parent.childCount - 1, popup.transform.GetSiblingIndex(), "Content 맨 위");
        Assert.IsTrue(popup.GetComponent<Image>().raycastTarget, "어두운 막이 뒤 클릭을 막는다");

        StringAssert.EndsWith("panel_popup.png", AssetDatabase.GetAssetPath(popup.transform.Find("Frame").GetComponent<Image>().sprite));
        Button close = Field("infoPopupCloseButton");
        Assert.IsNotNull(close, "infoPopupCloseButton 연결");
        StringAssert.EndsWith("button_close_base.png", AssetDatabase.GetAssetPath(((Image)close.targetGraphic).sprite));
        StringAssert.EndsWith("icon_cross_red.png", AssetDatabase.GetAssetPath(close.transform.Find("Icon").GetComponent<Image>().sprite));
        Assert.AreEqual("미구현", popup.GetComponentInChildren<TMP_Text>(true).text, "본문 하드코딩");
    }

    [Test]
    public void InfoButtons_OpenPopup_CloseButtonClosesOnlyPopup()
    {
        int closed = 0;
        _panel.Closed += _ => closed++;
        _panel.Show(0, false);

        Field("basicInfoButton").onClick.Invoke();
        Assert.IsTrue(Popup.activeSelf, "기본 → 팝업");
        Assert.AreEqual(CharacterSelectInfoTab.Basic, _panel.InfoPopup);

        Field("infoPopupCloseButton").onClick.Invoke();
        Assert.IsFalse(Popup.activeSelf, "X → 팝업만 닫힘");
        Assert.IsTrue(_panel.IsOpen, "패널은 그대로");
        Assert.AreEqual(0, closed);

        Field("coreInfoButton").onClick.Invoke();
        Assert.IsTrue(Popup.activeSelf, "코어 → 같은 팝업");
        Assert.AreEqual(CharacterSelectInfoTab.Core, _panel.InfoPopup);
    }

    [Test]
    public void InfoPopup_BlocksConfirmAndMove_ClosedWithPanel()
    {
        int chosen = 0;
        _panel.CharacterChosen += _ => chosen++;
        _panel.Show(0, false);
        _panel.MoveFocus(1);
        _panel.OpenInfoPopup(CharacterSelectInfoTab.Basic);

        Field("confirmButton").onClick.Invoke();
        Assert.AreEqual(CharacterSelectConfirmResult.PopupOpen, _panel.Confirm(), "팝업 뒤 확정 막힘");
        _panel.MoveFocus(1);
        Assert.AreEqual(1, _panel.FocusedCharacterId, "팝업 뒤 이동 막힘");
        Assert.AreEqual(0, chosen);
        Assert.IsTrue(_panel.IsOpen);

        _panel.Hide();
        Assert.IsFalse(Popup.activeSelf, "패널이 닫히면 팝업도 꺼진다");
        _panel.Show(0, false);
        Assert.IsFalse(Popup.activeSelf, "다시 열면 팝업은 닫힌 채");
    }

    [Test]
    public void Chevron_Ring_ThicknessIs7px()
    {
        Assert.AreEqual(7f, CharacterSelectPanelAuthoring.ChevronRingThickness, "링 두께(화면 px) — PNG 대조는 CharacterSelectChevronHoverOutlineTests");
        Assert.AreEqual(3f, CharacterSelectPanelAuthoring.ChevronRingGap, "간격은 그대로");
    }
}
