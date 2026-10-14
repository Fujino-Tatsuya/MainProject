using NUnit.Framework;
using UnityEngine;

/// <summary>에임 인디케이터 3D 쿼드 배치 순수 계산(할 일 33).</summary>
public sealed class AimIndicatorPlacementTests
{
    const float Eps = 1e-4f;

    [Test]
    public void Yaw_ForwardIsZero_RightIsNinety()
    {
        Assert.AreEqual(0f, AimIndicatorPlacement.YawDegrees(Vector3.forward), Eps);
        Assert.AreEqual(90f, AimIndicatorPlacement.YawDegrees(Vector3.right), Eps);
        Assert.AreEqual(-90f, AimIndicatorPlacement.YawDegrees(Vector3.left), Eps);
        Assert.AreEqual(180f, Mathf.Abs(AimIndicatorPlacement.YawDegrees(Vector3.back)), Eps);
    }

    [Test]
    public void Rotation_LiesFlatFacingUp_RegardlessOfAim()
    {
        foreach (Vector3 aim in new[] { Vector3.forward, Vector3.right, new Vector3(-1f, 0f, -1f).normalized })
        {
            Quaternion rotation = AimIndicatorPlacement.Rotation(aim, 90f);
            // 쿼드 앞면(-Z)이 위를 본다 — 수평 유지.
            AssertVector3(Vector3.up, rotation * Vector3.back);
        }
    }

    [Test]
    public void Rotation_ArrowAxisPointsAlongAim()
    {
        // 화살표 텍스처는 -U(로컬 -X) 쪽을 가리킨다 — 데칼 시절과 같은 매핑.
        Vector3 aim = new Vector3(1f, 0f, 1f).normalized;
        Quaternion rotation = AimIndicatorPlacement.Rotation(aim, 90f);

        AssertVector3(aim, rotation * Vector3.left);
    }

    [Test]
    public void Height_UsesGroundWhenHit_ElseCharacterY()
    {
        Assert.AreEqual(1.08f, AimIndicatorPlacement.Height(true, 1f, 1.5f, 0.08f), Eps);
        Assert.AreEqual(1.58f, AimIndicatorPlacement.Height(false, 1f, 1.5f, 0.08f), Eps);
    }

    [Test]
    public void Position_OffsetsAlongFlatAim_AtFixedHeight()
    {
        Vector3 result = AimIndicatorPlacement.Position(
            new Vector3(2f, 5f, 3f), 0.58f, new Vector3(0f, 0.7f, 2f), 0.6f);

        AssertVector3(new Vector3(2f, 0.58f, 3.6f), result);
    }

    [Test]
    public void Position_ZeroAim_StaysAtFoot()
    {
        Vector3 result = AimIndicatorPlacement.Position(new Vector3(1f, 0f, 1f), 0.08f, Vector3.zero, 0.6f);

        AssertVector3(new Vector3(1f, 0.08f, 1f), result);
    }

    [Test]
    public void LocalScale_CancelsParentScale()
    {
        AssertVector3(new Vector3(1.2f, 1.2f, 1f), AimIndicatorPlacement.LocalScale(1.2f, 1f));
        AssertVector3(new Vector3(0.6f, 0.6f, 1f), AimIndicatorPlacement.LocalScale(1.2f, 2f));
        AssertVector3(new Vector3(1.2f, 1.2f, 1f), AimIndicatorPlacement.LocalScale(1.2f, 0f));
    }

    static void AssertVector3(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.x, actual.x, Eps, "x");
        Assert.AreEqual(expected.y, actual.y, Eps, "y");
        Assert.AreEqual(expected.z, actual.z, Eps, "z");
    }
}
