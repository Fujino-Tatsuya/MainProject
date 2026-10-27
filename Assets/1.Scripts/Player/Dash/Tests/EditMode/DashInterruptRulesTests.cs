using NUnit.Framework;
using UnityEngine;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashInterruptRules"/> 검증: 창 경계·0=끔·서버 허용오차·판정 시점·예상 대시 끝. (할 일 115)
    /// </summary>
    public class DashInterruptRulesTests
    {
        private const float Window = 0.1f;
        private const float Tolerance = 0.1f;

        [Test]
        public void PressInWindow_BoundariesInclusive()
        {
            Assert.IsTrue(DashInterruptRules.IsPressInWindow(10f, 10f, Window), "대시 시작과 같은 시각");
            Assert.IsTrue(DashInterruptRules.IsPressInWindow(10f, 10.05f, Window));
            Assert.IsTrue(DashInterruptRules.IsPressInWindow(0f, Window, Window), "창 끝 경계 포함");
        }

        [Test]
        public void PressOutsideWindow_Rejected()
        {
            Assert.IsFalse(DashInterruptRules.IsPressInWindow(10f, 10.15f, Window), "창이 지남");
            Assert.IsFalse(DashInterruptRules.IsPressInWindow(10f, 9.99f, Window), "대시 시작 전 입력");
        }

        [Test]
        public void ZeroOrNegativeWindow_Disabled()
        {
            Assert.IsFalse(DashInterruptRules.IsEnabled(0f));
            Assert.IsFalse(DashInterruptRules.IsEnabled(-0.1f));
            Assert.IsFalse(DashInterruptRules.IsPressInWindow(10f, 10f, 0f), "창 0 = 기능 끔(시작 시각 입력도 거부)");
            Assert.IsFalse(DashInterruptRules.IsPressInWindow(10f, 10f, -1f));
            Assert.IsFalse(DashInterruptRules.IsServerElapsedAccepted(0f, 0f, Tolerance));
        }

        [Test]
        public void ServerWindow_AddsTolerance()
        {
            Assert.IsTrue(DashInterruptRules.IsServerElapsedAccepted(0f, Window, Tolerance));
            Assert.IsTrue(DashInterruptRules.IsServerElapsedAccepted(0.15f, Window, Tolerance), "창은 지났지만 허용오차 안");
            Assert.IsTrue(DashInterruptRules.IsServerElapsedAccepted(0.1875f, 0.125f, 0.0625f), "창 + 허용오차 경계 포함");
            Assert.IsFalse(DashInterruptRules.IsServerElapsedAccepted(0.21f, Window, Tolerance));
            Assert.IsFalse(DashInterruptRules.IsServerElapsedAccepted(-0.01f, Window, Tolerance), "서버 대시 시작 전");
        }

        [Test]
        public void ServerWindow_NegativeToleranceTreatedAsZero()
        {
            Assert.IsTrue(DashInterruptRules.IsServerElapsedAccepted(0.1f, Window, -1f));
            Assert.IsFalse(DashInterruptRules.IsServerElapsedAccepted(0.11f, Window, -1f));
        }

        [Test]
        public void ResolveTiming_HitDuringDash_Held()
        {
            Assert.AreEqual(DashInterruptResolveTiming.HoldHit,
                DashInterruptRules.EvaluateResolveTiming(dashEnded: false, hitReached: true));
        }

        [Test]
        public void ResolveTiming_DashEndedWithHeldHit_ResolvesNow()
        {
            Assert.AreEqual(DashInterruptResolveTiming.ResolveNow,
                DashInterruptRules.EvaluateResolveTiming(dashEnded: true, hitReached: true));
        }

        [Test]
        public void ResolveTiming_BeforeHit_Waits()
        {
            Assert.AreEqual(DashInterruptResolveTiming.Wait,
                DashInterruptRules.EvaluateResolveTiming(dashEnded: false, hitReached: false));
            Assert.AreEqual(DashInterruptResolveTiming.Wait,
                DashInterruptRules.EvaluateResolveTiming(dashEnded: true, hitReached: false), "대시가 짧으면 남은 준비 시간만큼 대기");
        }

        [Test]
        public void ResolveTime_IsMaxOfDashEndAndNormalHit()
        {
            Assert.AreEqual(10.25f, DashInterruptRules.GetResolveTime(10.25f, 10.2f), 1e-5f, "대시가 준비보다 길면 대시 끝");
            Assert.AreEqual(10.4f, DashInterruptRules.GetResolveTime(10.25f, 10.4f), 1e-5f, "대시가 짧으면 평소 Hit");
            Assert.AreEqual(10.3f, DashInterruptRules.GetResolveTime(10.3f, 10.3f), 1e-5f);
        }

        [Test]
        public void PredictedEndOffset_DirectionTimesSpeedTimesRemaining()
        {
            Vector3 offset = DashInterruptRules.GetPredictedEndOffset(Vector3.forward, 20f, 0.2f);
            Assert.AreEqual(0f, offset.x, 1e-5f);
            Assert.AreEqual(0f, offset.y, 1e-5f);
            Assert.AreEqual(4f, offset.z, 1e-5f);

            Vector3 diagonal = DashInterruptRules.GetPredictedEndOffset(new Vector3(0.6f, 0f, 0.8f), 10f, 0.5f);
            Assert.AreEqual(3f, diagonal.x, 1e-5f);
            Assert.AreEqual(4f, diagonal.z, 1e-5f);
        }

        [Test]
        public void PredictedEndOffset_NegativeRemaining_Zero()
        {
            Assert.AreEqual(Vector3.zero, DashInterruptRules.GetPredictedEndOffset(Vector3.forward, 20f, -0.1f));
            Assert.AreEqual(Vector3.zero, DashInterruptRules.GetPredictedEndOffset(Vector3.forward, 20f, 0f));
        }
    }
}
