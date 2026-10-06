using NUnit.Framework;
using UnityEngine;

/// <summary>고정 거리 GroundPoint 조준 순수 계산(PLAN-assassin.md A4).</summary>
public sealed class PlayerGroundPointProjectionTests
{
    [Test]
    public void FixedDistance_CloseCursor_ProjectsToFullRange()
    {
        Vector3 result = PlayerGroundPointProjection.ProjectFixedDistance(
            Vector3.zero, new Vector3(0.5f, 2f, 0f), 5f, Vector3.forward);

        AssertVector3(new Vector3(5f, 2f, 0f), result);
    }

    [Test]
    public void FixedDistance_FarCursor_ProjectsBackToFullRange()
    {
        Vector3 result = PlayerGroundPointProjection.ProjectFixedDistance(
            Vector3.zero, new Vector3(0f, -1f, 100f), 5f, Vector3.right);

        AssertVector3(new Vector3(0f, -1f, 5f), result);
    }

    [Test]
    public void FixedDistance_OverlappingCursor_UsesLastDirection()
    {
        Vector3 origin = new Vector3(4f, 3f, 7f);
        Vector3 result = PlayerGroundPointProjection.ProjectFixedDistance(
            origin, new Vector3(4f, 0f, 7f), 2f, Vector3.left);

        AssertVector3(new Vector3(2f, 0f, 7f), result);
    }

    [Test]
    public void ServerProjection_OutsideTolerance_ReprojectsFromServerOrigin()
    {
        Vector3 serverOrigin = new Vector3(2f, 1f, 3f);
        Vector3 submittedPoint = new Vector3(2f, 0f, 13f);

        Vector3 result = PlayerGroundPointProjection.ReprojectServerFixedDistance(
            serverOrigin, submittedPoint, 4f, Vector3.right);

        AssertVector3(new Vector3(2f, 0f, 7f), result);
    }

    [Test]
    public void ServerProjection_WithinTolerance_PreservesSubmittedPoint()
    {
        Vector3 serverOrigin = new Vector3(1f, 2f, 1f);
        Vector3 submittedPoint = new Vector3(1f, 0f, 5.1f);

        Vector3 result = PlayerGroundPointProjection.ReprojectServerFixedDistance(
            serverOrigin, submittedPoint, 4f, Vector3.right);

        AssertVector3(submittedPoint, result);
    }

    private static void AssertVector3(Vector3 expected, Vector3 actual)
    {
        Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0001f));
    }
}
