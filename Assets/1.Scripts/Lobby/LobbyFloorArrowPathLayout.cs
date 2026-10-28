using System.Collections.Generic;
using UnityEngine;

/// <summary>경로형 바닥 화살표 조각 종류 — <see cref="LobbyFloorArrowPath"/>.</summary>
public enum LobbyFloorArrowPieceKind : byte
{
    Dash,
    SmallTriangle,
    EndTriangle,
}

/// <summary>배치된 조각 하나 — 위치·진행 방향은 경로 루트 로컬, <see cref="Distance"/> = 시작점부터 경로를 따라 잰 거리(m).</summary>
public readonly struct LobbyFloorArrowPiece
{
    public readonly LobbyFloorArrowPieceKind Kind;
    public readonly Vector3 Position;
    public readonly Vector3 Direction;
    public readonly float Distance;

    public LobbyFloorArrowPiece(LobbyFloorArrowPieceKind kind, Vector3 position, Vector3 direction, float distance)
    {
        Kind = kind;
        Position = position;
        Direction = direction;
        Distance = distance;
    }
}

/// <summary>배치 수치(m). 조각 크기는 꺾임·끝 여백 계산에만 쓴다.</summary>
public struct LobbyFloorArrowPathSettings
{
    public float Spacing;
    public float DashLength;
    public float DashWidth;
    public float SmallTriangleLength;
    public float SmallTriangleWidth;
    public float EndTriangleLength;

    /// <summary>몇 조각마다 하나를 작은 삼각형으로 — 0 이면 없음.</summary>
    public int TriangleEvery;
}

/// <summary>
/// 꺾은선 경로 위 조각 배치(할 일 111 후속) — 순수 계산, EditMode <c>LobbyFloorArrowPathLayoutTests</c>.
/// <list type="bullet">
/// <item>구간마다 따로 균일 배치한다: 구간 양 끝에서 여백을 빼고 남은 길이를 <see cref="LobbyFloorArrowPathSettings.Spacing"/> 에 가장 가까운 간격으로 나눈다.
/// 그래서 조각이 꺾임점을 넘어 걸치지 않고, 꺾임 양쪽 조각 간격도 대략 한 간격으로 맞는다.</item>
/// <item>꺾임 여백 = max(간격/2, (가장 긴 조각 길이 + 가장 넓은 조각 폭)/2) — 직각에서 양쪽 조각 사각형이 겹치지 않는 최소값.</item>
/// <item>작은 삼각형 = 경로 전체 순서로 <see cref="LobbyFloorArrowPathSettings.TriangleEvery"/> 번째마다. 도착점엔 큰 삼각형(중심 = 도착점).</item>
/// </list>
/// </summary>
public static class LobbyFloorArrowPathLayout
{
    private const float MinSegment = 0.0001f;

    // 내장 Quad(XY 평면, U=+X, V=+Y)를 바닥에 눕히는 회전. 점선은 U(긴 쪽)가, 삼각형은 V(꼭짓점)가 진행 방향.
    private static readonly Quaternion DashLay = Quaternion.Euler(90f, -90f, 0f);
    private static readonly Quaternion TriangleLay = Quaternion.Euler(90f, 0f, 0f);

    public static float PathLength(IReadOnlyList<Vector3> points)
    {
        if (points == null)
            return 0f;

        var total = 0f;
        for (var i = 1; i < points.Count; i++)
            total += Vector3.Distance(points[i - 1], points[i]);
        return total;
    }

    public static bool IsSmallTriangle(int index, int every) => every > 0 && (index + 1) % every == 0;

    /// <summary>구간 하나에 놓을 조각 수 — 남은 길이가 음수면 0, 아니면 간격에 가장 가까운 등분 + 1.</summary>
    public static int SegmentPieceCount(float usable, float spacing)
    {
        if (usable < 0f)
            return 0;
        return Mathf.RoundToInt(usable / Mathf.Max(0.01f, spacing)) + 1;
    }

