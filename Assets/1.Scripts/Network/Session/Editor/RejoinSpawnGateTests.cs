using NUnit.Framework;

/// <summary>
/// 판 도중 재합류 스폰 보류 판정(C14 §3.4 표) — 입력 하나씩 켜서 Ready/Hold(사유)/Drop(사유)와 우선순위를 본다.
/// </summary>
public sealed class RejoinSpawnGateTests
{
    // 기본 = 스폰해도 되는 상태. 테스트마다 한 값만 바꾼다.
    static RejoinSpawnSnapshot Snap(
        bool connected = true,
        bool synchronized = true,
        bool mapReady = true,
        bool resultReserved = false,
        bool hasPlayerObject = false,
        bool elevatorOrTeleport = false,
        bool bossCinematic = false,
        bool slowMotion = false,
        bool wipePending = false)
    {
        return new RejoinSpawnSnapshot(
            connected, synchronized, mapReady, resultReserved, hasPlayerObject,
            elevatorOrTeleport, bossCinematic, slowMotion, wipePending);
    }

    static void AssertVerdict(in RejoinSpawnSnapshot snapshot, RejoinSpawnDecision decision, RejoinSpawnReason reason)
    {
        var verdict = RejoinSpawnGate.Evaluate(snapshot);
        Assert.That(verdict.Decision, Is.EqualTo(decision), verdict.ToString());
        Assert.That(verdict.Reason, Is.EqualTo(reason), verdict.ToString());
    }

    [Test]
    public void 막는_것이_없으면_Ready()
    {
        AssertVerdict(Snap(), RejoinSpawnDecision.Ready, RejoinSpawnReason.None);
    }

    [Test]
    public void 대기_중_퇴장은_Drop()
    {
        AssertVerdict(Snap(connected: false), RejoinSpawnDecision.Drop, RejoinSpawnReason.NotConnected);
    }

    [Test]
    public void 결과_전환_예약은_Drop_로비_전환이_데려간다()
    {
        AssertVerdict(Snap(resultReserved: true), RejoinSpawnDecision.Drop, RejoinSpawnReason.ResultTransitionReserved);
    }

    [Test]
    public void 이미_PlayerObject_가_있으면_Drop_중복_방지()
    {
        AssertVerdict(Snap(hasPlayerObject: true), RejoinSpawnDecision.Drop, RejoinSpawnReason.AlreadyHasPlayerObject);
    }

    [Test]
    public void 동기화_전은_Hold()
    {
        AssertVerdict(Snap(synchronized: false), RejoinSpawnDecision.Hold, RejoinSpawnReason.NotSynchronized);
    }

    [Test]
    public void 맵_준비_전은_Hold()
    {
        AssertVerdict(Snap(mapReady: false), RejoinSpawnDecision.Hold, RejoinSpawnReason.MapNotReady);
    }

    [Test]
    public void 엘리베이터_텔레포트_중은_Hold()
    {
        AssertVerdict(Snap(elevatorOrTeleport: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.ElevatorOrTeleportBusy);
    }

    [Test]
    public void 보스_연출_중은_Hold()
    {
        AssertVerdict(Snap(bossCinematic: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.BossCinematic);
    }

    [Test]
    public void 슬로모션_중은_Hold()
    {
        AssertVerdict(Snap(slowMotion: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.SlowMotion);
    }

    [Test]
    public void 전멸_유예_중은_Hold()
    {
        AssertVerdict(Snap(wipePending: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.WipePending);
    }

    [Test]
    public void Drop_이_Hold_보다_먼저다()
    {
        var everythingHolds = Snap(synchronized: false, mapReady: false, elevatorOrTeleport: true, bossCinematic: true, slowMotion: true, wipePending: true);
        Assert.That(RejoinSpawnGate.Evaluate(everythingHolds).Reason, Is.EqualTo(RejoinSpawnReason.NotSynchronized));

        AssertVerdict(
            Snap(connected: false, resultReserved: true, hasPlayerObject: true, mapReady: false),
            RejoinSpawnDecision.Drop, RejoinSpawnReason.NotConnected);
        AssertVerdict(
            Snap(resultReserved: true, hasPlayerObject: true, mapReady: false),
            RejoinSpawnDecision.Drop, RejoinSpawnReason.ResultTransitionReserved);
        AssertVerdict(
            Snap(hasPlayerObject: true, mapReady: false, slowMotion: true),
            RejoinSpawnDecision.Drop, RejoinSpawnReason.AlreadyHasPlayerObject);
    }

    [Test]
    public void Hold_사유는_표_순서대로_하나만()
    {
        AssertVerdict(Snap(mapReady: false, elevatorOrTeleport: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.MapNotReady);
        AssertVerdict(Snap(elevatorOrTeleport: true, bossCinematic: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.ElevatorOrTeleportBusy);
        AssertVerdict(Snap(bossCinematic: true, slowMotion: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.BossCinematic);
        AssertVerdict(Snap(slowMotion: true, wipePending: true), RejoinSpawnDecision.Hold, RejoinSpawnReason.SlowMotion);
    }

    [Test]
    public void 같은_입력이면_같은_판정이다_사유_변경_로그_비교용()
    {
        Assert.That(RejoinSpawnGate.Evaluate(Snap(slowMotion: true)), Is.EqualTo(RejoinSpawnGate.Evaluate(Snap(slowMotion: true))));
        Assert.That(RejoinSpawnGate.Evaluate(Snap(slowMotion: true)), Is.Not.EqualTo(RejoinSpawnGate.Evaluate(Snap(wipePending: true))));
    }
}
