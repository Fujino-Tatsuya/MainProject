#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 전용] 공격 판정 도형 그리기(할 일 153) — 외곽선은 <see cref="AttackShape.AppendOutlines"/>(판정과 같은 수식)에서 받는다.
/// <see cref="DrawGizmos"/> = <c>OnDrawGizmos</c> 안, <see cref="DrawHandles"/> = 씬 뷰 GUI(<c>duringSceneGui</c>·<c>OnSceneGUI</c>) 안에서 부른다.
/// 높이는 바닥(기준점 높이) 한 겹만 — 높이 범위는 그리지 않는다.
/// </summary>
public static class AttackShapeDrawing
{
    private static readonly List<Vector3[]> Loops = new List<Vector3[]>();

    public static void DrawGizmos(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, Color color, float lift = 0.05f)
    {
        Collect(shapes, pose, lift);
        Color previous = Gizmos.color;
        Gizmos.color = color;
        foreach (Vector3[] loop in Loops)
        {
            for (int i = 0; i < loop.Length; i++)
                Gizmos.DrawLine(loop[i], loop[(i + 1) % loop.Length]);
        }

        Gizmos.color = previous;
        Loops.Clear();
    }

    public static void DrawHandles(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, Color color, float thickness = 3f, float lift = 0.05f)
    {
        Collect(shapes, pose, lift);
        Color previous = Handles.color;
        Handles.color = color;
        foreach (Vector3[] loop in Loops)
        {
            var closed = new Vector3[loop.Length + 1];
            loop.CopyTo(closed, 0);
            closed[loop.Length] = loop[0];
            Handles.DrawAAPolyLine(thickness, closed);
        }

        Handles.color = previous;
        Loops.Clear();
    }

    public static void DrawGizmos(AttackShapeSet set, in AttackShapePose pose, Color color, float lift = 0.05f)
    {
        if (set != null)
            DrawGizmos(set.Shapes, pose, color, lift);
    }

    public static void DrawHandles(AttackShapeSet set, in AttackShapePose pose, Color color, float thickness = 3f, float lift = 0.05f)
    {
        if (set != null)
            DrawHandles(set.Shapes, pose, color, thickness, lift);
    }

    private static void Collect(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, float lift)
    {
        Loops.Clear();
        if (shapes == null)
            return;

        foreach (AttackShape shape in shapes)
            shape?.AppendOutlines(pose, Loops, lift);
    }
}
#endif
