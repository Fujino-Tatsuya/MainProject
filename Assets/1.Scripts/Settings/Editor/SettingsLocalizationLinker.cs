using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 설정 패널의 고정 라벨에 <see cref="LocalizedText"/> 를 붙인다(PLAN-settings S15). <b>멱등</b>.
/// 라벨의 지금 글자(영어 원문)로 <see cref="SettingsStrings.FindKeyByEnglish"/> 를 찾아 키를 넣는다 —
/// 표에 없는 글자(값 표시 "100%", 키 "-", 드롭다운 자리 "Option A", 언어 이름 등)는 건드리지 않는다.
/// 🔴 이미 붙은 라벨은 지금 언어로 바뀌어 있을 수 있으므로(한국어 글자는 역조회가 안 된다) 키를 그대로 둔다.
/// 항목 배치 메뉴(<see cref="SettingsPanelAuthoring"/>)가 끝에 자동으로 부른다.
/// </summary>
public static class SettingsLocalizationLinker
{
    [MenuItem("Tools/UI/Settings/설정 패널 문구 키 연결 (멱등)")]
    static void LinkMenu()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SettingsPanelAuthoring.BodyPrefabPath);
        try
        {
            int added = Link(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, SettingsPanelAuthoring.BodyPrefabPath);
            Debug.Log($"[SettingsLocalization] 키 연결 {added}개 추가 · {SettingsPanelAuthoring.BodyPrefabPath} 저장");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <returns>새로 붙인 개수.</returns>
    internal static int Link(Transform root)
    {
        int added = 0;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.GetComponent<LocalizedText>() != null) continue;
            string key = SettingsStrings.FindKeyByEnglish(text.text != null ? text.text.Trim() : null);
            if (key == null) continue;
            var lt = text.gameObject.AddComponent<LocalizedText>();
            var so = new SerializedObject(lt);
            so.FindProperty("key").stringValue = key;
            so.ApplyModifiedPropertiesWithoutUndo();
            added++;
        }
        return added;
    }
}
