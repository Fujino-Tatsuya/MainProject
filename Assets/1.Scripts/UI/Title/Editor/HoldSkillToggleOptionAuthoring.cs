using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 옵션 Controls 탭에 "Hold Skill: Toggle" 토글을 배치한다(멱등 — 이미 있으면 건드리지 않는다).
/// 자리·글꼴은 Audio 탭 첫 줄(MasterVolume · MasterVolumeLabel)을 그대로 따른다.
/// </summary>
public static class HoldSkillToggleOptionAuthoring
{
    private const string ScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    private const string ToggleName = "HoldSkillToggle";
    private const string LabelText = "Hold Skill: Toggle";
    private const float BoxSize = 24f;

    [MenuItem("Tools/UI/Title/옵션 Controls — Hold 토글 배치")]
    private static void Author()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            TitleOptionsPanel panel = FindInScene<TitleOptionsPanel>(scene);
            if (panel == null)
            {
                Debug.LogError($"[HoldToggleAuthoring] {ScenePath} 에 TitleOptionsPanel 이 없다.");
                return;
            }

            var so = new SerializedObject(panel);
            var controls = so.FindProperty("controlsPanel").objectReferenceValue as GameObject;
            var audio = so.FindProperty("audioPanel").objectReferenceValue as GameObject;
            if (controls == null || audio == null)
            {
                Debug.LogError("[HoldToggleAuthoring] TitleOptionsPanel 의 controlsPanel/audioPanel 이 비어 있다.");
                return;
            }

            if (controls.transform.Find(ToggleName) != null)
            {
                Debug.Log($"[HoldToggleAuthoring] 이미 있다 — 유지: {controls.name}/{ToggleName}");
                return;
            }

            Transform masterRow = audio.transform.Find("MasterVolume");
            Transform masterLabel = masterRow != null ? masterRow.Find("MasterVolumeLabel") : null;
            if (masterRow == null || masterLabel == null)
            {
                Debug.LogError("[HoldToggleAuthoring] Audio 탭의 MasterVolume/MasterVolumeLabel 을 못 찾았다 — 자리·글꼴 기준이 없다.");
                return;
            }

            Build(controls.transform, (RectTransform)masterRow, masterLabel.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[HoldToggleAuthoring] 배치 완료: {controls.name}/{ToggleName} · 씬 저장");
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Build(Transform parent, RectTransform rowTemplate, GameObject labelTemplate)
    {
        var root = new GameObject(ToggleName, typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = rowTemplate.anchorMin;
        rootRect.anchorMax = rowTemplate.anchorMax;
        rootRect.pivot = rowTemplate.pivot;
        rootRect.anchoredPosition = rowTemplate.anchoredPosition;
        rootRect.sizeDelta = new Vector2(rowTemplate.sizeDelta.x, BoxSize);

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        var backgroundRect = (RectTransform)background.transform;
        backgroundRect.SetParent(rootRect, false);
        backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(0f, 0.5f);
        backgroundRect.pivot = new Vector2(0f, 0.5f);
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundRect.sizeDelta = new Vector2(BoxSize, BoxSize);
        var backgroundImage = background.GetComponent<Image>();
        backgroundImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        backgroundImage.type = Image.Type.Sliced;

        var checkmark = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        var checkRect = (RectTransform)checkmark.transform;
        checkRect.SetParent(backgroundRect, false);
        checkRect.anchorMin = Vector2.zero;
        checkRect.anchorMax = Vector2.one;
        checkRect.offsetMin = checkRect.offsetMax = Vector2.zero;
        var checkImage = checkmark.GetComponent<Image>();
        checkImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        checkImage.color = new Color(0.12f, 0.12f, 0.12f, 1f);

        // 라벨은 Audio 라벨을 복제해 글꼴·크기·색을 그대로 가져온다.
        GameObject label = Object.Instantiate(labelTemplate, rootRect, false);
        label.name = "HoldSkillToggleLabel";
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(BoxSize + 12f, 0f);
        labelRect.sizeDelta = new Vector2(rowTemplate.sizeDelta.x - BoxSize - 12f, BoxSize);
        label.GetComponent<TMP_Text>().text = LabelText;

        var toggle = root.AddComponent<Toggle>();
        toggle.targetGraphic = backgroundImage;
        toggle.graphic = checkImage;
        toggle.isOn = false;
        root.AddComponent<HoldSkillToggleOption>();
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            T found = go.GetComponentInChildren<T>(true);
            if (found != null)
                return found;
        }

        return null;
    }
}
