using UnityEditor;
using UnityEngine;

/// <summary>
/// 274 — Scene 뷰에서 체력바 미리보기 자리(빨간 막대)의 노란 점을 위아래로 끌면 footScreenOffset 이 바뀐다(Undo 지원).
/// Scene 뷰 카메라 기준 화면 px 를 1080p 캔버스 px 로 바꿔 넣는다 — 게임 카메라도 같은 캔버스 규칙이라 같은 오프셋이면 같은 거리다.
/// </summary>
[CustomEditor(typeof(MonsterOverheadHealthBarOffset))]
public sealed class MonsterOverheadHealthBarOffsetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Scene 뷰: 빨간 막대 = 체력바가 뜰 자리. 가운데 노란 점을 위아래로 끌면 값이 바뀐다. Play 중엔 이미 뜬 바도 바로 옮겨진다.", MessageType.None);
    }

    void OnSceneGUI()
    {
        var t = (MonsterOverheadHealthBarOffset)target;
        SceneView view = SceneView.currentDrawingSceneView;
        Camera cam = view != null ? view.camera : null;
        if (!t.TryGetPreviewPoint(cam, out Vector3 center, out float ppu)) return;

        float size = HandleUtility.GetHandleSize(center) * 0.08f;
        Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.FreeMoveHandle(center, size, Vector3.zero, Handles.DotHandleCap);
        if (!EditorGUI.EndChangeCheck()) return;

        Vector3 feet = cam.WorldToScreenPoint(t.AnchorWorld);
        Vector3 now = cam.WorldToScreenPoint(moved);
        float offset = MonsterOverheadHealthBarVisibility.FootOffsetFromScreenDrop(feet.y - now.y, ppu);
        Undo.RecordObject(t, "체력바 발밑 오프셋");
        t.FootScreenOffset = Mathf.Round(offset);
        EditorUtility.SetDirty(t);
        if (PrefabUtility.IsPartOfPrefabInstance(t))
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }
}
