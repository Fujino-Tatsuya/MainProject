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
        EditorGUILayout.PropertyField(serializedObject.FindProperty("normalSpeedAfterSuccess"), new GUIContent("성공 후 1배속(초, 실제 시간)"));
        serializedObject.ApplyModifiedProperties();

        if (!Application.isPlaying)
            return;

        var debug = (DebugTimeScale)target;
        GlobalTimeScale layer = GlobalTimeScale.Instance;
        string slow = layer != null && layer.IsActive ? $"슬로우 {layer.CurrentScale:0.###} ({layer.CurrentPhase})" : "슬로우 없음";
        string hold = debug.IsHoldingNormalSpeed ? $"  |  성공 후 1배속 {debug.NormalSpeedRemaining:0.0}s 남음" : string.Empty;
        EditorGUILayout.HelpBox(
            layer != null
                ? $"Time.timeScale = {Time.timeScale:0.###}  |  {slow}  ×  기본 배속 {layer.BaseScale:0.###}{hold}"
                : "GlobalTimeScale 없음 — Dev Boot 로 들어왔는지 확인(NetworkManager.prefab 소속).",
            MessageType.None);
        Repaint();
    }
}
