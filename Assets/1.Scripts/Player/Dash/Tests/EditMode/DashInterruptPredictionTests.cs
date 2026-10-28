using NUnit.Framework;
using UnityEngine;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashInterruptPredictor"/> 검증: 예상 위치·판정 시각·성공/실패 예측과 근거(할 일 116 이식),
    /// 근접 판정 시각·준비 시간 추정(할 일 132).
    /// </summary>
    public class DashInterruptPredictionTests
    {
        private static DashInterruptPredictInput Input(
            float dashRemaining = 0.2f, float prep = 0.3f, float skillAge = 0f,
            bool inRange = true, bool open = true, float openFor = -1f, float expDur = -1f) =>
            new DashInterruptPredictInput
            {
                CastTime = 10f,
                CastPosition = new Vector3(1f, 0f, 2f),
                DashDirection = Vector3.forward,
                DashSpeed = 20f,
                DashRemaining = dashRemaining,
                SkillStartTime = 10f - skillAge,
                PrepDelay = prep,
                HasTargetInRange = inRange,
                TargetWindowOpen = open,
                WindowOpenElapsed = openFor,
                WindowExpectedDuration = expDur,
            };

        private static DashInterruptPredictInput Melee(
            float prep = 0.3f, bool inRange = true, bool open = true, float openFor = -1f, float expDur = -1f)
        {
            var input = DashInterruptPredictInput.ForMelee(10f, new Vector3(1f, 0f, 2f), prep);
            input.HasTargetInRange = inRange;
            input.TargetWindowOpen = open;
            input.WindowOpenElapsed = openFor;
            input.WindowExpectedDuration = expDur;
            return input;
        }

        [Test]
        public void PredictedEnd_IsStraightDashOffset()
        {
            DashInterruptPrediction p = DashInterruptPredictor.Predict(Input(dashRemaining: 0.2f));
            Assert.AreEqual(1f, p.PredictedEndPosition.x, 1e-4f);
            Assert.AreEqual(6f, p.PredictedEndPosition.z, 1e-4f, "2 + 20 × 0.2");
            Assert.AreEqual(10.2f, p.PredictedDashEndTime, 1e-4f);
        }

        [Test]
        public void ResolveTime_IsMaxOfDashEndAndNormalHit()
        {
            // 준비 0.3 > 대시 0.2 → 남은 준비 시간만큼 미룬다
            Assert.AreEqual(10.3f, DashInterruptPredictor.Predict(Input(dashRemaining: 0.2f, prep: 0.3f)).PredictedResolveTime, 1e-4f);
            // 대시 0.5 > 준비 0.3 → 대시 끝
            Assert.AreEqual(10.5f, DashInterruptPredictor.Predict(Input(dashRemaining: 0.5f, prep: 0.3f)).PredictedResolveTime, 1e-4f);
            // 우클릭 → 대시: 준비 기준점 t0 = 0.1초 전 → 평소 Hit = 10.2, 대시 끝 10.1 → 10.2
            Assert.AreEqual(10.2f, DashInterruptPredictor.Predict(Input(dashRemaining: 0.1f, prep: 0.3f, skillAge: 0.1f)).PredictedResolveTime, 1e-4f);
            // 준비 시간 모름(-1) = 0 으로 본다 → 대시 끝
            Assert.AreEqual(10.2f, DashInterruptPredictor.Predict(Input(dashRemaining: 0.2f, prep: -1f)).PredictedResolveTime, 1e-4f);
        }

        [Test]
        public void Predict_ReasonOrder()
        {
            Assert.AreEqual(DashInterruptPredictReason.NoTargetInRange,
                DashInterruptPredictor.Predict(Input(inRange: false, open: true)).Reason, "범위 밖이 먼저");
            Assert.AreEqual(DashInterruptPredictReason.WindowClosedNow,
                DashInterruptPredictor.Predict(Input(open: false)).Reason);
            // 창 남은 0.1 < 판정까지 0.3 → 닫힘 예측
            Assert.AreEqual(DashInterruptPredictReason.WindowClosesBeforeResolve,
                DashInterruptPredictor.Predict(Input(dashRemaining: 0.2f, prep: 0.3f, openFor: 0.4f, expDur: 0.5f)).Reason);
            DashInterruptPrediction ok = DashInterruptPredictor.Predict(Input(dashRemaining: 0.2f, prep: 0.3f, openFor: 0.1f, expDur: 0.5f));
            Assert.AreEqual(DashInterruptPredictReason.Success, ok.Reason);
            Assert.IsTrue(ok.PredictsSuccess);
            Assert.AreEqual(0.4f, ok.WindowRemaining, 1e-4f);
        }

        [Test]
        public void Predict_UnknownWindowLength_OpenMeansSuccess()
        {
            DashInterruptPrediction p = DashInterruptPredictor.Predict(Input(openFor: 0.2f, expDur: -1f));
            Assert.IsTrue(p.PredictsSuccess);
            Assert.AreEqual(-1f, p.WindowRemaining);
        }

        [Test]
        public void WindowRemaining_ClampsAndUnknown()
        {
            Assert.AreEqual(0f, DashInterruptPredictor.EstimateWindowRemaining(0.8f, 0.5f), "이미 지남 = 0");
            Assert.AreEqual(-1f, DashInterruptPredictor.EstimateWindowRemaining(-1f, 0.5f));
            Assert.AreEqual(-1f, DashInterruptPredictor.EstimateWindowRemaining(0.1f, -1f));
        }

        // ── 근접(할 일 132) — 판정 시각 = 지금 + 준비 시간 ──

        [Test]
        public void Melee_ResolveTime_IsCastPlusPrep()
        {
            Assert.AreEqual(10.3f, DashInterruptPredictor.GetMeleeResolveTime(10f, 0.3f), 1e-4f);
            Assert.AreEqual(10f, DashInterruptPredictor.GetMeleeResolveTime(10f, -1f), 1e-4f, "모름 = 0");

            DashInterruptPrediction p = DashInterruptPredictor.Predict(Melee(prep: 0.3f));
            Assert.AreEqual(10.3f, p.PredictedResolveTime, 1e-4f);
            Assert.AreEqual(10f, p.PredictedDashEndTime, 1e-4f, "대시 없음 = 시전 시각");
            Assert.AreEqual(new Vector3(1f, 0f, 2f), p.PredictedEndPosition, "위치는 시전 위치 그대로");
        }

        [Test]
        public void Melee_ForMelee_UnknownWindowByDefault()
        {
            DashInterruptPredictInput input = DashInterruptPredictInput.ForMelee(5f, Vector3.zero, 0.2f);
            Assert.AreEqual(0f, input.DashRemaining);
            Assert.AreEqual(5f, input.SkillStartTime);
            Assert.AreEqual(-1f, input.WindowOpenElapsed);
            Assert.AreEqual(-1f, input.WindowExpectedDuration);
            Assert.IsFalse(input.HasTargetInRange, "대상은 호출측이 채운다");
        }

        [Test]
        public void Melee_ReasonFollowsSameRules()
        {
            Assert.AreEqual(DashInterruptPredictReason.NoTargetInRange, DashInterruptPredictor.Predict(Melee(inRange: false)).Reason);
            Assert.AreEqual(DashInterruptPredictReason.WindowClosedNow, DashInterruptPredictor.Predict(Melee(open: false)).Reason);
            // 창 남은 0.2 < 준비 0.3 → 판정 전에 닫힘(약)
            Assert.AreEqual(DashInterruptPredictReason.WindowClosesBeforeResolve,
                DashInterruptPredictor.Predict(Melee(prep: 0.3f, openFor: 0.3f, expDur: 0.5f)).Reason);
            // 창 남은 0.4 ≥ 준비 0.3 → 성공(강)
            Assert.IsTrue(DashInterruptPredictor.Predict(Melee(prep: 0.3f, openFor: 0.1f, expDur: 0.5f)).PredictsSuccess);
        }

        [Test]
        public void PrepDelay_ObservedThenDataThenUnknown()
        {
            Assert.AreEqual(0.42f, DashInterruptPredictor.EstimatePrepDelay(0.42f, true, 0.3f), 1e-6f, "관측값 우선");
            Assert.AreEqual(0.3f, DashInterruptPredictor.EstimatePrepDelay(-1f, true, 0.3f), 1e-6f, "관측 전 = 데이터 HitDelay");
            Assert.AreEqual(-1f, DashInterruptPredictor.EstimatePrepDelay(-1f, false, 0f), "데이터도 없으면 모름");
        }
    }
}
