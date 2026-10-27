using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 맵 포드 착지 연출 순수 규칙(<see cref="PodArrivalRules"/>, 할 일 100 C3) — 진입 종류 판정·착지점·좌석 오프셋·도착 연출 끝(S0 조건)·착지 효과 시점.
/// </summary>
public sealed class PodArrivalRulesTests
{
    const float Eps = 1e-4f;

    static readonly Vector3[] Seats = { new Vector3(-0.6f, 0f, 0.45f), new Vector3(0.6f, 0f, 0.45f), new Vector3(0f, 0f, -0.55f) };

    // ─── 진입 종류 판정 ─────────────────────────────────────────────────

    [Test]
    public void EntryKind_DefaultIsDirect()
    {
        Assert.AreEqual(MapEntryKind.Direct, default(MapEntryKind), "기본값(Dev Boot·새 세션) = 연출 없음");
    }

    [Test]
    public void SkipReason_NullOnlyForLobbyWithEverything()
    {
        Assert.IsNull(PodArrivalRules.SkipReason(MapEntryKind.Lobby, hasSettings: true, hasPodPrefab: true, hasGameManager: true));
    }

    [TestCase(MapEntryKind.Direct)]
    [TestCase(MapEntryKind.Restart)]
    public void SkipReason_NonLobbyEntryNeverPlays(MapEntryKind kind)
    {
        Assert.IsNotNull(PodArrivalRules.SkipReason(kind, true, true, true), "Dev Boot·Alt+0 재시작은 지금과 같다(낙하 스폰)");
    }

    [Test]
    public void SkipReason_LobbyFallsBackWhenSetupMissing()
    {
        Assert.IsNotNull(PodArrivalRules.SkipReason(MapEntryKind.Lobby, hasSettings: false, hasPodPrefab: true, hasGameManager: true), "설정 없음");
        Assert.IsNotNull(PodArrivalRules.SkipReason(MapEntryKind.Lobby, hasSettings: true, hasPodPrefab: false, hasGameManager: true), "포드 프리팹 누락");
        Assert.IsNotNull(PodArrivalRules.SkipReason(MapEntryKind.Lobby, hasSettings: true, hasPodPrefab: true, hasGameManager: false), "로딩 걷힘을 못 듣는다");
    }

    // ─── 착지점 · 좌석 ───────────────────────────────────────────────────

    [Test]
    public void LandingPose_SlotPlusLocalOffset()
    {
        Quaternion yaw90 = Quaternion.Euler(0f, 90f, 0f);
        Pose landing = PodArrivalRules.LandingPose(new Vector3(10f, 0f, -5f), yaw90, new Vector3(0f, 0f, 2f));
        Assert.That(Vector3.Distance(new Vector3(12f, 0f, -5f), landing.position), Is.LessThan(Eps), "로컬 +Z 2m = 슬롯이 90° 돌면 월드 +X 2m");
        Assert.That(Quaternion.Angle(yaw90, landing.rotation), Is.LessThan(0.01f), "회전은 슬롯 그대로");
    }

    [Test]
    public void SeatOffset_WrapsAndEmptyIsZero()
    {
        Assert.AreEqual(Seats[0], PodArrivalRules.SeatOffset(0, Seats));
        Assert.AreEqual(Seats[2], PodArrivalRules.SeatOffset(2, Seats));
        Assert.AreEqual(Seats[0], PodArrivalRules.SeatOffset(3, Seats), "4번째 사람은 첫 좌석을 같이 쓴다");
        Assert.AreEqual(Vector3.zero, PodArrivalRules.SeatOffset(1, new Vector3[0]), "좌석이 없으면 착지점");
        Assert.AreEqual(Vector3.zero, PodArrivalRules.SeatOffset(1, null));
        Assert.AreEqual(Seats[0], PodArrivalRules.SeatOffset(-1, Seats), "음수 인덱스는 0");
    }

    [Test]
    public void SeatPose_RotatesWithLandingAndHasNoFallHeight()
    {
        var landing = new Pose(new Vector3(29.83f, 0f, -200.4f), Quaternion.Euler(0f, 180f, 0f));
        Pose seat = PodArrivalRules.SeatPose(landing, 1, Seats);
        Vector3 expected = landing.position + new Vector3(-0.6f, 0f, -0.45f); // (0.6, 0, 0.45) 를 180° 돌린 값
        Assert.That(Vector3.Distance(expected, seat.position), Is.LessThan(Eps));
        Assert.AreEqual(landing.position.y, seat.position.y, Eps, "spawn_Offset(10m) 낙하 없이 착지 높이에 선다");
        Assert.That(Quaternion.Angle(landing.rotation, seat.rotation), Is.LessThan(0.01f));
    }

    // ─── 도착 연출 끝(튜토리얼 S0 조건) ─────────────────────────────────

