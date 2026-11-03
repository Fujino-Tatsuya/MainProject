using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 카메라 대상 중심(할 일 196 단계 1) — 강/약/없음 × 클릭·실패·거부·성공 복귀 시점표, 비율 0·0.5·1, 스위치 꺼짐, 오너 아님,
/// 성공 슬로우 복귀 시작 시점, 슬로우 SO ④ 칸 기본값·디렉터 스위치 기본값.
/// </summary>
public sealed class InterruptCameraCenterTests
{
    private static readonly Vector3 Base = new Vector3(0f, 0f, 0f);
    private static readonly Vector3 Target = new Vector3(4f, 0f, 2f);

    // ---- 1. 복귀 시점표 ----

    [TestCase(InterruptAttemptForecast.Strong, InterruptCameraCenterPhase.Click, ExpectedResult = InterruptCameraReturnTiming.Hold)]
    [TestCase(InterruptAttemptForecast.Strong, InterruptCameraCenterPhase.JudgeFailed, ExpectedResult = InterruptCameraReturnTiming.Now)]
    [TestCase(InterruptAttemptForecast.Strong, InterruptCameraCenterPhase.ServerRejected, ExpectedResult = InterruptCameraReturnTiming.Now)]
    [TestCase(InterruptAttemptForecast.Strong, InterruptCameraCenterPhase.SuccessSlow, ExpectedResult = InterruptCameraReturnTiming.Hold)]
    [TestCase(InterruptAttemptForecast.Strong, InterruptCameraCenterPhase.SuccessSlowExit, ExpectedResult = InterruptCameraReturnTiming.Now)]
    [TestCase(InterruptAttemptForecast.Weak, InterruptCameraCenterPhase.Click, ExpectedResult = InterruptCameraReturnTiming.Hold)]
    [TestCase(InterruptAttemptForecast.Weak, InterruptCameraCenterPhase.JudgeFailed, ExpectedResult = InterruptCameraReturnTiming.Now)]
    [TestCase(InterruptAttemptForecast.Weak, InterruptCameraCenterPhase.ServerRejected, ExpectedResult = InterruptCameraReturnTiming.Now)]
    [TestCase(InterruptAttemptForecast.Weak, InterruptCameraCenterPhase.SuccessSlow, ExpectedResult = InterruptCameraReturnTiming.Hold)]
    [TestCase(InterruptAttemptForecast.Weak, InterruptCameraCenterPhase.SuccessSlowExit, ExpectedResult = InterruptCameraReturnTiming.Now)]
    [TestCase(InterruptAttemptForecast.NoTarget, InterruptCameraCenterPhase.Click, ExpectedResult = InterruptCameraReturnTiming.None)]
    [TestCase(InterruptAttemptForecast.NoTarget, InterruptCameraCenterPhase.JudgeFailed, ExpectedResult = InterruptCameraReturnTiming.None)]
    [TestCase(InterruptAttemptForecast.NoTarget, InterruptCameraCenterPhase.ServerRejected, ExpectedResult = InterruptCameraReturnTiming.None)]
    [TestCase(InterruptAttemptForecast.NoTarget, InterruptCameraCenterPhase.SuccessSlow, ExpectedResult = InterruptCameraReturnTiming.None)]
    [TestCase(InterruptAttemptForecast.NoTarget, InterruptCameraCenterPhase.SuccessSlowExit, ExpectedResult = InterruptCameraReturnTiming.None)]
    public InterruptCameraReturnTiming 복귀_시점표(InterruptAttemptForecast forecast, InterruptCameraCenterPhase phase) =>
        InterruptCameraCenterRules.Decide(forecast, phase, true, 1f, true, Base, Target).Return;

    [TestCase(InterruptAttemptForecast.Strong)]
    [TestCase(InterruptAttemptForecast.Weak)]
    public void 강약_클릭은_대상이_정중앙(InterruptAttemptForecast forecast)
    {
        InterruptCameraCenterDecision d = InterruptCameraCenterRules.Decide(forecast, InterruptCameraCenterPhase.Click, true, 1f, true, Base, Target);
        Assert.IsTrue(d.Centers);
        Assert.AreEqual(Target, d.Center);
    }

    [TestCase(InterruptCameraCenterPhase.JudgeFailed)]
    [TestCase(InterruptCameraCenterPhase.ServerRejected)]
    [TestCase(InterruptCameraCenterPhase.SuccessSlowExit)]
    public void 복귀_단계의_목표는_평소_중심(InterruptCameraCenterPhase phase)
    {
        InterruptCameraCenterDecision d = InterruptCameraCenterRules.Decide(InterruptAttemptForecast.Strong, phase, true, 1f, true, Base, Target);
        Assert.IsFalse(d.Centers);
        Assert.AreEqual(Base, d.Center);
    }

    [Test]
    public void 대상_없음은_그대로()
    {
        InterruptCameraCenterDecision d = InterruptCameraCenterRules.Decide(InterruptAttemptForecast.NoTarget,
            InterruptCameraCenterPhase.Click, true, 1f, true, Base, Target);
        Assert.IsFalse(d.Centers);
        Assert.AreEqual(Base, d.Center);
    }

