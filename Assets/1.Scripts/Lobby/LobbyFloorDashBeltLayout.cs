using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경로형 대시 벨트 계산(할 일 257) — 순수 계산, EditMode <c>LobbyFloorDashBeltLayoutTests</c>.
/// <list type="bullet">
/// <item>중심선 = 경로 점을 잇는 꺾은선. 꺾임마다 반지름 <c>cornerRadius</c> 의 원호로 모서리를 둥글린다
/// (인접 구간 길이의 절반까지만 — 짧은 구간에선 반지름이 줄어든다). 반지름 0 = 마이터(뾰족한 모서리).</item>
/// <item>띠 바탕 = 중심선 꼭짓점마다 좌·우 꼭짓점 두 개를 둔 하나로 이어진 삼각형 띠. 폭 일정(마이터 보정, 상한 <c>maxMiter</c>).
/// UV: U = 시작점부터 중심선을 따라 잰 거리 / 전체 길이(0~1), V = 폭 0(오른쪽)~1(왼쪽). 바탕색·끝 페이드만 쓴다.</item>
/// <item>아이콘 = 아이콘마다 따로인 정사각 쿼드(257 보완). 경로 위 호 길이 s 자리에서 그 자리 접선 방향으로 돌린다 —
/// 띠 UV 로 찍지 않으므로 원호 안쪽·바깥쪽 길이 차로 찌그러지지 않고 회전만 한다. 쿼드 수는 고정, 위치·회전은 셰이더가
/// 경로 표본(<see cref="BuildPathSamples"/>)과 시간으로 계산한다. 같은 규칙의 C# 판 = <see cref="IconCenterCells"/>·<see cref="SamplePath"/>·<see cref="IconCorner"/>.</item>
/// <item>점 2개(직선) = 직선 띠(내장 Quad 를 (90,-90,0) 으로 눕힌 것)와 같은 꼭짓점·UV.</item>
/// </list>
/// 좌표는 경로 루트 로컬. 폭 방향은 바닥 평면(XZ)에서 잰다.
/// </summary>
public static class LobbyFloorDashBeltLayout
{
    public const float MinSegment = 0.0001f;

    /// <summary>셰이더에 넘기는 경로 표본 최대 개수 — <c>Lobby/FloorDashBelt</c> 의 배열 크기와 같아야 한다.</summary>
    public const int MaxPathSamples = 128;

    public static float PathLength(IReadOnlyList<Vector3> points)
    {
        if (points == null)
            return 0f;

        var total = 0f;
        for (var i = 1; i < points.Count; i++)
            total += Vector3.Distance(points[i - 1], points[i]);
        return total;
    }

    /// <summary>바닥 평면에서 진행 방향의 왼쪽(진행 방향 +Z → -X). 수평 성분이 없으면 0.</summary>
    public static Vector3 LeftOf(Vector3 direction)
    {
        var left = new Vector3(-direction.z, 0f, direction.x);
        return left.sqrMagnitude > 1e-12f ? left.normalized : Vector3.zero;
    }

