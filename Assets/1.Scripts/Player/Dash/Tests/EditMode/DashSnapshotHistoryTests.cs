using NUnit.Framework;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashSnapshotHistory"/> 순수 계산 검증(at-or-before 선택, freshness 거부, Ring 덮어쓰기).
    /// </summary>
    public class DashSnapshotHistoryTests
    {
        private static DashStateSnapshot Snap(double t) =>
            new DashStateSnapshot(
                serverTime: t,
                grounded: true, dead: false, soul: false,
                crowdControlled: false, landingProtected: false,
                chargeCount: 1, nextChargeReadyTime: t + 2.0,
                chargeEpoch: 0u, chargeRevision: 0u);

        [Test]
        public void Empty_SelectFails()
        {
            var h = new DashSnapshotHistory(8);
            Assert.IsFalse(h.TrySelectAtOrBefore(1.0, 0.1, out _));
        }

        [Test]
        public void SelectsNewestAtOrBeforeRequest()
        {
            var h = new DashSnapshotHistory(8);
            h.Push(Snap(1.0));
            h.Push(Snap(2.0));
            h.Push(Snap(3.0));

            Assert.IsTrue(h.TrySelectAtOrBefore(2.05, 0.1, out var s));
            Assert.AreEqual(2.0, s.ServerTime, 1e-9, "요청 이하 중 최신(2.0) 선택");
        }

        [Test]
        public void FutureOnly_SelectFails()
        {
            var h = new DashSnapshotHistory(8);
            h.Push(Snap(2.0));
            h.Push(Snap(3.0));

            Assert.IsFalse(h.TrySelectAtOrBefore(1.0, 0.1, out _), "요청시각 이하 스냅샷 없음");
        }

        [Test]
        public void RejectsWhenSelectedTooStale()
        {
            var h = new DashSnapshotHistory(8);
            h.Push(Snap(1.0));

            Assert.IsFalse(h.TrySelectAtOrBefore(1.2, 0.1, out _), "0.2 > 0.1 허용값 → 거부");
            Assert.IsTrue(h.TrySelectAtOrBefore(1.05, 0.1, out _), "0.05 <= 0.1 → 통과");
        }

        [Test]
        public void RingOverflow_DropsOldest()
        {
            var h = new DashSnapshotHistory(3);
            h.Push(Snap(1.0));
            h.Push(Snap(2.0));
            h.Push(Snap(3.0));
            h.Push(Snap(4.0)); // 1.0 밀려남

            Assert.AreEqual(3, h.Count);
            Assert.IsFalse(h.TrySelectAtOrBefore(1.0, 0.1, out _), "가장 오래된 1.0은 덮어써짐");
            Assert.IsTrue(h.TrySelectAtOrBefore(2.05, 0.1, out var s));
            Assert.AreEqual(2.0, s.ServerTime, 1e-9);
        }

        [Test]
        public void OutOfOrderPush_Ignored()
        {
            var h = new DashSnapshotHistory(8);
            Assert.IsTrue(h.Push(Snap(5.0)));
            Assert.IsFalse(h.Push(Snap(3.0)), "과거 시각 Push는 무시");
            Assert.AreEqual(1, h.Count);
            Assert.IsTrue(h.TrySelectAtOrBefore(5.0, 0.1, out var s));
            Assert.AreEqual(5.0, s.ServerTime, 1e-9);
        }
    }
}
