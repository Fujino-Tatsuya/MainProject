using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 카메라 대상 중심 적용(할 일 196 단계 2) — 상태 기계 전이(이동·유지·추적·복귀), 새 간파 대체, 대상 소실, 스위치 꺼짐·오너 아님 무동작,
/// 성공 복귀(내 성공 슬로우 복귀 구간·앞 슬로우 무시·시간표 대체), cue 없는 종료, 다른 연출이 카메라를 잡음(디렉터).
/// </summary>
public sealed class InterruptCameraCenterTrackerTests
{
    const float Move = 0.1f;
    const float Back = 0.2f;
    static readonly Vector3 Base = Vector3.zero;
    static readonly Vector3 Target = new Vector3(4f, 3f, 2f);
    static readonly Vector3 TargetFlat = new Vector3(4f, 0f, 2f);

    static InterruptCameraCenterTracker Centered(float ratio = 1f)
    {
        var tracker = new InterruptCameraCenterTracker();
        Assert.IsTrue(tracker.Begin(InterruptAttemptForecast.Strong, true, true, true, ratio, Move, Back, 0f));
        tracker.Step(0f, Base, true, Target);
        tracker.Step(Move, Base, true, Target);
        return tracker;
    }

    static void Near(Vector3 expected, Vector3 actual, string message = null) =>
        Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), message ?? $"{expected} ≠ {actual}");

    // ---- 1. 이동·유지·추적 ----

    [TestCase(InterruptAttemptForecast.Strong)]
    [TestCase(InterruptAttemptForecast.Weak)]
    public void 강약_클릭은_이동_시간_동안_대상_쪽으로_보간하고_수평만(InterruptAttemptForecast forecast)
    {
        var tracker = new InterruptCameraCenterTracker();
        Assert.IsTrue(tracker.Begin(forecast, true, true, true, 1f, Move, Back, 0f));
        Assert.AreEqual(InterruptCameraCenterState.Centering, tracker.State);
        Near(Vector3.zero, tracker.Step(0f, Base, true, Target), "시작 = 지금 이동량(0)");
        Vector3 mid = tracker.Step(Move * 0.5f, Base, true, Target);
        Assert.That(mid.x, Is.GreaterThan(0f).And.LessThan(4f));
        Near(TargetFlat, tracker.Step(Move, Base, true, Target), "끝 = 대상 정중앙(높이 무시)");
    }

    [Test]
    public void 비율만큼만_옮긴다()
    {
        InterruptCameraCenterTracker tracker = Centered(0.5f);
        Near(TargetFlat * 0.5f, tracker.Step(1f, Base, true, Target));
    }

    [Test]
    public void 대상이_움직이면_따라간다()
    {
        InterruptCameraCenterTracker tracker = Centered();
        Near(new Vector3(10f, 0f, 2f), tracker.Step(0.5f, Base, true, new Vector3(10f, 0f, 2f)));
        Near(new Vector3(9f, 0f, 1f), tracker.Step(0.6f, new Vector3(1f, 0f, 1f), true, new Vector3(10f, 0f, 2f)), "평소 중심도 움직이면 차이만큼");
    }

    [Test]
    public void 이동_시간_0이면_바로()
    {
        var tracker = new InterruptCameraCenterTracker();
        tracker.Begin(InterruptAttemptForecast.Strong, true, true, true, 1f, 0f, Back, 0f);
        Near(TargetFlat, tracker.Step(0f, Base, true, Target));
    }

    // ---- 2. 무동작 ----

    [TestCase(InterruptAttemptForecast.NoTarget, true, true, true)]
    [TestCase(InterruptAttemptForecast.Strong, false, true, true)]
    [TestCase(InterruptAttemptForecast.Strong, true, false, true)]
    [TestCase(InterruptAttemptForecast.Weak, true, true, false)]
    public void 대상_없음_스위치_꺼짐_오너_아님은_그대로(InterruptAttemptForecast forecast, bool hasTarget, bool switchOn, bool isOwner)
    {
        var tracker = new InterruptCameraCenterTracker();
        Assert.IsFalse(tracker.Begin(forecast, hasTarget, switchOn, isOwner, 1f, Move, Back, 0f));
        Assert.AreEqual(InterruptCameraCenterState.Idle, tracker.State);
        Assert.AreEqual(Vector3.zero, tracker.Step(1f, Base, true, Target));
        tracker.Succeeded(1f, 1f, 0);
        tracker.Return(1f);
        Assert.AreEqual(InterruptCameraCenterState.Idle, tracker.State);
    }

    // ---- 3. 복귀 ----

    [Test]
    public void 복귀는_복귀_시간_동안_0으로_가고_정확히_0에서_끝난다()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Return(1f);
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
        Near(TargetFlat, tracker.Step(1f, Base, true, Target), "시작 = 지금 이동량");
        Vector3 mid = tracker.Step(1f + Back * 0.5f, Base, true, Target);
        Assert.That(mid.x, Is.GreaterThan(0f).And.LessThan(4f));
        Assert.AreEqual(Vector3.zero, tracker.Step(1f + Back + 0.001f, Base, true, Target));
        Assert.AreEqual(InterruptCameraCenterState.Idle, tracker.State);
    }

    [Test]
    public void 대상이_사라지면_마지막_위치에서_복귀()
    {
        InterruptCameraCenterTracker tracker = Centered();
        Near(TargetFlat, tracker.Step(0.5f, Base, false, Vector3.zero), "사라진 프레임 = 마지막 이동량에서 복귀 시작");
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
        Assert.AreEqual(Vector3.zero, tracker.Step(0.5f + Back + 0.001f, Base, false, Vector3.zero));
    }

    [Test]
    public void 이동_중_복귀는_그_자리에서_이어진다()
    {
        var tracker = new InterruptCameraCenterTracker();
        tracker.Begin(InterruptAttemptForecast.Strong, true, true, true, 1f, Move, Back, 0f);
        tracker.Step(0f, Base, true, Target);
        Vector3 partial = tracker.Step(Move * 0.5f, Base, true, Target);
        tracker.Return(Move * 0.5f);
        Near(partial, tracker.Step(Move * 0.5f, Base, true, Target));
    }

    // ---- 4. 새 간파가 대체 ----

    [Test]
    public void 새_간파는_지금_이동량에서_새_대상으로_이어진다()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Succeeded(0.2f, 5f, 0);
        var other = new Vector3(-6f, 0f, 0f);
        Assert.IsTrue(tracker.Begin(InterruptAttemptForecast.Weak, true, true, true, 1f, Move, Back, 0.3f));
        Assert.IsFalse(tracker.AwaitsSuccessReturn, "앞 성공 복귀 대기는 버린다");
        Near(TargetFlat, tracker.Step(0.3f, Base, true, other), "튀지 않음");
        Near(other, tracker.Step(0.3f + Move, Base, true, other));
    }

    [Test]
    public void 새_간파가_대상_없음이면_복귀()
    {
        InterruptCameraCenterTracker tracker = Centered();
        Assert.IsFalse(tracker.Begin(InterruptAttemptForecast.NoTarget, false, true, true, 1f, Move, Back, 0.5f));
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
    }

    [Test]
    public void 복귀_중_새_간파는_그_자리에서_다시_대상으로()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Return(1f);
        Vector3 partial = tracker.Step(1f + Back * 0.5f, Base, true, Target);
        tracker.Begin(InterruptAttemptForecast.Strong, true, true, true, 1f, Move, Back, 1f + Back * 0.5f);
        Assert.AreEqual(InterruptCameraCenterState.Centering, tracker.State);
        Near(partial, tracker.Step(1f + Back * 0.5f, Base, true, Target));
    }

    // ---- 5. 성공 복귀 ----

    [Test]
    public void 성공은_내_성공_슬로우_복귀_구간까지_유지()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Succeeded(0.2f, 0.3f, 0);
        Assert.IsTrue(tracker.AwaitsSuccessReturn);
        tracker.ObserveSuccessSlow(7, InterruptCameraCenterPhase.SuccessSlow, 0.3f);
        tracker.Step(2f, Base, true, Target);
        Assert.AreEqual(InterruptCameraCenterState.Centering, tracker.State, "슬로우가 보이면 시간표보다 슬로우를 따른다");
        tracker.ObserveSuccessSlow(7, InterruptCameraCenterPhase.SuccessSlowExit, 2.1f);
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
    }

    [Test]
    public void 성공_슬로우가_보였다가_사라지면_복귀()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Succeeded(0.2f, 5f, 0);
        tracker.ObserveSuccessSlow(7, InterruptCameraCenterPhase.SuccessSlow, 0.3f);
        tracker.ObserveSuccessSlow(0, InterruptCameraCenterPhase.SuccessSlowExit, 0.4f);
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
    }

    [Test]
    public void 앞_성공_슬로우는_이번_것이_아니다()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Succeeded(0.2f, 5f, 3);
        tracker.ObserveSuccessSlow(3, InterruptCameraCenterPhase.SuccessSlowExit, 0.3f);
        Assert.AreEqual(InterruptCameraCenterState.Centering, tracker.State, "연속 간파 — 앞 슬로우의 복귀 구간에 끌려가지 않는다");
        tracker.ObserveSuccessSlow(4, InterruptCameraCenterPhase.SuccessSlow, 0.4f);
        tracker.ObserveSuccessSlow(4, InterruptCameraCenterPhase.SuccessSlowExit, 1.5f);
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
    }

    [Test]
    public void 성공_슬로우가_끝내_안_보이면_시간표로_복귀()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Succeeded(0.2f, 1f, 0);
        tracker.ObserveSuccessSlow(0, InterruptCameraCenterPhase.SuccessSlowExit, 0.5f);
        tracker.Step(1.1f, Base, true, Target);
        Assert.AreEqual(InterruptCameraCenterState.Centering, tracker.State);
        tracker.Step(1.2f, Base, true, Target);
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
    }

    [Test]
    public void 성공_뒤_서버_거부는_그_순간_복귀()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Succeeded(0.2f, 5f, 0);
        tracker.Return(0.3f);
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
        Assert.IsFalse(tracker.AwaitsSuccessReturn);
    }

    [Test]
    public void cue_없는_종료는_성공_유지_중이면_그대로()
    {
        InterruptCameraCenterTracker held = Centered();
        held.Succeeded(0.2f, 5f, 0);
        held.ReturnUnlessSucceeded(0.3f);
        Assert.AreEqual(InterruptCameraCenterState.Centering, held.State);

        InterruptCameraCenterTracker pending = Centered();
        pending.ReturnUnlessSucceeded(0.3f);
        Assert.AreEqual(InterruptCameraCenterState.Returning, pending.State);
    }

    [TestCase(SlowMotionPhase.HitStop, ExpectedResult = InterruptCameraCenterPhase.SuccessSlow)]
    [TestCase(SlowMotionPhase.Enter, ExpectedResult = InterruptCameraCenterPhase.SuccessSlow)]
    [TestCase(SlowMotionPhase.Hold, ExpectedResult = InterruptCameraCenterPhase.SuccessSlow)]
    [TestCase(SlowMotionPhase.Exit, ExpectedResult = InterruptCameraCenterPhase.SuccessSlowExit)]
    [TestCase(SlowMotionPhase.FailExit, ExpectedResult = InterruptCameraCenterPhase.SuccessSlowExit)]
    [TestCase(SlowMotionPhase.None, ExpectedResult = InterruptCameraCenterPhase.SuccessSlowExit)]
    public InterruptCameraCenterPhase 슬로우_단계_대응(SlowMotionPhase phase) => InterruptCameraCenterRules.SuccessPhaseOf(phase);

    // ---- 6. 즉시 원래대로 ----

    [Test]
    public void 취소는_즉시_0()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Cancel();
        Assert.AreEqual(InterruptCameraCenterState.Idle, tracker.State);
        Assert.AreEqual(Vector3.zero, tracker.Step(0.5f, Base, true, Target));
    }

    // ---- 7. 디렉터 연결(cue → 상태, 카메라 질의) ----

    static InterruptOwnerCue StrongCue(GameObject target) => new InterruptOwnerCue
    {
        Result = new InterruptJudgeResult { Outcome = InterruptJudgeOutcome.Success },
        Target = target,
    };

    [Test]
    public void 디렉터_스위치_꺼짐이면_시도_cue_에도_그대로()
    {
        var go = new GameObject("InterruptCameraCenterTrackerTests.Director");
        var target = new GameObject("Target");
        var follow = new GameObject("Follow");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            director.CameraCenterEnabled = false;
            ((IInterruptOwnerCueSink)director).OwnerAttempt(StrongCue(target));
            Assert.AreEqual(InterruptCameraCenterState.Idle, director.CameraCenterState);
            Assert.AreEqual(Vector3.zero, director.StepCameraCenter(follow.transform, false, 1f));
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(follow);
        }
    }

    [Test]
    public void 디렉터_시도_cue_는_대상_중심_다른_연출이_잡으면_즉시_버림()
    {
        var go = new GameObject("InterruptCameraCenterTrackerTests.Director");
        var target = new GameObject("Target");
        var follow = new GameObject("Follow");
        try
        {
            target.transform.position = new Vector3(3f, 0f, 0f);
            var director = go.AddComponent<InterruptProductionDirector>();
            Assert.IsTrue(director.CameraCenterEnabled);
            ((IInterruptOwnerCueSink)director).OwnerAttempt(StrongCue(target));
            Assert.AreEqual(InterruptCameraCenterState.Centering, director.CameraCenterState);
            director.StepCameraCenter(follow.transform, false, Time.unscaledTime);
            Near(new Vector3(3f, 0f, 0f), director.StepCameraCenter(follow.transform, false, Time.unscaledTime + 10f));

            Assert.AreEqual(Vector3.zero, director.StepCameraCenter(follow.transform, true, Time.unscaledTime + 10f));
            Assert.AreEqual(InterruptCameraCenterState.Idle, director.CameraCenterState, "연출 뒤 평소 중심 그대로");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(follow);
        }
    }

    [Test]
    public void 디렉터_대상이_파괴되면_복귀()
    {
        var go = new GameObject("InterruptCameraCenterTrackerTests.Director");
        var target = new GameObject("Target");
        var follow = new GameObject("Follow");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            ((IInterruptOwnerCueSink)director).OwnerAttempt(StrongCue(target));
            Object.DestroyImmediate(target);
            director.StepCameraCenter(follow.transform, false, Time.unscaledTime);
            Assert.AreEqual(InterruptCameraCenterState.Returning, director.CameraCenterState);
        }
        finally
        {
            Object.DestroyImmediate(go);
            if (target != null)
                Object.DestroyImmediate(target);
            Object.DestroyImmediate(follow);
        }
    }

    [Test]
    public void 디렉터_대상_없음_cue_는_그대로()
    {
        var go = new GameObject("InterruptCameraCenterTrackerTests.Director");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            ((IInterruptOwnerCueSink)director).OwnerAttempt(new InterruptOwnerCue
            {
                Result = new InterruptJudgeResult { Outcome = InterruptJudgeOutcome.NoTarget },
            });
            Assert.AreEqual(InterruptCameraCenterState.Idle, director.CameraCenterState);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
