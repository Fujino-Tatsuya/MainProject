using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설정창 새 아트(PLAN-settings-art) — 설정창을 담는 두 씬의 옛 틀을 끈다. <b>멱등</b>. 삭제하지 않고 끄기만 한다(되돌리기 쉽게).
/// - 맵 ESC(<c>4.MapScene</c> <c>OptionPanel</c>): 배경은 투명(클릭 차단은 유지) · 옛 제목·"Option" 글자·X 버튼 끔 ·
///   나가기 버튼을 새 틀 제목줄 오른쪽(닫기 버튼 왼쪽)의 빨간 알약으로(경석 10-20). 버튼 기능·연결은 그대로.
/// - 타이틀(<c>1.TitleScene</c>): 옛 Close_Button·Options_Title 끔 · 배경 투명 · 설정창 배율 1.4 → 1.0(새 틀 1475 폭이 모니터 RT 1536 에 맞게).
/// </summary>
public static class SettingsHostSceneAuthoring
{
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";

    // 새 틀 제목줄 안 나가기 버튼 — 패널 중심 기준. 제목줄 띠(목업 y 115~225) 세로 중앙, 닫기 버튼(목업 x 1496) 왼쪽 20.
    static readonly Vector2 ExitPos = new Vector2(394.6f, 358f);
    static readonly Vector2 ExitSize = new Vector2(220f, 76f);
    const string PillRedGuid = "ba9c4ad2c9534c66b0b003202a73d7dd";

    [MenuItem("Tools/UI/Settings/설정창 옛 틀 끄기 — 타이틀·ESC (멱등)")]
    static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[SettingsHost] Play 중에는 실행하지 않는다 — 정지 후 다시."); return; }
        bool map = WithScene(MapScenePath, ApplyMap);
        bool title = WithScene(SettingsPanelAuthoring.TitleScenePath, ApplyTitle);
        Debug.Log($"[SettingsHost] 맵 {(map ? "저장" : "실패")} · 타이틀 {(title ? "저장" : "실패")}");
    }

    static bool ApplyMap(Scene scene)
    {
        GameObject panel = Find(scene, "OptionPanel");
        if (panel == null) { Debug.LogError("[SettingsHost] 맵 OptionPanel 이 없다."); return false; }

        Image bg = panel.GetComponent<Image>();
        if (bg != null) bg.color = Color.clear;   // 새 틀이 그림 — 바깥 클릭 차단(레이캐스트)은 남긴다
        Hide(panel.transform, "Options_Title");
        Hide(panel.transform, "OptionPanel_Text");
        Hide(panel.transform, "OptionPanel_Close");

        Transform exit = panel.transform.Find("ExitButton");
        if (exit == null) { Debug.LogError("[SettingsHost] 맵 ExitButton 이 없다."); return false; }
        var r = (RectTransform)exit;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = ExitPos;
        r.sizeDelta = ExitSize;
        exit.SetAsLastSibling();   // 설정창(SettingsBody) 위에 그려지게 — 경고창은 그 다음
        Transform warning = panel.transform.Find("WarnningMessage_Panel");
        if (warning != null) warning.SetAsLastSibling();

        var pill = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(PillRedGuid));
        Image img = exit.GetComponent<Image>();
        if (pill != null && img != null) SettingsArtSkin.SetSliced(img, pill, ExitSize);
        Button b = exit.GetComponent<Button>();
        if (b != null)
        {
            ColorBlock c = b.colors;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            c.selectedColor = Color.white;
            c.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            c.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            b.colors = c;
        }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SettingsArtSkin.FontAssetPath);
        foreach (TMP_Text t in exit.GetComponentsInChildren<TMP_Text>(true))
        {
            if (font != null) { t.font = font; t.fontSharedMaterial = font.material; }
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            SettingsArtSkin.AutoSize(t, 14f, 30f);
            SettingsArtSkin.Stretch(t.rectTransform, new Vector2(18f, 0f), new Vector2(-18f, 0f));
        }
        return true;
    }

    static bool ApplyTitle(Scene scene)
    {
        GameObject bgGo = Find(scene, "Option_Panel_Background");
        if (bgGo == null) { Debug.LogError("[SettingsHost] 타이틀 Option_Panel_Background 가 없다."); return false; }
        Image bg = bgGo.GetComponent<Image>();
        if (bg != null) bg.color = Color.clear;
        Hide(bgGo.transform, "Close_Button");
        Hide(bgGo.transform, "Options_Title");

        TitleFlowDirector director = Object.FindFirstObjectByType<TitleFlowDirector>(FindObjectsInactive.Include);
        if (director != null)
        {
            var so = new SerializedObject(director);
            so.FindProperty("_titleSettingsScale").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else Debug.LogWarning("[SettingsHost] TitleFlowDirector 가 없다 — 배율은 그대로.");
        return true;
    }

    static void Hide(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) t.gameObject.SetActive(false);
        else Debug.LogWarning($"[SettingsHost] {parent.name}/{name} 이 없다 — 건너뜀.");
    }

    static GameObject Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform hit = SettingsPanelAuthoring.FindDeep(root.transform, name);
            if (hit != null) return hit.gameObject;
        }
        return null;
    }

    static bool WithScene(string path, System.Func<Scene, bool> apply)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            if (!apply(scene)) return false;
            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
