#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 242 디버그 씬의 '원본에 쓰기' — 에디터에서만. Play 중에도 에셋(SO·프리팹)에 바로 써서 Play 가 끝나도 남는다.
/// </summary>
public static class DebugFloatingDamageLabEditing
{
    /// <summary>설정 SO 의 그 종류 숫자 높이(px)를 바꾸고 저장한다. 다음 숫자부터 반영.</summary>
    public static string NudgeStyleHeight(FloatingDamageSettings settings, PopupKind kind, float delta)
    {
        var so = new SerializedObject(settings);
        SerializedProperty styles = so.FindProperty("popupStyles");
        for (int i = 0; i < styles.arraySize; i++)
        {
            SerializedProperty style = styles.GetArrayElementAtIndex(i);
            if (style.FindPropertyRelative("kind").intValue != (int)kind)
                continue;

            SerializedProperty height = style.FindPropertyRelative("height");
            height.floatValue = Mathf.Max(1f, height.floatValue + delta);
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(settings);
            return $"{kind} 높이 = {height.floatValue:0.#}px (저장됨: {AssetDatabase.GetAssetPath(settings)})";
        }

        return $"{kind} 스타일이 설정에 없다.";
    }

    /// <summary>설정 SO 의 float 칸(직렬화 이름) 값을 읽는다. 없으면 0.</summary>
    public static float GetFloat(FloatingDamageSettings settings, string field)
    {
        SerializedProperty property = new SerializedObject(settings).FindProperty(field);
        return property != null ? property.floatValue : 0f;
    }

    /// <summary>설정 SO 의 float 칸(직렬화 이름)을 바꾸고 저장한다(268 슬라이더). 다음 숫자부터 반영.</summary>
    public static string SetFloat(FloatingDamageSettings settings, string field, float value)
    {
        var so = new SerializedObject(settings);
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
            return $"{field} 칸이 설정에 없다.";

        property.floatValue = value;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssetIfDirty(settings);
        return $"{field} = {value:0.##} (저장됨: {AssetDatabase.GetAssetPath(settings)})";
    }

    /// <summary>
    /// 씬 인스턴스 앵커의 localOffset·worldOffset 을 그 컴포넌트가 정의된 원본 프리팹(23호 = TwentyThree.prefab, 레이드 변형이 물려받음)에 쓴다.
    /// 본(anchorTransform) 지정은 바꾸지 않는다. 중간 변형 프리팹이 같은 칸을 덮어쓰고 있으면 경고한다.
    /// </summary>
    public static string SaveAnchorToPrefab(FloatingDamageAnchor live)
    {
        FloatingDamageAnchor source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(live);
        if (source == null || source == live)
            return "프리팹 인스턴스의 앵커가 아니다 — 저장 안 함.";

        var so = new SerializedObject(source);
        so.FindProperty("localOffset").vector3Value = live.LocalOffset;
        so.FindProperty("worldOffset").vector3Value = live.WorldOffset;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(source);
        AssetDatabase.SaveAssetIfDirty(source);

        string path = AssetDatabase.GetAssetPath(source);
        string message = $"앵커 저장: {path} (local {live.LocalOffset}, world {live.WorldOffset})";

        // 변형(예: TwentyThree_Raid)이 같은 칸을 덮어쓰면 원본에 써도 게임 값은 안 바뀐다.
        FloatingDamageAnchor nearest = PrefabUtility.GetCorrespondingObjectFromSource(live);
        if (nearest != null && nearest != source)
        {
            var nearSo = new SerializedObject(nearest);
            if (nearSo.FindProperty("localOffset").prefabOverride || nearSo.FindProperty("worldOffset").prefabOverride)
                message += $"\n⚠ {AssetDatabase.GetAssetPath(nearest)} 가 같은 칸을 덮어쓴다 — 그 변형 값이 우선.";
        }

        Debug.Log("[DebugFloatingDamage] " + message, source);
        return message;
    }
}
#endif
