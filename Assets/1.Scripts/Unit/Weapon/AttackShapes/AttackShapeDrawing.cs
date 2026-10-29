#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 전용] 공격 판정 도형 그리기(할 일 153) — 외곽선은 <see cref="AttackShape.AppendOutlines"/>(판정과 같은 수식)에서 받는다.
/// <see cref="DrawGizmos"/> = <c>OnDrawGizmos</c> 안, <see cref="DrawHandles"/> = 씬 뷰 GUI(<c>duringSceneGui</c>·<c>OnSceneGUI</c>) 안에서 부른다.
/// 바닥(기준점 높이 + lift) 고리는 진하게, <c>heightRange</c> 면 도형 높이 범위(minHeight·maxHeight) 고리와 세로 모서리를 옅게 더 그린다.
/// 도넛(부채꼴 minOffset·원 minRadius)은 고리 2개라 안쪽 빈 곳도 그려진다.
/// </summary>
public static class AttackShapeDrawing
{
    // 높이 범위 선의 불투명도 배율, 고리 하나에 긋는 세로 모서리 최대 수(꼭짓점이 적은 다각형은 전부).
    private const float HeightAlpha = 0.35f;
    private const int MaxVerticalsPerLoop = 8;

    private static readonly List<Vector3[]> Floor = new List<Vector3[]>();
    private static readonly List<Vector3[]> Heights = new List<Vector3[]>();
    private static readonly List<Vector3> Verticals = new List<Vector3>();

    public static void DrawGizmos(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, Color color, float lift = 0.05f, bool heightRange = true)
    {
        Collect(shapes, pose, lift, heightRange);
        Color previous = Gizmos.color;
        Gizmos.color = color;
        foreach (Vector3[] loop in Floor)
            GizmoLoop(loop);

        Gizmos.color = Faded(color);
        foreach (Vector3[] loop in Heights)
            GizmoLoop(loop);
        for (int i = 0; i + 1 < Verticals.Count; i += 2)
            Gizmos.DrawLine(Verticals[i], Verticals[i + 1]);

        Gizmos.color = previous;
        Clear();
    }

    public static void DrawHandles(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, Color color, float thickness = 3f, float lift = 0.05f,
        bool heightRange = true)
    {
        Collect(shapes, pose, lift, heightRange);
        Color previous = Handles.color;
        Handles.color = color;
        foreach (Vector3[] loop in Floor)
            HandlesLoop(loop, thickness);

        Handles.color = Faded(color);
        foreach (Vector3[] loop in Heights)
            HandlesLoop(loop, 1f);
        for (int i = 0; i + 1 < Verticals.Count; i += 2)
            Handles.DrawLine(Verticals[i], Verticals[i + 1]);

        Handles.color = previous;
        Clear();
    }

    public static void DrawGizmos(AttackShapeSet set, in AttackShapePose pose, Color color, float lift = 0.05f, bool heightRange = true)
    {
        if (set != null)
            DrawGizmos(set.Shapes, pose, color, lift, heightRange);
    }

    public static void DrawHandles(AttackShapeSet set, in AttackShapePose pose, Color color, float thickness = 3f, float lift = 0.05f, bool heightRange = true)
    {
        if (set != null)
            DrawHandles(set.Shapes, pose, color, thickness, lift, heightRange);
    }

    /// <summary>
    /// 그리기가 긋는 닫힌 고리 전부(월드) — 바닥 고리(기준점 높이 + <paramref name="lift"/>)를 <paramref name="floor"/> 에,
    /// <paramref name="heightRange"/> 면 도형마다 minHeight·maxHeight 고리를 <paramref name="heights"/> 에 덧붙인다. 테스트가 판정 경계와 맞대 본다.
    /// </summary>
    public static void CollectOutlines(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, List<Vector3[]> floor, List<Vector3[]> heights,
        float lift = 0.05f, bool heightRange = true) =>
        AppendAll(shapes, pose, floor, heights, null, lift, heightRange);

    private static void AppendAll(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, List<Vector3[]> floor, List<Vector3[]> heights,
        List<Vector3> verticals, float lift, bool heightRange)
    {
        if (shapes == null)
            return;

        foreach (AttackShape shape in shapes)
        {
            if (shape == null)
                continue;

            shape.AppendOutlines(pose, floor, lift);
            if (!heightRange || heights == null)
                continue;

            int bottom = heights.Count;
            shape.AppendOutlines(pose, heights, shape.MinHeight);
            int top = heights.Count;
            shape.AppendOutlines(pose, heights, shape.MaxHeight);
            if (verticals != null)
                AppendVerticals(heights, bottom, top, verticals);
        }
    }

    private static void Collect(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, float lift, bool heightRange)
    {
        Clear();
        AppendAll(shapes, pose, Floor, Heights, Verticals, lift, heightRange);
    }

    // 같은 도형의 아래·위 고리는 점 순서가 같다 — 고리마다 고르게 몇 개만 세로로 잇는다.
    private static void AppendVerticals(List<Vector3[]> heights, int bottom, int top, List<Vector3> verticals)
    {
        int loopCount = top - bottom;
        for (int l = 0; l < loopCount && top + l < heights.Count; l++)
        {
            Vector3[] low = heights[bottom + l];
            Vector3[] high = heights[top + l];
            int count = Mathf.Min(low.Length, high.Length);
            int step = Mathf.Max(1, Mathf.CeilToInt(count / (float)MaxVerticalsPerLoop));
            for (int i = 0; i < count; i += step)
            {
                verticals.Add(low[i]);
                verticals.Add(high[i]);
            }
        }
    }

    private static void GizmoLoop(Vector3[] loop)
    {
        for (int i = 0; i < loop.Length; i++)
            Gizmos.DrawLine(loop[i], loop[(i + 1) % loop.Length]);
    }

    private static void HandlesLoop(Vector3[] loop, float thickness)
    {
        var closed = new Vector3[loop.Length + 1];
        loop.CopyTo(closed, 0);
        closed[loop.Length] = loop[0];
        Handles.DrawAAPolyLine(thickness, closed);
    }

    private static Color Faded(Color color) => new Color(color.r, color.g, color.b, color.a * HeightAlpha);

    private static void Clear()
    {
        Floor.Clear();
        Heights.Clear();
        Verticals.Clear();
    }
}
#endif
