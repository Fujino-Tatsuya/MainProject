using NUnit.Framework;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashInterruptLayerFade"/> 검증: 선형 진행·끝 고정·0초 즉시·진행 중 재시작 이음·취소 고정·인수인계 시각. (할 일 115 §11)
    /// </summary>
    public class DashInterruptLayerFadeTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void FadeIn_LinearOverDuration()
        {
            var fade = DashInterruptLayerFade.Start(0f, 1f, 0.1f);
            Assert.IsTrue(fade.IsActive);
            Assert.AreEqual(0f, fade.Weight, Eps, "시작 = 0");
            Assert.AreEqual(0.5f, fade.Advance(0.05f), Eps);
            Assert.IsTrue(fade.IsActive);
            Assert.AreEqual(1f, fade.Advance(0.05f), Eps, "끝 경계 = 목표");
            Assert.IsFalse(fade.IsActive);
        }

        [Test]
        public void FadeOut_ClampsAtTargetAfterOvershoot()
        {
            var fade = DashInterruptLayerFade.Start(1f, 0f, 0.05f);
            Assert.AreEqual(0.4f, fade.Advance(0.03f), Eps);
            Assert.AreEqual(0f, fade.Advance(1f), Eps, "넘쳐도 목표에서 멈춘다");
            Assert.IsFalse(fade.IsActive);
            Assert.AreEqual(0f, fade.Advance(1f), Eps, "끝난 뒤 진행해도 그대로");
        }

        [Test]
        public void ZeroOrNegativeDuration_Immediate()
        {
            var zero = DashInterruptLayerFade.Start(0f, 1f, 0f);
            Assert.AreEqual(1f, zero.Weight, Eps, "0초 = 즉시 목표");
            Assert.AreEqual(1f, zero.Advance(0f), Eps);
            Assert.IsFalse(zero.IsActive);

            var negative = DashInterruptLayerFade.Start(1f, 0f, -1f);
            Assert.AreEqual(0f, negative.Advance(0f), Eps);
            Assert.IsFalse(negative.IsActive);
        }

        [Test]
        public void RestartMidFade_ContinuesFromCurrentWeight()
        {
            var fadeIn = DashInterruptLayerFade.Start(0f, 1f, 0.1f);
            fadeIn.Advance(0.03f); // 0.3

            var fadeOut = DashInterruptLayerFade.Start(fadeIn.Weight, 0f, 0.1f);
            Assert.AreEqual(0.3f, fadeOut.Weight, Eps, "지금 가중치에서 이어 간다");
            Assert.AreEqual(0.15f, fadeOut.Advance(0.05f), Eps);
        }

        [Test]
        public void Start_ClampsWeightsAndNegativeDelta()
        {
            var fade = DashInterruptLayerFade.Start(-2f, 3f, 0.1f);
            Assert.AreEqual(0f, fade.Weight, Eps);
            Assert.AreEqual(1f, fade.Target, Eps);
            Assert.AreEqual(0f, fade.Advance(-1f), Eps, "음수 시간은 진행하지 않는다");
        }

        [Test]
        public void Hold_IsInactiveAtWeight()
        {
            var hold = DashInterruptLayerFade.Hold(0f);
            Assert.IsFalse(hold.IsActive);
            Assert.AreEqual(0f, hold.Weight, Eps);
            Assert.AreEqual(0f, hold.Advance(0.1f), Eps);
        }

        [Test]
        public void HandoffNormalizedTime_UsesUpperTimeOnlyInState()
        {
            Assert.AreEqual(0.4f, DashInterruptLayerFade.HandoffNormalizedTime(true, 0.4f), Eps);
            Assert.AreEqual(1f, DashInterruptLayerFade.HandoffNormalizedTime(true, 1.7f), Eps, "한 번 재생 클립 — 끝을 넘으면 1");
            Assert.AreEqual(0f, DashInterruptLayerFade.HandoffNormalizedTime(true, -0.2f), Eps);
            Assert.AreEqual(0f, DashInterruptLayerFade.HandoffNormalizedTime(false, 0.6f), Eps, "상태 밖이면 처음부터");
        }
    }
}
