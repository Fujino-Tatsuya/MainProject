using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 안내 UI 프리팹과 문구 테이블을 만드는 멱등 메뉴(할 일 15 S6).
/// 프리팹은 매번 통째로 다시 만든다(guid 유지). 문구 테이블은 빠진 행만 덧붙이고 채운 문구는 건드리지 않는다.
/// </summary>
public static class TutorialGuideUIAuthoring
{
    private const string PrefabFolder = "Assets/2.Prefabs/UI/Tutorial";
    private const string PrefabPath = PrefabFolder + "/TutorialGuideUI.prefab";
    private const string TextFolder = "Assets/9.ScriptableObject/Tutorial/Guide";
    private const string TextTablePath = TextFolder + "/TutorialGuideTexts.asset";
    // 기존 HUD(스킬 툴팁)와 같은 한국어 폰트. 정적 아틀라스라 ☐/☑ 가 없어 체크 표시는 □/■ 를 쓴다.
    private const string FontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";

    // CombatHUD(0)·캐릭터 HUD(10) 위. 로딩 화면 등 더 위에 와야 하는 캔버스는 이보다 크게 둘 것.
    private const int SortingOrder = 50;
    private const float DefaultSeconds = 6f;

    private static readonly Color Accent = new Color(1f, 0.82f, 0.3f, 1f);

    // 초안 문구(할 일 15 위임 사양 그대로). 나머지 id 는 빈 행으로 만든다 — 메인 S7~S10 에서 채운다.
    private static readonly Dictionary<string, (string text, float seconds)> Seeds = new Dictionary<string, (string, float)>
    {
        { TutorialGuideIds.S0Welcome, ("작전 구역에 진입했습니다. 기본 조작부터 점검합니다.", DefaultSeconds) },
        { TutorialGuideIds.ZoneClear, ("구역 확보. 다음 복도가 열렸습니다.", DefaultSeconds) },
        { TutorialGuideIds.TipRCancel, ("R(궁극기)은 대상을 지정하는 동안 좌클릭으로 확정합니다. 취소는 R 을 다시 누르거나 우클릭.", 8f) },
        { TutorialGuideIds.TimerExpired, ("시간이 다 되었습니다 — 원흉이 기다립니다.", DefaultSeconds) },
        { TutorialGuideIds.WaitParty, ("동료를 기다리는 중 ({n}/{total}) · {mm:ss} 후 자동 진행", DefaultSeconds) },
    };

