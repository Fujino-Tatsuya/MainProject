using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설정창 항목 배치(PLAN-settings 2단계) — 타이틀 옵션 패널의 탭 4개 내용에 항목 행을 쌓는다. <b>멱등</b>.
///
/// 규칙:
/// - 탭 내용마다 세로 정렬 컨테이너 <c>Rows</c>(제목 아래)를 두고 행을 쌓는다. 기존 행(Hold·대시 토글, 볼륨 3개)은 그 안으로 옮긴다.
/// - 이 메뉴가 만든 행은 이름이 <c>Settings_</c> 로 시작한다. 다시 실행하면 그 행만 지우고 새로 만든다(기존 행은 유지).
/// - 겉모습 = SVN r466 UI_Panel 새 아트(PLAN-settings-art) — 행 배치 후 <see cref="SettingsArtSkin"/> 이 틀·탭·컨트롤 그림·글꼴을 입힌다.
/// 패널 본체를 프리팹으로 뽑아 ESC 메뉴와 공유하는 것은 <see cref="SettingsBodyPrefabAuthoring"/>.
/// </summary>
public static class SettingsPanelAuthoring
{
    internal const string TitleScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    internal const string GeneratedPrefix = "Settings_";
    // 새 아트(PLAN-settings-art) — 목업 06_Settings 행 크기. 투명 여백(2px×2)을 포함한 이미지 크기다.
    // 라벨 캡슐 274×57 · 컨트롤 553×57 · 둘 사이 21 · 행 간격 98(목업 드롭다운→슬라이더) → 이미지 61 + 간격 37.
    // 그래픽 12줄·조작 10줄은 스크롤로 넣는다(A2) — 예전처럼 줄 높이를 줄여 맞추지 않는다.
    const float RowHeight = 57f + SettingsArtSkin.Pad * 2;
    const float RowSpacing = 98f - RowHeight;
    const float LabelWidth = 274f + SettingsArtSkin.Pad * 2;
    const float ControlX = 295f;                                  // 라벨 이미지 왼쪽 → 컨트롤 이미지 왼쪽
    const float ControlWidth = 553f + SettingsArtSkin.Pad * 2;
    const float SliderX = 12f;                                    // 컨트롤 칸 왼쪽 → 슬라이더 트랙 이미지
    const float SliderWidth = 432.32f + SettingsArtSkin.Pad * 2;
    const float SliderHeight = 47f + SettingsArtSkin.Pad * 2;     // 손잡이 높이
    const float ValueCenterX = 512.7f;                            // 컨트롤 칸 왼쪽 → "100%" 중심
    static readonly RectOffset RowsPadding = new RectOffset(28, 20, 30, 30);   // 내용 영역(밝은 회색) 안쪽 여백

    static TMP_FontAsset _font;
    static float _fontSize = 18f;
    static Color _fontColor = Color.white;

    internal const string BodyPrefabPath = "Assets/2.Prefabs/UI/SettingsBody.prefab";

