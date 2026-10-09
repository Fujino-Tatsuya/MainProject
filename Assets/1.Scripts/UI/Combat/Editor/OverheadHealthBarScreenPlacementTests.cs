using NUnit.Framework;
using UnityEngine;

/// <summary>머리 위 체력바 화면 배치 고정 — 월드 높이, Canvas 좌표 변환, 카메라 뒤·화면 밖 숨김.</summary>
public sealed class OverheadHealthBarScreenPlacementTests
{
    static readonly Vector2 Screen1080 = new Vector2(1920f, 1080f);
    static readonly Vector2 HalfBar = new Vector2(54f, 6.3f);

    [Test]
    public void AnchorWorld_AddsHeightAlongWorldUp()
    {
        Vector3 anchor = OverheadHealthBarScreenPlacement.AnchorWorld(new Vector3(3f, 1f, -2f), 1.84f);

        Assert.That(anchor.x, Is.EqualTo(3f));
        Assert.That(anchor.y, Is.EqualTo(2.84f).Within(1e-5f));
        Assert.That(anchor.z, Is.EqualTo(-2f));
    }

    [Test]
    public void InsideScreen_ReturnsScreenPointPlusOffset()
    {
        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(960f, 540f, 20f), Screen1080, 1f, new Vector2(0f, 80f), HalfBar, out Vector2 position);

        Assert.That(visible, Is.True);
        Assert.That(position, Is.EqualTo(new Vector2(960f, 620f)));
    }

    [Test]
    public void ScaleFactor_DividesPixelsIntoCanvasUnits_OffsetStaysInCanvasUnits()
    {
        // 4K(세로 2160) = scaleFactor 2 — 오프셋은 1080 기준 단위라 나누지 않는다.
        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(1920f, 1080f, 20f), new Vector2(3840f, 2160f), 2f, new Vector2(0f, 80f), HalfBar, out Vector2 position);

        Assert.That(visible, Is.True);
        Assert.That(position, Is.EqualTo(new Vector2(960f, 620f)));
    }

    [TestCase(0f)]
    [TestCase(-5f)]
    public void BehindOrOnCameraPlane_IsHidden(float z)
    {
        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(960f, 540f, z), Screen1080, 1f, Vector2.zero, HalfBar, out _);

        Assert.That(visible, Is.False, "카메라 뒤 투영은 화면 안 좌표로 뒤집혀 나오므로 숨긴다");
    }

    [TestCase(-60f, 540f)]
    [TestCase(1980f, 540f)]
    [TestCase(960f, -10f)]
    [TestCase(960f, 1090f)]
    public void FullyOutsideScreen_IsHidden(float x, float y)
    {
        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(x, y, 20f), Screen1080, 1f, Vector2.zero, HalfBar, out _);

        Assert.That(visible, Is.False);
    }

    [TestCase(-50f, 540f)]
    [TestCase(1970f, 540f)]
    [TestCase(960f, 1085f)]
    public void StraddlingScreenEdge_StaysVisible(float x, float y)
    {
        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(x, y, 20f), Screen1080, 1f, Vector2.zero, HalfBar, out _);

        Assert.That(visible, Is.True, "바가 화면에 조금이라도 걸치면 그린다");
    }

    [Test]
    public void OffsetCanPushAnchorBackOnScreen()
    {
        // 기준점은 화면 아래 밖이지만 위 오프셋으로 바가 화면 안에 들어온다.
        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(960f, -40f, 20f), Screen1080, 1f, new Vector2(0f, 80f), HalfBar, out Vector2 position);

        Assert.That(visible, Is.True);
        Assert.That(position.y, Is.EqualTo(40f));
    }

    [Test]
    public void NaNScreenPoint_IsHidden()
    {
        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(float.NaN, 540f, 20f), Screen1080, 1f, Vector2.zero, HalfBar, out _);

        Assert.That(visible, Is.False);
    }
}