    // v8 시나리오(Docs/design/tutorial-scenario.md §5) 문구 — 메뉴 "튜토리얼 안내 문구 v8 적용" 이 덮어쓴다(행이 없으면 추가).
    // 🔴 빈 행 규칙: 목표 줄 변형과 이름이 겹치는 TUT_Z1_ATTACK_GUNNER · TUT_Z1_PASSIVE_{캐릭터} 는 여기서 채우지 않는다
    //    (채우면 그 캐릭터의 목표 줄이 자막 문구로 바뀐다 — 자막은 TUT_Z1_ATTACK_HEAT · TUT_Z1_PASSIVE_INFO_* 행). Docs/tech/tutorial-zone1.md.
    //    반대로 TUT_Z1_Q_GUNNER · TUT_Z1_E_* · TUT_Z1_R_ASSASSIN 은 일부러 채운다 — 그 캐릭터의 Q/E/R 목표 줄을 힌트로 바꾸는 것이 의도(v8 캐릭터별 힌트).
    internal static readonly (string id, string text, float seconds)[] V8Texts =
    {
        (TutorialGuideIds.WaitPartyNoAuto, "동료를 기다리는 중 ({n}/{total})", DefaultSeconds),
        (TutorialGuideIds.Z1Attack, "<b>좌클릭</b>으로 기본 공격을 사용하세요 ({n}/1)", DefaultSeconds),
        (TutorialGuideIds.Z1Q, "<b>Q</b> 스킬 [{Q}]을 사용하세요 ({n}/1)", DefaultSeconds),
        (TutorialGuideIds.Z1E, "<b>E</b> 스킬 [{E}]을 사용하세요 ({n}/1)", DefaultSeconds),
        (TutorialGuideIds.Z1Rmb, "<b>우클릭</b> 스킬을 사용하세요 ({n}/1)", DefaultSeconds),
        (TutorialGuideIds.Z1R, "<b>R</b> 궁극기를 사용하세요 ({n}/1)", DefaultSeconds),
        (TutorialGuideIds.Z1R + "_ASSASSIN", "허수아비를 공격해 분노를 모은 뒤 <b>R</b>로 변신하세요.", DefaultSeconds),
        (TutorialGuideIds.Z1Q + "_GUNNER", "<b>Q</b>로 충전을 시작하고 <b>좌클릭</b>으로 발사하세요.", DefaultSeconds),
        (TutorialGuideIds.Z1E + "_PALADIN", "<b>E</b>로 방어 자세를 취하세요. 공격을 막으면 반격합니다.", DefaultSeconds),
        (TutorialGuideIds.Z1E + "_GUNNER", "<b>E</b>로 뒤로 물러나며 과열을 식혀 보세요.", DefaultSeconds),
        (TutorialGuideIds.Z1E + "_ASSASSIN", "<b>E</b>로 다음 기본 공격을 강화하세요.", DefaultSeconds),
        ("TUT_Z1_CHECK_ATTACK", "기본 공격", DefaultSeconds),
        ("TUT_Z1_CHECK_Q", "Q 스킬", DefaultSeconds),
        ("TUT_Z1_CHECK_E", "E 스킬", DefaultSeconds),
        ("TUT_Z1_CHECK_RMB", "우클릭 스킬", DefaultSeconds),
        ("TUT_Z1_CHECK_R", "R 궁극기", DefaultSeconds),
        (TutorialCorridor1CooldownFlow.CooldownResetId, "모든 스킬의 재사용 대기시간이 초기화되었습니다.", 5f),
        (TutorialGuideIds.Z2Intro, "적이 깨어납니다! 모든 적을 처치하세요.", 4f),
        (TutorialGuideIds.Z2Goal, "적 처치 {n}/{total}", DefaultSeconds),
        // 존3 — 공통 안내만(캐릭터별 간파 설명 없음). "/3" 은 목표 복제값 {total}.
        (TutorialGuideIds.Z3Intro, "공격을 <b>간파</b>해 기회를 만들고, 취약한 적을 <b>벽으로 밀어내세요</b>. 동료도 이어서 밀 수 있습니다.", 7f),
        (TutorialGuideIds.Z3PracticeNote, "연계를 세 번 성공할 때까지 스피너봇은 쓰러지지 않고 공격도 피해를 주지 않습니다.", 6f),
        (TutorialGuideIds.Z3Goal, "간파 → 밀치기 → 벽 충돌 연계 ({n}/{total})", DefaultSeconds),
        (TutorialGuideIds.Z3CounterGoal, "노랗게 빛날 때 가까이에서 적을 향해 <b>우클릭</b>으로 간파하세요.", DefaultSeconds),
        (TutorialGuideIds.Z3Miss, "빗나갔습니다 — 더 가까이, 스피너봇을 향해.", 4f),
        (TutorialGuideIds.Z3Early, "아직입니다 — 노랗게 빛날 때만 간파됩니다.", 4f),
        (TutorialGuideIds.Z3FirstSuccess, "간파 성공! 공격이 끊기고 적이 취약해집니다.", 4f),
        (TutorialGuideIds.Z3PushIntro, "우클릭이 다시 준비되었습니다. <b>벽 반대편</b>에서 적을 향해 우클릭하면 벽으로 밀어낼 수 있습니다.", 7f),
        (TutorialGuideIds.Z3PushGoal, "취약한 스피너봇을 <b>우클릭으로 벽에 부딪히게</b> 하세요.", DefaultSeconds),
        (TutorialGuideIds.Z3PushHint, "아직 벽에 닿지 않았습니다. 위치를 잡고 다시 밀어내세요. 동료도 이어서 밀 수 있습니다.", 5f),
        (TutorialGuideIds.Z3WallSuccess, "벽 충돌! 연계 성공 ({n}/{total})", 4f),
        (TutorialGuideIds.Z3Return, "다음 연습을 준비합니다.", 3f),
        (TutorialGuideIds.Z3Hint, "요령: 노란빛이 보이면 바로 붙어서 우클릭. 간파는 다시 쓰기까지 몇 초 걸립니다.", 6f),
        (TutorialGuideIds.Z3Real, "연습 끝. 이제 진짜입니다! 노란빛이 보이면 언제든 간파하세요.", 6f),
    };

