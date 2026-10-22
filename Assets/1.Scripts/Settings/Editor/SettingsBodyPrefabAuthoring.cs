using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설정 패널 본체를 프리팹으로 뽑아 타이틀·인게임 ESC 가 같이 쓰게 한다(PLAN-settings S1·S12 — 3단계). <b>멱등</b>.
///
/// 본체 = 탭 4개 + 탭 내용 4개 + <see cref="TitleOptionsPanel"/>(탭 전환). 제목·닫기·나가기 같은 **바깥 틀은 각 씬 것을 그대로 둔다**
/// (타이틀 닫기 = TitleSceneManager 코드 연결 / 맵 닫기 = 버튼 SetActive — 씬마다 달라 본체에 넣으면 안 된다).
///
/// 1) 타이틀: <c>Option_Panel_Background</c> 아래 탭·내용을 <c>SettingsBody</c> 로 모으고, 탭 전환 컴포넌트를 그리로 옮긴 뒤
///    (탭 버튼 OnClick 재연결) 프리팹 <c>Assets/2.Prefabs/UI/SettingsBody.prefab</c> 로 저장·연결.
/// 2) 맵: <c>OptionPanel</c> 의 옛 <c>Audio_Content</c>(볼륨 3개)를 지우고 그 자리에 프리팹 인스턴스.
/// 씬 변경은 이것뿐이다(S12 — 튜토리얼 브랜치와의 충돌 최소화).
/// </summary>
public static class SettingsBodyPrefabAuthoring
{
    const string PrefabPath = "Assets/2.Prefabs/UI/SettingsBody.prefab";
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";
    const string BodyName = "SettingsBody";
    static readonly string[] TabNames = { "Gameplay_Tab", "Graphics_Tab", "Controls_Tab", "Audio_Tab" };
    static readonly string[] ContentNames = { "Gameplay_Content", "Graphics_Content", "Controls_Content", "Audio_Content" };
    static readonly string[] ShowMethods = { "ShowGameplay", "ShowGraphics", "ShowControls", "ShowAudio" };

    [MenuItem("Tools/UI/Settings/설정 패널 프리팹화 + ESC 메뉴 적용 (멱등)")]
    static void Run()
    {
        // 🔴 PLAN-settings-ddol(10-22) 이후 타이틀·맵 씬에 설정창이 없다 — 설정창은 GameMenu.prefab(DDOL) 하나다.
        //    이 일회성 이관을 다시 돌리면 옛 구조를 찾다 실패하거나 씬에 되살린다. 항목 배치는 SettingsPanelAuthoring(프리팹 원본).
        Debug.LogError("[SettingsBody] 쓰지 않는 도구 — 설정창은 GameMenu.prefab 으로 옮겼다(PLAN-settings-ddol). 항목은 '설정 패널 항목 배치'.");
    }

    static bool BuildTitle()
    {
        Scene scene = SceneManager.GetSceneByPath(SettingsPanelAuthoring.TitleScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(SettingsPanelAuthoring.TitleScenePath, OpenSceneMode.Additive);
        try
        {
            TitleOptionsPanel oldPanel = HoldSkillToggleOptionAuthoring.FindInScene<TitleOptionsPanel>(scene);
            if (oldPanel == null) { Debug.LogError("[SettingsBody] 타이틀에 TitleOptionsPanel 이 없다."); return false; }

            if (oldPanel.gameObject.name == BodyName && PrefabUtility.IsPartOfPrefabInstance(oldPanel.gameObject))
            {
                Debug.Log("[SettingsBody] 타이틀은 이미 프리팹 인스턴스 — 프리팹만 갱신한다.");
                PrefabUtility.ApplyPrefabInstance(oldPanel.gameObject, InteractionMode.AutomatedAction);
                EditorSceneManager.SaveScene(scene);
                return true;
            }

            Transform background = SettingsPanelAuthoring.FindDeep(oldPanel.transform, "Option_Panel_Background");
            if (background == null) { Debug.LogError("[SettingsBody] Option_Panel_Background 를 못 찾았다."); return false; }

            // 본체 — 배경을 꽉 채운다(자식들은 가운데 기준 배치라 그대로 유지된다).
            var bodyGo = new GameObject(BodyName, typeof(RectTransform));
            var body = (RectTransform)bodyGo.transform;
            body.SetParent(background, false);
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.offsetMin = body.offsetMax = Vector2.zero;

            foreach (string n in ContentNames) Move(background, body, n);
            foreach (string n in TabNames) Move(background, body, n);

            // 탭 전환 컴포넌트를 본체로 옮긴다 — 참조 복사 + 탭 버튼 OnClick 재연결.
            var newPanel = bodyGo.AddComponent<TitleOptionsPanel>();
            EditorUtility.CopySerialized(oldPanel, newPanel);
            for (int i = 0; i < TabNames.Length; i++)
            {
                Button tab = body.Find(TabNames[i])?.GetComponent<Button>();
                if (tab == null) { Debug.LogWarning($"[SettingsBody] 탭 버튼 {TabNames[i]} 이 없다."); continue; }
                for (int k = tab.onClick.GetPersistentEventCount() - 1; k >= 0; k--)
                    if (tab.onClick.GetPersistentTarget(k) == oldPanel) UnityEventTools.RemovePersistentListener(tab.onClick, k);
                var call = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), newPanel, ShowMethods[i]);
                UnityEventTools.AddVoidPersistentListener(tab.onClick, call);
            }
            Object.DestroyImmediate(oldPanel);

            System.IO.Directory.CreateDirectory("Assets/2.Prefabs/UI");
            PrefabUtility.SaveAsPrefabAssetAndConnect(bodyGo, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SettingsBody] 타이틀 본체 → {PrefabPath} 저장·연결");
            return true;
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void ApplyToMap()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError($"[SettingsBody] {PrefabPath} 가 없다."); return; }

