using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 타이틀 옵션 Controls 탭에 "Dash: Mouse Direction" 토글을 배치한다(멱등 — 이미 있으면 건드리지 않는다).
/// Hold 토글이 Audio 첫 줄(MasterVolume) 자리이므로 이 토글은 둘째 줄(BGMVolume) 자리에 둔다. 글꼴은 MasterVolumeLabel.
/// </summary>
public static class DashDirectionOptionAuthoring
{
    private const string ScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    private const string ToggleName = "DashDirectionToggle";
    private const string LabelText = "Dash: Mouse Direction";

    [MenuItem("Tools/UI/Title/옵션 Controls — 대시 방향 토글 배치")]
    private static void Author()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            TitleOptionsPanel panel = HoldSkillToggleOptionAuthoring.FindInScene<TitleOptionsPanel>(scene);
            if (panel == null)
            {
                Debug.LogError($"[DashDirectionAuthoring] {ScenePath} 에 TitleOptionsPanel 이 없다.");
                return;
            }

            var so = new SerializedObject(panel);
            var controls = so.FindProperty("controlsPanel").objectReferenceValue as GameObject;
            var audio = so.FindProperty("audioPanel").objectReferenceValue as GameObject;
            if (controls == null || audio == null)
            {
                Debug.LogError("[DashDirectionAuthoring] TitleOptionsPanel 의 controlsPanel/audioPanel 이 비어 있다.");
                return;
            }

            if (controls.transform.Find(ToggleName) != null)
            {
                Debug.Log($"[DashDirectionAuthoring] 이미 있다 — 유지: {controls.name}/{ToggleName}");
                return;
            }

            Transform secondRow = audio.transform.Find("BGMVolume");
            Transform masterRow = audio.transform.Find("MasterVolume");
            Transform masterLabel = masterRow != null ? masterRow.Find("MasterVolumeLabel") : null;
            if (secondRow == null || masterLabel == null)
            {
                Debug.LogError("[DashDirectionAuthoring] Audio 탭의 BGMVolume / MasterVolume/MasterVolumeLabel 을 못 찾았다 — 자리·글꼴 기준이 없다.");
                return;
            }

            HoldSkillToggleOptionAuthoring.BuildToggleRow<DashDirectionOption>(
                controls.transform, (RectTransform)secondRow, masterLabel.gameObject,
                ToggleName, "DashDirectionToggleLabel", LabelText);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DashDirectionAuthoring] 배치 완료: {controls.name}/{ToggleName} · 씬 저장");
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}
