#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

[CustomEditor(typeof(ElevationLevel))]
public sealed class ElevationLevelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "XZ Areas are data-only boxes. Y is ignored. Use the Scene handles to edit " +
            "center, Y rotation, and XZ size. Do not add BoxCollider trigger volumes.",
            MessageType.Info);
    }

    private void OnSceneGUI()
    {
        var level = (ElevationLevel)target;
        SerializedProperty areas = serializedObject.FindProperty("xzAreas");
        if (areas == null)
            return;

        serializedObject.Update();
        for (int i = 0; i < areas.arraySize; i++)
        {
            SerializedProperty area = areas.GetArrayElementAtIndex(i);
            SerializedProperty centerProperty = area.FindPropertyRelative("center");
            SerializedProperty sizeProperty = area.FindPropertyRelative("size");
            SerializedProperty rotationProperty = area.FindPropertyRelative("rotationDegrees");

            Vector2 center = centerProperty.vector2Value;
            Vector2 size = sizeProperty.vector2Value;
            float rotation = rotationProperty.floatValue;
            Vector3 worldCenter = level.transform.TransformPoint(new Vector3(center.x, 0f, center.y));
            Quaternion worldRotation = level.transform.rotation * Quaternion.Euler(0f, rotation, 0f);
            float handleSize = HandleUtility.GetHandleSize(worldCenter);

            Handles.color = new Color(0.1f, 0.85f, 1f, 1f);
            EditorGUI.BeginChangeCheck();
            Vector3 movedCenter = Handles.PositionHandle(worldCenter, worldRotation);
            Quaternion rotated = Handles.Disc(
                worldRotation,
                movedCenter,
                level.transform.up,
                handleSize * 0.8f,
                false,
                1f);
            Vector3 right = rotated * Vector3.right;
            Vector3 forward = rotated * Vector3.forward;
            float halfX = Handles.ScaleSlider(
                Mathf.Max(0.005f, size.x * 0.5f),
                movedCenter + right * size.x * 0.5f,
                right,
                rotated,
                handleSize * 0.6f,
                0.1f);
            float halfZ = Handles.ScaleSlider(
                Mathf.Max(0.005f, size.y * 0.5f),
                movedCenter + forward * size.y * 0.5f,
                forward,
                rotated,
                handleSize * 0.6f,
                0.1f);

            if (!EditorGUI.EndChangeCheck())
                continue;

            Undo.RecordObject(level, "Edit Elevation XZ Area");
            Vector3 localCenter = level.transform.InverseTransformPoint(movedCenter);
            Quaternion localRotation = Quaternion.Inverse(level.transform.rotation) * rotated;
            centerProperty.vector2Value = new Vector2(localCenter.x, localCenter.z);
            sizeProperty.vector2Value = new Vector2(
                Mathf.Max(0.01f, Mathf.Abs(halfX) * 2f),
                Mathf.Max(0.01f, Mathf.Abs(halfZ) * 2f));
            rotationProperty.floatValue = NormalizeAngle(localRotation.eulerAngles.y);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static float NormalizeAngle(float angle)
    {
        return Mathf.Repeat(angle + 180f, 360f) - 180f;
    }
}
#endif
