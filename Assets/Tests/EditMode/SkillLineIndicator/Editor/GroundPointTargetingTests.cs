using NUnit.Framework;
using UnityEngine;

public sealed class GroundPointTargetingTests
{
    private static readonly Vector3 Origin = new Vector3(1f, 0f, 1f);

    [Test]
    public void IsWithinRange_IgnoresHeight()
    {
        Assert.That(GroundPointTargeting.IsWithinRange(Origin, Origin + new Vector3(3f, 50f, 4f), 5f), Is.True);
        Assert.That(GroundPointTargeting.IsWithinRange(Origin, Origin + new Vector3(3f, 0f, 4.1f), 5f), Is.False);
    }

    [Test]
    public void IsWithinRange_ZeroRangeIsAlwaysOutside()
    {
        Assert.That(GroundPointTargeting.IsWithinRange(Origin, Origin, 0f), Is.False);
    }

    [Test]
    public void Confirm_InsideRange_CastsAtClickedPoint_InBothModes()
    {
        Vector3 point = Origin + new Vector3(0f, 0.2f, 6f);

        foreach (GroundPointOutOfRangeMode mode in new[]
                 { GroundPointOutOfRangeMode.Clamp, GroundPointOutOfRangeMode.AutoApproach })
        {
            GroundPointConfirmAction action = GroundPointTargeting.ResolveConfirm(Origin, point, 12f, mode, out Vector3 cast);
            Assert.That(action, Is.EqualTo(GroundPointConfirmAction.CastNow));
            Assert.That(cast, Is.EqualTo(point));
        }
    }

    [Test]
    public void Confirm_OutsideRange_Clamp_CastsNowAtBoundary()
    {
        Vector3 point = Origin + new Vector3(0f, 0.5f, 20f);

        GroundPointConfirmAction action = GroundPointTargeting.ResolveConfirm(
            Origin, point, 12f, GroundPointOutOfRangeMode.Clamp, out Vector3 cast);

        Assert.That(action, Is.EqualTo(GroundPointConfirmAction.CastNow));
        Assert.That(cast.x, Is.EqualTo(Origin.x).Within(0.0001f));
        Assert.That(cast.z, Is.EqualTo(Origin.z + 12f).Within(0.0001f));
        Assert.That(cast.y, Is.EqualTo(0.5f), "높이는 클릭 지점 것을 쓴다");
    }

    [Test]
    public void Confirm_OutsideRange_AutoApproach_KeepsClickedPoint()
    {
        Vector3 point = Origin + new Vector3(20f, 0f, 0f);

        GroundPointConfirmAction action = GroundPointTargeting.ResolveConfirm(
            Origin, point, 12f, GroundPointOutOfRangeMode.AutoApproach, out Vector3 cast);

        Assert.That(action, Is.EqualTo(GroundPointConfirmAction.Approach));
        Assert.That(cast, Is.EqualTo(point));
    }

    [Test]
    public void PreviewMarker_AutoApproach_FollowsMouseOutsideRange_ClampSnapsToBoundary()
    {
        Vector3 point = Origin + new Vector3(0f, 0f, 20f);

        Vector3 follow = GroundPointTargeting.PreviewMarkerPoint(Origin, point, 12f, GroundPointOutOfRangeMode.AutoApproach);
        Vector3 snapped = GroundPointTargeting.PreviewMarkerPoint(Origin, point, 12f, GroundPointOutOfRangeMode.Clamp);

        Assert.That(follow, Is.EqualTo(point));
        Assert.That(snapped.z, Is.EqualTo(Origin.z + 12f).Within(0.0001f));
    }

    [Test]
    public void ApproachStop_UsesCastRangeMinusBuffer()
    {
        const float castRange = 12f;
        float stop = castRange - GroundPointTargeting.RangeBuffer;

        Assert.That(GroundPointTargeting.HasReachedApproachStop(
            Origin, Origin + new Vector3(stop - 0.01f, 0f, 0f), castRange), Is.True);
        Assert.That(GroundPointTargeting.HasReachedApproachStop(
            Origin, Origin + new Vector3(stop + 0.01f, 0f, 0f), castRange), Is.False);
        // 사거리 경계 바로 안쪽은 즉시 시전 대상이지만 접근 정지 거리보다는 멀다
        Assert.That(GroundPointTargeting.HasReachedApproachStop(
            Origin, Origin + new Vector3(castRange - 0.1f, 0f, 0f), castRange), Is.False);
    }

    [Test]
    public void ServerApproval_ToleratesPositionErrorUpToBuffer()
    {
        const float castRange = 12f;

        Assert.That(GroundPointTargeting.IsApprovableRange(
            Origin, Origin + new Vector3(0f, 0f, castRange + GroundPointTargeting.RangeBuffer - 0.01f), castRange), Is.True);
        Assert.That(GroundPointTargeting.IsApprovableRange(
            Origin, Origin + new Vector3(0f, 0f, castRange + GroundPointTargeting.RangeBuffer + 0.01f), castRange), Is.False);
    }

    [Test]
    public void ApproachStop_IsInsideServerApproval()
    {
        // 접근이 멈춘 자리에서 보낸 지점은 서버가 반드시 승인해야 한다.
        const float castRange = 8f;
        Vector3 point = Origin + new Vector3(castRange - GroundPointTargeting.RangeBuffer - 0.001f, 0f, 0f);

        Assert.That(GroundPointTargeting.HasReachedApproachStop(Origin, point, castRange), Is.True);
        Assert.That(GroundPointTargeting.IsApprovableRange(Origin, point, castRange), Is.True);
    }

    [Test]
    public void HoverMarker_IsDistanceAheadOfFacing_OnCasterHeight()
    {
        Vector3 origin = new Vector3(2f, 1.5f, -3f);

        Vector3 marker = GroundPointTargeting.HoverMarkerPoint(origin, new Vector3(0f, 0.7f, 2f), 3f);

        Assert.That(marker.x, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(marker.y, Is.EqualTo(1.5f).Within(0.0001f));
        Assert.That(marker.z, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void HoverMarker_ZeroFacing_StaysOnCaster()
    {
        Assert.That(GroundPointTargeting.HoverMarkerPoint(Origin, Vector3.zero, 3f), Is.EqualTo(Origin));
    }

    [Test]
    public void SkillData_Defaults_KeepPreviousGroundPointBehaviour()
    {
        var data = ScriptableObject.CreateInstance<PlayerSkillData>();
        try
        {
            Assert.That(data.GroundPointOutOfRange, Is.EqualTo(GroundPointOutOfRangeMode.Clamp));
            Assert.That(data.GroundMarkerRadius, Is.EqualTo(0f));
            Assert.That(data.HoverMarkerDistance, Is.EqualTo(3f));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void GunnerLaserData_MarkerRadius_DefaultsToLaserRadius()
    {
        var data = ScriptableObject.CreateInstance<GunnerTrackingLaserData>();
        try
        {
            Assert.That(data.GroundMarkerRadius, Is.EqualTo(data.Radius));
            Assert.That(data.GroundMarkerRadius, Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }
}
