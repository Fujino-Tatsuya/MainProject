using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 특성 선택 UI 프리팹 저작(할 일 15 T2). 실행할 때마다 프리팹 내용을 처음부터 다시 만든다(같은 경로 = guid 유지).
/// 손으로 다듬기 시작하면 이 메뉴를 다시 돌리지 말 것 — 덮어쓴다.
/// 별도 Screen Space Overlay 캔버스라 CombatHUD 와 무관하다. 씬 배치는 S12(복도 씬)에서 프리팹을 끌어다 둔다.
/// </summary>
public static class TraitUiAuthoring
{
    private const string Folder = "Assets/2.Prefabs/UI/Tutorial";
    private const string PrefabPath = Folder + "/TraitUI.prefab";
    private const string FontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";
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
        Sprite placeholder = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
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
            cardsLayout.spacing = 24f;
            cardsLayout.childAlignment = TextAnchor.MiddleCenter;
            cardsLayout.childControlWidth = cardsLayout.childControlHeight = false;
            cardsLayout.childForceExpandWidth = cardsLayout.childForceExpandHeight = false;
            LayoutElement cardsElement = cards.gameObject.AddComponent<LayoutElement>();
            cardsElement.preferredHeight = cardsElement.minHeight = 380f;

            RectTransform cardTemplate = NewUi("CardTemplate", cards);
            cardTemplate.sizeDelta = new Vector2(320f, 380f);
            Image cardBackground = cardTemplate.gameObject.AddComponent<Image>();
            cardBackground.color = new Color(0.09f, 0.11f, 0.15f, 1f);
            Outline cardOutline = cardTemplate.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = OutlineColor;
            cardOutline.effectDistance = new Vector2(2f, -2f);
            Button button = cardTemplate.gameObject.AddComponent<Button>();
            button.targetGraphic = cardBackground;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            VerticalLayoutGroup cardLayout = AddVertical(cardTemplate.gameObject, 20, 14f);
            cardLayout.childAlignment = TextAnchor.UpperCenter;

            RectTransform cardIconRect = NewUi("Icon", cardTemplate);
            Image cardIcon = cardIconRect.gameObject.AddComponent<Image>();
            cardIcon.raycastTarget = false;
            cardIcon.preserveAspect = true;
            LayoutElement cardIconElement = cardIconRect.gameObject.AddComponent<LayoutElement>();
            cardIconElement.preferredHeight = cardIconElement.minHeight = 104f;
            TMP_Text cardName = NewText("Name", cardTemplate, font, 26f, FontStyles.Bold, TextAlignmentOptions.Center, AccentColor);
            TMP_Text cardDescription = NewText("Description", cardTemplate, font, 19f, FontStyles.Normal, TextAlignmentOptions.Top, Color.white);
            TraitCardView cardView = cardTemplate.gameObject.AddComponent<TraitCardView>();
            SetRef(cardView, "button", button);
            SetRef(cardView, "icon", cardIcon);
            SetRef(cardView, "displayName", cardName);
            SetRef(cardView, "description", cardDescription);

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
