using NUnit.Framework;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashValidationPolicy"/> 순수 검증 로직.
    /// 요청 시각은 서버 도메인(serverNow - oneWay)으로 추정한다(크로스머신 시계 비의존).
    /// </summary>
    public class DashValidationPolicyTests
    {
        private const double DashDuration = 0.25;
        private const double Freshness = 0.1;

        private static DashStateSnapshot Snap(
            double t,
            bool grounded = true, bool dead = false, bool soul = false,
            bool cc = false, bool landing = false, int charge = 1) =>
            new DashStateSnapshot(t, grounded, dead, soul, cc, landing, charge, t + 2.0, 0u, 0u);

        private static DashSnapshotHistory History(params DashStateSnapshot[] snaps)
        {
            var h = new DashSnapshotHistory(32);
            foreach (var s in snaps)
            {
                h.Push(s);
            }
            return h;
        }

        private static DashValidationPolicy.Request Req(double dx = 1.0, double dz = 0.0)
            => new DashValidationPolicy.Request(0.0, dx, dz); // clientLocalTime은 타이밍에 미사용(유한성만 검사)

        private static DashValidationPolicy.CurrentState Now(bool dead = false, bool soul = false, bool cc = false)
            => new DashValidationPolicy.CurrentState(dead, soul, cc);

        private static DashValidationResult Validate(
            DashSnapshotHistory history,
            double serverNow,
            double serverRtt = 0.1,
            bool rttAvailable = true,
            bool dashEnabled = true,
            DashValidationPolicy.Request? req = null,
            DashValidationPolicy.CurrentState current = default)
            => DashValidationPolicy.Validate(
                dashEnabled, DashDuration, Freshness, serverNow, serverRtt, rttAvailable,
                req ?? Req(), history, current);

        [Test]
        public void ConfigDisabled_Rejected()
        {
            var r = Validate(History(Snap(9.95)), serverNow: 10.0, dashEnabled: false);
            Assert.AreEqual(DashRejectReason.ConfigDisabled, r.Reason);
        }

        [Test]
        public void NaNDirection_RejectedAsInvalidPayload()
        {
            var r = Validate(History(Snap(9.95)), serverNow: 10.0, req: Req(dx: double.NaN));
            Assert.AreEqual(DashRejectReason.InvalidPayload, r.Reason);
        }

        [Test]
        public void ZeroDirection_RejectedAsInvalidPayload()
        {
            var r = Validate(History(Snap(9.95)), serverNow: 10.0, req: Req(dx: 0.0, dz: 0.0));
            Assert.AreEqual(DashRejectReason.InvalidPayload, r.Reason);
        }

        [Test]
        public void RttUnavailable_Rejected()
        {
            var r = Validate(History(Snap(9.95)), serverNow: 10.0, rttAvailable: false);
            Assert.AreEqual(DashRejectReason.RttUnavailable, r.Reason);
        }

        [Test]
        public void Rtt100ms_SelectsServerSideStart_AndComputesRemaining()
        {
            // rtt=0.1 → oneWay=0.05 → estStart=serverNow-0.05=9.95, estEnd=10.2
            var r = Validate(History(Snap(9.95)), serverNow: 10.0, serverRtt: 0.1);
            Assert.IsTrue(r.IsApproved);
            Assert.IsFalse(r.WasInterruptedByServerState);
            Assert.AreEqual(0.2, r.RemainingServerDuration, 1e-9, "9.95 + 0.25 - 10.0");
        }

        [Test]
        public void Rtt250ms_OneWay125ms()
        {
            // rtt=0.25 → oneWay=0.125 → estStart=9.875, estEnd=10.125
            var r = Validate(History(Snap(9.875)), serverNow: 10.0, serverRtt: 0.25);
            Assert.IsTrue(r.IsApproved);
            Assert.AreEqual(0.125, r.RemainingServerDuration, 1e-9);
        }

        [Test]
        public void HighLatency_AlreadyEnded_ApprovedWithZeroRemaining()
        {
            // oneWay(0.3) > dashDuration(0.25) → 이미 종료로 간주, remaining=0
            var r = Validate(History(Snap(9.7)), serverNow: 10.0, serverRtt: 0.6);
            Assert.IsTrue(r.IsApproved);
            Assert.AreEqual(0.0, r.RemainingServerDuration, 1e-9);
        }

        [Test]
        public void NoSnapshot_Rejected()
        {
            var r = Validate(History(), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.NoFreshSnapshot, r.Reason);
        }

        [Test]
        public void StaleSnapshot_Rejected()
        {
            // estStart=9.95, 유일 스냅샷 9.5 → 0.45 > 0.1 → 거부
            var r = Validate(History(Snap(9.5)), serverNow: 10.0, serverRtt: 0.1);
            Assert.AreEqual(DashRejectReason.NoFreshSnapshot, r.Reason);
        }

        [Test]
        public void NotGroundedAtSnapshot_Rejected()
        {
            var r = Validate(History(Snap(9.95, grounded: false)), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.NotGrounded, r.Reason);
        }

        [Test]
        public void NoChargeAtSnapshot_Rejected()
        {
            var r = Validate(History(Snap(9.95, charge: 0)), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.NoCharge, r.Reason);
        }

        [Test]
        public void DeadBeforeGrounded_DeadReasonWins_ValidationOrder()
        {
            var r = Validate(History(Snap(9.95, grounded: false, dead: true)), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.DeadOrSoul, r.Reason);
        }

        [Test]
        public void ValidPastButCurrentlyDead_ApprovedButInterrupted()
        {
            var r = Validate(History(Snap(9.95)), serverNow: 10.0, serverRtt: 0.1, current: Now(dead: true));
            Assert.IsTrue(r.IsApproved, "과거엔 유효 → 충전 인정");
            Assert.IsTrue(r.WasInterruptedByServerState, "현재 사망 → 대시 중단");
        }
    }
}
