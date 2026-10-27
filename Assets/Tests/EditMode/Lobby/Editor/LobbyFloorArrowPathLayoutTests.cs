using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>경로형 바닥 화살표 배치 — 할 일 111 후속.</summary>
public sealed class LobbyFloorArrowPathLayoutTests
{
    private static LobbyFloorArrowPathSettings Settings(int triangleEvery = 4) => new LobbyFloorArrowPathSettings
    {
        Spacing = 0.8f,
        DashLength = 0.45f,
        DashWidth = 0.2f,
        SmallTriangleLength = 0.31f,
        SmallTriangleWidth = 0.35f,
        EndTriangleLength = 0.8f,
        TriangleEvery = triangleEvery,
    };

    private static List<LobbyFloorArrowPiece> Layout(LobbyFloorArrowPathSettings s, params Vector3[] points)
    {
        var output = new List<LobbyFloorArrowPiece>();
        LobbyFloorArrowPathLayout.Layout(points, s, output);
        return output;
    }

    [Test]
    public void 경로_길이는_구간_합()
    {
        var points = new[] { Vector3.zero, new Vector3(0, 0, 4), new Vector3(3, 0, 4) };
        Assert.That(LobbyFloorArrowPathLayout.PathLength(points), Is.EqualTo(7f).Within(1e-4f));
    }

    [Test]
    public void 직선은_균일_간격이고_끝에_큰_삼각형()
    {
        var s = Settings(0);
        var pieces = Layout(s, Vector3.zero, new Vector3(0, 0, 10));
        var end = pieces.Last();
        var body = pieces.Take(pieces.Count - 1).ToList();

        Assert.That(end.Kind, Is.EqualTo(LobbyFloorArrowPieceKind.EndTriangle));
        Assert.That(end.Position, Is.EqualTo(new Vector3(0, 0, 10)));
        Assert.That(end.Distance, Is.EqualTo(10f).Within(1e-4f));
        Assert.That(body.Count, Is.GreaterThan(5));
        Assert.That(body.All(p => p.Kind == LobbyFloorArrowPieceKind.Dash), Is.True);
        Assert.That(body[0].Distance, Is.EqualTo(LobbyFloorArrowPathLayout.StartPad(s)).Within(1e-4f));
        Assert.That(body.Last().Distance, Is.EqualTo(10f - LobbyFloorArrowPathLayout.EndPad(s)).Within(1e-4f));

        var step = body[1].Distance - body[0].Distance;
        for (var i = 1; i < body.Count; i++)
            Assert.That(body[i].Distance - body[i - 1].Distance, Is.EqualTo(step).Within(1e-4f));
        Assert.That(step, Is.EqualTo(s.Spacing).Within(s.Spacing * 0.25f));
    }

    [Test]
    public void 직각_꺾임에선_조각이_모서리를_넘지_않고_구간_방향을_따른다()
    {
        var s = Settings();
        var corner = new Vector3(0, 0, 5);
        var pieces = Layout(s, Vector3.zero, corner, new Vector3(4, 0, 5));
        var pad = LobbyFloorArrowPathLayout.CornerPad(s);

        foreach (var p in pieces.Where(p => p.Kind != LobbyFloorArrowPieceKind.EndTriangle))
        {
            Assert.That(Vector3.Distance(p.Position, corner), Is.GreaterThanOrEqualTo(pad - 1e-4f));
            var expected = p.Distance < 5f ? Vector3.forward : Vector3.right;
            Assert.That(Vector3.Dot(p.Direction, expected), Is.EqualTo(1f).Within(1e-4f));
        }

        Assert.That(pieces.Any(p => p.Direction == Vector3.forward), Is.True);
        Assert.That(pieces.Any(p => p.Direction == Vector3.right), Is.True);
        // 여백 = 직각에서 양쪽 조각 사각형이 안 겹치는 최소값 이상.
        Assert.That(pad, Is.GreaterThanOrEqualTo((Mathf.Max(s.DashLength, s.SmallTriangleLength) + Mathf.Max(s.DashWidth, s.SmallTriangleWidth)) * 0.5f));
    }