    [Test]
    public void ArrivalDone_NotSpawnedWaits()
    {
        Assert.IsFalse(PodArrivalRules.IsArrivalDone(spawned: false, armed: false, startAt: 0d, openedAt: 0d, now: 10d, releaseAfterOpen: 1.5f),
            "복제 값을 읽기 전엔 켜질지 몰라 기다린다");
    }

    [Test]
    public void ArrivalDone_NotArmedIsImmediatelyTrue()
    {
        Assert.IsTrue(PodArrivalRules.IsArrivalDone(spawned: true, armed: false, startAt: 0d, openedAt: 0d, now: 0d, releaseAfterOpen: 1.5f),
            "Lobby 가 아닌 진입(연출 꺼짐)은 즉시 참 — 지금 동작 유지");
    }

    [Test]
    public void ArrivalDone_ArmedWaitsForStartOpenThenRelease()
    {
        Assert.IsFalse(PodArrivalRules.IsArrivalDone(true, true, startAt: 0d, openedAt: 0d, now: 100d, releaseAfterOpen: 1.5f), "로딩이 걷히기 전");
        Assert.IsFalse(PodArrivalRules.IsArrivalDone(true, true, startAt: 100d, openedAt: 0d, now: 1000d, releaseAfterOpen: 1.5f), "열기 끝 이벤트 전(포드 클립 진행 중)");
        Assert.IsFalse(PodArrivalRules.IsArrivalDone(true, true, startAt: 100d, openedAt: 108d, now: 109.49d, releaseAfterOpen: 1.5f), "디졸브·레터박스 아웃 중");
        Assert.IsTrue(PodArrivalRules.IsArrivalDone(true, true, startAt: 100d, openedAt: 108d, now: 109.5d, releaseAfterOpen: 1.5f), "해제 = 레터박스가 다 걷힌 순간");
    }

    [Test]
    public void ArrivalDone_MatchesDefaultTimelineRelease()
    {
        var timeline = new PodDropMapTimeline(PodDropMapTimings.Default);
        const double start = 50d, opened = 58d;
        double openedElapsed = opened - start;
        Assert.IsFalse(PodArrivalRules.IsArrivalDone(true, true, start, opened, start + timeline.LetterboxOutAt(openedElapsed), timeline.ReleaseAfterOpenSeconds));
        Assert.IsTrue(PodArrivalRules.IsArrivalDone(true, true, start, opened, start + timeline.ReleaseAt(openedElapsed), timeline.ReleaseAfterOpenSeconds));
        Assert.IsTrue(timeline.IsReleasedAt(timeline.ReleaseAt(openedElapsed), openedElapsed), "시간표 해제와 같은 순간");
    }

    // ─── 착지 효과 시점 ────────────────────────────────────────────────

    [Test]
    public void ShouldPlayImpact_OnceInWindow()
    {
        Assert.IsFalse(PodArrivalRules.ShouldPlayImpact(6.49, impactAt: 6.5, dissolveAt: 8.0, alreadyPlayed: false), "착륙 전");
        Assert.IsTrue(PodArrivalRules.ShouldPlayImpact(6.5, 6.5, 8.0, false), "지면에 닿은 순간");
        Assert.IsFalse(PodArrivalRules.ShouldPlayImpact(7.0, 6.5, 8.0, true), "이미 냈다");
        Assert.IsFalse(PodArrivalRules.ShouldPlayImpact(8.0, 6.5, 8.0, false), "디졸브가 시작된 뒤 본 피어는 건너뛴다");
    }

    [Test]
    public void ShouldPlayImpact_EventsNotYet()
    {
        Assert.IsFalse(PodArrivalRules.ShouldPlayImpact(100.0, PodDropTimeline.NotYet, PodDropTimeline.NotYet, false), "펴기 이벤트 전 = 착륙 없음");
        Assert.IsTrue(PodArrivalRules.ShouldPlayImpact(6.6, 6.5, PodDropTimeline.NotYet, false), "열기 이벤트 전(착륙 직후)엔 낸다");
        Assert.IsTrue(PodArrivalRules.ShouldPlayImpact(6.95, impactAt: 6.9, dissolveAt: 6.9, alreadyPlayed: false), "착지 = 디졸브 시작이어도 한 번 낸다");
    }

    // ─── 설정 기본값 ───────────────────────────────────────────────────

    [Test]
    public void DefaultSeats_InsidePodFootprint()
    {
        PodDropMapTimings map = PodDropMapTimings.Default;
        Assert.AreEqual(3, map.seatOffsets.Length, "3인 좌석");
        foreach (Vector3 seat in map.seatOffsets)
        {
            Assert.That(Mathf.Abs(seat.x), Is.LessThan(2.35f / 2f), "포드 폭 안");
            Assert.That(Mathf.Abs(seat.z), Is.LessThan(2.69f / 2f), "포드 깊이 안");
        }
    }
}
