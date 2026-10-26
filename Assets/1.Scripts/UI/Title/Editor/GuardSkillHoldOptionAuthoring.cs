using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 설정창(SettingsBody.prefab) Controls 탭에 "Guard Skill: Hold" 토글 행을 배치한다(할 일 95, 멱등 — 이미 있으면 건드리지 않는다).
/// Hold·대시 토글 행(<c>Row_DashDirectionToggle</c>)을 복제해 바로 아래에 둔다 — 모양·저장 방식이 같다.
/// 라벨 글자는 ko(시트 행이 없을 때 폴백), 키는 <see cref="SettingsUiStrings.ControlsGuardSkillHold"/>.
/// </summary>
public static class GuardSkillHoldOptionAuthoring
{
    private const string PrefabPath = "Assets/2.Prefabs/UI/SettingsBody.prefab";
    private const string TemplateRowName = "Row_DashDirectionToggle";
    private const string TemplateToggleName = "DashDirectionToggle";
    private const string RowName = "Row_GuardSkillHoldToggle";
    private const string ToggleName = "GuardSkillHoldToggle";
    private const string LabelName = "GuardSkillHoldToggleLabel";

    [MenuItem("Tools/UI/Settings/조작 — 방어 스킬 홀드 토글 배치")]
    private static void Author()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform template = FindDeep(root.transform, TemplateRowName);
            if (template == null)
            {
                Debug.LogError($"[GuardHoldAuthoring] {PrefabPath} 에 {TemplateRowName} 이 없다.");
                return;
            }

            if (template.parent.Find(RowName) != null)
            {
                Debug.Log($"[GuardHoldAuthoring] 이미 있다 — 유지: {RowName}");
                return;
            }

            GameObject row = Object.Instantiate(template.gameObject, template.parent);
            row.name = RowName;
            row.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);

            Transform toggle = row.transform.Find(TemplateToggleName);
            if (toggle == null)
            {
                Debug.LogError($"[GuardHoldAuthoring] 복제한 행에 {TemplateToggleName} 이 없다 — 저장하지 않는다.");
                return;
            }

            toggle.name = ToggleName;
            Object.DestroyImmediate(toggle.GetComponent<DashDirectionOption>());
            toggle.gameObject.AddComponent<GuardSkillHoldOption>();

            TMP_Text label = toggle.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                Debug.LogError("[GuardHoldAuthoring] 토글 라벨(TMP)이 없다 — 저장하지 않는다.");
                return;
            }

            label.gameObject.name = LabelName;
            label.text = SettingsUiStrings.ControlsGuardSkillHold.Ko;

            LocalizedText localized = label.GetComponent<LocalizedText>();
            if (localized == null)
                localized = label.gameObject.AddComponent<LocalizedText>();
            var so = new SerializedObject(localized);
            so.FindProperty("key").stringValue = SettingsUiStrings.ControlsGuardSkillHold.Key;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[GuardHoldAuthoring] 배치 완료: {AnimationUtility.CalculateTransformPath(row.transform, root.transform)}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }

        return null;
    }
}