    /// <summary>
    /// 둥근 모서리 중심선. 너무 가까운 점(<see cref="MinSegment"/>)은 하나로 친다. 유효 점이 2개 미만이면 비운다.
    /// </summary>
    /// <param name="arcStepDegrees">원호를 나누는 각도 — 이 각도마다 꼭짓점 하나.</param>
    /// <param name="tangentsIn">(선택) 꼭짓점마다 들어오는 진행 방향(단위). 원호 위는 원의 정확한 접선.</param>
    /// <param name="tangentsOut">(선택) 꼭짓점마다 나가는 진행 방향. 마이터 꺾임에서만 <paramref name="tangentsIn"/> 과 다르다.</param>
    public static void BuildCenterline(IReadOnlyList<Vector3> points, float cornerRadius, float arcStepDegrees, List<Vector3> output,
        List<Vector3> tangentsIn = null, List<Vector3> tangentsOut = null)
    {
        output.Clear();
        tangentsIn?.Clear();
        tangentsOut?.Clear();
        if (points == null)
            return;

        var clean = new List<Vector3>(points.Count);
        foreach (var p in points)
        {
            if (clean.Count == 0 || Vector3.Distance(clean[clean.Count - 1], p) > MinSegment)
                clean.Add(p);
        }

        if (clean.Count < 2)
            return;

        void Add(Vector3 position, Vector3 tin, Vector3 tout)
        {
            output.Add(position);
            tangentsIn?.Add(tin);
            tangentsOut?.Add(tout);
        }

        var first = (clean[1] - clean[0]).normalized;
        Add(clean[0], first, first);
        var step = Mathf.Max(1f, arcStepDegrees);
        for (var i = 1; i < clean.Count - 1; i++)
        {
            var a = clean[i - 1];
            var b = clean[i];
            var c = clean[i + 1];
            var len1 = Vector3.Distance(a, b);
            var len2 = Vector3.Distance(b, c);
            var d1 = (b - a) / len1;
            var d2 = (c - b) / len2;
            var turn = Vector3.Angle(d1, d2); // 도
            if (cornerRadius <= 0f || turn < 0.5f)
            {
                Add(b, d1, d2);
                continue;
            }

            // 꺾임 양쪽에서 원호가 쓰는 길이 t = r·tan(θ/2). 이웃 꺾임과 겹치지 않게 구간 절반(양 끝 구간은 전부)까지만.
            var avail1 = i - 1 == 0 ? len1 : len1 * 0.5f;
            var avail2 = i + 1 == clean.Count - 1 ? len2 : len2 * 0.5f;
            var tanHalf = Mathf.Tan(Mathf.Min(turn, 179f) * 0.5f * Mathf.Deg2Rad);
            var t = Mathf.Min(cornerRadius * tanHalf, avail1, avail2);
            var radius = t / tanHalf;
            if (radius <= 1e-5f)
            {
                Add(b, d1, d2);
                continue;
            }

            var axis = Vector3.Cross(d1, d2);
            axis = axis.sqrMagnitude > 1e-10f ? axis.normalized : Vector3.up;
            var arcStart = b - d1 * t;
            var toCenter = Vector3.Cross(axis, d1); // d1 을 회전 방향으로 90° — 원호 중심 쪽
            var center = arcStart + toCenter * radius;
            var radial = arcStart - center;
            var steps = Mathf.Max(1, Mathf.CeilToInt(turn / step - 0.001f)); // 90.00001° 가 한 칸 더 나오지 않게
            for (var k = 0; k <= steps; k++)
            {
                var rotation = Quaternion.AngleAxis(turn * k / steps, axis);
                var tangent = rotation * d1; // 원의 정확한 접선 — 원호 양 끝은 정확히 d1·d2
                Add(center + rotation * radial, tangent, tangent);
            }
        }

        var last = (clean[clean.Count - 1] - clean[clean.Count - 2]).normalized;
        Add(clean[clean.Count - 1], last, last);

        // 원호 끝이 다음 점과 겹치면(구간을 다 쓴 경우) 중복 제거. 남는 쪽이 들어오는 방향은 앞 점, 나가는 방향은 뒤 점 것을 갖는다.
        for (var i = output.Count - 1; i > 0; i--)
        {
            if (Vector3.Distance(output[i], output[i - 1]) > MinSegment)
                continue;
            var remove = i == output.Count - 1 ? i - 1 : i;
            if (tangentsIn != null && tangentsOut != null)
            {
                var tin = tangentsIn[i - 1];
                var tout = tangentsOut[i];
                tangentsIn[remove == i ? i - 1 : i] = tin;
                tangentsOut[remove == i ? i - 1 : i] = tout;
            }

            output.RemoveAt(remove);
            tangentsIn?.RemoveAt(remove);
            tangentsOut?.RemoveAt(remove);
        }
    }

