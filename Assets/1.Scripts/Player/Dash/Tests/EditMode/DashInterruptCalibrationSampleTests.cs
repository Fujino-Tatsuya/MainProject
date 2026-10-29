using NUnit.Framework;
using UnityEngine;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// 할 일 139-2 — 139 예측 보정 랩 표본(세션 139calib-20261029_170741, 27개)의 CSV 값으로 바뀐 예측 규칙을 재현한다.
    /// ① 열린 창의 길이 추정이 틀렸으면(경과 &gt; 추정 길이) 버리고 판정까지 남은 시간이 짧으면 강(seq 5·8·15·19·26, 22 는 해당 없음),
    /// ② 누를 때 창이 닫혀 약이면 대시 중 재예측 대상(seq 16). (③ 겹침 규칙은 139-3 에서 앵커 형상 오버랩으로 대체 — seq 10 은 HitboxAnchorShapeTests)
    /// </summary>
    public class DashInterruptCalibrationSampleTests
    {
        // CSV 한 줄의 오너 예측 입력 칸(cast_time·cast_pos·dash_dir·dash_speed·dash_remaining·skill_start_time·prep_delay·
        // has_target_in_range·target_window_open·window_open_elapsed·window_expected_duration) 그대로.
        private static DashInterruptPredictInput Row(float castTime, float posX, float posZ, float dirX, float dirZ, float remaining,
            float prep, bool inRange, bool open, float openElapsed, float expected) =>
            new DashInterruptPredictInput
            {
                CastTime = castTime,
                CastPosition = new Vector3(posX, 0.015f, posZ),
                DashDirection = new Vector3(dirX, 0f, dirZ),
                DashSpeed = 16f,
                DashRemaining = remaining,
                SkillStartTime = castTime,
                PrepDelay = prep,
                HasTargetInRange = inRange,
                TargetWindowOpen = open,
                WindowOpenElapsed = openElapsed,
                WindowExpectedDuration = expected,
            };

        // ─── ① 창 길이 추정 버리기 ───

        [TestCase(5, 32.6423f, -3.23897f, 2.14325f, 0.70711f, 0.70711f, 0.22902f, 0.33711f, 1.80125f, 1.19439f)]
        [TestCase(8, 44.5521f, 1.81389f, 1.19768f, 0f, 1f, 0.20824f, 0.34427f, 3.47348f, 1.53443f)]
        [TestCase(15, 61.62914f, 3.67737f, 7.62226f, -0.70711f, -0.70711f, 0.18729f, 0.34066f, 8.43842f, 1.53443f)]
        [TestCase(19, 76.5457f, 2.61059f, 7.3296f, -0.70711f, -0.70711f, 0.18754f, 0.34679f, 8.19611f, 1.53443f)]
        [TestCase(26, 100.2732f, -2.44486f, 2.06291f, 0.70711f, 0.70711f, 0.18805f, 0.33707f, 1.86797f, 1.24323f)]
        public void 표본_열린_창의_경과가_추정_길이를_넘으면_추정을_버리고_강(int seq, float cast, float x, float z, float dx, float dz,
            float remaining, float prep, float openElapsed, float expected)
        {
            DashInterruptPrediction p = DashInterruptPredictor.Predict(
                Row(cast, x, z, dx, dz, remaining, prep, true, true, openElapsed, expected));

            Assert.IsTrue(DashInterruptPredictor.IsWindowEstimateStale(openElapsed, expected), $"seq {seq}");
            Assert.LessOrEqual(p.TimeToResolve(cast), DashInterruptPredictor.StaleWindowMaxTimeToResolve, $"seq {seq} 판정까지 남은 시간");
            Assert.AreEqual(DashInterruptPredictReason.Success, p.Reason, $"seq {seq} — 이전 규칙은 WindowClosesBeforeResolve(약)");
            Assert.AreEqual(InterruptAttemptForecast.Strong, p.Forecast, $"seq {seq}");
            Assert.AreEqual(-1f, p.WindowRemaining, $"seq {seq} 버린 추정 = 남은 창 모름");
        }

        [Test]
        public void 표본22_경과가_추정_길이_안이면_예전대로_남은_창으로_본다()
        {
            // seq 22: 경과 0.924 < 추정 1.243 → 남은 0.319 < 판정까지 0.345 → 약(그대로). 무기한 창이라 실제로는 열려 있었다(답 true).
            DashInterruptPrediction p = DashInterruptPredictor.Predict(
                Row(84.99547f, 0.41469f, 1.14153f, 0f, 1f, 0.22197f, 0.34473f, true, true, 0.92383f, 1.24323f));
            Assert.AreEqual(DashInterruptPredictReason.WindowClosesBeforeResolve, p.Reason);
            Assert.AreEqual(0.3194f, p.WindowRemaining, 1e-3f);
        }

        [Test]
        public void 추정을_버려도_판정까지_남은_시간이_길면_약()
        {
            // 판정까지 0.5초(> 0.4) — 지금 열려 있어도 그때까지 열려 있을지 모른다.
            DashInterruptPrediction p = DashInterruptPredictor.Predict(
                Row(10f, 0f, 0f, 0f, 1f, 0.2f, 0.5f, true, true, 2f, 1.2f));
            Assert.AreEqual(0.5f, p.TimeToResolve(10f), 1e-4f);
            Assert.AreEqual(DashInterruptPredictReason.WindowClosesBeforeResolve, p.Reason);

            // 경계 = 0.4 포함
            DashInterruptPrediction edge = DashInterruptPredictor.Predict(
                Row(10f, 0f, 0f, 0f, 1f, 0.2f, DashInterruptPredictor.StaleWindowMaxTimeToResolve, true, true, 2f, 1.2f));
            Assert.AreEqual(DashInterruptPredictReason.Success, edge.Reason);
        }

        [Test]
        public void 추정이_틀렸다고_보는_조건()
        {
            Assert.IsTrue(DashInterruptPredictor.IsWindowEstimateStale(1.3f, 1.2f));
            Assert.IsFalse(DashInterruptPredictor.IsWindowEstimateStale(1.2f, 1.2f), "같으면 아직 아니다(남은 0)");
            Assert.IsFalse(DashInterruptPredictor.IsWindowEstimateStale(-1f, 1.2f), "경과 모름");
            Assert.IsFalse(DashInterruptPredictor.IsWindowEstimateStale(5f, -1f), "길이 모름");

            // 창이 닫혀 있으면 경과 칸이 있어도 버리기 규칙을 타지 않는다(닫힘이 먼저).
            DashInterruptPrediction closed = DashInterruptPredictor.Predict(
                Row(10f, 0f, 0f, 0f, 1f, 0.2f, 0.3f, true, false, 2f, 1.2f));
            Assert.AreEqual(DashInterruptPredictReason.WindowClosedNow, closed.Reason);
        }

        // ─── ② 대쉬 중 재예측(seq 16) ───

        [Test]
        public void 표본16_누를_때_닫힘은_재예측_대상이고_대시_중_열리면_강()
        {
            // 누른 순간: 대상 범위 안 · 창 닫힘 → 약(WindowClosedNow).
            const float cast = 63.93419f;
            DashInterruptPredictInput press = Row(cast, 3.73756f, 8.23445f, -0.70711f, -0.70711f, 0.23616f, 0.34573f,
                true, false, -1f, 1.55886f);
            DashInterruptPrediction atPress = DashInterruptPredictor.Predict(press);
            Assert.AreEqual(DashInterruptPredictReason.WindowClosedNow, atPress.Reason);
            Assert.AreEqual(InterruptAttemptForecast.Weak, atPress.Forecast);
            Assert.IsTrue(DashInterruptPredictor.ShouldRepredictDuringDash(atPress));

            // 판정(≈ 64.27992) 때 실제 창 경과 0.30391 → 누른 뒤 약 0.042초에 열렸다(대시 남은 0.236 안).
            float opened = atPress.PredictedResolveTime - 0.30391f;
            float age = opened - cast;
            Assert.Greater(age, 0f);
            Assert.Less(age, press.DashRemaining, "대시 도중 열림");

            DashInterruptPredictInput again = press;
            again.CastTime = opened;
            again.CastPosition += DashInterruptRules.GetPredictedEndOffset(press.DashDirection, press.DashSpeed, age);
            again.DashRemaining -= age;
            again.SkillStartTime = cast; // 준비 기준점은 누른 순간 그대로(InterruptAttemptPrediction.ForReprediction)
            again.TargetWindowOpen = true;
            again.WindowOpenElapsed = 0f;

            DashInterruptPrediction re = DashInterruptPredictor.Predict(again);
            Assert.AreEqual(InterruptAttemptForecast.Strong, re.Forecast, "남은 창 1.559 ≥ 판정까지 0.304");
            Assert.AreEqual(atPress.PredictedResolveTime, re.PredictedResolveTime, 1e-4f, "판정 시각은 시도 하나에서 같다");
            Assert.AreEqual(atPress.PredictedEndPosition.x, re.PredictedEndPosition.x, 1e-4f);
            Assert.AreEqual(atPress.PredictedEndPosition.z, re.PredictedEndPosition.z, 1e-4f);
        }

        [Test]
        public void 재예측은_창이_닫혀_약이었던_예측만()
        {
            Assert.IsFalse(DashInterruptPredictor.ShouldRepredictDuringDash(new DashInterruptPrediction { Reason = DashInterruptPredictReason.Success }));
            Assert.IsFalse(DashInterruptPredictor.ShouldRepredictDuringDash(new DashInterruptPrediction { Reason = DashInterruptPredictReason.NoTargetInRange }));
            Assert.IsFalse(DashInterruptPredictor.ShouldRepredictDuringDash(new DashInterruptPrediction { Reason = DashInterruptPredictReason.WindowClosesBeforeResolve }));
            Assert.IsTrue(DashInterruptPredictor.ShouldRepredictDuringDash(new DashInterruptPrediction { Reason = DashInterruptPredictReason.WindowClosedNow }));
        }

        // ③(겹침이면 공격 방향 생략, seq 10)은 할 일 139-3 에서 없앴다 — 판정 앵커 실제 모양 오버랩이 seq 10 을 대상 있음으로 본다.
        // 표본 7·8·10 형상 검사는 Assets/1.Scripts/Player/Editor/HitboxAnchorShapeTests.cs.

        [Test]
        public void 겹쳐도_대상_정면_허용각은_그대로_본다()
        {
            // 23호처럼 정면 판정(±60)이 있는 대상의 등 뒤에서 겹치면 여전히 대상 없음 — 정면 판정은 대상의 실제 간파 규칙.
            Vector3 dummy = new Vector3(0f, 0f, 5f);
            Assert.IsFalse(InterruptTargetGeometry.FacesAttacker(dummy + Vector3.forward * 0.3f, dummy, Vector3.back, 60f));
            Assert.IsTrue(InterruptTargetGeometry.FacesAttacker(dummy + Vector3.back * 0.3f, dummy, Vector3.back, 60f));
        }
    }
}
