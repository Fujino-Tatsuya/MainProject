// ----------------------------------------------------------------------------
//  FogVolumeEditor.cs - FogVolume 씬뷰 핸들 편집
// ----------------------------------------------------------------------------
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(FogVolume))]
[CanEditMultipleObjects]
public sealed class FogVolumeEditor : Editor
{
    private readonly BoxBoundsHandle _box = new BoxBoundsHandle();

    private void OnSceneGUI()
    {
        var v = (FogVolume)target;

        if (v.shape == FogVolumeShape.Box)
        {
            Matrix4x4 m = Matrix4x4.TRS(v.transform.position, v.transform.rotation, v.transform.lossyScale);
            using (new Handles.DrawingScope(m))
            {
                _box.center = Vector3.zero;
                _box.size = v.boxSize;
                EditorGUI.BeginChangeCheck();
                _box.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(v, "Edit Fog Volume Box");
                    v.boxSize = _box.size;
                }
            }
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            float r = Handles.RadiusHandle(v.transform.rotation, v.transform.position, v.sphereRadius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(v, "Edit Fog Volume Radius");
                v.sphereRadius = Mathf.Max(0f, r);
            }
        }
    }
}
