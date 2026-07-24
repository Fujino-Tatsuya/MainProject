using NUnit.Framework;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashValidationPolicy"/> 순수 검증 로직: RTT 환산, 비정상 payload/방향,
    /// freshness, 당시 상태/충전 검증 순서, 이미 끝난 대시, ApprovedButInterrupted.
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

        private static DashValidationPolicy.Request Req(double clientTime, double dx = 1.0, double dz = 0.0)
            => new DashValidationPolicy.Request(clientTime, dx, dz);

        private static DashValidationPolicy.CurrentState Now(bool dead = false, bool soul = false, bool cc = false)
            => new DashValidationPolicy.CurrentState(dead, soul, cc);

        private static DashValidationResult Validate(
            DashSnapshotHistory history,
            DashValidationPolicy.Request req,
            double serverNow,
            double serverRtt = 0.1,
            bool rttAvailable = true,
            bool dashEnabled = true,
            DashValidationPolicy.CurrentState current = default)
            => DashValidationPolicy.Validate(
                dashEnabled, DashDuration, Freshness, serverNow, serverRtt, rttAvailable, req, history, current);

        [Test]
        public void ConfigDisabled_Rejected()
        {
            var r = Validate(History(Snap(9.9)), Req(10.0), serverNow: 10.0, dashEnabled: false);
            Assert.IsFalse(r.IsApproved);
            Assert.AreEqual(DashRejectReason.ConfigDisabled, r.Reason);
        }

        [Test]
        public void NaNDirection_RejectedAsInvalidPayload()
        {
            var r = Validate(History(Snap(9.9)), Req(10.0, dx: double.NaN, dz: 0.0), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.InvalidPayload, r.Reason);
        }

        [Test]
        public void ZeroDirection_RejectedAsInvalidPayload()
        {
            var r = Validate(History(Snap(9.9)), Req(10.0, dx: 0.0, dz: 0.0), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.InvalidPayload, r.Reason);
        }

        [Test]
        public void RttUnavailable_Rejected()
        {
            var r = Validate(History(Snap(9.9)), Req(10.0), serverNow: 10.0, rttAvailable: false);
            Assert.AreEqual(DashRejectReason.RttUnavailable, r.Reason);
        }

        [Test]
        public void Rtt100ms_ConvertsToOneWay50ms_AndComputesRemaining()
        {
            // clientLocalTime=10.0, rtt=0.1 → oneWay=0.05 → estStart=9.95 → estEnd=10.2
            var history = History(Snap(9.95));
            var r = Validate(history, Req(10.0), serverNow: 10.1, serverRtt: 0.1);
            Assert.IsTrue(r.IsApproved);
            Assert.IsFalse(r.WasInterruptedByServerState);
            Assert.AreEqual(0.1, r.RemainingServerDuration, 1e-9, "10.2 - 10.1");
        }

        [Test]
        public void Rtt250ms_ConvertsToOneWay125ms()
        {
            // clientLocalTime=10.0, rtt=0.25 → oneWay=0.125 → estStart=9.875 → estEnd=10.125
            var history = History(Snap(9.875));
            var r = Validate(history, Req(10.0), serverNow: 10.0, serverRtt: 0.25);
            Assert.IsTrue(r.IsApproved);
            Assert.AreEqual(0.125, r.RemainingServerDuration, 1e-9, "10.125 - 10.0");
        }

        [Test]
        public void RequestAlreadyEnded_ApprovedWithZeroRemaining()
        {
            // estStart=9.95, estEnd=10.2, serverNow=10.5 → remaining 0 (충전만 인정, 새 대시 없음)
            var history = History(Snap(9.95));
            var r = Validate(history, Req(10.0), serverNow: 10.5, serverRtt: 0.1);
            Assert.IsTrue(r.IsApproved);
            Assert.AreEqual(0.0, r.RemainingServerDuration, 1e-9);
        }

        [Test]
        public void NoSnapshot_Rejected()
        {
            var r = Validate(History(), Req(10.0), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.NoFreshSnapshot, r.Reason);
        }

        [Test]
        public void StaleSnapshot_Rejected()
        {
            // estStart=9.95, 유일 스냅샷 t=9.5 → 0.45 > 0.1 freshness → 거부
            var r = Validate(History(Snap(9.5)), Req(10.0), serverNow: 10.0, serverRtt: 0.1);
            Assert.AreEqual(DashRejectReason.NoFreshSnapshot, r.Reason);
        }

        [Test]
        public void NotGroundedAtSnapshot_Rejected()
        {
            var r = Validate(History(Snap(9.95, grounded: false)), Req(10.0), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.NotGrounded, r.Reason);
        }

        [Test]
        public void NoChargeAtSnapshot_Rejected()
        {
            var r = Validate(History(Snap(9.95, charge: 0)), Req(10.0), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.NoCharge, r.Reason);
        }

        [Test]
        public void DeadBeforeGrounded_DeadReasonWins_ValidationOrder()
        {
            // 당시 Dead + NotGrounded 동시 → 검증 순서상 DeadOrSoul(step5)이 Grounded(step6)보다 먼저
            var r = Validate(History(Snap(9.95, grounded: false, dead: true)), Req(10.0), serverNow: 10.0);
            Assert.AreEqual(DashRejectReason.DeadOrSoul, r.Reason);
        }

        [Test]
        public void ValidPastButCurrentlyDead_ApprovedButInterrupted()
        {
            var history = History(Snap(9.95));
            var r = Validate(history, Req(10.0), serverNow: 10.1, serverRtt: 0.1, current: Now(dead: true));
            Assert.IsTrue(r.IsApproved, "과거엔 유효 → 충전 인정");
            Assert.IsTrue(r.WasInterruptedByServerState, "현재 사망 → 대시 중단");
        }
    }
}
