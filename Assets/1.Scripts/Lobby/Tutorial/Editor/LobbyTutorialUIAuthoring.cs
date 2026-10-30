using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 튜토리얼 UI 프리팹 저작(할 일 158) — 메뉴 <c>Tools/UI/로비 튜토리얼 UI 구성</c>.
/// <c>Assets/2.Prefabs/UI/Lobby/Tutorial/LobbyTutorialUI.prefab</c> 을 만들거나 고친다. 멱등 — 이름으로 찾아 있으면 값만 다시 맞춘다
/// (사람이 인스펙터에서 바꾼 배치·크기도 이 메뉴를 다시 돌리면 아래 기본값으로 돌아간다).
/// 아트 = 50.Art/UI/Tutorial(SVN). 런타임 생성 없음 — 에디터에서 한 번 굽는다.
/// </summary>
internal static class LobbyTutorialUIAuthoring
{
    private const string PrefabPath = "Assets/2.Prefabs/UI/Lobby/Tutorial/LobbyTutorialUI.prefab";
    private const string ArtDir = "Assets/50.Art/UI/Tutorial/";
    private const string FontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";
    private const string TagStylesPath = "Assets/2.Prefabs/UI/SkillTooltipGenerated/SkillTooltipTagStyles.asset";

    private static readonly Color TitleGold = new Color32(0xF0, 0xC8, 0x70, 0xFF);

