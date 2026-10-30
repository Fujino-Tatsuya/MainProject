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

    // ─── 개인 경로 + 합류 번호 (C2e) ─────────────────────────────────────

    static readonly Vector3 E1 = new Vector3(9f, 0f, 0f);
    static readonly Vector3 E2 = new Vector3(9f, 0f, 2f);

    [Test]
    public void BuildRoute_NoEntryJoinZero_SameAsSharedOnly()
    {
        var shared = new Vector3?[] { A, B, C };
        PodLaunchRules.WalkRoute legacy = PodLaunchRules.BuildRoute(shared, Door, Seat);
        PodLaunchRules.WalkRoute personal = PodLaunchRules.BuildRoute(new Vector3?[0], shared, 0, Door, Seat);
        CollectionAssert.AreEqual(legacy.Ground, personal.Ground, "개인 경로 비고 합류 0 = C2d 까지와 같다");
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, personal.Source);
        Assert.AreEqual(0, personal.EntryCount);
        Assert.AreEqual(0, personal.JoinIndex);
        Assert.IsFalse(personal.JoinClamped);
        Assert.AreEqual(legacy.Ground.Length, PodLaunchRules.BuildRoute(null, shared, 0, Door, Seat).Ground.Length, "개인 경로 리스트가 없어도 같다");
    }

    [Test]
    public void BuildRoute_EntryThenJoinMiddle()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { E1, E2 }, new Vector3?[] { A, B, C }, 1, Door, Seat);
        CollectionAssert.AreEqual(new[] { E1, E2, B, C }, route.Ground, "개인 경로 → 공용 [1..]");
        CollectionAssert.AreEqual(new[] { -1, -2, 1, 2 }, route.Source, "출처: 개인 −1·−2, 공용 칸 번호");
        Assert.AreEqual(2, route.EntryCount, "Ground[EntryCount] = 합류점");
        Assert.AreEqual(B, route.Ground[route.EntryCount]);
        CollectionAssert.AreEqual(new[] { Door, Seat }, route.Interior);
    }

    [Test]
    public void BuildRoute_JoinAtEnd_SkipsSharedAndGoesToThreshold()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { E1 }, new Vector3?[] { A, B, C }, 3, Door, Seat);
        CollectionAssert.AreEqual(new[] { E1 }, route.Ground, "합류 = 칸 수 → 공용 경로 없이 문턱으로");
        Assert.AreEqual(3, route.JoinIndex);
        Assert.IsFalse(route.JoinClamped, "칸 수는 범위 안");
        Assert.AreEqual(H(Vector3.zero, E1) + H(E1, Door) + H(Door, Seat), PodLaunchRules.WalkLength(Vector3.zero, route), Eps);
    }

    [Test]
    public void BuildRoute_JoinOutOfRange_ClampsAndFlags()
    {
        var shared = new Vector3?[] { A, B, C };
        PodLaunchRules.WalkRoute over = PodLaunchRules.BuildRoute(null, shared, 7, Door, Seat);
        Assert.AreEqual(3, over.JoinIndex, "넘치면 칸 수(= 공용 없이 문턱)");
        Assert.IsTrue(over.JoinClamped);
        Assert.AreEqual(0, over.Ground.Length);

        PodLaunchRules.WalkRoute under = PodLaunchRules.BuildRoute(null, shared, -2, Door, Seat);
        Assert.AreEqual(0, under.JoinIndex, "음수면 0(처음부터)");
        Assert.IsTrue(under.JoinClamped);
        CollectionAssert.AreEqual(new[] { A, B, C }, under.Ground);

        PodLaunchRules.WalkRoute empty = PodLaunchRules.BuildRoute(null, null, 1, Door, Seat);
        Assert.AreEqual(0, empty.JoinIndex, "공용 경로가 없으면 0 이 칸 수");
        Assert.IsTrue(empty.JoinClamped);
    }

    [Test]
    public void BuildRoute_NullEntriesSkippedAndCounted_OnlyUsedSharedCounted()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { null, E1, null }, new Vector3?[] { null, A, null, B }, 2, Door, Seat);
        CollectionAssert.AreEqual(new[] { E1, B }, route.Ground);
        CollectionAssert.AreEqual(new[] { -2, 3 }, route.Source, "개인 출처 번호는 빈 칸 포함 칸 번호(#2)");
        Assert.AreEqual(1, route.EntryCount);
        Assert.AreEqual(3, route.SkippedCount, "개인 빈 칸 2 + 합류 이후 공용 빈 칸 1(합류 앞 빈 칸은 안 센다)");

        PodLaunchRules.WalkRoute joinOnNull = PodLaunchRules.BuildRoute(null, new Vector3?[] { A, null, B }, 1, Door, Seat);
        CollectionAssert.AreEqual(new[] { B }, joinOnNull.Ground, "합류 칸이 비면 다음 점으로 합류");
    }

    [Test]
    public void WalkLength_FollowsPersonalRoute()
    {
        var shared = new Vector3?[] { A, B };
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { E1 }, shared, 1, Door, Seat);
        Vector3 start = new Vector3(10f, 0f, 0f);
        float ground = H(start, E1) + H(E1, B);
        Assert.AreEqual(ground + H(B, Door) + H(Door, Seat), PodLaunchRules.WalkLength(start, route), Eps, "시작 → 개인 → 공용[합류..] → 문턱 → 좌석");
        Assert.AreEqual(ground, PodLaunchRules.GroundLength(start, route), Eps);
    }

    // ─── 걸어가는 길이 (C2d → 172 — 보행 시간 = 좌석까지 수평 길이 ÷ 걷는 속도) ──────────────

    // 수평(XZ) 거리 — 테스트 쪽 독립 계산.
    static float H(Vector3 a, Vector3 b) => new Vector2(b.x - a.x, b.z - a.z).magnitude;

    [Test]
    public void WalkLength_StartThroughPathThresholdToSeat_Horizontal()
    {
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(new Vector3?[] { A, B, C }, Door, Seat);
        float ground = H(Vector3.zero, A) + H(A, B) + H(B, C);
        Assert.AreEqual(ground, PodLaunchRules.GroundLength(Vector3.zero, route), Eps, "앞 구간 = 마지막 경로점까지(수평)");
        Assert.AreEqual(H(C, Door), PodLaunchRules.ApproachLength(Vector3.zero, route), Eps, "문턱 구간(수평)");
        Assert.AreEqual(H(Door, Seat), PodLaunchRules.SlideLength(Vector3.zero, route), Eps, "미끄러짐(수평)");
        Assert.AreEqual(ground + H(C, Door) + H(Door, Seat), PodLaunchRules.WalkLength(Vector3.zero, route), Eps, "걷기 시작 → 경로점 → 문턱 → 좌석(수평)");
        Assert.IsTrue(route.HasThreshold);
    }

    [Test]
    public void WalkLength_IgnoresHeight()
    {
        // 같은 평면 경로에 높이만 다르게 — 계단·문턱·좌석 높이는 길이(= 시간)를 바꾸지 않는다.
        PodLaunchRules.WalkRoute flat = PodLaunchRules.BuildRoute(new Vector3?[] { new Vector3(3f, 0f, 0f), new Vector3(3f, 0f, 4f) }, new Vector3(5f, 0f, 4f), new Vector3(5f, 0f, 5f));
        PodLaunchRules.WalkRoute stairs = PodLaunchRules.BuildRoute(new Vector3?[] { new Vector3(3f, 2f, 0f), new Vector3(3f, -1f, 4f) }, new Vector3(5f, 1.5f, 4f), new Vector3(5f, 0.3f, 5f));
        Assert.AreEqual(3f + 4f + 2f + 1f, PodLaunchRules.WalkLength(Vector3.zero, flat), Eps);
        Assert.AreEqual(PodLaunchRules.WalkLength(Vector3.zero, flat), PodLaunchRules.WalkLength(Vector3.zero, stairs), Eps);
        Assert.AreEqual(PodLaunchRules.GroundLength(Vector3.zero, flat), PodLaunchRules.GroundLength(Vector3.zero, stairs), Eps);
        Assert.AreEqual(PodLaunchRules.SlideLength(Vector3.zero, flat), PodLaunchRules.SlideLength(Vector3.zero, stairs), Eps);
    }

    [Test]
    public void Walk_AllSegmentsSameHorizontalSpeed_TotalIsLengthOverSpeed()
    {
        // 할 일 172 — 앞 구간(WalkStepAlongPath, 충돌 켬) · 문턱 구간(PointAlong) · 미끄러짐(Lerp) 이 모두 같은 수평 속도이고,
        // 총 보행 시간 = 좌석까지 수평 길이 ÷ 걷는 속도(오너 LobbyPodLaunch 의 시간 나누기와 같은 계산). 구간 길이·높이 차를 일부러 다르게 둔다.
        const float speed = 4.5f;
        const float dt = 0.02f;
        Vector3 start = new Vector3(0f, 0f, 0f);
        var ground = new[] { new Vector3(2f, 0f, 0f), new Vector3(5.5f, 1.96f, 0f), new Vector3(6.6f, 1.96f, 0f) }; // 평지 → 계단(높이 1.96) → 평지, 일직선
        Vector3 door = new Vector3(7.34f, 2.3f, 0f);
        Vector3 seat = new Vector3(7.64f, 2.1f, 0.25f);
        PodLaunchRules.WalkRoute route = PodLaunchRules.BuildRoute(System.Array.ConvertAll(ground, p => (Vector3?)p), door, seat);

        float groundLength = PodLaunchRules.GroundLength(start, route);
        float arriveLength = groundLength + PodLaunchRules.ApproachLength(start, route);
        float slideLength = PodLaunchRules.SlideLength(start, route);
        float total = PodDropLobbyTimeline.WalkSecondsFor(PodLaunchRules.WalkLength(start, route), speed);
        Assert.AreEqual((H(start, ground[2]) + H(ground[2], door) + H(door, seat)) / speed, total, Eps, "총 보행 시간 = 수평 총 길이 ÷ 걷는 속도");

        float groundEnd = total * PodLaunchRules.GroundShare(groundLength, arriveLength - groundLength + slideLength);
        float arrive = total * PodLaunchRules.GroundShare(arriveLength, slideLength);

        // 구간별 배정 시간 = 구간 수평 길이 ÷ 같은 속도.
        Assert.AreEqual(groundLength / speed, groundEnd, Eps, "앞 구간");
        Assert.AreEqual(H(ground[2], door) / speed, arrive - groundEnd, Eps, "문턱 구간");
        Assert.AreEqual(H(door, seat) / speed, total - arrive, Eps, "미끄러짐");

        // 앞 구간 — 틱마다 수평 변위 ÷ dt (일직선이라 변위 = 경로 진행). 마지막 자투리 틱은 뺀다.
        Vector3 position = start;
        int index = 0;
        double now = 0d;
        while (groundEnd - now > dt)
        {
            Vector3 step = BossElevatorTimeline.WalkStepAlongPath(position, ground, ref index, groundEnd - now, dt);
            Assert.AreEqual(speed, step.magnitude / dt, 1e-3f, $"앞 구간 t={now:0.00} 수평 속도");
            position += step;
            now += dt;
        }

        // 문턱 구간 · 미끄러짐 — 진행률 → 점, 수평 변위 ÷ dt.
        AssertHorizontalSpeed(t => PodLaunchRules.PointAlong(ground[2], new[] { door }, t), arrive - groundEnd, dt, speed, "문턱 구간");
        AssertHorizontalSpeed(t => Vector3.Lerp(door, seat, t), total - arrive, dt, speed, "미끄러짐");
    }

    static void AssertHorizontalSpeed(System.Func<float, Vector3> pointAt, float duration, float dt, float speed, string label)
    {
        int ticks = Mathf.FloorToInt(duration / dt);
        Assert.Greater(ticks, 0, $"{label} — 틱이 하나는 있어야 한다");
        for (int i = 0; i < ticks; i++)
        {
            Vector3 a = pointAt(i * dt / duration);
            Vector3 b = pointAt((i + 1) * dt / duration);
            Assert.AreEqual(speed, H(a, b) / dt, 1e-3f, $"{label} {i}틱 수평 속도");
        }
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
        Assert.AreEqual(0f, PodLaunchRules.ApproachLength(Vector3.zero, noDoor), Eps, "문턱 없음 → 문턱 구간 0");
        Assert.AreEqual(1f + H(A, Seat), PodLaunchRules.WalkLength(Vector3.zero, noDoor), Eps, "문턱 없음 → 마지막 경로점 → 좌석");
        Assert.IsFalse(noDoor.HasThreshold);

        PodLaunchRules.WalkRoute noPath = PodLaunchRules.BuildRoute(new Vector3?[0], Door, Seat);
        Assert.AreEqual(H(Vector3.zero, Door) + H(Door, Seat), PodLaunchRules.WalkLength(Vector3.zero, noPath), Eps, "경로점 없음 → 곧바로 문턱 → 좌석");
        Assert.AreEqual(0f, PodLaunchRules.GroundLength(Vector3.zero, noPath), Eps);

        PodLaunchRules.WalkRoute nothing = PodLaunchRules.BuildRoute(new Vector3?[0], null, Seat);
        Assert.AreEqual(H(Vector3.zero, Seat), PodLaunchRules.WalkLength(Vector3.zero, nothing), Eps, "둘 다 없음 → 걷기 시작 지점 → 좌석 미끄러짐만");
        Assert.AreEqual(H(Vector3.zero, Seat), PodLaunchRules.SlideLength(Vector3.zero, nothing), Eps);
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
    public void PathLengthHorizontal_IgnoresHeight()
    {
        var path = new[] { new Vector3(3f, 4f, 0f), new Vector3(3f, 4f, 2f) };
        Assert.AreEqual(3f + 2f, PodLaunchRules.PathLengthHorizontal(Vector3.zero, path), Eps);
    }

    [Test]
    public void PointAlong_HorizontalProgressWithHeight()
    {
        // 높이가 다른 두 구간 — 진행률은 수평 길이 기준(1 + 3 = 4m), 높이는 구간 안에서 같이 보간.
        var path = new[] { new Vector3(1f, 5f, 0f), new Vector3(4f, 5f, 0f) };
        Assert.AreEqual(new Vector3(1f, 5f, 0f), PodLaunchRules.PointAlong(Vector3.zero, path, 0.25f), "수평 1m = 첫 구간 끝(3차원 길이였다면 못 닿는다)");
        Assert.AreEqual(new Vector3(2.5f, 5f, 0f), PodLaunchRules.PointAlong(Vector3.zero, path, 0.625f));
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