    [MenuItem("Tools/UI/튜토리얼 안내 문구 v8 적용")]
    public static void ApplyV8Texts()
    {
        TutorialGuideTextTable table = EnsureTextTable();
        int changed = 0;
        foreach ((string id, string text, float seconds) in V8Texts)
        {
            if (table.SetRowText(id, text, seconds))
                changed++;
        }

        if (changed > 0)
        {
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
        }
        Debug.Log($"[TutorialGuide] v8 문구 적용 — {changed}행 바뀜(전체 {V8Texts.Length}행).");
    }

    [MenuItem("Tools/UI/튜토리얼 안내 UI 구성")]
    public static void Configure()
    {
        TutorialGuideTextTable table = EnsureTextTable();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            Debug.LogWarning($"[TutorialGuide] 폰트를 못 찾았다: {FontPath} — TMP 기본 폰트로 만든다.");

        BuildPrefab(table, font);
        AssetDatabase.SaveAssets();
        Debug.Log($"[TutorialGuide] 구성 완료 — {PrefabPath}, {TextTablePath}");
    }

    // ---------------- 문구 테이블 ----------------

    private static TutorialGuideTextTable EnsureTextTable()
    {
        EnsureFolder(TextFolder);
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TextTablePath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<TutorialGuideTextTable>();
            AssetDatabase.CreateAsset(table, TextTablePath);
        }

        int added = 0;
        foreach (string id in TutorialGuideIds.All)
        {
            (string text, float seconds) seed = Seeds.TryGetValue(id, out var value) ? value : (string.Empty, DefaultSeconds);
            if (table.AddRowIfMissing(id, seed.text, seed.seconds))
                added++;
        }

        if (added > 0)
        {
            EditorUtility.SetDirty(table);
            Debug.Log($"[TutorialGuide] 문구 행 {added}개 추가.");
        }