    /// <summary>
    /// 중심선 → 하나로 이어진 띠. 꼭짓점 2N 개(짝수 = 오른쪽 V0, 홀수 = 왼쪽 V1), 삼각형 (N-1)×2 개. 반환 = 중심선 길이(m).
    /// </summary>
    public static float BuildRibbon(IReadOnlyList<Vector3> centerline, float width, float maxMiter,
        List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        vertices.Clear();
        uvs.Clear();
        triangles.Clear();
        if (centerline == null || centerline.Count < 2)
            return 0f;

        var n = centerline.Count;
        var length = PathLength(centerline);
        if (length <= MinSegment)
            return 0f;

        var half = width * 0.5f;
        var minCos = 1f / Mathf.Max(1f, maxMiter);
        var distance = 0f;
        for (var i = 0; i < n; i++)
        {
            if (i > 0)
                distance += Vector3.Distance(centerline[i - 1], centerline[i]);

            var leftPrev = i > 0 ? LeftOf(centerline[i] - centerline[i - 1]) : Vector3.zero;
            var leftNext = i < n - 1 ? LeftOf(centerline[i + 1] - centerline[i]) : Vector3.zero;
            var left = (leftPrev + leftNext);
            left = left.sqrMagnitude > 1e-10f ? left.normalized : (leftNext != Vector3.zero ? leftNext : leftPrev);

            // 마이터 보정 — 꺾인 쪽에서도 각 구간에 수직으로 잰 폭이 같게. 너무 뾰족하면 상한.
            var reference = leftPrev != Vector3.zero ? leftPrev : leftNext;
            var cos = Mathf.Max(Vector3.Dot(left, reference), minCos);
            var offset = left * (half / cos);

            var u = distance / length;
            vertices.Add(centerline[i] - offset);
            uvs.Add(new Vector2(u, 0f));
            vertices.Add(centerline[i] + offset);
            uvs.Add(new Vector2(u, 1f));
        }

        for (var i = 0; i < n - 1; i++)
        {
            var r0 = i * 2;
            var l0 = r0 + 1;
            var r1 = r0 + 2;
            var l1 = r0 + 3;
            // 위(+Y)에서 볼 때 시계 방향 = Unity 앞면.
            triangles.Add(r0);
            triangles.Add(l0);
            triangles.Add(l1);
            triangles.Add(r0);
            triangles.Add(l1);
            triangles.Add(r1);
        }

        return length;
    }

    // ───────────── 아이콘 쿼드(257 보완) ─────────────

    /// <summary>
    /// 셰이더에 넘길 경로 표본. positions = (xyz, 시작점부터 누적 거리), tangents = (진행 방향 xyz, 0).
    /// 마이터 꺾임(들어오는·나가는 방향이 다름)은 같은 자리·같은 거리에 표본 2개를 둬 그 자리에서 방향이 바뀐다
    /// (그래야 직선 구간에서 방향이 보간돼 아이콘이 기울지 않는다). 원호 위는 정확한 접선이라 아이콘이 매끄럽게 돈다.
    /// 표본이 <paramref name="maxSamples"/> 를 넘으면 같은 간격으로 다시 뽑는다. 반환 = 경로 길이(띠 <see cref="BuildRibbon"/> 과 같다).
    /// </summary>
    public static float BuildPathSamples(IReadOnlyList<Vector3> centerline, IReadOnlyList<Vector3> tangentsIn, IReadOnlyList<Vector3> tangentsOut,
        int maxSamples, List<Vector4> positions, List<Vector4> tangents)
    {
        positions.Clear();
        tangents.Clear();
        if (centerline == null || centerline.Count < 2)
            return 0f;

        var distance = 0f;
        for (var i = 0; i < centerline.Count; i++)
        {
            if (i > 0)
                distance += Vector3.Distance(centerline[i - 1], centerline[i]);
            var p = centerline[i];
            var tin = tangentsIn[i];
            var tout = tangentsOut[i];
            positions.Add(new Vector4(p.x, p.y, p.z, distance));
            tangents.Add(tin);
            if (Vector3.Angle(tin, tout) > 0.01f)
            {
                positions.Add(new Vector4(p.x, p.y, p.z, distance));
                tangents.Add(tout);
            }
        }

        if (positions.Count > Mathf.Max(2, maxSamples))
            ResampleUniform(positions, tangents, Mathf.Max(2, maxSamples), distance);
        return distance;
    }

