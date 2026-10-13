using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 옵션 Gameplay 탭에 언어 선택(한국어 / English)을 배치한다(멱등 — 이미 있으면 건드리지 않는다).
/// 자리·글꼴은 <see cref="HoldSkillToggleOptionAuthoring"/> 처럼 Audio 탭 첫 줄(MasterVolume · MasterVolumeLabel)을 따른다.
/// 행 = "Language" 라벨(키 <c>UI.Options.Language</c> — LocalizedText) + 라디오 토글 두 개(같은 ToggleGroup).
/// 언어 이름은 번역하지 않는다(각 언어 사용자가 자기 언어로 찾는다). 이미 배치된 행의 라벨 키는 L3 메뉴(<see cref="UiStringsAuthoring"/>)가 붙인다.
/// </summary>
public static class LanguageOptionAuthoring
{
    private const string ScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    private const string RowName = "LanguageOption";
    private const string LabelName = "LanguageLabel";

    /// <summary>행 라벨 경로 — L3 저작 메뉴(<see cref="UiStringsAuthoring"/>)가 이미 배치된 행에 키를 붙일 때 쓴다.</summary>
    public const string LabelPath = RowName + "/" + LabelName;
    private const float BoxSize = 24f;
    private const float LabelWidth = 160f;
    private const float ChoiceWidth = 160f;

    [MenuItem("Tools/UI/Title/옵션 Gameplay — 언어 선택 배치")]
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
                Debug.LogError($"[LanguageOptionAuthoring] {ScenePath} 에 TitleOptionsPanel 이 없다.");
                return;
            }

            var so = new SerializedObject(panel);
            var gameplay = so.FindProperty("gameplayPanel").objectReferenceValue as GameObject;
            var controls = so.FindProperty("controlsPanel").objectReferenceValue as GameObject;
            var audio = so.FindProperty("audioPanel").objectReferenceValue as GameObject;
            GameObject parent = gameplay != null ? gameplay : controls; // Gameplay 탭이 없으면 Controls 탭
            if (parent == null || audio == null)
            {
                Debug.LogError("[LanguageOptionAuthoring] TitleOptionsPanel 의 gameplayPanel(controlsPanel)/audioPanel 이 비어 있다.");
                return;
            }

            if (parent.transform.Find(RowName) != null)
            {
                Debug.Log($"[LanguageOptionAuthoring] 이미 있다 — 유지: {parent.name}/{RowName}");
                return;
            }

            Transform masterRow = audio.transform.Find("MasterVolume");
            Transform masterLabel = masterRow != null ? masterRow.Find("MasterVolumeLabel") : null;
            if (masterRow == null || masterLabel == null)
            {
                Debug.LogError("[LanguageOptionAuthoring] Audio 탭의 MasterVolume/MasterVolumeLabel 을 못 찾았다 — 자리·글꼴 기준이 없다.");
                return;
            }

            Build(parent.transform, (RectTransform)masterRow, masterLabel.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[LanguageOptionAuthoring] 배치 완료: {parent.name}/{RowName} · 씬 저장");
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Build(Transform parent, RectTransform rowTemplate, GameObject labelTemplate)
    {
        var root = new GameObject(RowName, typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = rowTemplate.anchorMin;
        rootRect.anchorMax = rowTemplate.anchorMax;
        rootRect.pivot = rowTemplate.pivot;
        rootRect.anchoredPosition = rowTemplate.anchoredPosition;
        rootRect.sizeDelta = new Vector2(rowTemplate.sizeDelta.x, BoxSize);

        GameObject label = Object.Instantiate(labelTemplate, rootRect, false);
        label.name = LabelName;
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(LabelWidth, BoxSize);
        label.GetComponent<TMP_Text>().text = UiStrings.OptionsLanguage.Ko;
        UiStringsAuthoring.Apply(label, UiStrings.OptionsLanguage.Key);

        var group = root.AddComponent<ToggleGroup>();
        group.allowSwitchOff = false;

        Toggle korean = BuildChoice(rootRect, labelTemplate, group, "KoreanToggle", "한국어", LabelWidth, true);
        Toggle english = BuildChoice(rootRect, labelTemplate, group, "EnglishToggle", "English", LabelWidth + ChoiceWidth, false);

        var option = root.AddComponent<LanguageOption>();
        var optionSo = new SerializedObject(option);
        optionSo.FindProperty("koreanToggle").objectReferenceValue = korean;
        optionSo.FindProperty("englishToggle").objectReferenceValue = english;
        optionSo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Toggle BuildChoice(RectTransform parent, GameObject labelTemplate, ToggleGroup group, string name, string text, float x, bool isOn)
    {
        var root = new GameObject(name, typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 0.5f);
        rootRect.pivot = new Vector2(0f, 0.5f);
        rootRect.anchoredPosition = new Vector2(x, 0f);
        rootRect.sizeDelta = new Vector2(ChoiceWidth, BoxSize);

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

        GameObject label = Object.Instantiate(labelTemplate, rootRect, false);
        label.name = name + "Label";
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(BoxSize + 12f, 0f);
        labelRect.sizeDelta = new Vector2(ChoiceWidth - BoxSize - 12f, BoxSize);
        label.GetComponent<TMP_Text>().text = text;

        var toggle = root.AddComponent<Toggle>();
        toggle.targetGraphic = backgroundImage;
        toggle.graphic = checkImage;
        toggle.group = group;
        toggle.isOn = isOn;
        return toggle;
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