    public static float CornerPad(in LobbyFloorArrowPathSettings s)
    {
        var length = Mathf.Max(s.DashLength, s.TriangleEvery > 0 ? s.SmallTriangleLength : 0f);
        var width = Mathf.Max(s.DashWidth, s.TriangleEvery > 0 ? s.SmallTriangleWidth : 0f);
        return Mathf.Max(s.Spacing * 0.5f, (length + width) * 0.5f);
    }

    public static float StartPad(in LobbyFloorArrowPathSettings s) => s.DashLength * 0.5f;

    public static float EndPad(in LobbyFloorArrowPathSettings s)
    {
        var pieceHalf = Mathf.Max(s.DashLength, s.TriangleEvery > 0 ? s.SmallTriangleLength : 0f) * 0.5f;
        return s.EndTriangleLength * 0.5f + Mathf.Max(s.Spacing * 0.5f, pieceHalf);
    }

    /// <summary>조각 로컬 회전 — 진행 방향(경로 루트 로컬)으로 눕힌 Quad.</summary>
    public static Quaternion PieceRotation(LobbyFloorArrowPieceKind kind, Vector3 direction)
    {
        var look = direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        return look * (kind == LobbyFloorArrowPieceKind.Dash ? DashLay : TriangleLay);
    }

    /// <summary>조각 로컬 스케일 — 점선 (길이, 폭), 삼각형 (폭, 길이). Quad 축 기준.</summary>
    public static Vector3 PieceScale(LobbyFloorArrowPieceKind kind, float length, float width)
    {
        return kind == LobbyFloorArrowPieceKind.Dash ? new Vector3(length, width, 1f) : new Vector3(width, length, 1f);
    }

    /// <summary>삼각형 폭에 대한 길이 비 — 텍스처 사각형 픽셀 비율 그대로.</summary>
    public static float RectAspect(Rect uv, int textureWidth, int textureHeight)
    {
        var w = uv.width * Mathf.Max(1, textureWidth);
        var h = uv.height * Mathf.Max(1, textureHeight);
        return w > 0f ? h / w : 1f;
    }

    public static void Layout(IReadOnlyList<Vector3> points, in LobbyFloorArrowPathSettings s, List<LobbyFloorArrowPiece> output)
    {
        output.Clear();
        if (points == null || points.Count < 2)
            return;

        var first = -1;
        var last = -1;
        for (var i = 1; i < points.Count; i++)
        {
            if (Vector3.Distance(points[i - 1], points[i]) < MinSegment)
                continue;
            if (first < 0)
                first = i;
            last = i;
        }

        if (first < 0)
            return;

        var spacing = Mathf.Max(0.01f, s.Spacing);
        var cornerPad = CornerPad(s);
        var startPad = StartPad(s);
        var endPad = EndPad(s);
        var index = 0;
        var travelled = 0f;
        var lastDir = Vector3.forward;

        for (var i = first; i <= last; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var length = Vector3.Distance(a, b);
            if (length < MinSegment)
                continue;

            var dir = (b - a) / length;
            lastDir = dir;
            var usable = length - (i == first ? startPad : cornerPad) - (i == last ? endPad : cornerPad);
            var count = SegmentPieceCount(usable, spacing);
            var from = i == first ? startPad : cornerPad;
            for (var k = 0; k < count; k++)
            {
                var along = from + (count == 1 ? usable * 0.5f : usable * k / (count - 1));
                var kind = IsSmallTriangle(index, s.TriangleEvery) ? LobbyFloorArrowPieceKind.SmallTriangle : LobbyFloorArrowPieceKind.Dash;
                output.Add(new LobbyFloorArrowPiece(kind, a + dir * along, dir, travelled + along));
                index++;
            }

            travelled += length;
        }

        output.Add(new LobbyFloorArrowPiece(LobbyFloorArrowPieceKind.EndTriangle, points[last], lastDir, travelled));
    }
}
