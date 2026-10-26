using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설정창·ESC 메뉴 DDOL 프리팹(PLAN-settings-ddol) — <b>멱등</b>.
/// 1) <c>Assets/2.Prefabs/UI/GameMenu.prefab</c> 을 처음부터 만든다: 루트(Canvas·Scaler·레이캐스터 2종·<see cref="GameMenuController"/>) →
///    Content(반투명 검정·<see cref="UiModalBlocker"/>) → SettingsBody.prefab · PauseMenu.prefab 중첩 인스턴스.
///    겉모습은 중첩 프리팹 원본을 그대로 쓴다 — 설정 항목은 <c>SettingsPanelAuthoring</c>, 메뉴 아트는 <c>PauseMenuAuthoring</c>.
/// 2) <c>0.BootStrapScene</c>·<c>Dev_Boot</c> 루트에 인스턴스(없을 때만).
/// 3) 옛 씬 설정창을 지운다: <c>4.MapScene</c> <c>OptionPanel</c>(PauseMenu·SettingsBody·클라 경고창·ExitButton 포함) ·
///    <c>1.TitleScene</c> <c>Settings_Root</c>(투명 틀 + SettingsBody).
/// 🔴 EventSystem 은 넣지 않는다 — PersistentEventSystem 이 다른 EventSystem 의 GameObject 를 통째로 지운다.
/// </summary>
public static class GameMenuAuthoring
{
    const string PrefabPath = "Assets/2.Prefabs/UI/GameMenu.prefab";
    const string SettingsBodyPath = "Assets/2.Prefabs/UI/SettingsBody.prefab";
    const string PauseMenuPath = "Assets/2.Prefabs/UI/PauseMenu.prefab";
    const string BootScenePath = "Assets/0.Scenes/MainFlow/0.BootStrapScene.unity";
    const string DevBootScenePath = "Assets/0.Scenes/Debug/Dev_Boot.unity";
    const string TitleScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";

    [MenuItem("Tools/UI/GameMenu/설정·ESC 메뉴 DDOL 프리팹 + 씬 이관 (멱등)")]
    static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[GameMenu] Play 중에는 실행하지 않는다."); return; }
        if (!BuildPrefab()) return;

        WithScene(BootScenePath, PlaceInstance);
        WithScene(DevBootScenePath, PlaceInstance);
        WithScene(MapScenePath, s => DeleteRootChild(s, "OptionPanel"));
        WithScene(TitleScenePath, s => DeleteRootChild(s, "Settings_Root"));
        Debug.Log("[GameMenu] 완료 — 프리팹·부트스트랩·Dev_Boot 배치, 맵 OptionPanel·타이틀 Settings_Root 삭제");
    }

    // ─── 프리팹 ─────────────────────────────────────────────────────

    static bool BuildPrefab()
    {
        var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsBodyPath);
        var pausePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuPath);
        if (bodyPrefab == null || pausePrefab == null)
        {
            Debug.LogError($"[GameMenu] 중첩할 프리팹이 없다 — {SettingsBodyPath}={bodyPrefab != null} {PauseMenuPath}={pausePrefab != null}");
            return false;
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        var root = new GameObject("GameMenu", typeof(RectTransform));
        try
        {
            root.layer = uiLayer;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;   // 타이틀 모니터 캔버스와 같다(세로 맞춤)
            var screenRaycaster = root.AddComponent<GraphicRaycaster>();
            var titleRaycaster = root.AddComponent<TitleScreenRaycaster>();
            titleRaycaster.enabled = false;
            var controller = root.AddComponent<GameMenuController>();

            var content = new GameObject("Content", typeof(RectTransform), typeof(Image), typeof(UiModalBlocker));
            content.layer = uiLayer;
            var contentRt = (RectTransform)content.transform;
            contentRt.SetParent(root.transform, false);
            Stretch(contentRt);
            var dim = content.GetComponent<Image>();
            dim.color = PauseMenuAuthoring.DimColor;
            dim.raycastTarget = true;   // 뒤의 HUD·월드 클릭을 막는다

            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab);
            body.transform.SetParent(content.transform, false);
            Stretch((RectTransform)body.transform);
            body.SetActive(false);

            var pause = (GameObject)PrefabUtility.InstantiatePrefab(pausePrefab);
            pause.transform.SetParent(content.transform, false);
            Stretch((RectTransform)pause.transform);

            content.SetActive(false);

            var so = new SerializedObject(controller);
            so.FindProperty("canvas").objectReferenceValue = canvas;
            so.FindProperty("scaler").objectReferenceValue = scaler;
            so.FindProperty("screenRaycaster").objectReferenceValue = screenRaycaster;
            so.FindProperty("titleRaycaster").objectReferenceValue = titleRaycaster;
            so.FindProperty("content").objectReferenceValue = content;
            so.FindProperty("dim").objectReferenceValue = dim;
            so.FindProperty("pauseMenu").objectReferenceValue = pause.GetComponent<PauseMenuView>();
            so.FindProperty("settingsBody").objectReferenceValue = body.GetComponentInChildren<SettingsBodyView>(true);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (pause.GetComponent<PauseMenuView>() == null || body.GetComponentInChildren<SettingsBodyView>(true) == null)
            {
                Debug.LogError("[GameMenu] PauseMenuView·SettingsBodyView 를 못 찾았다 — 프리팹 원본 확인.");
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[GameMenu] {PrefabPath} 저장");
            return true;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    // ─── 씬 ─────────────────────────────────────────────────────────

    static bool PlaceInstance(Scene scene)
    {
        foreach (GameObject r in scene.GetRootGameObjects())
        {
            if (r.GetComponentInChildren<GameMenuController>(true) != null)
            {
                Debug.Log($"[GameMenu] {scene.name} 에 이미 있다");
                return false;
            }
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        inst.transform.SetAsLastSibling();
        Debug.Log($"[GameMenu] {scene.name} 에 배치");
        return true;
    }

    static bool DeleteRootChild(Scene scene, string name)
    {
        foreach (GameObject r in scene.GetRootGameObjects())
        {
            Transform t = r.name == name ? r.transform : SettingsPanelAuthoring.FindDeep(r.transform, name);
            if (t == null) continue;
            Object.DestroyImmediate(t.gameObject);
            Debug.Log($"[GameMenu] {scene.name} 의 {name} 삭제");
            return true;
        }

        Debug.Log($"[GameMenu] {scene.name} 에 {name} 이 없다(이미 지움)");
        return false;
    }

    static void WithScene(string path, System.Func<Scene, bool> apply)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
        {
            Debug.LogWarning($"[GameMenu] 씬이 없다 — {path}");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            if (apply(scene))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