        Scene scene = SceneManager.GetSceneByPath(MapScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
        try
        {
            Transform optionPanel = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                optionPanel = SettingsPanelAuthoring.FindDeep(root.transform, "OptionPanel");
                if (optionPanel != null) break;
            }
            if (optionPanel == null) { Debug.LogError("[SettingsBody] 맵 씬에 OptionPanel 이 없다."); return; }

            if (optionPanel.Find(BodyName) != null)
            {
                Debug.Log("[SettingsBody] 맵 ESC 에 이미 본체가 있다 — 유지.");
                return;
            }

            Transform oldAudio = optionPanel.Find("Audio_Content");
            if (oldAudio != null) Object.DestroyImmediate(oldAudio.gameObject);   // 볼륨 3개 → 본체의 Audio 탭으로 대체

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var r = (RectTransform)instance.transform;
            r.SetParent(optionPanel, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            r.SetAsFirstSibling();   // 닫기·나가기 버튼이 위에 그려지고 먼저 클릭되게

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SettingsBody] 맵 ESC 메뉴에 본체 적용 · 옛 Audio_Content 제거 · 씬 저장");
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ─── 상호작용 안내 키 표기 (S14) ─────────────────────────────────────
    const string InteractPromptPath = "Assets/2.Prefabs/UI/InteractPrompt.prefab";

    /// <summary>
    /// <c>InteractPrompt.prefab</c> 의 키캡(<c>InteractKeyIcon</c>, F 가 그려진 그림) 위에 키 이름 텍스트 <c>KeyLabel</c> 을 두고
    /// <see cref="InteractPromptView"/> 에 연결한다. 키가 F 면 그림, 다른 키면 그림을 끄고 키 이름(팀장 10-12 — Q 로 바꿨는데 F 로 보임).
    /// </summary>
    [MenuItem("Tools/UI/Settings/상호작용 안내 키 표기 배치 (멱등)")]
    static void AuthorInteractPromptLabel()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(InteractPromptPath);
        try
        {
            var view = root.GetComponent<InteractPromptView>();
            Transform icon = SettingsPanelAuthoring.FindDeep(root.transform, "InteractKeyIcon");
            TMPro.TMP_Text fontRef = SettingsPanelAuthoring.FindDeep(root.transform, "InteractText")?.GetComponent<TMPro.TMP_Text>();
            if (view == null || icon == null) { Debug.LogError("[SettingsBody] InteractPrompt 에 InteractPromptView / InteractKeyIcon 이 없다."); return; }

            Transform existing = icon.Find("KeyLabel");
            TMPro.TextMeshProUGUI label = existing != null ? existing.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (label == null)
            {
                var go = new GameObject("KeyLabel", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                var r = (RectTransform)go.transform;
                r.SetParent(icon, false);
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = r.offsetMax = Vector2.zero;
                label = go.GetComponent<TMPro.TextMeshProUGUI>();
            }
            if (fontRef != null) { label.font = fontRef.font; label.color = fontRef.color; }
            label.fontSize = ((RectTransform)icon).rect.height * 0.6f;
            label.fontStyle = TMPro.FontStyles.Bold;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.text = "F";
            label.gameObject.SetActive(false);   // F 일 때는 그림 — 런타임에 키가 바뀌면 켠다

            var so = new SerializedObject(view);
            so.FindProperty("keyIconImage").objectReferenceValue = icon.GetComponent<Image>();
            so.FindProperty("keyLabel").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, InteractPromptPath);
            Debug.Log("[SettingsBody] InteractPrompt 키 표기(KeyLabel) 배치·연결 · 저장");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Move(Transform from, Transform to, string name)
    {
        Transform t = from.Find(name);
        if (t == null) { Debug.LogWarning($"[SettingsBody] {from.name}/{name} 이 없다."); return; }
        t.SetParent(to, false);   // 같은 크기의 부모(배경 꽉 채움)라 위치 그대로
    }
}
