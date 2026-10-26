using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 로비 포드 탑승 연출 순수 규칙(<see cref="PodLaunchRules"/>, 할 일 100 C2) — 참가 명단 순서·좌석 돌려 쓰기·폴백 판단·보행 시간 나누기·충돌 없는 보간.
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
    public void SeatIndex_WrapsWhenMorePlayersThanSeats()
    {
        Assert.AreEqual(0, PodLaunchRules.SeatIndex(0, 3));
        Assert.AreEqual(2, PodLaunchRules.SeatIndex(2, 3));
        Assert.AreEqual(0, PodLaunchRules.SeatIndex(3, 3), "4번째 사람은 첫 좌석을 같이 쓴다(숨김 지점)");
        Assert.AreEqual(-1, PodLaunchRules.SeatIndex(0, 0), "좌석 없음");
    }

    // ─── 폴백 ───────────────────────────────────────────────────────────

    [Test]
    public void MissingSetup_NullWhenReady()
    {
        Assert.IsNull(PodLaunchRules.MissingSetup(hasSettings: true, seatCount: 3, participantCount: 1));
    }

    [Test]
    public void MissingSetup_FallsBackWithoutSettingsSeatsOrPlayers()
    {
        Assert.IsNotNull(PodLaunchRules.MissingSetup(false, 3, 2), "설정 없음 → 곧바로 로딩");
        Assert.IsNotNull(PodLaunchRules.MissingSetup(true, 0, 2), "좌석 없음 → 곧바로 로딩");
        Assert.IsNotNull(PodLaunchRules.MissingSetup(true, 3, 0), "참가자 없음 → 곧바로 로딩");
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
