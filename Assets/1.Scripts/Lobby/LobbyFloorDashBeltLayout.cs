using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경로형 대시 벨트 띠 메시 계산(할 일 257) — 순수 계산, EditMode <c>LobbyFloorDashBeltLayoutTests</c>.
/// <list type="bullet">
/// <item>중심선 = 경로 점을 잇는 꺾은선. 꺾임마다 반지름 <c>cornerRadius</c> 의 원호로 모서리를 둥글린다
/// (인접 구간 길이의 절반까지만 — 짧은 구간에선 반지름이 줄어든다). 반지름 0 = 마이터(뾰족한 모서리).</item>
/// <item>띠 = 중심선 꼭짓점마다 좌·우 꼭짓점 두 개를 둔 하나로 이어진 삼각형 띠. 폭 일정(마이터 보정, 상한 <c>maxMiter</c>).</item>
/// <item>UV: U = 시작점부터 중심선을 따라 잰 거리 / 전체 길이(0~1), V = 폭 0(오른쪽)~1(왼쪽).
/// 셰이더가 U × (길이 ÷ 간격) 으로 칸을 나누므로 아이콘이 꺾임을 지나 경로를 따라 흐르고 그 자리 진행 방향을 가리킨다.</item>
/// <item>점 2개(직선) = 직선 띠(내장 Quad 를 (90,-90,0) 으로 눕힌 것)와 같은 꼭짓점·UV.</item>
/// </list>
/// 좌표는 경로 루트 로컬. 폭 방향은 바닥 평면(XZ)에서 잰다.
/// </summary>
public static class LobbyFloorDashBeltLayout
{
    public const float MinSegment = 0.0001f;

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
    public static void BuildCenterline(IReadOnlyList<Vector3> points, float cornerRadius, float arcStepDegrees, List<Vector3> output)
    {
        output.Clear();
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

        output.Add(clean[0]);
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
                output.Add(b);
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
                output.Add(b);
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
                output.Add(center + Quaternion.AngleAxis(turn * k / steps, axis) * radial);
        }

        output.Add(clean[clean.Count - 1]);

        // 원호 끝이 다음 점과 겹치면(구간을 다 쓴 경우) 중복 제거.
        for (var i = output.Count - 1; i > 0; i--)
        {
            if (Vector3.Distance(output[i], output[i - 1]) <= MinSegment)
                output.RemoveAt(i == output.Count - 1 ? i - 1 : i);
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
}
