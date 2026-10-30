using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 특성 선택 UI 프리팹 저작(할 일 15 T2). 실행할 때마다 프리팹 내용을 처음부터 다시 만든다(같은 경로 = guid 유지).
/// 손으로 다듬기 시작하면 이 메뉴를 다시 돌리지 말 것 — 덮어쓴다.
/// 별도 Screen Space Overlay 캔버스라 CombatHUD 와 무관하다. 씬 배치는 S12(복도 씬)에서 프리팹을 끌어다 둔다.
/// <para>
/// 할 일 125 5/5 — 카드 분류 줄·창 도움말 줄은 <see cref="AddCategoryAndHelp"/>(<c>분류·도움말 줄 추가</c>)가 <b>지금 프리팹에 빠진 것만</b> 더한다
/// (배치·글꼴·색 유지, 이미 있으면 저장하지 않음). 상충 확인창은 프리팹이 아니라 런타임 생성(<see cref="TraitSelectionView"/>).
/// </para>
/// <para>
/// 할 일 165 — 카드 C1 아트는 <see cref="ApplyCardC1"/>(<c>카드 C1 아트 적용</c>)가 지금 프리팹의 카드 템플릿만 다시 만든다. 전체 생성도 같은 카드 빌더를 쓴다.
/// </para>
/// </summary>
public static class TraitUiAuthoring
{
    private const string Folder = "Assets/2.Prefabs/UI/Tutorial";
    private const string PrefabPath = Folder + "/TraitUI.prefab";
    // 10-21 서울알림체 교체(518ae574) — 전체 재생성이 옛 NotoSansKR 로 되돌리지 않게.
    private const string FontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";
    private const string TextTablePath = "Assets/9.ScriptableObject/Traits/TraitTexts.asset";
    private const int SortingOrder = 60; // CombatHUD(0) 위

    private static readonly Color PanelColor = new Color(0.035f, 0.045f, 0.065f, 0.97f);
    private static readonly Color OutlineColor = new Color(0.48f, 0.42f, 0.25f, 0.9f);
    private static readonly Color AccentColor = new Color(0.95f, 0.82f, 0.42f, 1f);
    private static readonly Color SubtleColor = new Color(0.78f, 0.80f, 0.84f, 1f);

    [MenuItem("Tools/Tutorial/특성 선택 UI 프리팹 생성")]
    public static void Build()
    {
        EnsureFolder(Folder);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Sprite placeholder = AssetDatabase.LoadAssetAtPath<Sprite>(EmptyIconPath);
        TraitTextTable texts = EnsureTextTable();

        var root = new GameObject("TraitUI", typeof(RectTransform));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            TraitSelectionView view = root.AddComponent<TraitSelectionView>();

            // ── 획득 특성 다시 보기(좌상단) ──
            RectTransform list = NewUi("AcquiredList", root.transform);
            Anchor(list, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(0f, 56f));
            HorizontalLayoutGroup listLayout = list.gameObject.AddComponent<HorizontalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childControlWidth = listLayout.childControlHeight = false;
            listLayout.childForceExpandWidth = listLayout.childForceExpandHeight = false;
            list.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform iconTemplate = NewUi("IconTemplate", list);
            iconTemplate.sizeDelta = new Vector2(56f, 56f);
            Image iconFrame = AddPanel(iconTemplate.gameObject);
            iconFrame.raycastTarget = true; // 호버 판정
            RectTransform iconImageRect = NewUi("Icon", iconTemplate);
            Stretch(iconImageRect, 6f);
            Image iconImage = iconImageRect.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            TraitIconView iconView = iconTemplate.gameObject.AddComponent<TraitIconView>();
            SetRef(iconView, "icon", iconImage);

            RectTransform tooltip = NewUi("AcquiredTooltip", root.transform);
            Anchor(tooltip, new Vector2(0f, 1f), new Vector2(24f, -90f), new Vector2(380f, 0f));
            AddPanel(tooltip.gameObject);
            VerticalLayoutGroup tooltipLayout = AddVertical(tooltip.gameObject, 14, 6f);
            tooltipLayout.childAlignment = TextAnchor.UpperLeft;
            tooltip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            TMP_Text tooltipName = NewText("Name", tooltip, font, 22f, FontStyles.Bold, TextAlignmentOptions.TopLeft, AccentColor);
            TMP_Text tooltipDescription = NewText("Description", tooltip, font, 18f, FontStyles.Normal, TextAlignmentOptions.TopLeft, Color.white);

            // ── 선택 창(중앙 모달) ──
            RectTransform window = NewUi("Window", root.transform);
            Stretch(window, 0f);
            window.gameObject.AddComponent<UiModalBlocker>();
            Image dim = window.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true; // 창 밖 클릭이 뒤 UI 로 새지 않게

            RectTransform panel = NewUi("Panel", window);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1120f, 0f);
            AddPanel(panel.gameObject);
            VerticalLayoutGroup panelLayout = AddVertical(panel.gameObject, 32, 20f);
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TMP_Text title = NewText("Title", panel, font, 40f, FontStyles.Bold, TextAlignmentOptions.Center, AccentColor);
            title.text = TraitTexts.Title;

