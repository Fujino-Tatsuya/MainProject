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

    // ───────────── 아이콘 쿼드(257 보완) ─────────────

    private sealed class PathData
    {
        public readonly List<Vector3> Center = new List<Vector3>();
        public readonly List<Vector3> In = new List<Vector3>();
        public readonly List<Vector3> Out = new List<Vector3>();
        public readonly List<Vector4> Positions = new List<Vector4>();
        public readonly List<Vector4> Tangents = new List<Vector4>();
        public float Length;

        public void Sample(float s, out Vector3 position, out Vector3 tangent)
        {
            LobbyFloorDashBeltLayout.SamplePath(Positions, Tangents, Positions.Count, s, out position, out tangent);
        }
    }

    private static PathData Path(float radius, int maxSamples, params Vector3[] points)
    {
        var d = new PathData();
        LobbyFloorDashBeltLayout.BuildCenterline(points, radius, 10f, d.Center, d.In, d.Out);
        d.Length = LobbyFloorDashBeltLayout.BuildPathSamples(d.Center, d.In, d.Out, maxSamples, d.Positions, d.Tangents);
        return d;
    }

    [Test]
    public void 원호_접선은_정확하고_양_끝은_구간_방향()
    {
        var d = Path(1f, 128, Vector3.zero, new Vector3(0, 0, 4), new Vector3(4, 0, 4));
        Assert.That(d.In.Count, Is.EqualTo(d.Center.Count));
        Assert.That(d.Out.Count, Is.EqualTo(d.Center.Count));
        AssertNear(d.In[0], Vector3.forward);
        AssertNear(d.In[1], Vector3.forward); // 원호 시작 = 들어오는 구간 방향 그대로
        AssertNear(d.In[10], Vector3.right); // 원호 끝 = 나가는 구간 방향
        AssertNear(d.Out[d.Out.Count - 1], Vector3.right);
        for (var i = 1; i <= 10; i++)
        {
            // 원 접선 ⟂ 반지름, 들어오고 나가는 방향이 같다(매끄럽게 돈다).
            Assert.That(Vector3.Dot(d.In[i], d.Center[i] - new Vector3(1, 0, 3)), Is.EqualTo(0f).Within(Eps));
            AssertNear(d.In[i], d.Out[i]);
        }

        // 원호 아닌 경로 표본은 꼭짓점마다 하나 — 길이는 띠와 같다.
        Assert.That(d.Positions.Count, Is.EqualTo(d.Center.Count));
        Assert.That(d.Length, Is.EqualTo(Ribbon(d.Center).length).Within(Eps));
        Assert.That(d.Positions[d.Positions.Count - 1].w, Is.EqualTo(d.Length).Within(Eps));
    }

    [Test]
    public void 직선_구간에선_아이콘_방향이_기울지_않는다()
    {
        // 둥근 모서리 직전 직선 구간(z 0~3)과 직후 구간(x 1~4) — 진행 방향이 정확히 구간 방향이어야 예전 직선 모양 그대로.
        var d = Path(1f, 128, Vector3.zero, new Vector3(0, 0, 4), new Vector3(4, 0, 4));
        for (var s = 0f; s <= 3f; s += 0.25f)
        {
            d.Sample(s, out var p, out var t);
            AssertNear(p, new Vector3(0, 0, s));
            AssertNear(t, Vector3.forward);
        }

        var afterArc = 3f + Mathf.PI * 0.5f;
        for (var s = afterArc + 0.1f; s <= d.Length; s += 0.25f)
        {
            d.Sample(s, out _, out var t);
            AssertNear(t, Vector3.right);
        }
    }

    [Test]
    public void 원호_위에선_아이콘이_돌기만_한다()
    {
        var d = Path(1f, 128, Vector3.zero, new Vector3(0, 0, 4), new Vector3(4, 0, 4));
        var previousAngle = 0f;
        for (var s = 3f; s <= 3f + Mathf.PI * 0.5f; s += 0.05f)
        {
            d.Sample(s, out var p, out var t);
            // 각도는 0°→90° 로 단조 증가(코너를 따라 돈다), 위치는 원 위(현 근사 오차만).
            var angle = Vector3.SignedAngle(Vector3.forward, t, Vector3.up);
            Assert.That(angle, Is.GreaterThanOrEqualTo(previousAngle - 0.01f));
            previousAngle = angle;
            Assert.That(Vector3.Distance(p, new Vector3(1, 0, 3)), Is.EqualTo(1f).Within(0.01f));

            // 쿼드 = 같은 정사각형을 돌린 것 — 변·대각선 길이가 어디서나 같다(찌그러짐·전단 없음).
            var c00 = LobbyFloorDashBeltLayout.IconCorner(p, t, -0.4f, -0.4f);
            var c01 = LobbyFloorDashBeltLayout.IconCorner(p, t, -0.4f, 0.4f);
            var c10 = LobbyFloorDashBeltLayout.IconCorner(p, t, 0.4f, -0.4f);
            var c11 = LobbyFloorDashBeltLayout.IconCorner(p, t, 0.4f, 0.4f);
            Assert.That(Vector3.Distance(c00, c01), Is.EqualTo(0.8f).Within(Eps));
            Assert.That(Vector3.Distance(c00, c10), Is.EqualTo(0.8f).Within(Eps));
            Assert.That(Vector3.Distance(c10, c11), Is.EqualTo(0.8f).Within(Eps));
            Assert.That(Vector3.Distance(c00, c11), Is.EqualTo(0.8f * Mathf.Sqrt(2f)).Within(Eps));
            Assert.That(Vector3.Distance(c01, c10), Is.EqualTo(0.8f * Mathf.Sqrt(2f)).Within(Eps));
        }

        Assert.That(previousAngle, Is.GreaterThan(80f));
    }

    [Test]
    public void 직선_쿼드는_예전_띠와_같은_축()
    {
        // +Z 진행: 진행 방향 = +Z(U), 왼쪽 = -X(V1). 예전 띠 UV 의 아이콘 칸과 같은 자리.
        var c = LobbyFloorDashBeltLayout.IconCorner(new Vector3(0, 0, 2), Vector3.forward, 0.5f, 0.5f);
        AssertNear(c, new Vector3(-0.5f, 0, 2.5f));
    }

    [Test]
    public void 마이터_꺾임은_같은_자리에_표본_둘()
    {
        var d = Path(0f, 128, Vector3.zero, new Vector3(0, 0, 4), new Vector3(3, 0, 4));
        Assert.That(d.Positions.Count, Is.EqualTo(4));
        Assert.That(d.Positions[1].w, Is.EqualTo(4f).Within(Eps));
        Assert.That(d.Positions[2].w, Is.EqualTo(4f).Within(Eps));
        AssertNear(d.Tangents[1], Vector3.forward);
        AssertNear(d.Tangents[2], Vector3.right);

        // 꺾임 앞뒤 직선 구간에서 방향이 섞이지 않는다.
        d.Sample(3.9f, out _, out var before);
        d.Sample(4.1f, out _, out var after);
        AssertNear(before, Vector3.forward);
        AssertNear(after, Vector3.right);
    }

    [Test]
    public void 경로_밖은_끝_방향으로_곧게_늘인다()
    {
        var d = Path(0.5f, 128, Vector3.zero, new Vector3(0, 0, 4), new Vector3(3, 0, 4));
        d.Sample(-0.5f, out var p0, out var t0);
        AssertNear(p0, new Vector3(0, 0, -0.5f));
        AssertNear(t0, Vector3.forward);
        d.Sample(d.Length + 0.5f, out var p1, out var t1);
        AssertNear(p1, new Vector3(3.5f, 0, 4));
        AssertNear(t1, Vector3.right);
    }

    [Test]
    public void 표본이_넘치면_같은_간격으로_다시_뽑는다()
    {
        var d = Path(1f, 8, Vector3.zero, new Vector3(0, 0, 4), new Vector3(4, 0, 4), new Vector3(4, 0, 0));
        Assert.That(d.Positions.Count, Is.EqualTo(8));
        Assert.That(d.Positions[0].w, Is.EqualTo(0f).Within(Eps));
        Assert.That(d.Positions[7].w, Is.EqualTo(d.Length).Within(Eps));
        for (var i = 1; i < 8; i++)
            Assert.That(d.Positions[i].w - d.Positions[i - 1].w, Is.EqualTo(d.Length / 7f).Within(Eps));
    }

    [Test]
    public void 아이콘_중심과_번호는_예전_띠_UV_와_같다()
    {
        // 예전: 칸 n 의 아이콘 중심 = n + 흐름 + 0.5(칸 단위). 흘러도 같은 n 은 같은 맥동 위상.
        foreach (var flow in new[] { 0f, 0.3f, 1.7f, -2.4f, 12.99f })
        {
            var count = LobbyFloorDashBeltLayout.IconQuadCount(5.5f, 1f);
            var lastCenter = float.NegativeInfinity;
            for (var q = 0; q < count; q++)
            {
                var x = LobbyFloorDashBeltLayout.IconCenterCells(q, flow, out var n);
                Assert.That(x, Is.EqualTo(n + flow + 0.5f).Within(Eps), $"flow {flow} quad {q}");
                Assert.That(n, Is.EqualTo(Mathf.Round(n)));
                Assert.That(x, Is.GreaterThan(lastCenter)); // 쿼드 순서 = 경로 순서
                lastCenter = x;
                Assert.That(x, Is.GreaterThanOrEqualTo(-0.5f - Eps));
                Assert.That(x, Is.LessThan(count - 0.5f));
            }
        }
    }

    [Test]
    public void 쿼드_수는_양_끝에_걸친_아이콘까지_덮는다()
    {
        foreach (var length in new[] { 0.3f, 1f, 4f, 5.5f, 17.25f })
        foreach (var spacing in new[] { 0.5f, 1f, 1.3f })
        {
            var count = LobbyFloorDashBeltLayout.IconQuadCount(length, spacing);
            // 흐름 소수부 0 일 때 마지막 중심 = count - 1.5 — 끝(길이/간격)에서 반 칸 넘게 걸친 아이콘까지 있어야 한다.
            var last = LobbyFloorDashBeltLayout.IconCenterCells(count - 1, 0f, out _);
            Assert.That(last, Is.GreaterThanOrEqualTo(length / spacing + 0.5f - Eps), $"length {length} spacing {spacing}");
            Assert.That(LobbyFloorDashBeltLayout.IconCenterCells(0, 0.999f, out _), Is.LessThanOrEqualTo(0.5f));
        }
    }

    private static void AssertNear(Vector3 actual, Vector3 expected)
    {
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-3f), $"actual {actual} expected {expected}");
    }
}
