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

    // ── 판정 앵커 형상(상자·구·캡슐 — 도형 판정 아님, 간파 상자 등) ──

    /// <summary>원 하나를 긋는 선분 수(구·캡슐).</summary>
    public const int AnchorCircleSegments = 32;

    /// <summary>
    /// 판정 앵커 형상(<see cref="HitboxAnchorShape"/> — 판정 오버랩이 질의하는 값)의 선분을 끝점 쌍으로 <paramref name="segments"/> 에 덧붙인다(할 일 156).
    /// 상자 = 모서리 12개, 구 = 수평·수직 원 3개, 캡슐 = 양 끝 구 + 옆선 4개(특성 랩 앵커 기즈모와 같은 모양). 호출측이 같은 선분을 Gizmos·GL 로 긋는다.
    /// </summary>
    public static void CollectAnchorSegments(in HitboxAnchorShape shape, List<Vector3> segments)
    {
        switch (shape.Kind)
        {
            case OverlapCollider.Box:
                AppendBox(shape.Box.center, shape.Box.halfExtents, shape.Box.orientation, segments);
                break;
            case OverlapCollider.Sphere:
                AppendSphere(shape.Sphere.center, shape.Sphere.radius, segments);
                break;
            case OverlapCollider.Capsule:
                AppendCapsule(shape.Capsule.point0, shape.Capsule.point1, shape.Capsule.radius, segments);
                break;
        }
    }

    private static void AppendBox(Vector3 center, Vector3 half, Quaternion rotation, List<Vector3> segments)
    {
        Vector3 Corner(int i) => center + rotation * new Vector3(
            (i & 1) != 0 ? half.x : -half.x, (i & 2) != 0 ? half.y : -half.y, (i & 4) != 0 ? half.z : -half.z);

        // 꼭짓점 번호의 비트 하나만 다른 쌍 = 모서리.
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) != 0)
                    continue;
                segments.Add(Corner(i));
                segments.Add(Corner(i | bit));
            }
        }
    }

    private static void AppendSphere(Vector3 center, float radius, List<Vector3> segments)
    {
        AppendCircle(center, Vector3.right, Vector3.forward, radius, segments);
        AppendCircle(center, Vector3.right, Vector3.up, radius, segments);
        AppendCircle(center, Vector3.forward, Vector3.up, radius, segments);
    }

    private static void AppendCapsule(Vector3 p0, Vector3 p1, float radius, List<Vector3> segments)
    {
        AppendSphere(p0, radius, segments);
        AppendSphere(p1, radius, segments);
        Vector3 axis = p1 - p0;
        Vector3 side = axis.sqrMagnitude > 1e-6f ? Vector3.Cross(axis.normalized, Vector3.up) : Vector3.right;
        if (side.sqrMagnitude < 1e-6f)
            side = Vector3.right;
        side = side.normalized * radius;
        Vector3 up = (axis.sqrMagnitude > 1e-6f ? Vector3.Cross(side, axis.normalized) : Vector3.up).normalized * radius;
        segments.Add(p0 + side); segments.Add(p1 + side);
        segments.Add(p0 - side); segments.Add(p1 - side);
        segments.Add(p0 + up); segments.Add(p1 + up);
        segments.Add(p0 - up); segments.Add(p1 - up);
    }

    private static void AppendCircle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, List<Vector3> segments)
    {
        Vector3 previous = center + axisA * radius;
        for (int i = 1; i <= AnchorCircleSegments; i++)
        {
            float angle = i * (2f * Mathf.PI / AnchorCircleSegments);
            Vector3 next = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
            segments.Add(previous);
            segments.Add(next);
            previous = next;
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
