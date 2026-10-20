using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설정창 새 아트(PLAN-settings-art) — 설정창을 담는 두 씬의 옛 틀을 끈다. <b>멱등</b>. 삭제하지 않고 끄기만 한다(되돌리기 쉽게).
/// - 맵 ESC(<c>4.MapScene</c> <c>OptionPanel</c>): 배경 = 반투명 검정(클릭 차단 유지) · 옛 제목·"Option" 글자·X 버튼 끔 ·
///   나가기 버튼 끔(ESC 일시정지 메뉴의 그만두기가 대신 — PLAN-pause-menu).
/// - 타이틀(<c>1.TitleScene</c>): 옛 Close_Button·Options_Title 끔 · 배경 투명 · 설정창 배율 1.4 → 1.0(새 틀 1475 폭이 모니터 RT 1536 에 맞게).
/// </summary>
public static class SettingsHostSceneAuthoring
{
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";

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
        if (bg != null) bg.color = PauseMenuAuthoring.DimColor;   // 반투명 검정으로 게임을 덮는다(펴기·정렬은 PauseMenuAuthoring)
        Hide(panel.transform, "Options_Title");
        Hide(panel.transform, "OptionPanel_Text");
        Hide(panel.transform, "OptionPanel_Close");

        // 나가기 버튼은 끈다 — ESC 일시정지 메뉴의 '그만두기'가 대신한다(PLAN-pause-menu, 경석 10-20).
        //   (10-20 오전엔 제목줄 빨간 알약으로 옮겼으나 일시정지 메뉴 도입으로 뒤집음.)
        Hide(panel.transform, "ExitButton");
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
