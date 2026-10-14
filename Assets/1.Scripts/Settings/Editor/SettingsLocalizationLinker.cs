using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 설정 패널의 고정 라벨에 <see cref="LocalizedText"/>(로컬라이징 키 + ko 폴백)를 붙인다(PLAN-settings S15 → 할 일 38). <b>멱등</b>.
/// - 아직 안 붙은 라벨: 지금 글자(영어 저작 원문 또는 ko)로 <see cref="SettingsUiStrings.FindKeyByLabel"/> 를 찾아 키를 넣는다 —
///   표에 없는 글자(값 표시 "100%", 키 "-", 드롭다운 자리 "Option A", 언어 이름 등)는 건드리지 않는다.
/// - 키가 붙은 설정 라벨(<c>UI.Settings.*</c>)은 저작 글자를 그 키의 ko 로 맞춘다 — 시트에 행이 없을 때의 폴백이 ko 가 되게.
/// 이미 맞으면 아무것도 바꾸지 않는다. 항목 배치 메뉴(<see cref="SettingsPanelAuthoring"/>)가 끝에 자동으로 부른다.
/// </summary>
public static class SettingsLocalizationLinker
{
    [MenuItem("Tools/UI/Settings/설정 패널 문구 키 연결 (멱등)")]
    static void LinkMenu()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SettingsPanelAuthoring.BodyPrefabPath);
        try
        {
            int changed = Link(root.transform);
            if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, SettingsPanelAuthoring.BodyPrefabPath);
            Debug.Log($"[SettingsLocalization] 라벨 {changed}개 바뀜{(changed > 0 ? " · " + SettingsPanelAuthoring.BodyPrefabPath + " 저장" : "(전부 이미 맞음, 저장 안 함)")}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <returns>키를 새로 붙이거나 저작 글자를 ko 로 맞춘 라벨 수.</returns>
    internal static int Link(Transform root)
    {
        int changed = 0;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            LocalizedText localized = text.GetComponent<LocalizedText>();
            string key = localized != null
                ? localized.Key
                : SettingsUiStrings.FindKeyByLabel(text.text != null ? text.text.Trim() : null);
            if (!TryGetSetting(key, out UiString setting)) continue;

            bool touched = false;
            if (localized == null)
            {
                UiStringsAuthoring.Apply(text.gameObject, setting.Key);
                touched = true;
            }

            if (text.text != setting.Ko)
            {
                var so = new SerializedObject(text);
                so.FindProperty("m_text").stringValue = setting.Ko;
                so.ApplyModifiedPropertiesWithoutUndo();
                touched = true;
            }

            if (touched) changed++;
        }
        return changed;
    }

    static bool TryGetSetting(string key, out UiString setting)
    {
        if (!string.IsNullOrEmpty(key))
        {
            foreach (UiString s in SettingsUiStrings.All)
            {
                if (s.Key != key) continue;
                setting = s;
                return true;
            }
        }

        setting = default;
        return false;
    }
}