            TMP_Text intro = NewText("Intro", panel, font, 21f, FontStyles.Normal, TextAlignmentOptions.Center, SubtleColor);
            intro.text = TraitTexts.Intro;

            RectTransform cards = NewUi("Cards", panel);
            HorizontalLayoutGroup cardsLayout = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            cardsLayout.spacing = CardSpacing;
            cardsLayout.childAlignment = TextAnchor.MiddleCenter;
            cardsLayout.childControlWidth = cardsLayout.childControlHeight = false;
            cardsLayout.childForceExpandWidth = cardsLayout.childForceExpandHeight = false;
            cards.gameObject.AddComponent<LayoutElement>();

            RectTransform cardTemplate = NewUi("CardTemplate", cards);
            cardTemplate.gameObject.AddComponent<Image>();
            cardTemplate.gameObject.AddComponent<Button>();
            TraitCardView cardView = cardTemplate.gameObject.AddComponent<TraitCardView>();
            BuildCardC1(cardView, font);
            FitWindowToCards(panel, cards);

            TMP_Text help = NewText(HelpName, panel, font, HelpSize, FontStyles.Normal, TextAlignmentOptions.Center, SubtleColor);
            help.text = TraitUiStrings.Help.Ko;

            TMP_Text lastNotice = NewText("LastNotice", panel, font, 20f, FontStyles.Italic, TextAlignmentOptions.Center, AccentColor);
            lastNotice.text = TraitTexts.LastNotice;

            // ── 교착 방지 게이지(창 아래 — 첫 완료자 뒤에만 켜진다, 기본 꺼짐이라 꺼져 있을 땐 레이아웃 변화 없음) ──
            RectTransform deadline = NewUi("Deadline", panel);
            VerticalLayoutGroup deadlineLayout = AddVertical(deadline.gameObject, 0, 6f);
            deadlineLayout.childAlignment = TextAnchor.UpperCenter;

            RectTransform bar = NewUi("Bar", deadline);
            Image barBackground = bar.gameObject.AddComponent<Image>();
            barBackground.color = new Color(0.09f, 0.11f, 0.15f, 1f);
            barBackground.raycastTarget = false;
            LayoutElement barElement = bar.gameObject.AddComponent<LayoutElement>();
            barElement.preferredHeight = barElement.minHeight = 14f;
            RectTransform fillRect = NewUi("Fill", bar);
            Stretch(fillRect, 2f);
            Image fill = fillRect.gameObject.AddComponent<Image>();
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            fill.color = AccentColor;
            fill.raycastTarget = false;

