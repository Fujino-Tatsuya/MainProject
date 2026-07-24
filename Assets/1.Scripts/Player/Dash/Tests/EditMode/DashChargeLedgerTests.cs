using NUnit.Framework;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashChargeLedger"/> 순수 계산 검증. 실제 물리/RPC/시간 없이 double now만 주입한다.
    /// </summary>
    public class DashChargeLedgerTests
    {
        [Test]
        public void SequentialRecharge_FillsOneByOne()
        {
            var ledger = new DashChargeLedger(maxCharge: 3, rechargeDuration: 2.0, initialCount: 0, now: 0.0);

            Assert.AreEqual(0, ledger.Count);
            ledger.Advance(1.0);
            Assert.AreEqual(0, ledger.Count, "회복 완료 전이므로 그대로");
            ledger.Advance(2.0);
            Assert.AreEqual(1, ledger.Count);
            ledger.Advance(3.0);
            Assert.AreEqual(1, ledger.Count);
            ledger.Advance(4.0);
            Assert.AreEqual(2, ledger.Count);
            ledger.Advance(6.0);
            Assert.AreEqual(3, ledger.Count);
            Assert.IsTrue(ledger.IsFull);
            Assert.AreEqual(double.PositiveInfinity, ledger.NextReadyTime);
        }

        [Test]
        public void Advance_CatchesUpMultipleCharges_InOneCall()
        {
            var ledger = new DashChargeLedger(3, 2.0, 0, 0.0);

            ledger.Advance(100.0);

            Assert.AreEqual(3, ledger.Count, "오래 조회가 없어도 경과 횟수만큼 한 번에 따라잡는다");
            Assert.IsTrue(ledger.IsFull);
        }

        [Test]
        public void Consume_WhenRecharging_DoesNotResetInProgressTimer()
        {
            var ledger = new DashChargeLedger(3, 2.0, initialCount: 3, now: 0.0); // 만충

            Assert.IsTrue(ledger.TryConsume(0.0)); // 3 -> 2, 회복 시작: next = 2
            Assert.AreEqual(2, ledger.Count);
            Assert.AreEqual(2.0, ledger.NextReadyTime, 1e-9);

            Assert.IsTrue(ledger.TryConsume(1.0)); // 회복 중 소비: 2 -> 1, 타이머 보존
            Assert.AreEqual(1, ledger.Count);
            Assert.AreEqual(2.0, ledger.NextReadyTime, 1e-9, "진행 중인 회복 타이머는 초기화되지 않는다");
        }

        [Test]
        public void Consume_WhenFull_StartsFreshTimer()
        {
            var ledger = new DashChargeLedger(3, 2.0, 3, 0.0);

            Assert.IsTrue(ledger.TryConsume(10.0));
            Assert.AreEqual(2, ledger.Count);
            Assert.AreEqual(12.0, ledger.NextReadyTime, 1e-9);
        }

        [Test]
        public void Consume_WhenEmpty_Fails()
        {
            var ledger = new DashChargeLedger(1, 2.0, 0, 0.0);

            Assert.IsFalse(ledger.TryConsume(0.5), "아직 회복 전이라 소비 불가");
            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void ForceReset_SetsCount_BumpsEpoch_ResetsRevision()
        {
            var ledger = new DashChargeLedger(3, 2.0, 3, 0.0);
            ledger.TryConsume(0.0); // Revision 증가
            uint epochBefore = ledger.Epoch;

            ledger.ForceReset(1, 5.0);

            Assert.AreEqual(1, ledger.Count);
            Assert.AreEqual(epochBefore + 1u, ledger.Epoch, "강제 초기화는 Epoch을 증가시킨다");
            Assert.AreEqual(0u, ledger.Revision, "강제 초기화는 Revision을 0으로 되돌린다");
            Assert.AreEqual(7.0, ledger.NextReadyTime, 1e-9, "다음 충전 진행도는 0부터 시작(5 + 2)");
        }

        [Test]
        public void MaxChargeOne_IsFullWhenOne_AndRechargesAfterConsume()
        {
            var ledger = new DashChargeLedger(1, 2.0, 1, 0.0);

            Assert.IsTrue(ledger.IsFull);
            Assert.IsTrue(ledger.TryConsume(0.0));
            Assert.AreEqual(0, ledger.Count);
            Assert.IsFalse(ledger.IsFull);

            ledger.Advance(2.0);
            Assert.AreEqual(1, ledger.Count);
            Assert.IsTrue(ledger.IsFull);
        }
    }
}
