using UnityEditor;
using UnityEngine;

/// <summary>Vent의 상태 주기와 damageCollider 토글을 편집 모드 씬 뷰에서 미리 본다.</summary>
[CustomEditor(typeof(Vent))]
public class VentEditor : Editor
{
    private bool _previewing;
    private double _previewStartTime;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 실제 로직으로 동작합니다.", MessageType.Info);
            return;
        }

        if (!_previewing)
        {
            if (GUILayout.Button("▶ 미리보기 재생 (씬 뷰)"))
            {
                StartPreview();
            }
        }
        else
        {
            if (GUILayout.Button("■ 미리보기 정지"))
            {
                StopPreview();
            }

            if (target is Vent vent)
            {
                EditorGUILayout.HelpBox(
                    $"미리보기 중 — 현재 상태: {vent.EditorDisplayState}\n" +
                    "시간 설정 변경이 실시간 반영됩니다. UnityEvent는 호출하지 않습니다.",
                    MessageType.None);
            }
        }
    }

    private void StartPreview()
    {
        if (!(target is Vent vent))
        {
            return;
        }

        vent.EditorPreviewBegin();
        _previewStartTime = EditorApplication.timeSinceStartup;
        _previewing = true;
        EditorApplication.update += OnEditorUpdate;
        SceneView.RepaintAll();
    }

    private void StopPreview()
    {
        _previewing = false;
        EditorApplication.update -= OnEditorUpdate;

        if (target is Vent vent)
        {
            vent.EditorPreviewEnd();
        }

        Repaint();
        SceneView.RepaintAll();
    }

    private void OnEditorUpdate()
    {
        if (!_previewing ||
            !(target is Vent vent) ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            StopPreview();
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - _previewStartTime;
        vent.EditorPreviewTick(elapsed);
        Repaint();
        SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        if (_previewing)
        {
            StopPreview();
        }
    }

    private void OnSceneGUI()
    {
        if (!(target is Vent vent))
        {
            return;
        }

        Color previousColor = Handles.color;
        Handles.color = StateColor(vent.EditorDisplayState);
        Handles.Label(
            vent.transform.position + Vector3.up * 0.85f,
            $"Vent [{vent.EditorDisplayState}]");
        Handles.color = previousColor;
    }

    private static Color StateColor(Vent.VentState state)
    {
        switch (state)
        {
            case Vent.VentState.Warning:
                return Color.yellow;
            case Vent.VentState.Active:
                return Color.red;
            default:
                return Color.green;
        }
    }
}
