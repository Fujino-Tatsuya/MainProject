using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 로비 포드 탑승 연출 순수 규칙(<see cref="PodLaunchRules"/>, 할 일 100 C2) — 참가 명단 순서·좌석 돌려 쓰기·폴백 판단·걸어가는 길이·보행 시간 나누기·충돌 없는 보간.
/// </summary>
public sealed class PodLaunchRulesTests
{
    const float Eps = 1e-4f;

    // ─── 명단 · 좌석 ─────────────────────────────────────────────────────

    [Test]
    public void OrderParticipants_ClientIdAscending()
    {
        var ids = new List<ulong> { 5, 0, 2 };
        PodLaunchRules.OrderParticipants(ids);
        CollectionAssert.AreEqual(new ulong[] { 0, 2, 5 }, ids, "접속 순서와 무관하게 clientId 오름차순 = 보행 슬롯");
    }

    [Test]
    public void PointIndex_WrapsWhenMorePlayersThanPoints()
    {
        Assert.AreEqual(0, PodLaunchRules.PointIndex(0, 3));
        Assert.AreEqual(2, PodLaunchRules.PointIndex(2, 3));
        Assert.AreEqual(0, PodLaunchRules.PointIndex(3, 3), "4번째 사람은 첫 지점을 같이 쓴다(좌석 = 숨김 지점, 걷기 시작 = 출발 간격)");
        Assert.AreEqual(1, PodLaunchRules.PointIndex(3, 2), "걷기 시작 지점 2개에 4번째 = 둘째");
        Assert.AreEqual(0, PodLaunchRules.PointIndex(2, 1), "지점 1개 = 전원 같은 지점");
        Assert.AreEqual(0, PodLaunchRules.PointIndex(-1, 3), "음수 슬롯 = 첫 지점");
        Assert.AreEqual(-1, PodLaunchRules.PointIndex(0, 0), "지점 없음");
    }

    // ─── 폴백 ───────────────────────────────────────────────────────────

    [Test]
    public void MissingSetup_NullWhenReady()
    {
        Assert.IsNull(PodLaunchRules.MissingSetup(hasSettings: true, seatCount: 3, walkStartCount: 3, participantCount: 1));
        Assert.IsNull(PodLaunchRules.MissingSetup(true, 3, 1, 3), "걷기 시작 지점이 사람보다 적어도 돌려 쓴다");
    }

    [Test]
    public void MissingSetup_FallsBackWithoutSettingsSeatsWalkStartsOrPlayers()
    {
        Assert.IsNotNull(PodLaunchRules.MissingSetup(false, 3, 3, 2), "설정 없음 → 곧바로 로딩");
        Assert.IsNotNull(PodLaunchRules.MissingSetup(true, 0, 3, 2), "좌석 없음 → 곧바로 로딩");
        Assert.IsNotNull(PodLaunchRules.MissingSetup(true, 3, 0, 2), "걷기 시작 지점 없음 → 곧바로 로딩(지금 자리 출발 폴백 없음)");
        Assert.IsNotNull(PodLaunchRules.MissingSetup(true, 3, 3, 0), "참가자 없음 → 곧바로 로딩");
    }

    // ─── 보행 경로(공용 경로점 리스트 · 문턱 · 좌석) ─────────────────────────

    static readonly Vector3 A = new Vector3(1f, 0f, 0f);
    static readonly Vector3 B = new Vector3(2f, 0.5f, 0f);
    static readonly Vector3 C = new Vector3(2f, 1f, 3f);
    static readonly Vector3 Door = new Vector3(5f, 1f, 3f);
    static readonly Vector3 Seat = new Vector3(6f, 1.2f, 3f);

