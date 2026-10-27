#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>119 C5 로비 출격 걸음 경유점 — <see cref="QABoss103Plan.WalkRoute"/> 최단 경로·막힘 우회·길 없음.</summary>
public sealed class QABoss103RouteTests
{
    // 평면 점 (x, z) — 0 = 출발, 끝 = 도착.
    private static Func<int, int, float> Dist(float[,] p) => (a, b) =>
    {
        float dx = p[a, 0] - p[b, 0], dz = p[a, 1] - p[b, 1];
        return (float)Math.Sqrt(dx * dx + dz * dz);
    };

    private static Func<int, int, bool> Blocked(params (int, int)[] pairs)
    {
        var set = new HashSet<(int, int)>();
        foreach ((int a, int b) in pairs)
        {
            set.Add((a, b));
            set.Add((b, a));
        }
        return (a, b) => !set.Contains((a, b));
    }

    [Test]
    public void 직선이_열려_있으면_도착_하나()
    {
        var p = new float[,] { { 0, 0 }, { 5, 5 }, { 0, 10 } };
        CollectionAssert.AreEqual(new[] { 2 }, QABoss103Plan.WalkRoute(3, Blocked(), Dist(p)));
    }

    [Test]
    public void 직선이_막히면_경유점을_거친다()
    {
        var p = new float[,] { { 0, 0 }, { -3, 5 }, { 0, 10 } };
        CollectionAssert.AreEqual(new[] { 1, 2 }, QABoss103Plan.WalkRoute(3, Blocked((0, 2)), Dist(p)));
    }

    [Test]
    public void 열린_경유점_중_짧은_쪽()
    {
        // 1 = 먼 옆길, 2 = 가까운 옆길 — 둘 다 열림.
        var p = new float[,] { { 0, 0 }, { -9, 5 }, { -2, 5 }, { 0, 10 } };
        CollectionAssert.AreEqual(new[] { 2, 3 }, QABoss103Plan.WalkRoute(4, Blocked((0, 3)), Dist(p)));
    }

    [Test]
    public void 경유점_두_개를_잇는다()
    {
        // 로비 실측과 같은 모양: 선택대 → (콘솔 막힘) → 출격 패드 → 포드 걷기 시작점 → 집결 자리.
        var p = new float[,] { { 5, -6 }, { -2.5f, -6.6f }, { -3.3f, 5.9f }, { 0.7f, 8.1f } };
        var clear = Blocked((0, 3), (0, 2), (1, 3));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, QABoss103Plan.WalkRoute(4, clear, Dist(p)));
    }

    [Test]
    public void 길이_없으면_null()
    {
        var p = new float[,] { { 0, 0 }, { -3, 5 }, { 0, 10 } };
        Assert.IsNull(QABoss103Plan.WalkRoute(3, Blocked((0, 2), (1, 2)), Dist(p)));
    }

    [Test]
    public void 노드가_모자라면_null()
    {
        Assert.IsNull(QABoss103Plan.WalkRoute(1, (a, b) => true, (a, b) => 1f));
    }
}
#endif