        return table;
    }

    // ---------------- 프리팹 ----------------

    private static void BuildPrefab(TutorialGuideTextTable table, TMP_FontAsset font)
    {
        EnsureFolder(PrefabFolder);

        var root = new GameObject("TutorialGuideUI", typeof(RectTransform));
        try
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            // GraphicRaycaster 없음 — 이 캔버스는 클릭을 받지 않는다.

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;

            var ui = root.AddComponent<TutorialGuideUI>();

            // 강조가 먼저(뒤) — 목표 패널·자막이 화살표 위에 그려진다.
            RectTransform highlightLayer = Stretch(Child(root.transform, "HighlightLayer"));
            TutorialHighlightMarker template = BuildHighlightTemplate(highlightLayer, font);

            var objective = BuildObjectivePanel(root.transform, font);
            var subtitle = BuildSubtitle(root.transform, font);

            var so = new SerializedObject(ui);
            so.FindProperty("textTable").objectReferenceValue = table;
            so.FindProperty("objectivePanel").objectReferenceValue = objective.group;
            so.FindProperty("objectiveText").objectReferenceValue = objective.title;
            so.FindProperty("progressText").objectReferenceValue = objective.progress;
            so.FindProperty("checklistText").objectReferenceValue = objective.checklist;
            so.FindProperty("waitText").objectReferenceValue = objective.wait;
            so.FindProperty("subtitleGroup").objectReferenceValue = subtitle.group;
            so.FindProperty("subtitleText").objectReferenceValue = subtitle.text;
            so.FindProperty("highlightLayer").objectReferenceValue = highlightLayer;
            so.FindProperty("highlightTemplate").objectReferenceValue = template;
            so.FindProperty("sfxSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (!success)
                Debug.LogError($"[TutorialGuide] 프리팹 저장 실패: {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static TutorialHighlightMarker BuildHighlightTemplate(RectTransform layer, TMP_FontAsset font)
    {
        RectTransform marker = Child(layer, "HighlightTemplate");
        Center(marker, new Vector2(100f, 100f));
        var component = marker.gameObject.AddComponent<TutorialHighlightMarker>();

        RectTransform frame = Stretch(Child(marker, "Frame"));
        var frameGroup = frame.gameObject.AddComponent<CanvasGroup>();
        frameGroup.interactable = false;
        frameGroup.blocksRaycasts = false;
        const float thickness = 4f;
        Border(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness));
        Border(frame, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness));
        Border(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f));
        Border(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f));

        RectTransform arrow = Child(marker, "Arrow");
        arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0.5f);
        arrow.pivot = new Vector2(0.5f, 0f);
        arrow.sizeDelta = new Vector2(60f, 50f);
        TextMeshProUGUI arrowText = Text(arrow, font, 44f, Accent, TextAlignmentOptions.Bottom);
        arrowText.text = "▼";

        var so = new SerializedObject(component);
        so.FindProperty("frame").objectReferenceValue = frame;
        so.FindProperty("arrow").objectReferenceValue = arrow;
        so.FindProperty("frameGroup").objectReferenceValue = frameGroup;
        so.ApplyModifiedPropertiesWithoutUndo();

        marker.gameObject.SetActive(false);
        return component;
    }

    private static void Border(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
    {
        RectTransform edge = Child(parent, name);
        edge.anchorMin = anchorMin;
        edge.anchorMax = anchorMax;
        edge.pivot = new Vector2(Mathf.Approximately(anchorMin.x, anchorMax.x) ? anchorMin.x : 0.5f,
            Mathf.Approximately(anchorMin.y, anchorMax.y) ? anchorMin.y : 0.5f);
        edge.anchoredPosition = Vector2.zero;
        edge.sizeDelta = size;
        Image image = edge.gameObject.AddComponent<Image>();
        image.color = Accent;
        image.raycastTarget = false;
    }

    private static (CanvasGroup group, TMP_Text title, TMP_Text progress, TMP_Text checklist, TMP_Text wait)
        BuildObjectivePanel(Transform root, TMP_FontAsset font)
    {
        // 화면 상단 중앙 — 튜토리얼 중엔 보스 HP 바(BossInfoSlot)가 없어 비어 있다.
        RectTransform panel = Child(root, "ObjectivePanel");
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -24f);
        panel.sizeDelta = new Vector2(640f, 120f);
        Background(panel, new Color(0f, 0f, 0f, 0.55f));
        CanvasGroup group = Group(panel, 0f);
        Vertical(panel, new RectOffset(22, 22, 12, 14), 6f);

        RectTransform header = Child(panel, "Header");
        var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 12f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        TextMeshProUGUI title = Text(Child(header, "ObjectiveText"), font, 26f, Color.white, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        TextMeshProUGUI progress = Text(Child(header, "ProgressText"), font, 24f, Accent, TextAlignmentOptions.Right);
        progress.textWrappingMode = TextWrappingModes.NoWrap;

        TextMeshProUGUI checklist = Text(Child(panel, "ChecklistText"), font, 21f, new Color(0.9f, 0.9f, 0.9f, 1f), TextAlignmentOptions.Left);
        TextMeshProUGUI wait = Text(Child(panel, "WaitText"), font, 19f, new Color(0.6f, 0.85f, 1f, 1f), TextAlignmentOptions.Left);

        return (group, title, progress, checklist, wait);
    }

    private static (CanvasGroup group, TMP_Text text) BuildSubtitle(Transform root, TMP_FontAsset font)
    {
        // 화면 중앙 위쪽. 스킬바 위 툴팁 자리(430×270)와 우측 하단(타이머·미니맵·키가이드)을 피한다.
        // 목표 패널이 체크리스트 9줄까지 늘어나도 겹치지 않는 높이.
        RectTransform box = Child(root, "Subtitle");
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.anchoredPosition = new Vector2(0f, 120f);
        box.sizeDelta = new Vector2(1100f, 80f);
        Background(box, new Color(0f, 0f, 0f, 0.6f));
        CanvasGroup group = Group(box, 0f);
        Vertical(box, new RectOffset(28, 28, 14, 14), 0f);

        TextMeshProUGUI text = Text(Child(box, "SubtitleText"), font, 30f, Color.white, TextAlignmentOptions.Center);
        text.maxVisibleLines = 2;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return (group, text);
    }

    // ---------------- 헬퍼 ----------------

    private static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static RectTransform Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    private static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private static void Background(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static CanvasGroup Group(RectTransform rect, float alpha)
    {
        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = alpha;
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    private static void Vertical(RectTransform rect, RectOffset padding, float spacing)
    {
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private static TextMeshProUGUI Text(RectTransform rect, TMP_FontAsset font, float size, Color color, TextAlignmentOptions alignment)
    {
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
        }

        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.richText = true;
        text.text = string.Empty;
        return text;
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
