using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="EffectDurationProbe"/>에 실측 버튼을 붙인다. 플레이 모드에서만 눌린다.
/// </summary>
[CustomEditor(typeof(EffectDurationProbe))]
public class EffectDurationProbeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        var probe = (EffectDurationProbe)target;

        using (new EditorGUI.DisabledScope(!Application.isPlaying || probe.Entry == null))
        {
            if (GUILayout.Button("duration 실측 (Play Mode)", GUILayout.Height(28)))
            {
                probe.Measure();
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서만 실측할 수 있다. 결과는 콘솔에 찍힌다.", MessageType.Info);
        }
        else if (probe.Entry == null)
        {
            EditorGUILayout.HelpBox("측정할 EffectEntry를 연결할 것.", MessageType.Warning);
        }
    }
}
