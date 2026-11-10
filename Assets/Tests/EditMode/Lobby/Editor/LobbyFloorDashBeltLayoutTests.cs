using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>경로형 대시 벨트 띠 메시 — 할 일 257.</summary>
public sealed class LobbyFloorDashBeltLayoutTests
{
    private const float Eps = 1e-4f;

    private static List<Vector3> Centerline(float radius, params Vector3[] points)
    {
        var output = new List<Vector3>();
        LobbyFloorDashBeltLayout.BuildCenterline(points, radius, 10f, output);
        return output;
    }

    private static (List<Vector3> v, List<Vector2> uv, List<int> tri, float length) Ribbon(IReadOnlyList<Vector3> center, float width = 1f)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        var length = LobbyFloorDashBeltLayout.BuildRibbon(center, width, 4f, v, uv, tri);
        return (v, uv, tri, length);
    }

    [Test]
    public void 직선_두_점은_눕힌_Quad_와_같은_꼭짓점과_UV()
    {
        var center = Centerline(0.5f, Vector3.zero, new Vector3(0, 0, 4));
        var r = Ribbon(center);

        // 내장 Quad 를 (90,-90,0) 으로 눕혀 길이 4·폭 1 로 늘린 것: U → +Z, V → -X (V0 = +X 쪽).
        var quadRotation = Quaternion.Euler(90f, -90f, 0f);
        var quadCenter = new Vector3(0, 0, 2);
        var scale = new Vector3(4, 1, 1);
        Vector3 Quad(float x, float y) => quadCenter + quadRotation * Vector3.Scale(new Vector3(x, y, 0), scale);

        Assert.That(r.v.Count, Is.EqualTo(4));
        Assert.That(r.tri.Count, Is.EqualTo(6));
        Assert.That(r.length, Is.EqualTo(4f).Within(Eps));
        AssertNear(r.v[0], Quad(-0.5f, -0.5f)); Assert.That(r.uv[0], Is.EqualTo(new Vector2(0, 0)));
        AssertNear(r.v[1], Quad(-0.5f, 0.5f)); Assert.That(r.uv[1], Is.EqualTo(new Vector2(0, 1)));
        AssertNear(r.v[2], Quad(0.5f, -0.5f)); Assert.That(r.uv[2], Is.EqualTo(new Vector2(1, 0)));
        AssertNear(r.v[3], Quad(0.5f, 0.5f)); Assert.That(r.uv[3], Is.EqualTo(new Vector2(1, 1)));
    }

    [Test]
    public void 반지름_0_이면_꺾임점_그대로_마이터()
    {
        var center = Centerline(0f, Vector3.zero, new Vector3(0, 0, 4), new Vector3(3, 0, 4));
        Assert.That(center.Count, Is.EqualTo(3));
        AssertNear(center[1], new Vector3(0, 0, 4));

        // 직각 마이터: 꺾임 꼭짓점이 대각선으로 √2·반폭 — 양 구간에 수직으로 잰 폭이 1 로 같다.
        var r = Ribbon(center);
        AssertNear(r.v[2], new Vector3(0.5f, 0, 3.5f)); // 안쪽(오른쪽)
        AssertNear(r.v[3], new Vector3(-0.5f, 0, 4.5f)); // 바깥(왼쪽)
    }

    [Test]
    public void 둥근_모서리는_반지름_원호이고_길이가_줄어든다()
    {
        var center = Centerline(1f, Vector3.zero, new Vector3(0, 0, 4), new Vector3(4, 0, 4));

        // 원호 중심 (1,0,3), 반지름 1, 90° — 10° 마다 꼭짓점 → 원호 10점 + 양 끝 2점.
        Assert.That(center.Count, Is.EqualTo(12));
        AssertNear(center[1], new Vector3(0, 0, 3));
        AssertNear(center[10], new Vector3(1, 0, 4));
        for (var i = 1; i <= 10; i++)
            Assert.That(Vector3.Distance(center[i], new Vector3(1, 0, 3)), Is.EqualTo(1f).Within(Eps));

        // 길이 = 3 + (원호 현 길이 합 ≈ π/2) + 3 — 꺾은선 8 보다 짧다.
        var length = LobbyFloorDashBeltLayout.PathLength(center);
        Assert.That(length, Is.EqualTo(6f + Mathf.PI * 0.5f).Within(0.01f));
    }

    [Test]
    public void 반지름은_짧은_구간에서_줄어든다()
    {
        // 첫 구간 0.4 m — 원호는 그 구간 전체(0.4)까지만 쓴다: 직각이면 반지름 0.4.
        var center = Centerline(5f, Vector3.zero, new Vector3(0, 0, 0.4f), new Vector3(3, 0, 0.4f));
        AssertNear(center[0], Vector3.zero);
        Assert.That(center.Count, Is.GreaterThan(3));
        AssertNear(center[center.Count - 2], new Vector3(0.4f, 0, 0.4f));
        for (var i = 1; i < center.Count; i++)
            Assert.That(Vector3.Distance(center[i - 1], center[i]), Is.GreaterThan(LobbyFloorDashBeltLayout.MinSegment));
    }

    [Test]
    public void 띠는_하나로_이어지고_U_는_누적_길이_비율()
    {
        var center = Centerline(0.5f, Vector3.zero, new Vector3(0, 0, 4), new Vector3(3, 0, 4), new Vector3(3, 0, 8));
        var r = Ribbon(center);

        Assert.That(r.v.Count, Is.EqualTo(center.Count * 2));
        Assert.That(r.tri.Count, Is.EqualTo((center.Count - 1) * 6));
        var distance = 0f;
        for (var i = 0; i < center.Count; i++)
        {
            if (i > 0)
                distance += Vector3.Distance(center[i - 1], center[i]);
            Assert.That(r.uv[i * 2].x, Is.EqualTo(distance / r.length).Within(Eps));
            Assert.That(r.uv[i * 2 + 1].x, Is.EqualTo(r.uv[i * 2].x));
            Assert.That(r.uv[i * 2].y, Is.EqualTo(0f));
            Assert.That(r.uv[i * 2 + 1].y, Is.EqualTo(1f));
        }

        Assert.That(r.uv[r.uv.Count - 1].x, Is.EqualTo(1f).Within(Eps));
    }

    [Test]
    public void 폭은_어디서나_일정()
    {
        var center = Centerline(0.6f, Vector3.zero, new Vector3(0, 0, 4), new Vector3(3, 0, 6), new Vector3(-2, 0, 9));
        var r = Ribbon(center, 1.2f);
        for (var i = 0; i < center.Count; i++)
        {
            // 각 꼭짓점 쌍의 중점 = 중심선, 인접 구간 방향에 수직으로 잰 반폭 = 0.6.
            var right = r.v[i * 2];
            var left = r.v[i * 2 + 1];
            AssertNear((right + left) * 0.5f, center[i]);
            var dir = i < center.Count - 1 ? center[i + 1] - center[i] : center[i] - center[i - 1];
            var perp = LobbyFloorDashBeltLayout.LeftOf(dir);
            Assert.That(Vector3.Dot(left - center[i], perp), Is.EqualTo(0.6f).Within(0.01f));
        }
    }

    [Test]
    public void 겹친_점과_점_하나는_무시()
    {
        Assert.That(Centerline(0.5f, Vector3.zero), Is.Empty);
        Assert.That(Centerline(0.5f, Vector3.one, Vector3.one), Is.Empty);
        var center = Centerline(0.5f, Vector3.zero, Vector3.zero, new Vector3(0, 0, 2), new Vector3(0, 0, 2));
        Assert.That(center.Count, Is.EqualTo(2));
        Assert.That(Ribbon(new List<Vector3> { Vector3.zero }).v, Is.Empty);
    }

    [Test]
    public void 일직선_경유지는_꺾임_없음()
    {
        var center = Centerline(1f, Vector3.zero, new Vector3(0, 0, 2), new Vector3(0, 0, 5));
        Assert.That(center.Count, Is.EqualTo(3));
        Assert.That(LobbyFloorDashBeltLayout.PathLength(center), Is.EqualTo(5f).Within(Eps));
    }

    private static void AssertNear(Vector3 actual, Vector3 expected)
    {
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-3f), $"actual {actual} expected {expected}");
    }
}