    // ---- 2. 비율 ----

    [TestCase(0f, 0f, 0f)]
    [TestCase(0.5f, 2f, 1f)]
    [TestCase(1f, 4f, 2f)]
    [TestCase(-1f, 0f, 0f)]
    [TestCase(2f, 4f, 2f)]
    [TestCase(float.NaN, 0f, 0f)]
    public void 비율만큼_대상_쪽으로(float ratio, float x, float z)
    {
        InterruptCameraCenterDecision d = InterruptCameraCenterRules.Decide(InterruptAttemptForecast.Strong,
            InterruptCameraCenterPhase.Click, true, ratio, true, Base, Target);
        Assert.AreEqual(InterruptCameraReturnTiming.Hold, d.Return);
        Assert.That(Vector3.Distance(new Vector3(x, 0f, z), d.Center), Is.LessThan(1e-5f));
    }

    // ---- 3. 스위치 꺼짐·오너 아님 ----

    [TestCase(InterruptCameraCenterPhase.Click)]
    [TestCase(InterruptCameraCenterPhase.JudgeFailed)]
    [TestCase(InterruptCameraCenterPhase.SuccessSlow)]
    [TestCase(InterruptCameraCenterPhase.SuccessSlowExit)]
    public void 스위치_꺼짐은_그대로(InterruptCameraCenterPhase phase)
    {
        InterruptCameraCenterDecision d = InterruptCameraCenterRules.Decide(InterruptAttemptForecast.Strong, phase, false, 1f, true, Base, Target);
        Assert.AreEqual(InterruptCameraReturnTiming.None, d.Return);
        Assert.AreEqual(Base, d.Center);
    }

    [TestCase(InterruptCameraCenterPhase.Click)]
    [TestCase(InterruptCameraCenterPhase.ServerRejected)]
    [TestCase(InterruptCameraCenterPhase.SuccessSlow)]
    [TestCase(InterruptCameraCenterPhase.SuccessSlowExit)]
    public void 오너_아님은_그대로(InterruptCameraCenterPhase phase)
    {
        InterruptCameraCenterDecision d = InterruptCameraCenterRules.Decide(InterruptAttemptForecast.Weak, phase, true, 1f, false, Base, Target);
        Assert.AreEqual(InterruptCameraReturnTiming.None, d.Return);
        Assert.AreEqual(Base, d.Center);
    }

    // ---- 4. 성공 슬로우 복귀 시작 시점 ----

    [Test]
    public void 성공_복귀는_히트스톱_진입_유지_뒤()
    {
        var profile = new SlowMotionProfile { hitStopDuration = 0.05f, enterDuration = 0.05f, holdDuration = 1.2f, exitDuration = 0.4f };
        Assert.AreEqual(1.3f, InterruptCameraCenterRules.SuccessReturnDelay(profile), 1e-5f);
        Assert.AreEqual(InterruptCameraCenterPhase.SuccessSlow, InterruptCameraCenterRules.SuccessPhaseAt(0f, profile));
        Assert.AreEqual(InterruptCameraCenterPhase.SuccessSlow, InterruptCameraCenterRules.SuccessPhaseAt(1.29f, profile));
        Assert.AreEqual(InterruptCameraCenterPhase.SuccessSlowExit,
            InterruptCameraCenterRules.SuccessPhaseAt(InterruptCameraCenterRules.SuccessReturnDelay(profile), profile), "경계 = 복귀 구간");
        Assert.AreEqual(InterruptCameraCenterPhase.SuccessSlowExit, InterruptCameraCenterRules.SuccessPhaseAt(1.31f, profile));
        Assert.AreEqual(InterruptCameraCenterPhase.SuccessSlowExit, InterruptCameraCenterRules.SuccessPhaseAt(2f, profile));
    }

    [Test]
    public void 성공_복귀_시점은_음수_길이를_0으로_보고_프로필_없으면_바로()
    {
        var profile = new SlowMotionProfile { hitStopDuration = -1f, enterDuration = 0.1f, holdDuration = float.NaN };
        Assert.AreEqual(0.1f, InterruptCameraCenterRules.SuccessReturnDelay(profile), 1e-5f);
        Assert.AreEqual(0f, InterruptCameraCenterRules.SuccessReturnDelay(null));
        Assert.AreEqual(InterruptCameraCenterPhase.SuccessSlowExit, InterruptCameraCenterRules.SuccessPhaseAt(0f, null));
    }

    [Test]
    public void 기본_성공_프로필의_복귀_시점은_SO_값()
    {
        SlowMotionProfile success = InterruptSlowMotionSettings.OrDefault(null).InterruptSuccessProfile;
        Assert.AreEqual(success.hitStopDuration + success.enterDuration + success.holdDuration,
            InterruptCameraCenterRules.SuccessReturnDelay(success), 1e-5f);
    }

    // ---- 5. 기준점 = 카메라가 따라간 지점(196 후속) ----

    private static readonly Vector3 FollowOffset = new Vector3(0f, 12f, -9f);

