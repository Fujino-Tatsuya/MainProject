using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 구간 판정(<see cref="MapSceneAreaTracker"/> · <see cref="MapSceneRect"/> · <see cref="MapSceneCorridorGeometry"/>) —
/// 경계 안/밖, 회전된 사각형, 복도 계산, 히스테리시스, 복도 중간 지점 개인 1회, 파티 첫 진입 1회, 살아 있는 플레이어만.
/// 원본: Docs/design/tutorial-scenario.md §1.1·§3, Docs/tech/tutorial-director.md §6. 좌표는 테스트용 임의값.
/// </summary>
public sealed class MapSceneAreaTrackerTests
{
    const ulong A = 0, B = 1;
    const float Enter = 0.5f, Exit = 1f;

    // 존1 x 0~10 · 복도1 x 10~20(너비 z −2~2) · 존2 x 20~30. 중간 지점 = x 15~20.
    static MapSceneAreaTracker NewLine()
    {
        var t = new MapSceneAreaTracker(Enter, Exit);
        var z1 = new MapSceneRect(5f, 0f, 5f, 5f);
        var z2 = new MapSceneRect(25f, 0f, 5f, 5f);
        t.SetArea(MapSceneArea.Zone1, z1);
        t.SetArea(MapSceneArea.Zone2, z2);
        Assert.IsTrue(MapSceneCorridorGeometry.TryGetGap(z1, z2, out bool alongX, out float g0, out float g1, out _, out _));
        MapSceneCorridorGeometry.MakeCorridor(z1, z2, alongX, g0, g1, -2f, 2f, out MapSceneRect c, out MapSceneRect mid);
        t.SetArea(MapSceneArea.Corridor1, c);
        t.SetCorridorMid(1, mid);
        return t;
    }

    static void Walk(MapSceneAreaTracker t, ulong id, params float[] xs)
    {
        foreach (float x in xs) t.UpdatePlayer(id, x, 0f, PlayerLifeState.Alive);
    }

    // ─── 사각형 ──────────────────────────────────────────────────────────

    [Test]
    public void 사각형_경계_안과_밖()
    {
        var r = new MapSceneRect(0f, 0f, 2f, 5f);
        Assert.IsTrue(r.Contains(1.9f, 4.9f));
        Assert.IsTrue(r.Contains(-2f, -5f));
        Assert.IsFalse(r.Contains(2.1f, 0f));
        Assert.IsFalse(r.Contains(0f, -5.1f));
    }

    [Test]
    public void 여유_양수는_넓히고_음수는_좁힌다()
    {
        var r = new MapSceneRect(0f, 0f, 2f, 2f);
        Assert.IsTrue(r.Contains(2.8f, 0f, 1f));
        Assert.IsFalse(r.Contains(1.8f, 0f, -0.5f));
        Assert.IsTrue(r.Contains(1.4f, 0f, -0.5f));
    }

    [Test]
    public void 회전180_사각형은_중심_기준으로_같은_범위다()
    {
        // 스테이지 180° 회전 — 중심이 원점이 아닌 사각형(20×40)
        var r = new MapSceneRect(0.18f, -160.55f, 10f, 20f, 180f);
        Assert.IsTrue(r.Contains(0.18f + 9.9f, -160.55f - 19.9f));
        Assert.IsTrue(r.Contains(0.18f - 9.9f, -160.55f + 19.9f));
        Assert.IsFalse(r.Contains(0.18f + 10.2f, -160.55f));
        Assert.IsFalse(r.Contains(0.18f, -160.55f - 20.2f));
        r.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
        Assert.AreEqual(-9.82f, minX, 1e-3f);
        Assert.AreEqual(10.18f, maxX, 1e-3f);
        Assert.AreEqual(-180.55f, minZ, 1e-3f);
        Assert.AreEqual(-140.55f, maxZ, 1e-3f);
    }

    [Test]
    public void 회전90_사각형은_가로세로가_바뀐다()
    {
        var r = new MapSceneRect(0f, 0f, 2f, 5f, 90f);
        Assert.IsTrue(r.Contains(4.9f, 0f));
        Assert.IsFalse(r.Contains(0f, 4.9f));
        Assert.IsTrue(r.Contains(0f, 1.9f));
    }

    // ─── 복도 계산 ────────────────────────────────────────────────────────

