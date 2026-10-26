using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 대시 충전 장부 쿨 시계 이관(할 일 91 T3) — 서버 응답은 남은 초, 오너는 자기 쿨 시계에 더한다.
/// 서버 검증 시간축(GameNow)·서버 쿨 시계·오너 쿨 시계의 원점이 전부 달라도 남은 시간이 같아야 한다.
/// </summary>
public sealed class DashCooldownClockSyncTests
{
    private const ulong ObjectId = 1;
    private const ulong OwnerId = 7;
    private const double Recharge = 2.0;

    private GameObject host;
    private PlayerDashValidationManager manager;
    private CooldownClock serverClock;
    private CooldownClock ownerClock;
    private double serverSource;   // 서버 GameNow (스냅샷·검증 축이자 서버 쿨 시계 원천)
    private double ownerSource;    // 오너 쪽 원천 — 원점이 다르다

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("DashManagerTest");
        manager = host.AddComponent<PlayerDashValidationManager>();
        serverClock = new CooldownClock();
        ownerClock = new CooldownClock();
        serverSource = 1000.0;
        ownerSource = 3.0;
        serverClock.AdvanceTo(serverSource, 1f);
        ownerClock.AdvanceTo(ownerSource, 1f);

        DashRuntimeConfig config = DashRuntimeConfig.Create(10.0, 0.2, 1, Recharge, 32, 0.25);
        manager.RegisterPlayer(ObjectId, OwnerId, config, serverClock.Now);
        Capture();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(host);

    [Test]
    public void ApprovedResponse_CarriesRemainingSeconds_NotAbsoluteTime()
    {
        Step(0.1, 1f);
        DashServerResponse response = Request();

        Assert.That(response.IsApproved, Is.True);
        Assert.That(response.AuthoritativeChargeCount, Is.Zero);
        Assert.That(response.NextChargeRemaining, Is.EqualTo(Recharge).Within(1e-9),
            "서버 GameNow(1000)·쿨 시계 원점과 무관한 남은 초");
    }

    [Test]
    public void RejectedResponse_OwnerAdoptsRemaining_OnItsOwnClock_AndStaysInLockstep()
    {
        var owner = new DashChargeLedger(1, Recharge, 1, ownerClock.Now);

        Step(0.1, 1f);
        Assert.That(owner.TryConsume(ownerClock.Now), Is.True);
        Assert.That(Request().IsApproved, Is.True);

        // 오너가 충전 없이 한 번 더 눌렀다고 치고 서버 거부(NoCharge) 응답을 받는다.
        Step(0.5, 1f);
        DashServerResponse rejected = Request();
        Assert.That(rejected.IsApproved, Is.False);
        Assert.That(rejected.Reason, Is.EqualTo(DashRejectReason.NoCharge));
        Assert.That(rejected.NextChargeRemaining, Is.EqualTo(1.5).Within(1e-9));

        owner.ForceAdoptAuthoritative(
            rejected.AuthoritativeChargeCount, rejected.ChargeEpoch, rejected.ChargeRevision,
            ownerClock.Now, rejected.NextChargeRemaining);
        Assert.That(owner.RemainingToReady(ownerClock.Now), Is.EqualTo(1.5).Within(1e-9), "오너 = 서버 남은 시간");

        // 쿨 속도 2배 구간: 두 피어 모두 같은 배율이면 같은 순간에 찬다.
        Step(0.5, 2f);
        Assert.That(owner.RemainingToReady(ownerClock.Now), Is.EqualTo(0.5).Within(1e-9));
        Assert.That(ServerRemaining(), Is.EqualTo(0.5).Within(1e-9));

        // 실시간 0.25초 × 2 = 0.5초면 찬다. 부동소수 경계를 피해 조금 더 간다.
        Step(0.3, 2f);
        owner.Advance(ownerClock.Now);
        Assert.That(owner.Count, Is.EqualTo(1));
        Assert.That(Request().IsApproved, Is.True, "서버도 같은 구간에 충전됨");
    }

    [Test]
    public void ForceReset_UsesChargeClock()
    {
        Step(0.1, 1f);
        Assert.That(Request().IsApproved, Is.True);

        manager.ForceReset(ObjectId, 0, serverClock.Now);
        Step(1.0, 1f);
        Assert.That(ServerRemaining(), Is.EqualTo(1.0).Within(1e-9), "초기화 시점 쿨 시계부터 2초");
    }

    private void Step(double seconds, float rate)
    {
        serverSource += seconds;
        ownerSource += seconds;
        serverClock.AdvanceTo(serverSource, rate);
        ownerClock.AdvanceTo(ownerSource, rate);
        Capture();
    }

    private void Capture() =>
        manager.CaptureSnapshot(ObjectId, serverSource, serverClock.Now, true, false, false, false, false);

    // 요청 번호는 하나로 계속 올린다 — 지난 번호는 Stale 경로로 빠진다.
    private uint nextRequestId = 1u;

    private DashServerResponse Request() => Send(0.0);

    // 남은 초만 본다: 잘못된 payload(NaN)라 InvalidPayload 로 거부되고 충전은 건드리지 않는다.
    private double ServerRemaining() => Send(double.NaN).NextChargeRemaining;

    private DashServerResponse Send(double clientLocalTime) =>
        manager.ValidateRequest(
            ObjectId, OwnerId, nextRequestId++, clientLocalTime, 1.0, 0.0,
            serverSource, serverClock.Now, 0.0, true, false, false, false);
}