    [MenuItem("Tools/UI/로비 튜토리얼 UI 구성")]
    private static void Build()
    {
        var textBox = LoadSprite("TextBox");
        var nameTag = LoadSprite("NameTag");
        var operatorSprite = LoadSprite("Operator");
        var factory = LoadSprite("Scene_01");
        var rebellion = LoadSprite("Scene_02");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (textBox == null || nameTag == null || operatorSprite == null || factory == null || rebellion == null || font == null)
        {
            Debug.LogError($"[LobbyTutorialUIAuthoring] 아트·글꼴을 못 찾았다 — {ArtDir} 의 png 5장(.meta 포함, SVN r560)과 {FontPath} 확인.");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        bool exists = File.Exists(PrefabPath);
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("LobbyTutorialUI", typeof(RectTransform));
        try
        {
            BuildRoot(root);
            var illustration = BuildIllustration(root.transform, out Image back, out Image front);
            var safe = Child(root.transform, "SafeArea");
            Stretch(safe);
            Ensure<LobbySafeAreaFitter>(safe.gameObject);

            var objective = BuildObjectivePanel(safe, textBox, font);
            var dialogue = BuildDialoguePanel(safe, textBox, nameTag, operatorSprite, font, illustration, back, front, factory, rebellion);

            var ui = Ensure<LobbyTutorialUI>(root);
            var so = new SerializedObject(ui);
            so.FindProperty("objectivePanel").objectReferenceValue = objective;
            so.FindProperty("dialoguePanel").objectReferenceValue = dialogue;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 디버그 구동(C4) — 꺼 둔 채. 켜고 인스펙터 ⋮ 메뉴로 화면 01–27 을 넘긴다.
            var debug = Child(root.transform, "DebugDriver");
            Ensure<LobbyTutorialUIDebugDriver>(debug.gameObject);
            debug.gameObject.SetActive(false);

            DisableRaycasts(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[LobbyTutorialUIAuthoring] {(exists ? "갱신" : "생성")}: {PrefabPath}");
        }
        finally
        {
            if (exists)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);
        }
    }

    private static void BuildRoot(GameObject root)
    {
        root.name = "LobbyTutorialUI";
        var canvas = Ensure<Canvas>(root);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = Ensure<CanvasScaler>(root);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 클릭을 막지 않는다 — Raycaster 를 두지 않는다.
        var raycaster = root.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            Object.DestroyImmediate(raycaster, true);
    }

    // ---------------- 목표 패널 ----------------

    private static LobbyObjectivePanelView BuildObjectivePanel(Transform parent, Sprite textBox, TMP_FontAsset font)
    {
        var panel = Child(parent, "ObjectivePanel");
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(44f, -40f);
        panel.sizeDelta = new Vector2(560f, 0f);

        var group = Ensure<CanvasGroup>(panel.gameObject);
        group.alpha = 0f; // 목표가 들어오기 전엔 안 보인다(런타임은 LobbyTutorialUI 가 정한다).
        var background = Ensure<Image>(panel.gameObject);
        background.sprite = textBox;
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 1.5f;
        background.color = new Color(1f, 1f, 1f, 0.8f);

        var layout = Ensure<VerticalLayoutGroup>(panel.gameObject);
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = Ensure<ContentSizeFitter>(panel.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var chapter = Text(panel, "Chapter", font, 23f, FontStyles.Normal, TitleGold, 0);
        var objective = Text(panel, "Objective", font, 32f, FontStyles.Bold, Color.white, 1);
        var body = Text(panel, "Body", font, 24f, FontStyles.Normal, new Color32(0xE6, 0xE6, 0xE6, 0xFF), 2);
        var checklist = Text(panel, "Checklist", font, 24f, FontStyles.Normal, new Color32(0xE6, 0xE6, 0xE6, 0xFF), 3);
        checklist.lineSpacing = 8f;

        var dividerRect = Child(panel, "Divider");
        dividerRect.SetSiblingIndex(4);
        var divider = Ensure<Image>(dividerRect.gameObject);
        divider.sprite = null;
        divider.color = new Color32(0xE3, 0xB9, 0x5A, 0xFF);
        var dividerLayout = Ensure<LayoutElement>(dividerRect.gameObject);
        dividerLayout.minHeight = 2f;
        dividerLayout.preferredHeight = 2f;

        var progress = Text(panel, "Progress", font, 24f, FontStyles.Normal, Color.white, 5);

        var view = Ensure<LobbyObjectivePanelView>(panel.gameObject);
        var so = new SerializedObject(view);
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("panel").objectReferenceValue = panel;
        so.FindProperty("background").objectReferenceValue = background;
        so.FindProperty("chapterText").objectReferenceValue = chapter;
        so.FindProperty("objectiveText").objectReferenceValue = objective;
        so.FindProperty("bodyText").objectReferenceValue = body;
        so.FindProperty("checklistText").objectReferenceValue = checklist;
        so.FindProperty("divider").objectReferenceValue = divider;
        so.FindProperty("progressText").objectReferenceValue = progress;
        so.FindProperty("tagStyles").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SkillTooltipTagStyles>(TagStylesPath);
        so.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    // ---------------- 대사 패널 · 삽화 ----------------

    private static CanvasGroup BuildIllustration(Transform root, out Image back, out Image front)
    {
        var rect = Child(root, "Illustration");
        rect.SetSiblingIndex(0); // 맨 뒤(대사 패널이 위에 온다)
        Stretch(rect);
        var group = Ensure<CanvasGroup>(rect.gameObject);
        group.alpha = 0f;
        var black = Ensure<Image>(rect.gameObject);
        black.sprite = null;
        black.color = Color.black;

        back = FullImage(rect, "Back");
        front = FullImage(rect, "Front");
        return group;
    }

    private static Image FullImage(Transform parent, string name)
    {
        var rect = Child(parent, name);
        Stretch(rect);
        var image = Ensure<Image>(rect.gameObject);
        image.preserveAspect = true; // 16:9 외 비율은 레터박스(검정 바탕)
        image.color = Color.white;
        return image;
    }

    private static LobbyDialoguePanelView BuildDialoguePanel(Transform parent, Sprite textBox, Sprite nameTag, Sprite operatorSprite, TMP_FontAsset font,
        CanvasGroup illustration, Image back, Image front, Sprite factory, Sprite rebellion)
    {
        // 1920×1080 기준 가로 20–80%, 세로(위에서) 64–84% → 아래 기준 16–36%.
        var panel = Child(parent, "DialoguePanel");
        panel.anchorMin = new Vector2(0.2f, 0.16f);
        panel.anchorMax = new Vector2(0.8f, 0.36f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        var group = Ensure<CanvasGroup>(panel.gameObject);
        group.alpha = 0f;
        var background = Ensure<Image>(panel.gameObject);
        background.sprite = textBox;
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 1f;
        background.color = new Color(1f, 1f, 1f, 0.92f);

        // 인물화 — 패널 오른쪽 끝에 겹쳐 서고 위로 솟는다. 본문은 오른쪽 여백으로 피한다.
        var portraitRect = Child(panel, "Portrait");
        portraitRect.SetSiblingIndex(0);
        portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(1f, 0f);
        portraitRect.pivot = new Vector2(0.5f, 0f);
        portraitRect.anchoredPosition = new Vector2(-130f, 0f);
        portraitRect.sizeDelta = new Vector2(346f, 400f);
        var portrait = Ensure<Image>(portraitRect.gameObject);
        portrait.sprite = operatorSprite;
        portrait.preserveAspect = true;

        // 이름표 — 왼쪽 위 테두리에 걸친다.
        var tagRect = Child(panel, "NameTag");
        tagRect.SetSiblingIndex(1);
        tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
        tagRect.pivot = new Vector2(0f, 0.5f);
        tagRect.anchoredPosition = new Vector2(48f, 0f);
        tagRect.sizeDelta = new Vector2(300f, 64f);
        var tagImage = Ensure<Image>(tagRect.gameObject);
        tagImage.sprite = nameTag;
        tagImage.type = Image.Type.Sliced;
        tagImage.pixelsPerUnitMultiplier = 3.6f;
        var speaker = Text(tagRect, "Speaker", font, 28f, FontStyles.Bold, TitleGold, 0);
        Stretch((RectTransform)speaker.transform);
        speaker.alignment = TextAlignmentOptions.Center;
        speaker.textWrappingMode = TextWrappingModes.NoWrap;

        var line = Text(panel, "Line", font, 30f, FontStyles.Normal, Color.white, 2);
        var lineRect = (RectTransform)line.transform;
        lineRect.anchorMin = Vector2.zero;
        lineRect.anchorMax = Vector2.one;
        lineRect.offsetMin = new Vector2(56f, 28f);
        lineRect.offsetMax = new Vector2(-330f, -44f);
        line.alignment = TextAlignmentOptions.MidlineLeft;
        line.lineSpacing = 10f;

        var view = Ensure<LobbyDialoguePanelView>(panel.gameObject);
        var so = new SerializedObject(view);
        so.FindProperty("panelGroup").objectReferenceValue = group;
        so.FindProperty("speakerText").objectReferenceValue = speaker;
        so.FindProperty("lineText").objectReferenceValue = line;
        so.FindProperty("illustrationGroup").objectReferenceValue = illustration;
        so.FindProperty("illustrationBack").objectReferenceValue = back;
        so.FindProperty("illustrationFront").objectReferenceValue = front;
        so.FindProperty("factorySprite").objectReferenceValue = factory;
        so.FindProperty("rebellionSprite").objectReferenceValue = rebellion;
        so.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    // ---------------- 공용 ----------------

    private static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size, FontStyles style, Color color, int sibling)
    {
        var rect = Child(parent, name);
        rect.SetSiblingIndex(Mathf.Min(sibling, parent.childCount - 1));
        var text = Ensure<TextMeshProUGUI>(rect.gameObject);
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.enableAutoSizing = false; // 글자 축소보다 줄바꿈(명세 3.1)
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = true;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.text = string.Empty;
        return text;
    }

    private static RectTransform Child(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            found = go.transform;
        }

        return (RectTransform)found;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static T Ensure<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

    private static void DisableRaycasts(GameObject root)
    {
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
        foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true))
        {
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }

    private static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + name + ".png");
}
