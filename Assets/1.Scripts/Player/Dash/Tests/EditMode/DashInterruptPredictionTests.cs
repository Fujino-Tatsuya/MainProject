using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashInterruptPredictor"/> 검증: 예상 위치·판정 시각·성공/실패 예측과 근거·어긋남 분류·로그 형식. (할 일 116)
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

        [Test]
        public void Shortfall_AlongDashDirection()
        {
            Vector3 cast = Vector3.zero, predicted = new Vector3(0f, 0f, 4f);
            Assert.AreEqual(3f, DashInterruptPredictor.DashShortfall(cast, predicted, new Vector3(0.5f, 0f, 1f), Vector3.forward), 1e-4f);
            Assert.AreEqual(0f, DashInterruptPredictor.DashShortfall(cast, predicted, predicted, Vector3.forward), 1e-4f);
            Assert.AreEqual(0f, DashInterruptPredictor.DashShortfall(cast, predicted, predicted, Vector3.zero), "방향 없음 = 0");
        }

        [Test]
        public void PlanarAngleAndDistance()
        {
            Assert.AreEqual(90f, DashInterruptPredictor.PlanarAngle(Vector3.forward, Vector3.zero, new Vector3(3f, 5f, 0f)), 1e-3f);
            Assert.AreEqual(5f, DashInterruptPredictor.PlanarDistance(Vector3.zero, new Vector3(3f, 9f, 4f)), 1e-4f);
        }

        private static DashInterruptActualObservation Actual(DashInterruptActualResult result, float resolveTime = 10.3f,
            bool openAtResolve = false, float move = 0f, float yaw = 0f, bool remote = false) =>
            new DashInterruptActualObservation
            {
                Result = result,
                ResolveTime = resolveTime,
                TargetWindowOpenAtResolve = openAtResolve,
                TargetMoveDistance = move,
                TargetYawDelta = yaw,
                IsRemoteOwner = remote,
            };

        [Test]
        public void Classify_MatchIsNone()
        {
            DashInterruptPrediction success = DashInterruptPredictor.Predict(Input());
            DashInterruptPrediction fail = DashInterruptPredictor.Predict(Input(open: false));
            Assert.AreEqual(DashInterruptMismatchReason.None,
                DashInterruptPredictor.ClassifyMismatch(success, Actual(DashInterruptActualResult.Success), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.None,
                DashInterruptPredictor.ClassifyMismatch(fail, Actual(DashInterruptActualResult.NoTarget), 0f), "실패 예측 = 실패·대상 없음 둘 다 적중");
        }

        [Test]
        public void Classify_PredictedSuccessActualFail()
        {
            DashInterruptPrediction p = DashInterruptPredictor.Predict(Input()); // 판정 예상 10.3
            Assert.AreEqual(DashInterruptMismatchReason.DashShortened,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.NoTarget), 1f));
            Assert.AreEqual(DashInterruptMismatchReason.TargetMoved,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.NoTarget, move: 1f), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.TargetRotated,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.NoTarget, yaw: 45f), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.NetworkLag,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.NoTarget, remote: true), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.OutOfRange,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.NoTarget), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.WindowClosedBeforeResolve,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.Fail, resolveTime: 10.3f), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.ResolveDelayed,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.Fail, resolveTime: 10.5f), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.Unknown,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.Fail, openAtResolve: true), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.Aborted,
                DashInterruptPredictor.ClassifyMismatch(p, Actual(DashInterruptActualResult.Aborted), 0f));
        }

        [Test]
        public void Classify_PredictedFailActualSuccess()
        {
            Assert.AreEqual(DashInterruptMismatchReason.WindowOpenedAfterCast,
                DashInterruptPredictor.ClassifyMismatch(DashInterruptPredictor.Predict(Input(open: false)),
                    Actual(DashInterruptActualResult.Success), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.WindowLongerThanEstimated,
                DashInterruptPredictor.ClassifyMismatch(DashInterruptPredictor.Predict(Input(openFor: 0.4f, expDur: 0.5f)),
                    Actual(DashInterruptActualResult.Success), 0f));
            Assert.AreEqual(DashInterruptMismatchReason.TargetMoved,
                DashInterruptPredictor.ClassifyMismatch(DashInterruptPredictor.Predict(Input(inRange: false)),
                    Actual(DashInterruptActualResult.Success, move: 2f), 0f));
        }

        [Test]
        public void LogFormat_KeyValueRoundTrip()
        {
            var sb = new StringBuilder(DashInterruptPredictor.Tag).Append(" event=cast");
            DashInterruptPredictor.Append(sb, "target", "Twenty Three=Boss");
            DashInterruptPredictor.Append(sb, "predEndPos", new Vector3(1.234f, 9f, -2f));
            DashInterruptPredictor.Append(sb, "timeErrS", 0.05f);
            DashInterruptPredictor.Append(sb, "match", true);
            DashInterruptPredictor.Append(sb, "empty", (string)null);
            string line = sb.ToString();
            Assert.AreEqual("Twenty_Three_Boss", DashInterruptPredictor.ReadField(line, "target"));
            Assert.AreEqual("1.23,-2.00", DashInterruptPredictor.ReadField(line, "predEndPos"));
            Assert.AreEqual("0.050", DashInterruptPredictor.ReadField(line, "timeErrS"));
            Assert.AreEqual("true", DashInterruptPredictor.ReadField(line, "match"));
            Assert.AreEqual("none", DashInterruptPredictor.ReadField(line, "empty"));
            Assert.IsNull(DashInterruptPredictor.ReadField(line, "missing"));
        }

        [Test]
        public void FormatTokens()
        {
            Assert.AreEqual("dash_rmb", DashInterruptPredictor.Format(DashInterruptOrder.DashThenInterrupt));
            Assert.AreEqual("rmb_dash", DashInterruptPredictor.Format(DashInterruptOrder.InterruptThenDash));
            Assert.AreEqual("no_target", DashInterruptPredictor.Format(DashInterruptActualResult.NoTarget));
            Assert.AreEqual("window_closed_before_resolve",
                DashInterruptPredictor.Format(DashInterruptMismatchReason.WindowClosedBeforeResolve));
        }
    }
}