            RectTransform deadlineRow = NewUi("Row", deadline);
            HorizontalLayoutGroup rowLayout = deadlineRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            TMP_Text deadlineSeconds = NewText("Seconds", deadlineRow, font, 20f, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
            TMP_Text deadlineDone = NewText("Done", deadlineRow, font, 20f, FontStyles.Normal, TextAlignmentOptions.Right, SubtleColor);

            // ── 획득 토스트(화면 위쪽 가운데 — 우측 하단·하단 스킬 줄을 피한다) ──
            RectTransform toast = NewUi("Toast", root.transform);
            toast.anchorMin = toast.anchorMax = new Vector2(0.5f, 0.8f);
            toast.pivot = new Vector2(0.5f, 0.5f);
            toast.sizeDelta = new Vector2(900f, 0f);
            Image toastBackground = toast.gameObject.AddComponent<Image>();
            toastBackground.color = new Color(0f, 0f, 0f, 0.7f);
            toastBackground.raycastTarget = false;
            AddVertical(toast.gameObject, 16, 0f);
            toast.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            TMP_Text toastText = NewText("Text", toast, font, 22f, FontStyles.Normal, TextAlignmentOptions.Center, Color.white);

            var so = new SerializedObject(view);
            so.FindProperty("window").objectReferenceValue = window.gameObject;
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("introRoot").objectReferenceValue = intro.gameObject;
            so.FindProperty("introText").objectReferenceValue = intro;
            so.FindProperty("lastNoticeRoot").objectReferenceValue = lastNotice.gameObject;
            so.FindProperty("lastNoticeText").objectReferenceValue = lastNotice;
            so.FindProperty("cardContainer").objectReferenceValue = cards;
            so.FindProperty("cardTemplate").objectReferenceValue = cardView;
            so.FindProperty("helpText").objectReferenceValue = help;
            so.FindProperty("deadlineRoot").objectReferenceValue = deadline.gameObject;
            so.FindProperty("deadlineFill").objectReferenceValue = fill;
            so.FindProperty("deadlineSecondsText").objectReferenceValue = deadlineSeconds;
            so.FindProperty("deadlineDoneText").objectReferenceValue = deadlineDone;
            so.FindProperty("deadlineColor").colorValue = AccentColor;
            so.FindProperty("deadlineTextColor").colorValue = Color.white;
            so.FindProperty("toast").objectReferenceValue = toast.gameObject;
            so.FindProperty("toastText").objectReferenceValue = toastText;
            so.FindProperty("acquiredContainer").objectReferenceValue = list;
            so.FindProperty("iconTemplate").objectReferenceValue = iconView;
            so.FindProperty("tooltip").objectReferenceValue = tooltip.gameObject;
            so.FindProperty("tooltipName").objectReferenceValue = tooltipName;
            so.FindProperty("tooltipDescription").objectReferenceValue = tooltipDescription;
            so.FindProperty("placeholderIcon").objectReferenceValue = placeholder;
            so.FindProperty("texts").objectReferenceValue = texts;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 기본 꺼짐 — 창(UiModalBlocker)이 켜진 채 저장되면 씬에 두는 순간 입력이 막힌다.
            cardTemplate.gameObject.SetActive(false);
            iconTemplate.gameObject.SetActive(false);
            tooltip.gameObject.SetActive(false);
            toast.gameObject.SetActive(false);
            deadline.gameObject.SetActive(false);
            window.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Trait] UI 프리팹 저장 — {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private const string CategoryName = "Category";
    private const string HelpName = "Help";
    private const float CategorySize = 18f;
    private const float HelpSize = 18f;

    /// <summary>
    /// 지금 프리팹에 카드 분류 줄(<c>CardTemplate/Category</c> — 이름 바로 아래)·창 도움말 줄(<c>Panel/Help</c> — 카드 줄 바로 아래)이 없으면 더한다.
    /// 다른 자식·배치·글꼴은 건드리지 않는다(글꼴·색은 옆 글자에서 복사). 둘 다 이미 연결돼 있으면 저장하지 않는다(멱등).
    /// </summary>
    [MenuItem("Tools/Tutorial/특성 선택 UI — 분류·도움말 줄 추가 (배치 유지)")]
    public static void AddCategoryAndHelp()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            bool changed = false;
            TraitSelectionView view = root.GetComponent<TraitSelectionView>();
            var viewSo = new SerializedObject(view);
            var cardView = viewSo.FindProperty("cardTemplate").objectReferenceValue as TraitCardView;
            if (cardView == null)
            {
                Debug.LogError("[Trait] TraitUI.prefab 에 카드 템플릿(cardTemplate)이 없다 — 전체 생성 메뉴를 쓸 것.");
                return;
            }

            var cardSo = new SerializedObject(cardView);
            SerializedProperty category = cardSo.FindProperty("category");
            if (category.objectReferenceValue == null)
            {
                var name = cardSo.FindProperty("displayName").objectReferenceValue as TMP_Text;
                TMP_Text text = CloneTextStyle(CategoryName, cardView.transform, name, CategorySize, FontStyles.Normal, SubtleColor);
                text.alignment = TextAlignmentOptions.Center;
                if (name != null)
                    text.transform.SetSiblingIndex(name.transform.GetSiblingIndex() + 1);
                category.objectReferenceValue = text;
                cardSo.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            SerializedProperty help = viewSo.FindProperty("helpText");
            if (help.objectReferenceValue == null)
            {
                var cards = viewSo.FindProperty("cardContainer").objectReferenceValue as RectTransform;
                var title = viewSo.FindProperty("title").objectReferenceValue as TMP_Text;
                Transform panel = cards != null ? cards.parent : null;
                if (panel == null)
                {
                    Debug.LogError("[Trait] TraitUI.prefab 의 카드 줄(cardContainer) 부모 패널을 못 찾았다.");
                    return;
                }

                TMP_Text text = CloneTextStyle(HelpName, panel, title, HelpSize, FontStyles.Normal, SubtleColor);
                text.alignment = TextAlignmentOptions.Center;
                text.text = TraitUiStrings.Help.Ko;
                text.transform.SetSiblingIndex(cards.GetSiblingIndex() + 1);
                help.objectReferenceValue = text;
                viewSo.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (!changed)
            {
                Debug.Log("[Trait] TraitUI.prefab — 분류·도움말 줄이 이미 있다(저장 안 함).");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Trait] TraitUI.prefab — 분류·도움말 줄 추가 저장 ({PrefabPath})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── 할 일 165 — 카드 C1 아트 ──
    // 배치 = 납품 배치표.csv(카드 560x413, 좌상단 기준 X/Y·UI 크기). 스프라이트는 PPU 200(PNG 절반 = UI 크기).
    private const string CardArtFolder = "Assets/50.Art/UI/TraitCard_C1/Sprites/";
    private const string TraitIconFolder = "Assets/50.Art/UI/HUD/TraitIcon/";
    private const string EmptyIconPath = TraitIconFolder + "Common/Trait_Empty.png";
    private const string HeavyFontPath = "Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset";
    private static readonly Vector2 CardSize = new Vector2(560f, 413f);
    private const float CardSpacing = 24f;
    private const int WindowPadding = 32; // Panel 의 VerticalLayoutGroup 좌우 여백(Build 와 같은 값)
    private const float DescriptionLineHeight = 34f;

    private static readonly Color CardNameColor = new Color(0.96f, 0.96f, 0.94f, 1f);
    private static readonly Color CardCategoryColor = new Color(0.80f, 0.81f, 0.80f, 1f);
    private static readonly Color CardDescriptionColor = new Color(0.16f, 0.17f, 0.18f, 1f);

    /// <summary>
    /// 지금 프리팹의 카드 템플릿만 C1 레이아웃으로 바꾼다(할 일 165). <c>CardTemplate</c> 오브젝트·<c>Button</c>·<c>TraitCardView</c> 는 그대로 두고
    /// 자식과 옛 꾸밈(Outline·VerticalLayoutGroup)만 갈아 끼운다 — 창·획득 줄·씬 오버라이드는 유지. 창 패널 폭·카드 줄 높이는 카드 3장에 맞춘다.
    /// 빈 아이콘(<c>placeholderIcon</c>)은 <c>Trait_Empty</c>. 몇 번 돌려도 같은 결과.
    /// </summary>
    [MenuItem("Tools/Tutorial/특성 선택 UI — 카드 C1 아트 적용 (카드만 교체)")]
    public static void ApplyCardC1()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            TraitSelectionView view = root.GetComponent<TraitSelectionView>();
            var viewSo = new SerializedObject(view);
            var cardView = viewSo.FindProperty("cardTemplate").objectReferenceValue as TraitCardView;
            var cards = viewSo.FindProperty("cardContainer").objectReferenceValue as RectTransform;
            if (cardView == null || cards == null || cards.parent == null)
            {
                Debug.LogError("[Trait] TraitUI.prefab 에 카드 템플릿/카드 줄이 없다 — 전체 생성 메뉴를 쓸 것.");
                return;
            }

            for (int i = cardView.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(cardView.transform.GetChild(i).gameObject);
            foreach (Outline outline in cardView.GetComponents<Outline>())
                Object.DestroyImmediate(outline);
            foreach (LayoutGroup group in cardView.GetComponents<LayoutGroup>())
                Object.DestroyImmediate(group);

            TMP_FontAsset medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            BuildCardC1(cardView, medium);
            FitWindowToCards((RectTransform)cards.parent, cards);

            viewSo.FindProperty("placeholderIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(EmptyIconPath);
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Trait] TraitUI.prefab — 카드 C1 아트 적용 저장 ({PrefabPath})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 카드 오브젝트(Image·Button·TraitCardView 가 붙어 있고 자식이 없는 상태)를 C1 레이아웃으로 채운다.
    /// 그리는 순서 = 카드판 → 심볼 → 이름·분류 → 마스크(안에 아이콘) → 스킬 프레임 → 키캡 → 설명.
    /// </summary>
    private static void BuildCardC1(TraitCardView cardView, TMP_FontAsset medium)
    {
        TMP_FontAsset heavy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HeavyFontPath);
        var card = (RectTransform)cardView.transform;
        card.sizeDelta = CardSize;

        // 루트 Image = 투명 클릭 판정(카드 전체). 보이는 판은 자식 Panel — 버튼 색 변화는 그쪽에 건다.
        Image hitArea = card.GetComponent<Image>();
        hitArea.sprite = null;
        hitArea.color = new Color(1f, 1f, 1f, 0f);
        hitArea.raycastTarget = true;

        Image panel = CardImage("Panel", card, CardArt("TraitCard_C1_Panel"), -2f, -2f, 564f, 417f);
        Button button = card.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = panel;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;

        Image symbol = CardImage("CharacterSymbol", card, null, 19f, 10f, 118f, 118f);

        TMP_Text cardName = CardText("Name", card, heavy != null ? heavy : medium, 34f, 142f, 38f, 340f, 40f,
            TextAlignmentOptions.MidlineLeft, CardNameColor);
        cardName.textWrappingMode = TextWrappingModes.NoWrap;
        cardName.enableAutoSizing = true;
        cardName.fontSizeMin = 24f;
        cardName.fontSizeMax = 34f;

        TMP_Text cardCategory = CardText(CategoryName, card, medium, 17f, 144f, 84f, 340f, 22f,
            TextAlignmentOptions.MidlineLeft, CardCategoryColor);
        cardCategory.textWrappingMode = TextWrappingModes.NoWrap;
        cardCategory.overflowMode = TextOverflowModes.Ellipsis;

        // 스킬 슬롯 — 프레임·마스크·아이콘 중심 (113, 250) 동일. 마스크 136 중 실제 영역 132.
        Image mask = CardImage("SkillMask", card, CardArt("TraitCard_C1_SkillMask"), 45f, 182f, 136f, 136f);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        Image icon = CardImage("Icon", mask.transform, null, 2f, 2f, 132f, 132f);
        icon.preserveAspect = true;
        CardImage("SkillFrame", card, CardArt("TraitCard_C1_SkillFrame"), 37f, 174f, 152f, 152f);

        // 키캡 1곳 — 키캡 38x40(94,335) / 마우스 30x41(98,335) 을 둘 다 담는 38x41 상자, 비율 유지로 각 크기가 된다.
        Image keycap = CardImage("Keycap", card, CardArt("Keycap_Blank"), 94f, 335f, 38f, 41f);
        keycap.preserveAspect = true;

        TMP_Text cardDescription = CardText("Description", card, medium, 22f, 224f, 170f, 288f, 170f,
            TextAlignmentOptions.TopLeft, CardDescriptionColor);
        cardDescription.lineSpacing = LineSpacingFor(medium, 22f, DescriptionLineHeight);
        cardDescription.enableAutoSizing = true;
        cardDescription.fontSizeMin = 16f;
        cardDescription.fontSizeMax = 22f;

        var so = new SerializedObject(cardView);
        so.FindProperty("button").objectReferenceValue = button;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("displayName").objectReferenceValue = cardName;
        so.FindProperty("category").objectReferenceValue = cardCategory;
        so.FindProperty("description").objectReferenceValue = cardDescription;
        so.FindProperty("characterSymbol").objectReferenceValue = symbol;
        so.FindProperty("keycap").objectReferenceValue = keycap;
        so.FindProperty("paladinSymbol").objectReferenceValue = Symbol("Paladin");
        so.FindProperty("gunnerSymbol").objectReferenceValue = Symbol("Gunner");
        so.FindProperty("assassinSymbol").objectReferenceValue = Symbol("Assassin");
        so.FindProperty("keycapBlank").objectReferenceValue = CardArt("Keycap_Blank");
        so.FindProperty("keycapQ").objectReferenceValue = CardArt("Keycap_Q");
        so.FindProperty("keycapE").objectReferenceValue = CardArt("Keycap_E");
        so.FindProperty("keycapR").objectReferenceValue = CardArt("Keycap_R");
        so.FindProperty("inputLeftMouse").objectReferenceValue = CardArt("Input_LMB");
        so.FindProperty("inputRightMouse").objectReferenceValue = CardArt("Input_RMB");
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 카드 3장(TraitPool = 캐릭터×차수 3후보) + 간격 + 창 여백에 창 폭·카드 줄 높이를 맞춘다.
    private static void FitWindowToCards(RectTransform panel, RectTransform cards)
    {
        panel.sizeDelta = new Vector2(CardSize.x * 3f + CardSpacing * 2f + WindowPadding * 2f, panel.sizeDelta.y);
        LayoutElement element = cards.GetComponent<LayoutElement>();
        if (element == null)
            element = cards.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = element.minHeight = CardSize.y;
    }

    // TMP 줄 간격(em/100) — 글꼴 기본 줄 높이에서 목표 줄 높이(px)까지 더할 값.
    private static float LineSpacingFor(TMP_FontAsset font, float size, float lineHeight)
    {
        if (font == null || font.faceInfo.pointSize <= 0f)
            return 0f;
        float natural = font.faceInfo.lineHeight / font.faceInfo.pointSize * size;
        return (lineHeight - natural) / size * 100f;
    }

    private static Sprite CardArt(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(CardArtFolder + name + ".png");

    private static Sprite Symbol(string character) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"{TraitIconFolder}{character}/Trait_{character}.png");

    // 배치표 좌표(부모 좌상단 기준 X/Y, y 아래로 +)에 중앙 피벗으로 둔다.
    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x + width * 0.5f, -(y + height * 0.5f));
    }

    private static Image CardImage(string name, Transform parent, Sprite sprite, float x, float y, float width, float height)
    {
        RectTransform rect = NewUi(name, parent);
        PlaceTopLeft(rect, x, y, width, height);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CardText(string name, Transform parent, TMP_FontAsset font, float size,
        float x, float y, float width, float height, TextAlignmentOptions alignment, Color color)
    {
        TMP_Text text = NewText(name, parent, font, size, FontStyles.Normal, alignment, color);
        PlaceTopLeft((RectTransform)text.transform, x, y, width, height);
        return text;
    }

    // 옆 글자(source)의 글꼴을 따라 새 TMP 를 만든다. source 가 없으면 기본 글꼴.
    private static TMP_Text CloneTextStyle(string name, Transform parent, TMP_Text source, float size, FontStyles style, Color color)
    {
        TMP_FontAsset font = source != null ? source.font : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        TMP_Text text = NewText(name, parent, font, size, style, TextAlignmentOptions.Center, color);
        text.gameObject.layer = parent.gameObject.layer;
        return text;
    }

    private static Image AddPanel(GameObject target)
    {
        Image background = target.AddComponent<Image>();
        background.color = PanelColor;
        background.raycastTarget = false;
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = OutlineColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return background;
    }

    private static VerticalLayoutGroup AddVertical(GameObject target, int padding, float spacing)
    {
        VerticalLayoutGroup group = target.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(padding, padding, padding, padding);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return group;
    }

    private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, float size, FontStyles style,
        TextAlignmentOptions alignment, Color color)
    {
        RectTransform rect = NewUi(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = true;
        return text;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static RectTransform NewUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void SetRef(Object target, string property, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(property).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>문구 테이블 에셋(T3) — 없으면 상수 기본값으로 만든다. 있으면 그대로 쓴다(기획이 고친 문구 보존).</summary>
    public static TraitTextTable EnsureTextTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<TraitTextTable>(TextTablePath);
        if (table != null)
            return table;

        EnsureFolder(System.IO.Path.GetDirectoryName(TextTablePath).Replace('\\', '/'));
        table = ScriptableObject.CreateInstance<TraitTextTable>();
        AssetDatabase.CreateAsset(table, TextTablePath);
        AssetDatabase.SaveAssets();
        return table;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