    [Test]
    public void 복도는_두_존_사이_틈이고_중간지점은_다음_존_쪽_절반이다()
    {
        // 180° 회전된 스테이지처럼 다음 존이 −X 쪽에 있다(존1 x 19.8~39.8 → 존2 x −9.8~10.2)
        var z1 = new MapSceneRect(29.83f, -200.4f, 10f, 10f, 0f);
        var z2 = new MapSceneRect(0.18f, -200.4f, 10f, 10f, 0f);
        Assert.IsTrue(MapSceneCorridorGeometry.TryGetGap(z1, z2, out bool alongX, out float g0, out float g1, out float c0, out float c1));
        Assert.IsTrue(alongX);
        Assert.AreEqual(10.18f, g0, 1e-3f);
        Assert.AreEqual(19.83f, g1, 1e-3f);
        Assert.AreEqual(-210.4f, c0, 1e-3f);
        Assert.AreEqual(-190.4f, c1, 1e-3f);

        MapSceneCorridorGeometry.MakeCorridor(z1, z2, alongX, g0, g1, -202.4f, -198.4f, out MapSceneRect c, out MapSceneRect mid);
        Assert.AreEqual((10.18f + 19.83f) * 0.5f, c.CenterX, 1e-3f);
        Assert.AreEqual(-200.4f, c.CenterZ, 1e-3f);
        Assert.AreEqual(2f, c.HalfZ, 1e-3f);
        mid.GetWorldBounds(out float mMinX, out _, out float mMaxX, out _);
        Assert.AreEqual(10.18f, mMinX, 1e-3f);                      // 존2(−X) 쪽 끝
        Assert.AreEqual((10.18f + 19.83f) * 0.5f, mMaxX, 1e-3f);    // 복도 한가운데
    }

    [Test]
    public void Z축으로_이어진_존도_복도를_만든다()
    {
        var z2 = new MapSceneRect(0.18f, -200.4f, 10f, 10f);
        var z3 = new MapSceneRect(0.18f, -160.55f, 10f, 20f, 180f);
        Assert.IsTrue(MapSceneCorridorGeometry.TryGetGap(z2, z3, out bool alongX, out float g0, out float g1, out _, out _));
        Assert.IsFalse(alongX);
        Assert.AreEqual(-190.4f, g0, 1e-3f);
        Assert.AreEqual(-180.55f, g1, 1e-3f);
    }

    [Test]
    public void 겹치는_존은_복도가_없다()
    {
        var a = new MapSceneRect(0f, 0f, 5f, 5f);
        var b = new MapSceneRect(8f, 0f, 5f, 5f);
        Assert.IsFalse(MapSceneCorridorGeometry.TryGetGap(a, b, out _, out _, out _, out _, out _));
    }

    // ─── 현재 구간 · 히스테리시스 ──────────────────────────────────────────

    [Test]
    public void 구간_안이면_그_구간이다()
    {
        var t = NewLine();
        Walk(t, A, 5f);
        Assert.AreEqual(MapSceneArea.Zone1, t.GetCurrentArea(A));
        Walk(t, A, 12f);
        Assert.AreEqual(MapSceneArea.Corridor1, t.GetCurrentArea(A));
        Walk(t, A, 25f);
        Assert.AreEqual(MapSceneArea.Zone2, t.GetCurrentArea(A));
        Walk(t, A, 50f);
        Assert.AreEqual(MapSceneArea.None, t.GetCurrentArea(A));
    }

    [Test]
    public void 경계_근처_왕복은_깜빡이지_않는다()
    {
        var t = NewLine();
        int changes = 0;
        t.PlayerAreaChanged += (_, __, ___) => changes++;

        Walk(t, A, 5f);   // 존1 (None → Zone1 = 1회)
        for (int i = 0; i < 20; i++) Walk(t, A, 9.7f, 10.3f, 10.9f, 9.2f);   // 경계 10 ± 1 이내 왕복
        Assert.AreEqual(MapSceneArea.Zone1, t.GetCurrentArea(A));
        Assert.AreEqual(1, changes);

        Walk(t, A, 11.2f);   // 바깥 여유(1m)를 넘음 → 복도
        Assert.AreEqual(MapSceneArea.Corridor1, t.GetCurrentArea(A));
        for (int i = 0; i < 20; i++) Walk(t, A, 10.2f, 9.6f, 9.1f, 10.8f);   // 되돌아와도 존1 안쪽으로 1m 넘게 안 들어가면 복도 유지
        Assert.AreEqual(MapSceneArea.Corridor1, t.GetCurrentArea(A));
        Assert.AreEqual(2, changes);
    }

    [Test]
    public void None_에서는_안쪽_여유까지_들어와야_들어간다()
    {
        var t = NewLine();
        Walk(t, A, -0.3f);   // 존1 경계 0 의 바깥
        Assert.AreEqual(MapSceneArea.None, t.GetCurrentArea(A));
        Walk(t, A, 0.3f);    // 경계 안이지만 안쪽 여유(0.5) 전
        Assert.AreEqual(MapSceneArea.None, t.GetCurrentArea(A));
        Walk(t, A, 0.7f);
        Assert.AreEqual(MapSceneArea.Zone1, t.GetCurrentArea(A));
    }