    // 화면 중심(수평) = 따라간 지점 + 이번 이동량(y 0) — 카메라 스위처·트래커와 같은 계산.
    private static Vector3 ScreenCenterAfterShift(Vector3 followed, float ratio, out Vector3 shift)
    {
        shift = InterruptCameraCenterRules.CenterToward(followed, Target, ratio) - followed;
        shift.y = 0f;
        return followed + shift;
    }

    [Test]
    public void 기준점은_vcam_위치에서_적용_중_오프셋을_뺀_지점()
    {
        var followed = new Vector3(1.5f, 0.3f, -2f);
        float zoom = 0.85f;
        var shift = new Vector3(0.7f, 0f, 0.4f);
        Vector3 raw = followed + FollowOffset * zoom + shift;
        Assert.That(Vector3.Distance(followed, InterruptCameraCenterRules.FollowedPoint(raw, FollowOffset, zoom, shift)), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(followed, InterruptCameraCenterRules.FollowedPoint(followed + FollowOffset, FollowOffset, 1f, Vector3.zero)),
            Is.LessThan(1e-5f), "평소(배율 1·이동량 0)");
    }

    [Test]
    public void 뒤처져도_비율_1이면_대상이_화면_정중앙()
    {
        var player = new Vector3(2f, 0f, 1f);
        var followed = new Vector3(0.5f, 0f, -0.5f); // 감쇠로 뒤처진 지점 ≠ 플레이어 실제 위치
        Assert.AreNotEqual(player, followed);
        Vector3 raw = followed + FollowOffset * 0.85f + new Vector3(1f, 0f, 1f);
        Vector3 basePoint = InterruptCameraCenterRules.FollowedPoint(raw, FollowOffset, 0.85f, new Vector3(1f, 0f, 1f));

        Vector3 center = ScreenCenterAfterShift(basePoint, 1f, out Vector3 shift);
        Assert.AreEqual(0f, shift.y);
        Assert.That(Vector2.Distance(new Vector2(Target.x, Target.z), new Vector2(center.x, center.z)), Is.LessThan(1e-5f));
    }

    [Test]
    public void 뒤처져도_비율_반이면_따라간_지점과_대상의_정중간()
    {
        var followed = new Vector3(-1f, 0f, 3f);
        Vector3 raw = followed + FollowOffset * 1.2f + new Vector3(-0.5f, 0f, 0.25f);
        Vector3 basePoint = InterruptCameraCenterRules.FollowedPoint(raw, FollowOffset, 1.2f, new Vector3(-0.5f, 0f, 0.25f));

        Vector3 center = ScreenCenterAfterShift(basePoint, 0.5f, out Vector3 shift);
        Assert.AreEqual(0f, shift.y);
        Vector3 mid = (followed + Target) * 0.5f;
        Assert.That(Vector2.Distance(new Vector2(mid.x, mid.z), new Vector2(center.x, center.z)), Is.LessThan(1e-5f));
    }

    [Test]
    public void 기준점에_높이_차가_있어도_이동량_y는_0()
    {
        var followed = new Vector3(0f, 1.7f, 0f);
        Vector3 raw = followed + FollowOffset;
        Vector3 basePoint = InterruptCameraCenterRules.FollowedPoint(raw, FollowOffset, 1f, Vector3.zero);
        ScreenCenterAfterShift(basePoint, 1f, out Vector3 shift);
        Assert.AreEqual(0f, shift.y);
    }

    [Test]
    public void 디렉터_기준점_없으면_취소()
    {
        var go = new GameObject("InterruptCameraCenterTests.Director");
        var target = new GameObject("Target");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            director.CameraCenterEnabled = true;
            var cue = new InterruptOwnerCue
            {
                Result = new InterruptJudgeResult { Outcome = InterruptJudgeOutcome.Success },
                Target = target,
            };
            ((IInterruptOwnerCueSink)director).OwnerAttempt(cue);
            Assert.AreEqual(InterruptCameraCenterState.Centering, director.CameraCenterState);
            Assert.AreEqual(Vector3.zero, director.StepCameraCenter(null, false, Time.unscaledTime));
            Assert.AreEqual(InterruptCameraCenterState.Idle, director.CameraCenterState);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(target);
        }
    }

    // ---- 6. 칸 기본값 ----

    [Test]
    public void 슬로우_SO_카메라_중심_칸_기본값()
    {
        var settings = ScriptableObject.CreateInstance<InterruptSlowMotionSettings>();
        try
        {
            Assert.AreEqual(0.08f, settings.CameraCenterMoveDuration, 1e-6f);
            Assert.AreEqual(1f, settings.CameraCenterRatio, 1e-6f);
            Assert.AreEqual(0.25f, settings.CameraCenterReturnDuration, 1e-6f);
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 디렉터_카메라_중심_스위치_기본_켬()
    {
        var go = new GameObject("InterruptCameraCenterTests");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            Assert.IsTrue(director.CameraCenterEnabled);
            director.CameraCenterEnabled = false;
            Assert.IsFalse(director.CameraCenterEnabled);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
