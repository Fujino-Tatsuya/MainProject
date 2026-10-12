using NUnit.Framework;
using UnityEngine;

public sealed class InterruptOmniRangeModelTests
{
    private const float Tolerance = 1e-4f;

    // 플레이어 중심 원점, 정면(+Z)으로 1.5 나간 박스 판정(반폭 0.5, 반높이 0.5, 반길이 1) — 가장 먼 꼭짓점 (±0.5, ·, 2.5).
    private static readonly Vector3 Pivot = Vector3.zero;
    private static readonly Vector3 BoxCenter = new Vector3(0f, 0.5f, 1.5f);
    private static readonly Vector3 BoxHalfExtents = new Vector3(0.5f, 0.5f, 1f);
    private static readonly float BoxReach = Mathf.Sqrt(0.5f * 0.5f + 2.5f * 2.5f);

    private static float Reach() =>
        InterruptOmniRangeModel.BoxReach(Pivot, BoxCenter, BoxHalfExtents, Quaternion.identity);

    private static Bounds Target(float x, float z, float size = 1f) =>
        new Bounds(new Vector3(x, 1f, z), new Vector3(size, 2f, size));

    [Test]
    public void BoxReach_IsFarthestCornerIncludingForwardOffset()
    {
        Assert.That(Reach(), Is.EqualTo(BoxReach).Within(Tolerance));
    }

    [Test]
    public void BoxReach_SameWhicheverWayPlayerFaces()
    {
        // 판정이 플레이어와 함께 돈다 — 옆(+X)·뒤(-Z)를 보고 있어도 사거리는 같다.
        Quaternion right = Quaternion.Euler(0f, 90f, 0f);
        Quaternion back = Quaternion.Euler(0f, 180f, 0f);

        Assert.That(InterruptOmniRangeModel.BoxReach(Pivot, right * BoxCenter, BoxHalfExtents, right),
            Is.EqualTo(BoxReach).Within(Tolerance));
        Assert.That(InterruptOmniRangeModel.BoxReach(Pivot, back * BoxCenter, BoxHalfExtents, back),
            Is.EqualTo(BoxReach).Within(Tolerance));
    }

    [Test]
    public void IsInReach_FrontBackAndSideAllCount()
    {
        float reach = Reach();

        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 2.8f), reach), Is.True, "앞");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -2.8f), reach), Is.True, "뒤 — 바라보는 방향과 무관");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(2.8f, 0f), reach), Is.True, "오른쪽");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(-2.8f, 0f), reach), Is.True, "왼쪽");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(2f, -2f), reach), Is.True, "대각선 뒤");
    }

    [Test]
    public void IsInReach_OutsideRadiusIsOff()
    {
        float reach = Reach();

        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 3.2f), reach), Is.False, "앞 — 표면 2.7 > 사거리");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -3.2f), reach), Is.False, "뒤");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(3.2f, 0f), reach), Is.False, "옆");
    }

    [Test]
    public void IsInReach_UsesTargetColliderSurfaceNotCenter()
    {
        float reach = Reach();

        // 중심은 3.5 로 같다 — 작은 대상(표면 3.0)은 밖, 큰 대상(반폭 1.1 → 표면 2.4)은 안.
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -3.5f, 1f), reach), Is.False);
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -3.5f, 2.2f), reach), Is.True);

        // 표면이 사거리 경계 바로 안/밖.
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(reach + 0.49f, 0f), reach), Is.True);
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(reach + 0.51f, 0f), reach), Is.False);
    }

    [Test]
    public void IsInReach_IgnoresHeight()
    {
        // 탑다운 — 수평 거리만 본다(높이 띠는 호출 쪽 후보 질의가 거른다).
        Bounds high = new Bounds(new Vector3(0f, 10f, -2.8f), Vector3.one);
        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToBounds(Pivot, high), Is.EqualTo(2.3f).Within(Tolerance));
    }

    [Test]
    public void HorizontalDistanceToBounds_ZeroWhenStandingInside()
    {
        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToBounds(Pivot, Target(0.2f, -0.2f, 3f)), Is.EqualTo(0f));
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 0f, 3f), Reach()), Is.True);
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 0f, 3f), 0f), Is.False, "판정 형상이 없으면(사거리 0) 끈다");
    }

    [Test]
    public void SphereAndCapsuleReach_AddRadiusToFarthestPoint()
    {
        Assert.That(InterruptOmniRangeModel.SphereReach(Pivot, new Vector3(0f, 1f, 2f), 0.5f),
            Is.EqualTo(2.5f).Within(Tolerance));
        Assert.That(InterruptOmniRangeModel.CapsuleReach(Pivot, new Vector3(0f, 1f, 1f), new Vector3(0f, 1f, 3f), 0.4f),
            Is.EqualTo(3.4f).Within(Tolerance), "더 먼 끝점 + 반경");
    }
}