    [Test]
    public void 삼각형은_경로_전체_순서로_N_번째마다()
    {
        var pieces = Layout(Settings(3), Vector3.zero, new Vector3(0, 0, 6), new Vector3(6, 0, 6));
        var body = pieces.Where(p => p.Kind != LobbyFloorArrowPieceKind.EndTriangle).ToList();

        for (var i = 0; i < body.Count; i++)
        {
            var expected = (i + 1) % 3 == 0 ? LobbyFloorArrowPieceKind.SmallTriangle : LobbyFloorArrowPieceKind.Dash;
            Assert.That(body[i].Kind, Is.EqualTo(expected), $"index {i}");
        }

        Assert.That(LobbyFloorArrowPathLayout.IsSmallTriangle(5, 0), Is.False);
    }

    [Test]
    public void 점이_모자라거나_겹치면_빈_결과_또는_건너뜀()
    {
        Assert.That(Layout(Settings(), Vector3.zero), Is.Empty);
        Assert.That(Layout(Settings(), Vector3.zero, Vector3.zero), Is.Empty);

        var withDuplicate = Layout(Settings(0), Vector3.zero, new Vector3(0, 0, 5), new Vector3(0, 0, 5));
        var plain = Layout(Settings(0), Vector3.zero, new Vector3(0, 0, 5));
        Assert.That(withDuplicate.Count, Is.EqualTo(plain.Count));
        Assert.That(withDuplicate.Last().Direction, Is.EqualTo(Vector3.forward));
    }

    [Test]
    public void 구간이_여백보다_짧으면_그_구간엔_조각이_없다()
    {
        var pieces = Layout(Settings(0), Vector3.zero, new Vector3(0, 0, 0.3f), new Vector3(5, 0, 0.3f));
        Assert.That(pieces.Where(p => p.Kind == LobbyFloorArrowPieceKind.Dash).All(p => p.Direction == Vector3.right), Is.True);
        Assert.That(LobbyFloorArrowPathLayout.SegmentPieceCount(-0.1f, 0.8f), Is.EqualTo(0));
        Assert.That(LobbyFloorArrowPathLayout.SegmentPieceCount(0f, 0.8f), Is.EqualTo(1));
        Assert.That(LobbyFloorArrowPathLayout.SegmentPieceCount(3.2f, 0.8f), Is.EqualTo(5));
    }

    [Test]
    public void 회전_점선은_U가_삼각형은_V_꼭짓점이_진행_방향이고_면은_위()
    {
        var dir = new Vector3(1, 0, 1).normalized;
        var dash = LobbyFloorArrowPathLayout.PieceRotation(LobbyFloorArrowPieceKind.Dash, dir);
        var tri = LobbyFloorArrowPathLayout.PieceRotation(LobbyFloorArrowPieceKind.SmallTriangle, dir);

        Assert.That(Vector3.Dot(dash * Vector3.right, dir), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(Vector3.Dot(tri * Vector3.up, dir), Is.EqualTo(1f).Within(1e-4f));
        // 내장 Quad 앞면 = -Z.
        Assert.That(Vector3.Dot(dash * Vector3.back, Vector3.up), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(Vector3.Dot(tri * Vector3.back, Vector3.up), Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void 텍스처_영역_비율()
    {
        var rect = new Rect(726f / 1254f, 540f / 1254f, 211f / 1254f, 188f / 1254f);
        Assert.That(LobbyFloorArrowPathLayout.RectAspect(rect, 1254, 1254), Is.EqualTo(188f / 211f).Within(1e-4f));
        Assert.That(LobbyFloorArrowPathLayout.PieceScale(LobbyFloorArrowPieceKind.Dash, 0.5f, 0.2f), Is.EqualTo(new Vector3(0.5f, 0.2f, 1f)));
        Assert.That(LobbyFloorArrowPathLayout.PieceScale(LobbyFloorArrowPieceKind.EndTriangle, 0.8f, 0.9f), Is.EqualTo(new Vector3(0.9f, 0.8f, 1f)));
    }
}
