using UnityEditor;
using UnityEngine;

/// <summary>디버그 배속 인스펙터 — 슬라이더 + 프리셋 버튼 + 지금 배율 표시.</summary>
[CustomEditor(typeof(DebugTimeScale))]
internal sealed class DebugTimeScaleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty scale = serializedObject.FindProperty("scale");
        EditorGUILayout.PropertyField(scale);

        EditorGUILayout.BeginHorizontal();
        foreach (float preset in DebugTimeScale.Presets)
        {
            if (GUILayout.Button($"×{preset:0.##}"))
                scale.floatValue = preset;
        }
        EditorGUILayout.EndHorizontal();
        serializedObject.ApplyModifiedProperties();

        if (!Application.isPlaying)
            return;

        GlobalTimeScale layer = GlobalTimeScale.Instance;
        string slow = layer != null && layer.IsActive ? $"슬로우 {layer.CurrentScale:0.###} ({layer.CurrentPhase})" : "슬로우 없음";
        EditorGUILayout.HelpBox(
            layer != null
                ? $"Time.timeScale = {Time.timeScale:0.###}  |  {slow}  ×  기본 배속 {layer.BaseScale:0.###}"
                : "GlobalTimeScale 없음 — Dev Boot 로 들어왔는지 확인(NetworkManager.prefab 소속).",
            MessageType.None);
        Repaint();
    }
}