    [Test]
    public void BuildRoute_KeepsListOrderThenThresholdThenSeat()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { C, A, B }, Door, Seat);
        CollectionAssert.AreEqual(new[] { C, A, B }, route.Ground, "리스트 순서 그대로 — 이름·좌표 순서와 무관");
        CollectionAssert.AreEqual(new[] { Door, Seat }, route.Interior, "문턱 → 좌석은 충돌 없이 보간");
        Assert.AreEqual(0, route.SkippedCount);
    }

    [Test]
    public void BuildRoute_SkipsNullEntriesAndCountsThem()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { null, A, null, B }, Door, Seat);
        CollectionAssert.AreEqual(new[] { A, B }, route.Ground);
        Assert.AreEqual(2, route.SkippedCount, "빈 칸 수 = 경고에 쓴다");
    }

    [Test]
    public void BuildRoute_EmptyPathGoesStraightToThreshold()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[0], Door, Seat);
        Assert.AreEqual(0, route.Ground.Length, "공용 경로 없음 → 걷기 시작 지점에서 곧바로 문턱");
        CollectionAssert.AreEqual(new[] { Door, Seat }, route.Interior);

        PodLaunchRules.WalkRoute allNull = PodLaunchRules.BuildRoute(new Vector3?[] { null }, Door, Seat);
        Assert.AreEqual(0, allNull.Ground.Length);
        Assert.AreEqual(1, allNull.SkippedCount);

        Assert.AreEqual(0, PodLaunchRules.BuildRoute(null, Door, Seat).Ground.Length, "리스트 자체가 없어도 같다");
    }

    [Test]
    public void BuildRoute_NoThresholdEndsAtSeat()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { A }, null, Seat);
        CollectionAssert.AreEqual(new[] { Seat }, route.Interior, "문턱 없음 → 마지막 경로점에서 곧바로 좌석");
    }

    // ─── 걸어가는 길이 (C2d — 보행 시간 = 이 길이 ÷ 걷는 속도) ──────────────

    [Test]
    public void WalkLength_StartThroughPathToThreshold_ExcludesSeat()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { A, B, C }, Door, Seat);
        float expected = Vector3.Distance(Vector3.zero, A) + Vector3.Distance(A, B) + Vector3.Distance(B, C) + Vector3.Distance(C, Door);
        Assert.AreEqual(expected, PodLaunchRules.WalkLength(Vector3.zero, route), Eps, "걷기 시작 → 경로점 → 문턱(3차원), 좌석 제외");
        Assert.AreEqual(expected - Vector3.Distance(C, Door), PodLaunchRules.GroundLength(Vector3.zero, route), Eps, "앞 구간 = 마지막 경로점까지");
        Assert.IsTrue(route.HasThreshold);
    }

    [Test]
    public void WalkLength_DiffersPerWalkStart()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { A }, Door, Seat);
        float near = PodLaunchRules.WalkLength(new Vector3(0f, 0f, 0f), route);
        float far = PodLaunchRules.WalkLength(new Vector3(-3f, 0f, 0f), route);
        Assert.AreEqual(3f, far - near, Eps, "걷기 시작 지점만 다르면 첫 합류 구간만큼 차이");
    }

    [Test]
    public void WalkLength_NoThresholdOrPath()
    {
        PodLaunchRules.WalkRoute noDoor = PodLaunchRules.BuildRoute(new Vector3?[] { A }, null, Seat);
        Assert.AreEqual(1f, PodLaunchRules.WalkLength(Vector3.zero, noDoor), Eps, "문턱 없음 → 마지막 경로점까지");
        Assert.IsFalse(noDoor.HasThreshold);

        PodLaunchRules.WalkRoute noPath = PodLaunchRules.BuildRoute(new Vector3?[0], Door, Seat);
        Assert.AreEqual(Vector3.Distance(Vector3.zero, Door), PodLaunchRules.WalkLength(Vector3.zero, noPath), Eps, "경로점 없음 → 곧바로 문턱");
        Assert.AreEqual(0f, PodLaunchRules.GroundLength(Vector3.zero, noPath), Eps);

        PodLaunchRules.WalkRoute nothing = PodLaunchRules.BuildRoute(new Vector3?[0], null, Seat);
        Assert.AreEqual(0f, PodLaunchRules.WalkLength(Vector3.zero, nothing), Eps, "둘 다 없음 → 0(미끄러짐만)");
    }

    // ─── 보행 시간 나누기 ────────────────────────────────────────────────

    [Test]
    public void GroundShare_ProportionalToLength()
    {
        Assert.AreEqual(0.75f, PodLaunchRules.GroundShare(6f, 2f), Eps, "걷는 속도가 일정 — 길이 비율");
        Assert.AreEqual(1f, PodLaunchRules.GroundShare(5f, 0f), Eps, "보간 구간 없음 → 전부 걷기");
        Assert.AreEqual(0f, PodLaunchRules.GroundShare(0f, 2f), Eps, "이미 입구 → 전부 보간");
        Assert.AreEqual(1f, PodLaunchRules.GroundShare(0f, 0f), Eps, "거리 0 → 1(나눌 게 없다)");
    }

    // ─── 충돌 없는 보간 ──────────────────────────────────────────────────

    [Test]
    public void PathLength3D_CountsHeight()
    {
        var path = new[] { new Vector3(3f, 4f, 0f), new Vector3(3f, 4f, 2f) };
        Assert.AreEqual(5f + 2f, PodLaunchRules.PathLength3D(Vector3.zero, path), Eps);
    }

    [Test]
    public void PointAlong_EndsAndCorner()
    {
        var path = new[] { new Vector3(2f, 0f, 0f), new Vector3(2f, 0f, 2f) };
        Assert.AreEqual(Vector3.zero, PodLaunchRules.PointAlong(Vector3.zero, path, 0f));
        Assert.AreEqual(new Vector3(2f, 0f, 0f), PodLaunchRules.PointAlong(Vector3.zero, path, 0.5f), "길이 절반 = 꺾이는 점");
        Assert.AreEqual(new Vector3(2f, 0f, 1f), PodLaunchRules.PointAlong(Vector3.zero, path, 0.75f));
        Assert.AreEqual(new Vector3(2f, 0f, 2f), PodLaunchRules.PointAlong(Vector3.zero, path, 1f), "끝 = 좌석");
        Assert.AreEqual(new Vector3(2f, 0f, 2f), PodLaunchRules.PointAlong(Vector3.zero, path, 3f), "1 넘으면 끝에 머문다");
    }

    [Test]
    public void PointAlong_EmptyPathStaysAtStart()
    {
        Vector3 from = new Vector3(1f, 2f, 3f);
        Assert.AreEqual(from, PodLaunchRules.PointAlong(from, new Vector3[0], 0.5f));
    }

    [Test]
    public void PointAlong_DuplicatePointsDoNotDivideByZero()
    {
        var path = new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 4f) };
        Assert.AreEqual(new Vector3(0f, 0f, 2f), PodLaunchRules.PointAlong(Vector3.zero, path, 0.5f));
    }
}
