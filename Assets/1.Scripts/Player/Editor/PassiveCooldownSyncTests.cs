using NUnit.Framework;

/// <summary>
/// 패시브 내부 쿨 쿨 시계 이관(할 일 91 T4) — 가붕이(팔라딘) 불굴의 의지.
/// 서버는 남은 초 + 서버 시각을 복제하고, 각 피어는 원점이 다른 자기 쿨 시계로 환산한다.
/// </summary>
public sealed class PassiveCooldownSyncTests
{
    private const float Cooldown = 30f;

    [Test]
    public void ServerAndPeer_WithDifferentClockOrigins_SeeSameRemaining()
    {
        var server = new CooldownClock();
        var peer = new CooldownClock();
        server.AdvanceTo(500.0, 1f);
        peer.AdvanceTo(7.0, 1f);
        server.AdvanceTo(510.0, 1f);  // 서버 시계 10
        peer.AdvanceTo(18.0, 1f);     // 피어 시계 11 — 원점이 서버와 다르다

        double readyAt = server.Now + Cooldown;
        PassiveCooldownStamp stamp = PassiveCooldownSync.Stamp(readyAt, server.Now, serverTime: 1234.0);
        Assert.That(stamp.remaining, Is.EqualTo(Cooldown).Within(1e-5f));

        double peerReadyAt = PassiveCooldownSync.LocalReadyAt(stamp, peer.Now, serverTimeNow: 1234.0, rate: 1f);
        Assert.That(PassiveCooldownSync.Remaining(peerReadyAt, peer.Now), Is.EqualTo(Cooldown).Within(1e-5f));
    }

    [Test]
    public void LateJoin_SubtractsTimeSinceStamp()
    {
        PassiveCooldownStamp stamp = PassiveCooldownSync.Stamp(readyAt: 130.0, clockNow: 100.0, serverTime: 50.0);

        // 12초 뒤 들어온 피어: 30 − 12 = 18.
        double readyAt = PassiveCooldownSync.LocalReadyAt(stamp, clockNow: 3.0, serverTimeNow: 62.0, rate: 1f);
        Assert.That(PassiveCooldownSync.Remaining(readyAt, 3.0), Is.EqualTo(18f).Within(1e-5f));

        // 배율 2 면 흐른 12초가 쿨 24초.
        readyAt = PassiveCooldownSync.LocalReadyAt(stamp, clockNow: 3.0, serverTimeNow: 62.0, rate: 2f);
        Assert.That(PassiveCooldownSync.Remaining(readyAt, 3.0), Is.EqualTo(6f).Within(1e-5f));

        // 남은 시간보다 오래 지났으면 0, 서버 시각이 도장보다 과거(지연 보정 역전)면 그대로.
        readyAt = PassiveCooldownSync.LocalReadyAt(stamp, clockNow: 3.0, serverTimeNow: 999.0, rate: 1f);
        Assert.That(PassiveCooldownSync.Remaining(readyAt, 3.0), Is.Zero);
        readyAt = PassiveCooldownSync.LocalReadyAt(stamp, clockNow: 3.0, serverTimeNow: 49.0, rate: 1f);
        Assert.That(PassiveCooldownSync.Remaining(readyAt, 3.0), Is.EqualTo(30f).Within(1e-5f));
    }

    [Test]
    public void RateTwo_EndsCooldownInHalfRealTime_OnServerAndPeer()
    {
        var server = new CooldownClock();
        var peer = new CooldownClock();
        server.AdvanceTo(0.0, 1f);
        peer.AdvanceTo(40.0, 1f);

        double serverReadyAt = server.Now + Cooldown;
        PassiveCooldownStamp stamp = PassiveCooldownSync.Stamp(serverReadyAt, server.Now, 0.0);
        double peerReadyAt = PassiveCooldownSync.LocalReadyAt(stamp, peer.Now, 0.0, 1f);

        server.AdvanceTo(10.0, 1f);
        peer.AdvanceTo(50.0, 1f);
        server.AdvanceTo(15.0, 2f);   // 실시간 5초 × 2 = 10
        peer.AdvanceTo(55.0, 2f);

        Assert.That(PassiveCooldownSync.Remaining(serverReadyAt, server.Now), Is.EqualTo(10f).Within(1e-5f));
        Assert.That(PassiveCooldownSync.Remaining(peerReadyAt, peer.Now), Is.EqualTo(10f).Within(1e-5f));

        server.AdvanceTo(20.1, 2f);
        peer.AdvanceTo(60.1, 2f);
        Assert.That(server.Now >= serverReadyAt, Is.True, "실시간 15.1초에 30초 쿨 끝");
        Assert.That(peer.Now >= peerReadyAt, Is.True);
    }

    [Test]
    public void HitReduction_PullsReadyAtButNotPastNow()
    {
        Assert.That(PassiveCooldownSync.Reduce(130.0, 100.0, 2f), Is.EqualTo(128.0).Within(1e-9));
        Assert.That(PassiveCooldownSync.Reduce(101.0, 100.0, 2f), Is.EqualTo(100.0).Within(1e-9), "지금 아래로는 안 감");
        Assert.That(PassiveCooldownSync.Reduce(90.0, 100.0, 2f), Is.EqualTo(90.0).Within(1e-9), "쿨 끝난 뒤엔 그대로");
        Assert.That(PassiveCooldownSync.Reduce(130.0, 100.0, -5f), Is.EqualTo(130.0).Within(1e-9), "음수 감소 무시");
    }

    [Test]
    public void ReducedStamp_ReachesPeerAsSmallerRemaining()
    {
        double readyAt = PassiveCooldownSync.Reduce(130.0, 100.0, 2f);
        PassiveCooldownStamp stamp = PassiveCooldownSync.Stamp(readyAt, 100.0, 10.0);
        double peerReadyAt = PassiveCooldownSync.LocalReadyAt(stamp, 0.0, 10.0, 1f);
        Assert.That(PassiveCooldownSync.Remaining(peerReadyAt, 0.0), Is.EqualTo(28f).Within(1e-5f));
    }
}