    private static void ResampleUniform(List<Vector4> positions, List<Vector4> tangents, int count, float length)
    {
        var srcPositions = new List<Vector4>(positions);
        var srcTangents = new List<Vector4>(tangents);
        positions.Clear();
        tangents.Clear();
        for (var k = 0; k < count; k++)
        {
            SamplePath(srcPositions, srcTangents, srcPositions.Count, length * k / (count - 1), out var p, out var t);
            positions.Add(new Vector4(p.x, p.y, p.z, length * k / (count - 1)));
            tangents.Add(t);
        }
    }

    /// <summary>
    /// 경로 위 거리 <paramref name="s"/> 의 위치·진행 방향(셰이더 <c>SamplePath</c> 와 같은 규칙). 양 끝 밖은 끝 방향으로 곧게 늘인다
    /// — 끝에 걸친 아이콘이 반쯤 보이며 들어오고 나간다(띠 밖 부분은 셰이더가 잘라 낸다).
    /// </summary>
    public static void SamplePath(IReadOnlyList<Vector4> positions, IReadOnlyList<Vector4> tangents, int count, float s,
        out Vector3 position, out Vector3 tangent)
    {
        if (count <= 0)
        {
            position = Vector3.zero;
            tangent = Vector3.forward;
            return;
        }

        if (s <= positions[0].w || count == 1)
        {
            tangent = ((Vector3)tangents[0]).normalized;
            position = (Vector3)positions[0] + tangent * (s - positions[0].w);
            return;
        }

        var lastIndex = count - 1;
        if (s >= positions[lastIndex].w)
        {
            tangent = ((Vector3)tangents[lastIndex]).normalized;
            position = (Vector3)positions[lastIndex] + tangent * (s - positions[lastIndex].w);
            return;
        }

        var i = 1;
        while (i < lastIndex && positions[i].w < s)
            i++;
        var a = positions[i - 1];
        var b = positions[i];
        var t = Mathf.Clamp01((s - a.w) / Mathf.Max(b.w - a.w, 1e-6f));
        position = Vector3.Lerp(a, b, t);
        var blended = Vector3.Lerp(tangents[i - 1], tangents[i], t);
        tangent = blended.sqrMagnitude > 1e-12f ? blended.normalized : ((Vector3)tangents[i]).normalized;
    }

    /// <summary>쿼드 개수(고정) — 경로 칸 수 + 양 끝에 걸치는 2개. 흐르는 동안 쿼드가 빙 돌아 다시 쓰인다.</summary>
    public static int IconQuadCount(float length, float iconSpacing)
    {
        return Mathf.CeilToInt(Mathf.Max(0f, length) / Mathf.Max(0.05f, iconSpacing) - 0.0001f) + 2;
    }

    /// <summary>
    /// 쿼드 <paramref name="quad"/> 의 아이콘 중심(칸 단위, × 간격 = 경로 거리)과 아이콘 번호(맥동 위상용).
    /// <paramref name="flowCells"/> = 시간 × 흐름(칸/초). 예전 띠 UV 방식(칸 n 의 중심 = n + 흐름 + 0.5)과 같은 자리·같은 번호 —
    /// 직선 구간 모양이 그대로이고, 흘러도 같은 아이콘은 같은 위상이다. 중심 범위 = [-0.5, 쿼드 수 - 0.5).
    /// </summary>
    public static float IconCenterCells(int quad, float flowCells, out float iconIndex)
    {
        var whole = Mathf.Floor(flowCells);
        iconIndex = quad - 1 - whole;
        return quad - 0.5f + (flowCells - whole);
    }

    /// <summary>
    /// 아이콘 쿼드 꼭짓점 — 중심 <paramref name="center"/>, 진행 방향 <paramref name="tangent"/> 로 돌린 직사각
    /// (along = 진행 방향 반길이, left = 왼쪽 반폭). 모양은 방향과 무관하게 같다(회전만).
    /// </summary>
    public static Vector3 IconCorner(Vector3 center, Vector3 tangent, float along, float left)
    {
        var forward = tangent.sqrMagnitude > 1e-12f ? tangent.normalized : Vector3.forward;
        var side = LeftOf(forward);
        if (side == Vector3.zero)
            side = Vector3.left;
        return center + forward * along + side * left;
    }
}
