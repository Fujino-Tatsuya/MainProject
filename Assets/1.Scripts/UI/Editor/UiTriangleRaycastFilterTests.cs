using NUnit.Framework;
using UnityEngine;

/// <summary>
/// <see cref="UiTriangleRaycastFilter.Contains"/> — 셰브론 모양 삼각형(왼쪽 가운데 끝 (0, 0.5), 오른쪽 위·아래 모서리)으로
/// 안·밖·꼭짓점·변 근처, rect 크기·pivot 이 바뀌어도 같은 정규화 위치면 같은 답인지 본다(할 일 109).
/// </summary>
public sealed class UiTriangleRaycastFilterTests
{
    static readonly Vector2 A = new Vector2(0f, 0.5f);
    static readonly Vector2 B = new Vector2(1f, 1f);
    static readonly Vector2 C = new Vector2(1f, 0f);

    // 셰브론 프리팹과 같은 190×659, pivot 가운데 → 로컬 rect = (-95, -329.5) ~ (95, 329.5)
    static readonly Rect Chevron = new Rect(-95f, -329.5f, 190f, 659f);

    static bool Hit(Rect rect, Vector2 normalized) =>
        UiTriangleRaycastFilter.Contains(rect.min + Vector2.Scale(normalized, rect.size), rect, A, B, C);

    [Test]
    public void Inside_Center_And_RightEdgeMiddle()
    {
        Assert.IsTrue(Hit(Chevron, new Vector2(0.75f, 0.5f)));
        Assert.IsTrue(Hit(Chevron, new Vector2(1f, 0.5f)), "오른쪽 변 위 = 안");
        Assert.IsTrue(Hit(Chevron, new Vector2(0.5f, 0.5f)));
    }

    [Test]
    public void Outside_LeftCorners_OfRect()
    {
        Assert.IsFalse(Hit(Chevron, new Vector2(0.05f, 0.95f)), "왼쪽 위 빈 곳");
        Assert.IsFalse(Hit(Chevron, new Vector2(0.05f, 0.05f)), "왼쪽 아래 빈 곳");
        Assert.IsFalse(Hit(Chevron, new Vector2(0.3f, 0.9f)));
    }

    [Test]
    public void Vertices_AreInside()
    {
        Assert.IsTrue(Hit(Chevron, A));
        Assert.IsTrue(Hit(Chevron, B));
        Assert.IsTrue(Hit(Chevron, C));
    }

    [Test]
    public void NearSlantedEdge_JustInsideAndOutside()
    {
        // 위쪽 빗변: (0, 0.5) → (1, 1). x = 0.5 에서 y = 0.75.
        Assert.IsTrue(Hit(Chevron, new Vector2(0.5f, 0.75f)), "변 위 = 안");
        Assert.IsTrue(Hit(Chevron, new Vector2(0.5f, 0.745f)));
        Assert.IsFalse(Hit(Chevron, new Vector2(0.5f, 0.755f)));
        // 아래쪽 빗변: (0, 0.5) → (1, 0). x = 0.5 에서 y = 0.25.
        Assert.IsTrue(Hit(Chevron, new Vector2(0.5f, 0.255f)));
        Assert.IsFalse(Hit(Chevron, new Vector2(0.5f, 0.245f)));
    }

    [Test]
    public void OutsideRect_IsOutside()
    {
        Assert.IsFalse(Hit(Chevron, new Vector2(1.05f, 0.5f)));
        Assert.IsFalse(Hit(Chevron, new Vector2(-0.05f, 0.5f)));
    }

    [Test]
    public void SameAnswer_ForOtherSizeAndPivot()
    {
        Vector2[] samples =
        {
            new Vector2(0.75f, 0.5f), new Vector2(0.05f, 0.95f), new Vector2(0.5f, 0.745f), new Vector2(0.5f, 0.755f),
            new Vector2(0.5f, 0.255f), new Vector2(0.5f, 0.245f), new Vector2(0.98f, 0.02f), new Vector2(0.1f, 0.5f)
        };
        Rect[] rects =
        {
            new Rect(0f, 0f, 190f, 659f),        // pivot 왼쪽 아래
            new Rect(-190f, -659f, 190f, 659f),  // pivot 오른쪽 위
            new Rect(-50f, -10f, 400f, 120f),    // 다른 크기·비율·pivot
            new Rect(-0.5f, -0.5f, 1f, 1f)       // 아주 작은 rect
        };

        foreach (Vector2 n in samples)
        {
            bool expected = Hit(Chevron, n);
            foreach (Rect r in rects)
                Assert.AreEqual(expected, Hit(r, n), $"정규화 {n} · rect {r}");
        }
    }

    [Test]
    public void VertexOrder_DoesNotMatter_DegenerateIsOutside()
    {
        Vector2 p = Chevron.min + Vector2.Scale(new Vector2(0.75f, 0.5f), Chevron.size);
        Assert.IsTrue(UiTriangleRaycastFilter.Contains(p, Chevron, A, C, B), "반대 감김");
        Assert.IsFalse(UiTriangleRaycastFilter.Contains(p, Chevron, A, A, B), "넓이 0");
        Assert.IsFalse(UiTriangleRaycastFilter.Contains(p, new Rect(0f, 0f, 0f, 10f), A, B, C), "넓이 0 rect");
    }
}
