using UnityEditor;
using UnityEngine;

/// <summary>경로형 바닥 화살표 인스펙터 — '다시 굽기' 버튼과 구운 상태 표시(할 일 111 후속).</summary>
[CustomEditor(typeof(LobbyFloorArrowPath))]
[CanEditMultipleObjects]
public sealed class LobbyFloorArrowPathEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var path = (LobbyFloorArrowPath)target;
        if (targets.Length > 1 || path.IsBakeCurrent())
            EditorGUILayout.HelpBox($"구운 조각 {path.BakedCount}개. 값·점을 바꾸면 자동으로 다시 굽는다.", MessageType.None);
        else
            EditorGUILayout.HelpBox("구운 결과가 지금 값과 다르다 — 다시 굽기를 누르거나 잠시 기다린다.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("다시 굽기"))
            {
                foreach (var t in targets)
                    ((LobbyFloorArrowPath)t).BakeNow();
            }
        }
    }
}
