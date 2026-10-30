using NUnit.Framework;
using UnityEngine;

/// <summary>집결 구역 박스 → 바닥 테두리 배치 — 할 일 158 C6.</summary>
public sealed class LobbyTutorialUiGatherBorderTests
{
    private const float Eps = 1e-4f;

    private static void AssertVec(Vector3 expected, Vector3 actual, string what) =>
        Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Eps), $"{what}: 기대 {expected}, 실제 {actual}");

    [Test]
    public void UnrotatedBox_SitsOnOriginFloor()
    {
        var p = LobbyGatherBorderLayout.Compute(new Vector3(10f, 2f, -3f), Quaternion.identity, Vector3.one,
            new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 6f), 0.02f);

        AssertVec(new Vector3(10f, 2.02f, -3f), p.Position, "위치 = 원점 높이 바닥 + 0.02");
        AssertVec(new Vector3(4f, 6f, 1f), p.Scale, "Quad 크기 = 박스 XZ");
        Assert.That(p.SizeMeters, Is.EqualTo(new Vector2(4f, 6f)));
        AssertVec(Vector3.up, p.Rotation * Vector3.back, "Quad 앞면(-Z)이 위를 본다");
    }

    [Test]
    public void BoxReachingBelowFloor_StaysOnOriginFloor()
    {
        // 로비 Gather_* 실제 값 — 박스가 바닥 아래 0.5m 까지 내려가도 테두리는 원점 높이(바닥) 위(할 일 173).
        var p = LobbyGatherBorderLayout.Compute(new Vector3(1.61f, 0f, -5.16f), Quaternion.identity, Vector3.one,
            new Vector3(0f, 1f, 0f), new Vector3(11.5f, 3f, 3f), 0.02f);

        AssertVec(new Vector3(1.61f, 0.02f, -5.16f), p.Position, "위치 = 바닥(원점) + 0.02");
    }

    [Test]
    public void ScaledAndRotatedBox_UsesLossyScaleAndYaw()
    {
        Quaternion yaw = Quaternion.Euler(0f, 90f, 0f);
        var p = LobbyGatherBorderLayout.Compute(Vector3.zero, yaw, new Vector3(2f, 1f, 0.5f),
            new Vector3(1f, 0f, 0f), new Vector3(3f, 2f, 4f), 0f);

        // 로컬 바닥 중심 (1, 0, 0) × 스케일 (2,1,0.5) = (2, 0, 0) → yaw 90 = (0, 0, -2)
        AssertVec(new Vector3(0f, 0f, -2f), p.Position, "위치");
        AssertVec(new Vector3(6f, 2f, 1f), p.Scale, "크기 = size × lossyScale (XZ)");
        AssertVec(yaw * Vector3.right, p.Rotation * Vector3.right, "Quad 가로축 = 박스 X 축");
    }

    [Test]
    public void ShouldDraw_ActiveAndNotConfirmed()
    {
        Assert.That(LobbyGatherBorderLayout.ShouldDraw(true, false), Is.True);
        Assert.That(LobbyGatherBorderLayout.ShouldDraw(true, true), Is.False);
        Assert.That(LobbyGatherBorderLayout.ShouldDraw(false, false), Is.False);
    }
}