    // 🔴 프리팹화(SettingsBodyPrefabAuthoring) 이후엔 **프리팹 원본을 직접 연다** — 씬의 프리팹 인스턴스에서는
    //    자식을 지울 수 없어(Unity 가 막는다) Settings_ 행 재생성이 실패한다. 타이틀·ESC 둘 다 원본을 따라 갱신된다.
    [MenuItem("Tools/UI/Settings/설정 패널 항목 배치 (멱등)")]
    static void AuthorMenu()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BodyPrefabPath);
            try
            {
                if (Author(root.GetComponentInChildren<TitleOptionsPanel>(true)))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, BodyPrefabPath);
                    Debug.Log($"[SettingsAuthoring] 배치 완료 · {BodyPrefabPath} 저장");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(TitleScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);
        try
        {
            if (Author(HoldSkillToggleOptionAuthoring.FindInScene<TitleOptionsPanel>(scene)))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[SettingsAuthoring] 배치 완료 · 씬 저장");
            }
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    internal static bool Author(TitleOptionsPanel panel)
    {
        if (panel == null) { Debug.LogError("[SettingsAuthoring] TitleOptionsPanel 이 없다."); return false; }

        var so = new SerializedObject(panel);
        GameObject gameplay = so.FindProperty("gameplayPanel").objectReferenceValue as GameObject;
        GameObject graphics = so.FindProperty("graphicsPanel").objectReferenceValue as GameObject;
        GameObject controls = so.FindProperty("controlsPanel").objectReferenceValue as GameObject;
        GameObject audio = so.FindProperty("audioPanel").objectReferenceValue as GameObject;
        if (gameplay == null || graphics == null || controls == null || audio == null)
        {
            Debug.LogError("[SettingsAuthoring] TitleOptionsPanel 의 탭 내용 참조가 비어 있다.");
            return false;
        }

        // 글꼴 기준 — Audio 탭 MasterVolumeLabel(Rows 로 옮긴 뒤에도 찾도록 깊이 검색).
        TMP_Text fontRef = FindDeep(audio.transform, "MasterVolumeLabel")?.GetComponent<TMP_Text>();
        if (fontRef == null) { Debug.LogError("[SettingsAuthoring] MasterVolumeLabel 을 못 찾았다 — 글꼴 기준이 없다."); return false; }
        _font = fontRef.font;
        _fontSize = 24f;                 // 새 아트 캡슐 높이(57) 기준
        _fontColor = SettingsArtSkin.TextLight;

        // 새 아트(SVN r466 UI_Panel)가 없으면 아무것도 바꾸지 않는다 — 반쯤 바뀐 패널을 저장하지 않게.
        if (!SettingsArtSkin.LoadSprites()) return false;

        _sliderTemplate = FindDeep(audio.transform, "MasterVolume")?.GetComponent<Slider>();
        BuildGameplay(EnsureRows(gameplay.transform));
        BuildGraphics(EnsureRows(graphics.transform), graphics.transform);
        BuildControls(EnsureRows(controls.transform), controls.transform);
        BuildAudio(EnsureRows(audio.transform), audio.transform);

        var contents = new[] { gameplay, graphics, controls, audio };
        foreach (GameObject content in contents)
            SettingsArtSkin.SkinControls(content.transform);   // 예전 SettingsDarkTheme(10-13) 자리 — 새 아트가 대신한다
        SettingsArtSkin.ApplyFrame(panel.transform, contents);
        SetLayerRecursively(panel.transform, gameplay.layer);

        // 고정 라벨에 언어 전환 키를 붙인다(탭 이름·틀 제목 포함 — 패널 전체).
        SettingsLocalizationLinker.Link(panel.transform);
        SettingsArtSkin.ApplyFont(panel.transform);
        return true;
    }

    // ─── 탭별 ──────────────────────────────────────────────────────────

    static void BuildGameplay(RectTransform rows)
    {
        var c = GetOrAdd<GameplaySettingsController>(rows.gameObject);
        TMP_Dropdown damage = DropdownRow(rows, "DamageNumbers", "Damage Numbers");
        Slider shake = SliderRow(rows, "ScreenShake", "Screen Shake", out TMP_Text shakeValue);
        Button language = ButtonRow(rows, "Language", "Language", "English", out TMP_Text languageStatus);
        TMP_Dropdown performance = DropdownRow(rows, "Performance", "Performance Info");

        var cso = new SerializedObject(c);
        cso.FindProperty("performanceDropdown").objectReferenceValue = performance;
        cso.FindProperty("damageNumbersDropdown").objectReferenceValue = damage;
        cso.FindProperty("screenShakeSlider").objectReferenceValue = shake;
        cso.FindProperty("screenShakeValue").objectReferenceValue = shakeValue;
        cso.FindProperty("languageButton").objectReferenceValue = language;
        cso.FindProperty("languageStatus").objectReferenceValue = languageStatus;
        cso.ApplyModifiedPropertiesWithoutUndo();
    }

    static void BuildGraphics(RectTransform rows, Transform content)
    {
        var c = GetOrAdd<GraphicsSettingsController>(rows.gameObject);
        TMP_Dropdown resolution = DropdownRow(rows, "Resolution", "Resolution");
        TMP_Dropdown window = DropdownRow(rows, "WindowMode", "Window Mode");
        Button apply = ButtonRow(rows, "ApplyDisplay", "", "Apply", out _);
        TMP_Dropdown frame = DropdownRow(rows, "FrameLimit", "Frame Limit");
        Toggle vsync = ToggleRow(rows, "VSync", "VSync");

        // 품질(팀장 10-12) — 프리셋·렌더 스케일·그림자·AA·후처리·시야 디밍 크기/경계.
        var qc = GetOrAdd<GraphicsQualityController>(rows.gameObject);
        TMP_Dropdown preset = DropdownRow(rows, "QualityPreset", "Quality Preset");
        Slider renderScale = SliderRow(rows, "RenderScale", "Render Scale", out TMP_Text renderScaleValue);
        TMP_Dropdown shadows = DropdownRow(rows, "Shadows", "Shadows");
        TMP_Dropdown aa = DropdownRow(rows, "AntiAliasing", "Anti-Aliasing");
        Toggle post = ToggleRow(rows, "PostEffects", "Bloom & Chromatic Aberration");
        Slider vSize = SliderRow(rows, "VignetteSize", "Vignette Size", out TMP_Text vSizeValue);
        Slider vFade = SliderRow(rows, "VignetteEdge", "Vignette Edge", out TMP_Text vFadeValue);
        var qso = new SerializedObject(qc);
        qso.FindProperty("presetDropdown").objectReferenceValue = preset;
        qso.FindProperty("renderScaleSlider").objectReferenceValue = renderScale;
        qso.FindProperty("renderScaleValue").objectReferenceValue = renderScaleValue;
        qso.FindProperty("shadowDropdown").objectReferenceValue = shadows;
        qso.FindProperty("antiAliasingDropdown").objectReferenceValue = aa;
        qso.FindProperty("postEffectsToggle").objectReferenceValue = post;
        qso.FindProperty("vignetteSizeSlider").objectReferenceValue = vSize;
        qso.FindProperty("vignetteSizeValue").objectReferenceValue = vSizeValue;
        qso.FindProperty("vignetteFadeSlider").objectReferenceValue = vFade;
        qso.FindProperty("vignetteFadeValue").objectReferenceValue = vFadeValue;
        qso.ApplyModifiedPropertiesWithoutUndo();

        GameObject dialog = ConfirmDialog(content, out TMP_Text text, out Button keep, out Button revert);

        var cso = new SerializedObject(c);
        cso.FindProperty("resolutionDropdown").objectReferenceValue = resolution;
        cso.FindProperty("windowModeDropdown").objectReferenceValue = window;
        cso.FindProperty("frameLimitDropdown").objectReferenceValue = frame;
        cso.FindProperty("vSyncToggle").objectReferenceValue = vsync;
        cso.FindProperty("applyButton").objectReferenceValue = apply;
        cso.FindProperty("confirmDialog").objectReferenceValue = dialog;
        cso.FindProperty("confirmText").objectReferenceValue = text;
        cso.FindProperty("keepButton").objectReferenceValue = keep;
        cso.FindProperty("revertButton").objectReferenceValue = revert;
        cso.ApplyModifiedPropertiesWithoutUndo();
    }

    static readonly (string path, string label)[] RebindRows =
    {
        ("Player/Attack", "Attack"), ("Player/Interrupt", "Interrupt"), ("Player/SkillMain", "Skill 1 (Main)"),
        ("Player/SkillSub", "Skill 2 (Sub)"), ("Player/SkillUltimate", "Ultimate"), ("Player/Dash", "Dash"),
        ("Player/Interact", "Interact"),
    };

    static void BuildControls(RectTransform rows, Transform content)
    {
        // 기존 토글(Hold·대시)을 맨 위로.
        AdoptToggleRow(rows, content, "HoldSkillToggle");
        AdoptToggleRow(rows, content, "DashDirectionToggle");

        foreach ((string path, string label) in RebindRows)
        {
            RectTransform row = NewRow(rows, "Rebind_" + path.Substring(path.IndexOf('/') + 1), label, out RectTransform slot);
            Button button = NewButton(slot, "KeyButton", "-", out TMP_Text keyText);
            LeftAligned((RectTransform)button.transform, LabelWidth);   // 알약 하나 크기(라벨 캡슐과 같은 폭)
            var r = row.gameObject.AddComponent<KeyRebindRow>();
            var rso = new SerializedObject(r);
            rso.FindProperty("actionPath").stringValue = path;
            rso.FindProperty("actionLabel").objectReferenceValue = FindDeep(row, "Label").GetComponent<TMP_Text>();
            rso.FindProperty("bindButton").objectReferenceValue = button;
            rso.FindProperty("keyText").objectReferenceValue = keyText;
            rso.ApplyModifiedPropertiesWithoutUndo();
        }

        Button reset = ButtonRow(rows, "ResetKeys", "", "Reset Keys to Default", out TMP_Text status);
        var panel = GetOrAdd<KeyRebindPanel>(rows.gameObject);
        var pso = new SerializedObject(panel);
        pso.FindProperty("resetButton").objectReferenceValue = reset;
        pso.FindProperty("statusText").objectReferenceValue = status;
        pso.ApplyModifiedPropertiesWithoutUndo();
    }

    static void BuildAudio(RectTransform rows, Transform content)
    {
        AdoptSliderRow(rows, content, "MasterVolume");
        AdoptSliderRow(rows, content, "BGMVolume");
        AdoptSliderRow(rows, content, "SFXVolume");
        Toggle mute = ToggleRow(rows, "MuteInBackground", "Mute When Unfocused");
        if (mute.GetComponent<MuteInBackgroundOption>() == null) mute.gameObject.AddComponent<MuteInBackgroundOption>();
    }

    // ─── 컨테이너·행 ───────────────────────────────────────────────────

    /// <summary>
    /// 탭 내용의 Rows(세로 정렬)를 스크롤 영역 안에 보장하고, 이전 실행이 만든 Settings_ 행을 지운다.
    /// 🔴 Rows 는 부모 정렬을 무시한다(ignoreLayout) — Audio_Content 에는 원래 VerticalLayoutGroup 이 있어 Rows 자체를 끌고 가
    ///    오디오 탭만 오른쪽 아래로 밀렸다(팀장 10-12 Play). 새 아트에서는 그 정렬 자체를 끈다(<see cref="SettingsArtSkin.ApplyFrame"/>).
    /// </summary>
    static RectTransform EnsureRows(Transform content)
    {
        RectTransform rows = SettingsArtSkin.EnsureScroll(content, RowSpacing, RowsPadding);

        for (int i = rows.childCount - 1; i >= 0; i--)
        {
            Transform ch = rows.GetChild(i);
            if (ch.name.StartsWith(GeneratedPrefix)) Object.DestroyImmediate(ch.gameObject);
        }
        return rows;
    }

    // 🔴 기존 행(볼륨 슬라이더·Hold/대시 토글)은 높이를 가진 감싸개 Row_<이름> 에 넣는다. Rows 는 자식 높이를 제어하지
    //    않아(childControlHeight = false) LayoutElement 높이 대신 **자기 rect 높이(슬라이더 20)** 로 쌓여 볼륨 라벨이
    //    겹쳤다(팀장 10-12 Play). 감싸개 이름은 Settings_ 가 아니라 재실행 때 지워지지 않는다(안의 원본을 지키려고).
    static Transform AdoptExisting(RectTransform rows, Transform content, string name, out RectTransform wrap)
    {
        string wrapName = "Row_" + name;
        Transform found = rows.Find(wrapName);
        Transform t = found != null ? found.Find(name) : (content.Find(name) ?? rows.Find(name));
        wrap = null;
        if (t == null) { Debug.LogWarning($"[SettingsAuthoring] {content.name}/{name} 이 없다 — 건너뜀."); return null; }

        if (found == null)
        {
            var go = new GameObject(wrapName, typeof(RectTransform), typeof(LayoutElement));
            found = go.transform;
            found.SetParent(rows, false);
        }
        wrap = (RectTransform)found;
        wrap.sizeDelta = new Vector2(ControlX + ControlWidth, RowHeight);
        var le = wrap.GetComponent<LayoutElement>();
        le.preferredHeight = RowHeight;
        le.preferredWidth = wrap.sizeDelta.x;
        wrap.SetSiblingIndex(rows.childCount - 1);

        t.SetParent(wrap, false);
        LayoutElement old = t.GetComponent<LayoutElement>();
        if (old != null) Object.DestroyImmediate(old);   // 예전 실행이 붙인 것 — 감싸개가 대신한다
        return t;
    }

    // 기존 볼륨 슬라이더 → 새 행 모양(왼쪽 라벨 캡슐 · 오른쪽 슬라이더 + "100%"). 라벨(MasterVolumeLabel 등)은 슬라이더 자식이던 것을
    // 캡슐 안으로 옮긴다 — LocalizedText 가 붙은 그 오브젝트를 그대로 쓴다. VolumeSlider·TitleScreenSlider 는 손대지 않는다.
    static void AdoptSliderRow(RectTransform rows, Transform content, string name)
    {
        Transform t = AdoptExisting(rows, content, name, out RectTransform wrap);
        if (t == null) return;

        Transform labelBg = EnsureLabelBg(wrap);
        TMP_Text label = FirstText(t, "Value") ?? FirstText(labelBg, null);
        if (label != null) StyleRowLabel(label, labelBg);

        var tr = (RectTransform)t;
        tr.anchorMin = tr.anchorMax = new Vector2(0f, 0.5f);
        tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = new Vector2(ControlX + SliderX, 0f);
        tr.sizeDelta = new Vector2(SliderWidth, SliderHeight);

        Transform valueT = wrap.Find("Value");
        TMP_Text value = valueT != null ? valueT.GetComponent<TMP_Text>() : NewText(wrap, "Value", "100%", TextAlignmentOptions.Center);
        PlaceValue(value.rectTransform, ControlX);
        StyleValue(value);

        var percent = GetOrAdd<SettingsSliderPercent>(wrap.gameObject);
        var so = new SerializedObject(percent);
        so.FindProperty("slider").objectReferenceValue = t.GetComponent<Slider>();
        so.FindProperty("text").objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 기존 토글(Hold·대시 — 은희 저작) → 새 행 모양. 토글 루트를 행 전체로 펴고, 라벨은 캡슐 안, 체크 칸은 컨트롤 자리로.
    static void AdoptToggleRow(RectTransform rows, Transform content, string name)
    {
        Transform t = AdoptExisting(rows, content, name, out RectTransform wrap);
        if (t == null) return;
        var tr = (RectTransform)t;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.offsetMin = tr.offsetMax = Vector2.zero;

        Transform labelBg = EnsureLabelBg(t);
        TMP_Text label = FirstText(t, "LabelBg") ?? FirstText(labelBg, null);
        if (label != null) StyleRowLabel(label, labelBg);

        Toggle toggle = t.GetComponent<Toggle>();
        if (toggle != null && toggle.targetGraphic != null)
        {
            RectTransform box = toggle.targetGraphic.rectTransform;
            box.anchorMin = box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = new Vector2(ControlX, 0f);
            box.sizeDelta = ToggleSize;
        }
    }

    /// <summary>행 왼쪽 라벨 캡슐(settings_label_capsule). 이미 있으면 그대로.</summary>
    static Transform EnsureLabelBg(Transform row)
    {
        Transform t = row.Find("LabelBg");
        if (t == null)
        {
            var go = new GameObject("LabelBg", typeof(RectTransform), typeof(Image));
            t = go.transform;
            t.SetParent(row, false);
        }
        t.SetAsFirstSibling();
        var r = (RectTransform)t;
        r.anchorMin = new Vector2(0f, 0.5f);
        r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta = new Vector2(LabelWidth, RowHeight);
        Image img = t.GetComponent<Image>();
        SettingsArtSkin.SetSliced(img, SettingsArtSkin.LabelCapsule, r.sizeDelta);
        img.raycastTarget = false;
        return t;
    }

    static void StyleRowLabel(TMP_Text label, Transform labelBg)
    {
        label.transform.SetParent(labelBg, false);
        SettingsArtSkin.Stretch(label.rectTransform, new Vector2(18f, 0f), new Vector2(-18f, 0f));
        label.alignment = TextAlignmentOptions.Center;
        label.color = SettingsArtSkin.TextLight;
        label.raycastTarget = false;
        SettingsArtSkin.AutoSize(label, 14f, 26f);
    }

    static void PlaceValue(RectTransform r, float slotX)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(slotX + ValueCenterX, 0f);
        r.sizeDelta = new Vector2(88f, 40f);
    }

    static void StyleValue(TMP_Text t)
    {
        t.alignment = TextAlignmentOptions.Center;
        t.color = SettingsArtSkin.TextDark;
        t.fontSize = 25f;
        t.enableAutoSizing = false;
        t.raycastTarget = false;
    }

    static TMP_Text FirstText(Transform root, string skipName)
    {
        if (root == null) return null;
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (skipName != null && (t.name == skipName || HasAncestorNamed(t.transform, root, skipName))) continue;
            return t;
        }
        return null;
    }

    static bool HasAncestorNamed(Transform t, Transform stop, string name)
    {
        for (Transform p = t.parent; p != null && p != stop; p = p.parent)
            if (p.name == name) return true;
        return false;
    }

    static readonly Vector2 ToggleSize = new Vector2(71.86f + SettingsArtSkin.Pad * 2, 47.64f + SettingsArtSkin.Pad * 2);

    static void LeftAligned(RectTransform r, float width)
    {
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta = new Vector2(width, 0f);
    }

    // 생성·이동한 오브젝트를 탭 내용과 같은 레이어(UI)로. 🔴 새 GameObject 는 Default 레이어라
    // 별도 캔버스(드롭다운 목록)가 타이틀 모니터 카메라(UI 레이어만)에 안 찍혔다(팀장 10-12 Play).
    static void SetLayerRecursively(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
    }

    /// <summary>라벨(왼쪽) + 컨트롤 자리(오른쪽) 한 줄.</summary>
    static RectTransform NewRow(RectTransform rows, string name, string label, out RectTransform controlSlot)
    {
        var go = new GameObject(GeneratedPrefix + name, typeof(RectTransform), typeof(LayoutElement));
        var row = (RectTransform)go.transform;
        row.SetParent(rows, false);
        row.sizeDelta = new Vector2(ControlX + ControlWidth, RowHeight);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = RowHeight;
        le.preferredWidth = row.sizeDelta.x;

        // 라벨 = 캡슐(settings_label_capsule) 안 가운데 글자. 라벨이 빈 행(적용·초기화 버튼)은 캡슐도 없다.
        if (!string.IsNullOrEmpty(label))
        {
            Transform labelBg = EnsureLabelBg(row);
            TMP_Text text = NewText(row, "Label", label, TextAlignmentOptions.Center);
            StyleRowLabel(text, labelBg);
        }

        var slotGo = new GameObject("Control", typeof(RectTransform));
        controlSlot = (RectTransform)slotGo.transform;
        controlSlot.SetParent(row, false);
        controlSlot.anchorMin = new Vector2(0f, 0f); controlSlot.anchorMax = new Vector2(0f, 1f); controlSlot.pivot = new Vector2(0f, 0.5f);
        controlSlot.anchoredPosition = new Vector2(ControlX, 0f);
        controlSlot.sizeDelta = new Vector2(ControlWidth, 0f);
        return row;
    }

    // 🔴 기본 TMP_Dropdown 을 SettingsDropdown 으로 바꾸고 템플릿에 MonitorAwareRaycaster 를 미리 붙인다.
    //    기본 드롭다운은 목록·차단막에 일반 GraphicRaycaster 를 붙여 타이틀 모니터 RT 에서 항목이 안 눌렸다(팀장 10-12 Play).
    static TMP_Dropdown DropdownRow(RectTransform rows, string name, string label)
    {
        NewRow(rows, name, label, out RectTransform slot);
        GameObject dd = TMP_DefaultControls.CreateDropdown(Resources());
        dd.name = "Dropdown";
        Stretch(dd.transform, slot);
        foreach (TMP_Text t in dd.GetComponentsInChildren<TMP_Text>(true)) ApplyFont(t, _fontSize * 0.85f, Color.black);

        TMP_Dropdown basic = dd.GetComponent<TMP_Dropdown>();
        RectTransform template = basic.template;
        TMP_Text caption = basic.captionText;
        TMP_Text item = basic.itemText;
        Graphic target = basic.targetGraphic;
        Object.DestroyImmediate(basic);
        var settings = dd.AddComponent<SettingsDropdown>();
        settings.template = template;
        settings.captionText = caption;
        settings.itemText = item;
        settings.targetGraphic = target;
        if (template != null && template.GetComponent<MonitorAwareRaycaster>() == null)
            template.gameObject.AddComponent<MonitorAwareRaycaster>();   // RequireComponent 로 Canvas 도 붙는다
        return settings;
    }

    static Toggle ToggleRow(RectTransform rows, string name, string label)
    {
        NewRow(rows, name, label, out RectTransform slot);
        GameObject tg = DefaultControls.CreateToggle(UguiResources());
        tg.name = "Toggle";
        var tr = (RectTransform)tg.transform;
        tr.SetParent(slot, false);
        tr.anchorMin = tr.anchorMax = new Vector2(0f, 0.5f); tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = Vector2.zero; tr.sizeDelta = ToggleSize;
        Transform bg = tg.transform.Find("Background");
        if (bg != null) SettingsArtSkin.Stretch((RectTransform)bg, Vector2.zero, Vector2.zero);
        Transform defaultLabel = tg.transform.Find("Label");
        if (defaultLabel != null) Object.DestroyImmediate(defaultLabel.gameObject);
        return tg.GetComponent<Toggle>();
    }

    // 볼륨 슬라이더(TitleScreenSlider — 모니터 RT 드래그 좌표 보정)를 복제해 같은 모양·같은 보정을 쓴다.
    // 일반 Slider 로 만들면 타이틀에서 드래그 값이 튄다. 모니터가 없는 씬(ESC)에서는 TitleScreenSlider 가 기본 동작.
    static Slider _sliderTemplate;

    static Slider SliderRow(RectTransform rows, string name, string label, out TMP_Text valueText)
    {
        NewRow(rows, name, label, out RectTransform slot);
        GameObject sl;
        if (_sliderTemplate != null)
        {
            sl = Object.Instantiate(_sliderTemplate.gameObject);
            foreach (VolumeSlider v in sl.GetComponents<VolumeSlider>()) Object.DestroyImmediate(v);
            foreach (TMP_Text t in sl.GetComponentsInChildren<TMP_Text>(true)) Object.DestroyImmediate(t.gameObject);   // 볼륨 라벨
            LayoutElement copiedLayout = sl.GetComponent<LayoutElement>();
            if (copiedLayout != null) Object.DestroyImmediate(copiedLayout);
        }
        else sl = DefaultControls.CreateSlider(UguiResources());
        sl.name = "Slider";
        var sr = (RectTransform)sl.transform;
        sr.SetParent(slot, false);
        sr.anchorMin = sr.anchorMax = new Vector2(0f, 0.5f); sr.pivot = new Vector2(0f, 0.5f);
        sr.anchoredPosition = new Vector2(SliderX, 0f); sr.sizeDelta = new Vector2(SliderWidth, SliderHeight);

        valueText = NewText(slot, "Value", "100%", TextAlignmentOptions.Center);
        PlaceValue(valueText.rectTransform, 0f);
        StyleValue(valueText);
        return sl.GetComponent<Slider>();
    }

    /// <summary>버튼 한 줄 + 오른쪽 상태 텍스트(알림용). 라벨이 비면 왼쪽 라벨 자리도 빈칸.</summary>
    static Button ButtonRow(RectTransform rows, string name, string label, string buttonText, out TMP_Text status)
    {
        NewRow(rows, name, label, out RectTransform slot);
        var holder = new GameObject("ButtonHolder", typeof(RectTransform));
        var hr = (RectTransform)holder.transform;
        hr.SetParent(slot, false);
        hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(0f, 1f); hr.pivot = new Vector2(0f, 0.5f);
        hr.anchoredPosition = Vector2.zero; hr.sizeDelta = new Vector2(LabelWidth, 0f);
        Button b = NewButton(hr, "Button", buttonText, out _);

        // 상태 글자(초기화 알림 등)는 밝은 회색 내용 영역 위라 어두운 색.
        status = NewText(slot, "Status", "", TextAlignmentOptions.MidlineLeft);
        var st = (RectTransform)status.transform;
        st.anchorMin = new Vector2(0f, 0f); st.anchorMax = new Vector2(1f, 1f);
        st.offsetMin = new Vector2(LabelWidth + 12f, 0f); st.offsetMax = Vector2.zero;
        status.fontSize = 20f;
        status.color = SettingsArtSkin.TextDark;
        return b;
    }

    static Button NewButton(RectTransform parent, string name, string text, out TMP_Text label)
    {
        GameObject go = TMP_DefaultControls.CreateButton(Resources());
        go.name = name;
        Stretch(go.transform, parent);
        label = go.GetComponentInChildren<TMP_Text>(true);
        label.text = text;
        ApplyFont(label, _fontSize * 0.85f, Color.black);
        return go.GetComponent<Button>();
    }

    static GameObject ConfirmDialog(Transform content, out TMP_Text text, out Button keep, out Button revert)
    {
        Transform old = content.Find(GeneratedPrefix + "ConfirmDialog");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject(GeneratedPrefix + "ConfirmDialog", typeof(RectTransform), typeof(Image));
        var r = (RectTransform)go.transform;
        r.SetParent(content, false);
        r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(640f, 280f);
        SettingsArtSkin.SkinDialog(go.GetComponent<Image>());   // 펼친 드롭다운과 같은 어두운 판 · 뒤 클릭 차단

        text = NewText(r, "Message", "Keep these display settings?", TextAlignmentOptions.Center);
        var tr = (RectTransform)text.transform;
        tr.anchorMin = new Vector2(0f, 0.4f); tr.anchorMax = new Vector2(1f, 1f);
        tr.offsetMin = new Vector2(36f, 0f); tr.offsetMax = new Vector2(-36f, -28f);
        text.fontSize = 28f;

        // 유지 = 초록 알약 · 되돌리기 = 빨강 알약(SettingsArtSkin.SkinControls 가 부모 이름으로 고른다).
        keep = DialogButton(r, "Keep", "Keep", new Vector2(-140f, 62f));
        revert = DialogButton(r, "Revert", "Revert", new Vector2(140f, 62f));
        go.SetActive(false);
        return go;
    }

    static Button DialogButton(RectTransform parent, string name, string text, Vector2 pos)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        var hr = (RectTransform)holder.transform;
        hr.SetParent(parent, false);
        hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
        hr.anchoredPosition = pos; hr.sizeDelta = new Vector2(240f, RowHeight);
        return NewButton(hr, "Button", text, out _);
    }

    // ─── 공용 ─────────────────────────────────────────────────────────

    static TMP_Text NewText(RectTransform parent, string name, string value, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = value;
        t.alignment = align;
        t.raycastTarget = false;
        ApplyFont(t, _fontSize, _fontColor);
        return t;
    }

    static void ApplyFont(TMP_Text t, float size, Color color)
    {
        if (_font != null) t.font = _font;
        t.fontSize = size;
        t.color = color;
    }

    static void Stretch(Transform child, RectTransform parent)
    {
        var r = (RectTransform)child;
        r.SetParent(parent, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    static TMP_DefaultControls.Resources Resources() => new TMP_DefaultControls.Resources
    {
        standard = Builtin("UI/Skin/UISprite.psd"),
        background = Builtin("UI/Skin/Background.psd"),
        inputField = Builtin("UI/Skin/InputFieldBackground.psd"),
        knob = Builtin("UI/Skin/Knob.psd"),
        checkmark = Builtin("UI/Skin/Checkmark.psd"),
        dropdown = Builtin("UI/Skin/DropdownArrow.psd"),
        mask = Builtin("UI/Skin/UIMask.psd"),
    };

    static DefaultControls.Resources UguiResources() => new DefaultControls.Resources
    {
        standard = Builtin("UI/Skin/UISprite.psd"),
        background = Builtin("UI/Skin/Background.psd"),
        inputField = Builtin("UI/Skin/InputFieldBackground.psd"),
        knob = Builtin("UI/Skin/Knob.psd"),
        checkmark = Builtin("UI/Skin/Checkmark.psd"),
        dropdown = Builtin("UI/Skin/DropdownArrow.psd"),
        mask = Builtin("UI/Skin/UIMask.psd"),
    };

    // 🔴 `GetComponent<T>() ?? Add` 를 쓰지 말 것 — 에디터에서 없는 컴포넌트는 "가짜 null" 이라 ?? 가 통과시킨다.
    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static Sprite Builtin(string path) => AssetDatabase.GetBuiltinExtraResource<Sprite>(path);

    internal static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
