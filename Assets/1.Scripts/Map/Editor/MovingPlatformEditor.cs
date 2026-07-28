using UnityEditor;
using UnityEngine;

/// <summary>
/// MovingPlatform 부모를 선택하면 자식 웨이포인트에 이동 핸들을 띄워
/// 씬 뷰에서 바로 경로를 편집할 수 있게 한다(자식을 개별 선택할 필요 없음).
/// 편집은 Undo를 지원하며, 각 웨이포인트에 인덱스 라벨을 표시한다.
/// </summary>
[CustomEditor(typeof(MovingPlatform))]
public class MovingPlatformEditor : Editor
{
    private bool _previewing;
    private double _previewStartTime;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("웨이포인트 추가"))
        {
            AddWaypoint(WaypointNode.NodeType.Destination);
        }
        if (GUILayout.Button("경유지 추가"))
        {
            AddWaypoint(WaypointNode.NodeType.Waypoint);
        }

        EditorGUILayout.Space();
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 실제 네트워크 로직으로 동작합니다.", MessageType.Info);
        }
        else if (!_previewing)
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
            EditorGUILayout.HelpBox("미리보기 중 — 웨이포인트/속도/노드 변경이 실시간 반영됩니다.", MessageType.None);
        }
    }

    private void StartPreview()
    {
        var platform = target as MovingPlatform;
        if (platform == null)
        {
            return;
        }
        platform.EditorPreviewBegin();
        _previewStartTime = EditorApplication.timeSinceStartup;
        _previewing = true;
        EditorApplication.update += OnEditorUpdate;
    }

    private void StopPreview()
    {
        _previewing = false;
        EditorApplication.update -= OnEditorUpdate;
        if (target is MovingPlatform platform)
        {
            platform.EditorPreviewEnd();
        }
        SceneView.RepaintAll();
    }

    private void OnEditorUpdate()
    {
        var platform = target as MovingPlatform;
        if (!_previewing || platform == null || Application.isPlaying)
        {
            StopPreview();
            return;
        }
        double elapsed = EditorApplication.timeSinceStartup - _previewStartTime;
        platform.EditorPreviewTick(elapsed);
        SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        if (_previewing)
        {
            StopPreview();
        }
    }

    private void AddWaypoint(WaypointNode.NodeType nodeType)
    {
        var platform = (MovingPlatform)target;
        SerializedProperty waypointsProp = serializedObject.FindProperty("waypoints");
        if (waypointsProp == null)
        {
            return;
        }

        // 위치: 마지막 WP 기준 +X 3. 없으면 플랫폼 원점.
        Vector3 spawnPos = platform.transform.position;
        int count = waypointsProp.arraySize;
        if (count > 0)
        {
            Transform last = waypointsProp.GetArrayElementAtIndex(count - 1).objectReferenceValue as Transform;
            if (last != null)
            {
                spawnPos = last.position + new Vector3(3f, 0f, 0f);
            }
        }

        var go = new GameObject($"WP_{count}");
        Undo.RegisterCreatedObjectUndo(go, "Add Waypoint");
        go.transform.SetParent(platform.transform, worldPositionStays: true);
        go.transform.position = spawnPos;
        WaypointNode node = Undo.AddComponent<WaypointNode>(go);
        node.type = nodeType;

        // waypoints 리스트에 자동 추가.
        serializedObject.Update();
        waypointsProp.arraySize++;
        waypointsProp.GetArrayElementAtIndex(waypointsProp.arraySize - 1).objectReferenceValue = go.transform;
        serializedObject.ApplyModifiedProperties();

        // 포커스는 MovingPlatform(부모)에 유지 — 새 WP로 넘기지 않는다.
        Selection.activeGameObject = platform.gameObject;
        EditorUtility.SetDirty(platform);
    }

    private void OnSceneGUI()
    {
        SerializedProperty waypointsProp = serializedObject.FindProperty("waypoints");
        if (waypointsProp == null || !waypointsProp.isArray)
        {
            return;
        }

        Handles.color = Color.yellow;

        for (int i = 0; i < waypointsProp.arraySize; i++)
        {
            Transform wp = waypointsProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (wp == null)
            {
                continue;
            }

            Handles.Label(wp.position + Vector3.up * 0.4f, $"WP_{i}");

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(wp.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(wp, "Move MovingPlatform Waypoint");
                wp.position = newPos;
            }
        }
    }
}
