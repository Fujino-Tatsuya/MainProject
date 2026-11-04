using UnityEditor;
using UnityEngine;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215) 연결 — <c>LobbyResultPanel.prefab</c> 의 <c>Content</c> 에
/// <see cref="LobbyResultPanelAnimator"/> 를 붙이고 참조(<c>scaleTarget</c> = Content/Panel, <c>statsView</c>)를 잇는다.
///
/// 🔴 멱등 — 이미 붙어 있고 참조가 맞으면 프리팹을 저장하지 않는다(바이트 그대로). 튜닝한 시간·곡선은 건드리지 않는다.
/// 프리팹 병합 충돌로 이 컴포넌트가 빠지면 다시 돌리면 된다. 패널 재생성 메뉴도 이 연결을 부른다.
/// </summary>
public static class LobbyResultPanelAnimAuthoring
{
    const string Tag = "[LobbyResultPanelAnim]";

    [MenuItem("Tools/UI/로비 결과창 애니 연결")]
    public static void Connect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        string path = LobbyResultPanelAuthoring.PanelPath;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogError($"{Tag} 패널 프리팹이 없다: {path}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform content = root.transform.Find("Content");
            if (content == null)
            {
                Debug.LogError($"{Tag} 'Content' 자식을 찾지 못했다: {path}");
                return;
            }

            if (!Wire(content.gameObject))
            {
                Debug.Log($"{Tag} 이미 연결돼 있다 — 저장하지 않음: {path}");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Debug.Log($"{Tag} 연결 저장 {(success ? "성공" : "실패")}: {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// <paramref name="content"/> 에 애니 컴포넌트를 붙이고 참조를 잇는다. 바뀐 게 있으면 true.
    /// </summary>
    public static bool Wire(GameObject content)
    {
        bool changed = false;

        LobbyResultPanelAnimator animator = content.GetComponent<LobbyResultPanelAnimator>();
        if (animator == null)
        {
            animator = content.AddComponent<LobbyResultPanelAnimator>();
            changed = true;
        }

        RectTransform panel = content.transform.Find("Panel") as RectTransform;
        if (panel == null)
            Debug.LogWarning($"{Tag} 'Content/Panel' 이 없다 — scaleTarget 을 비워 둔다(크기 연출 없음).", content);
        ResultStatsView stats = content.GetComponentInChildren<ResultStatsView>(true);
        if (stats == null)
            Debug.LogWarning($"{Tag} ResultStatsView 가 없다 — 카운트업 없음.", content);

        var so = new SerializedObject(animator);
        changed |= SetReference(so, "scaleTarget", panel);
        changed |= SetReference(so, "statsView", stats);
        so.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    static bool SetReference(SerializedObject so, string property, Object value)
    {
        SerializedProperty prop = so.FindProperty(property);
        if (prop.objectReferenceValue == value)
            return false;
        prop.objectReferenceValue = value;
        return true;
    }
}