    // ─── 복도 중간 지점 — 개인 1회 ─────────────────────────────────────────

    [Test]
    public void 복도_중간지점은_플레이어마다_한번만_낸다()
    {
        var t = NewLine();
        var log = new List<(ulong, int)>();
        t.CorridorMidReached += (id, c) => log.Add((id, c));

        Walk(t, A, 5f, 12f, 14f);          // 아직 중간(15) 전
        Assert.AreEqual(0, log.Count);
        Walk(t, A, 16f);                   // 15 + 안쪽 여유 0.5 지남
        Walk(t, A, 12f, 5f, 12f, 17f, 25f, 17f);   // 왕복해도 다시 안 냄
        Walk(t, B, 5f, 16f, 17f);          // 다른 플레이어는 따로 1번

        CollectionAssert.AreEqual(new[] { (A, 1), (B, 1) }, log);
        Assert.IsTrue(t.HasReachedCorridorMid(A, 1));
        Assert.IsFalse(t.HasReachedCorridorMid(A, 2));
    }

    [Test]
    public void 복도_앞쪽_절반은_중간지점이_아니다()
    {
        var t = NewLine();
        int n = 0;
        t.CorridorMidReached += (_, __) => n++;
        Walk(t, A, 5f, 11f, 13f, 15.3f);   // 15.3 = 중간 지점 경계 안쪽 여유 전
        Assert.AreEqual(0, n);
    }

    // ─── 파티 첫 진입 — 1회 ──────────────────────────────────────────────

    [Test]
    public void 존_첫_진입은_파티에서_한번만_낸다()
    {
        var t = NewLine();
        var log = new List<(MapSceneArea, ulong)>();
        t.PartyFirstEntered += (a, id) => log.Add((a, id));

        Walk(t, A, 5f);
        Walk(t, B, 5f);
        Walk(t, A, 12f, 25f);
        Walk(t, B, 12f, 25f);
        Walk(t, A, 12f, 25f);   // 나갔다 다시 들어와도 안 냄

        CollectionAssert.AreEqual(new[] { (MapSceneArea.Zone1, A), (MapSceneArea.Zone2, A) }, log);
        Assert.IsTrue(t.HasPartyEntered(MapSceneArea.Zone2));
        Assert.IsFalse(t.HasPartyEntered(MapSceneArea.Corridor1));   // 복도는 파티 첫 진입 대상 아님
    }

    // ─── 판정 대상 — 살아 있는 플레이어만 ─────────────────────────────────

    [TestCase(PlayerLifeState.DeadPresentation)]
    [TestCase(PlayerLifeState.Soul)]
    [TestCase(PlayerLifeState.PermanentDead)]
    public void 살아_있지_않으면_판정하지_않는다(PlayerLifeState state)
    {
        var t = NewLine();
        int mids = 0, entries = 0;
        t.CorridorMidReached += (_, __) => mids++;
        t.PartyFirstEntered += (_, __) => entries++;

        Walk(t, A, 5f);   // 존1 진입(1)
        t.UpdatePlayer(A, 17f, 0f, state);   // 중간 지점이지만 판정 제외
        t.UpdatePlayer(A, 25f, 0f, state);   // 존2 지만 판정 제외
        Assert.AreEqual(MapSceneArea.None, t.GetCurrentArea(A));
        Assert.AreEqual(0, mids);
        Assert.AreEqual(1, entries);
        Assert.IsFalse(t.HasPartyEntered(MapSceneArea.Zone2));

        Walk(t, A, 17f);   // 되살아나면 그 자리부터 판정
        Assert.AreEqual(1, mids);
        Assert.AreEqual(MapSceneArea.Corridor1, t.GetCurrentArea(A));
    }

    [Test]
    public void 살아_있는_동료만_첫_진입을_만든다()
    {
        var t = NewLine();
        var log = new List<(MapSceneArea, ulong)>();
        t.PartyFirstEntered += (a, id) => log.Add((a, id));

        t.UpdatePlayer(B, 25f, 0f, PlayerLifeState.Soul);   // 영혼 B 가 존2 에 먼저
        Walk(t, A, 25f);                                      // 살아 있는 A 가 들어와야 첫 진입
        CollectionAssert.AreEqual(new[] { (MapSceneArea.Zone2, A) }, log);
    }

    [Test]
    public void 이탈하면_현재_구간만_지우고_1회_기록은_남긴다()
    {
        var t = NewLine();
        Walk(t, A, 5f, 17f);
        t.RemovePlayer(A);
        Assert.AreEqual(MapSceneArea.None, t.GetCurrentArea(A));
        Assert.IsTrue(t.HasReachedCorridorMid(A, 1));
    }
}
