using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 카메라 대상 중심 적용(할 일 196 단계 2 → 11-05 제압 간파 전용) — 상태 기계 전이(이동·유지·추적·복귀), 새 제압 간파 대체, 대상 소실, 대상 없음 무동작,
/// 성공 복귀(발동자 성공 슬로우 복귀 구간·앞 슬로우 무시·시간표 대체), 다른 연출이 카메라를 잡음(디렉터),
/// 규칙·디렉터 — 일반·대쉬는 안 움직임 · 제압은 오너(판정 순간)·팀원(서버 사건) 모두 · 오너 판정 + 서버 사건 중복은 한 번만 · 예측 보정.
/// </summary>
public sealed class InterruptCameraCenterTrackerTests
{
    const float Move = 0.1f;
    const float Back = 0.2f;
    static readonly Vector3 Base = Vector3.zero;
    static readonly Vector3 Target = new Vector3(4f, 3f, 2f);
    static readonly Vector3 TargetFlat = new Vector3(4f, 0f, 2f);

    // 시간표 복귀가 멀어(99초) 슬로우 관찰 없이는 유지된다.
    static bool BeginAt(InterruptCameraCenterTracker tracker, float now, bool hasTarget = true, float ratio = 1f, float move = Move) =>
        tracker.BeginSuccess(hasTarget, ratio, move, Back, 99f, 0, now);

    static InterruptCameraCenterTracker Centered(float ratio = 1f)
    {
        var tracker = new InterruptCameraCenterTracker();
        Assert.IsTrue(BeginAt(tracker, 0f, true, ratio));
        tracker.Step(0f, Base, true, Target);
        tracker.Step(Move, Base, true, Target);
        return tracker;
    }

    static void Near(Vector3 expected, Vector3 actual, string message = null) =>
        Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), message ?? $"{expected} ≠ {actual}");

    // ---- 1. 이동·유지·추적 ----

    [Test]
    public void 제압_성공은_이동_시간_동안_대상_쪽으로_보간하고_수평만()
    {
        var tracker = new InterruptCameraCenterTracker();
        Assert.IsTrue(BeginAt(tracker, 0f));
        Assert.AreEqual(InterruptCameraCenterState.Centering, tracker.State);
        Assert.IsTrue(tracker.AwaitsSuccessReturn, "시작하자마자 성공 복귀 대기");
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
        BeginAt(tracker, 0f, true, 1f, 0f);
        Near(TargetFlat, tracker.Step(0f, Base, true, Target));
    }

    // ---- 2. 무동작 ----

    [Test]
    public void 대상_없음은_그대로()
    {
        var tracker = new InterruptCameraCenterTracker();
        Assert.IsFalse(BeginAt(tracker, 0f, false));
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
        BeginAt(tracker, 0f);
        tracker.Step(0f, Base, true, Target);
        Vector3 partial = tracker.Step(Move * 0.5f, Base, true, Target);
        tracker.Return(Move * 0.5f);
        Near(partial, tracker.Step(Move * 0.5f, Base, true, Target));
    }

    // ---- 4. 새 제압 간파가 대체 ----

    [Test]
    public void 새_제압_간파는_지금_이동량에서_새_대상으로_이어진다()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Succeeded(0.2f, 5f, 3);
        var other = new Vector3(-6f, 0f, 0f);
        Assert.IsTrue(tracker.BeginSuccess(true, 1f, Move, Back, 99f, 0, 0.3f));
        Near(TargetFlat, tracker.Step(0.3f, Base, true, other), "튀지 않음");
        Near(other, tracker.Step(0.3f + Move, Base, true, other));
    }

    [Test]
    public void 새_제압_간파가_대상_없음이면_복귀()
    {
        InterruptCameraCenterTracker tracker = Centered();
        Assert.IsFalse(BeginAt(tracker, 0.5f, false));
        Assert.AreEqual(InterruptCameraCenterState.Returning, tracker.State);
    }

    [Test]
    public void 복귀_중_새_제압_간파는_그_자리에서_다시_대상으로()
    {
        InterruptCameraCenterTracker tracker = Centered();
        tracker.Return(1f);
        Vector3 partial = tracker.Step(1f + Back * 0.5f, Base, true, Target);
        BeginAt(tracker, 1f + Back * 0.5f);
        Assert.AreEqual(InterruptCameraCenterState.Centering, tracker.State);
        Near(partial, tracker.Step(1f + Back * 0.5f, Base, true, Target));
    }

    // ---- 5. 성공 복귀 ----

    [Test]
    public void 성공은_발동자_성공_슬로우_복귀_구간까지_유지()
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

    // ---- 7. 규칙 — 제압만 · 중복 한 번 · 보정 ----

    [TestCase(InterruptPresentationTier.Normal, true, ExpectedResult = false)]
    [TestCase(InterruptPresentationTier.Dash, true, ExpectedResult = false)]
    [TestCase(InterruptPresentationTier.Fail, true, ExpectedResult = false)]
    [TestCase(InterruptPresentationTier.Suppress, true, ExpectedResult = true)]
    [TestCase(InterruptPresentationTier.Suppress, false, ExpectedResult = false)]
    public bool 제압_간파일_때만_적용(InterruptPresentationTier tier, bool switchOn) =>
        InterruptCameraCenterRules.AppliesOnSuccess(tier, switchOn);

    [TestCase(InterruptPresentationTier.Normal, false, ExpectedResult = InterruptCameraCenterSuccessAction.None)]
    [TestCase(InterruptPresentationTier.Dash, false, ExpectedResult = InterruptCameraCenterSuccessAction.None)]
    [TestCase(InterruptPresentationTier.Suppress, false, ExpectedResult = InterruptCameraCenterSuccessAction.Begin)]
    [TestCase(InterruptPresentationTier.Suppress, true, ExpectedResult = InterruptCameraCenterSuccessAction.None)]
    [TestCase(InterruptPresentationTier.Normal, true, ExpectedResult = InterruptCameraCenterSuccessAction.Return)]
    [TestCase(InterruptPresentationTier.Dash, true, ExpectedResult = InterruptCameraCenterSuccessAction.Return)]
    public InterruptCameraCenterSuccessAction 서버_성공_사건_처리(InterruptPresentationTier serverTier, bool ownerAlreadyCentered) =>
        InterruptCameraCenterRules.OnServerSuccess(serverTier, true, ownerAlreadyCentered);

    [Test]
    public void 서버_성공_사건은_스위치_꺼짐이면_안_움직임() =>
        Assert.AreEqual(InterruptCameraCenterSuccessAction.None,
            InterruptCameraCenterRules.OnServerSuccess(InterruptPresentationTier.Suppress, false, false));

    // ---- 8. 디렉터 연결(판정 cue·서버 사건 → 상태, 카메라 질의) ----

    sealed class DirectorScope : System.IDisposable
    {
        public readonly GameObject Go = new GameObject("InterruptCameraCenterTrackerTests.Director");
        public readonly GameObject Target = new GameObject("Target");
        public readonly GameObject Follow = new GameObject("Follow");
        public readonly InterruptProductionDirector Director;

        public DirectorScope()
        {
            Director = Go.AddComponent<InterruptProductionDirector>();
            Target.transform.position = new Vector3(3f, 0f, 0f);
        }

        public Vector3 Step(float dt, bool busy = false) => Director.StepCameraCenter(Follow.transform.position, busy, Time.unscaledTime + dt);

        public void Dispose()
        {
            InterruptProductionDirector.DebugSuppressForecastOverride = null;
            Object.DestroyImmediate(Go);
            if (Target != null)
                Object.DestroyImmediate(Target);
            Object.DestroyImmediate(Follow);
        }
    }

    // 오너 판정 성공 cue — suppress = 판정 순간 제압 예측(219, 디버그 덮어쓰기로 고정).
    static void OwnerJudged(InterruptProductionDirector director, GameObject target, bool suppress, bool viaDash = false)
    {
        InterruptProductionDirector.DebugSuppressForecastOverride = _ => suppress;
        ((IInterruptOwnerCueSink)director).OwnerJudgeSucceeded(new InterruptOwnerCue
        {
            Result = new InterruptJudgeResult { Outcome = InterruptJudgeOutcome.Success },
            Target = target,
            FlashPosition = target != null ? target.transform.position : new Vector3(5f, 0f, 0f),
            ViaDash = viaDash,
        });
    }

    [Test]
    public void 디렉터_시도_cue_는_안_움직임()
    {
        using var s = new DirectorScope();
        ((IInterruptOwnerCueSink)s.Director).OwnerAttempt(new InterruptOwnerCue
        {
            Result = new InterruptJudgeResult { Outcome = InterruptJudgeOutcome.Success },
            Target = s.Target,
        });
        Assert.AreEqual(InterruptCameraCenterState.Idle, s.Director.CameraCenterState, "클릭 즉시 이동 없음(11-05)");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void 디렉터_일반_대쉬_판정은_안_움직임(bool viaDash)
    {
        using var s = new DirectorScope();
        OwnerJudged(s.Director, s.Target, false, viaDash);
        Assert.AreEqual(InterruptCameraCenterState.Idle, s.Director.CameraCenterState);
        Assert.AreEqual(Vector3.zero, s.Step(1f));
    }

    [Test]
    public void 디렉터_오너_제압_판정은_바로_대상_중심_다른_연출이_잡으면_즉시_버림()
    {
        using var s = new DirectorScope();
        OwnerJudged(s.Director, s.Target, true);
        Assert.AreEqual(InterruptCameraCenterState.Centering, s.Director.CameraCenterState);
        s.Step(0f);
        Near(new Vector3(3f, 0f, 0f), s.Step(0.5f));
        Assert.AreEqual(Vector3.zero, s.Step(0.5f, true));
        Assert.AreEqual(InterruptCameraCenterState.Idle, s.Director.CameraCenterState, "연출 뒤 평소 중심 그대로");
    }

    [Test]
    public void 디렉터_스위치_꺼짐이면_제압도_그대로()
    {
        using var s = new DirectorScope();
        s.Director.CameraCenterEnabled = false;
        OwnerJudged(s.Director, s.Target, true);
        Assert.AreEqual(InterruptCameraCenterState.Idle, s.Director.CameraCenterState);
        s.Director.CameraCenterServerSucceeded(InterruptPresentationTier.Suppress, false, 7, s.Target.transform.position, Time.unscaledTime);
        Assert.AreEqual(InterruptCameraCenterState.Idle, s.Director.CameraCenterState);
    }

    [Test]
    public void 디렉터_오너_판정과_서버_사건이_겹치면_한_번만()
    {
        using var s = new DirectorScope();
        OwnerJudged(s.Director, s.Target, true);
        s.Step(0f);
        Vector3 mid = s.Step(Move * 0.5f);
        s.Director.CameraCenterServerSucceeded(InterruptPresentationTier.Suppress, true, 0, s.Target.transform.position,
            Time.unscaledTime + Move * 0.5f);
        Assert.AreEqual(InterruptCameraCenterState.Centering, s.Director.CameraCenterState);
        Near(mid, s.Step(Move * 0.5f), "보간을 다시 시작하지 않는다");
    }

    [Test]
    public void 디렉터_오너_제압_예측이_틀리면_서버_사건으로_복귀()
    {
        using var s = new DirectorScope();
        OwnerJudged(s.Director, s.Target, true);
        s.Director.CameraCenterServerSucceeded(InterruptPresentationTier.Normal, true, 0, s.Target.transform.position, Time.unscaledTime);
        Assert.AreEqual(InterruptCameraCenterState.Returning, s.Director.CameraCenterState);
    }

    [Test]
    public void 디렉터_오너가_제압을_놓치면_서버_사건으로_옮긴다()
    {
        using var s = new DirectorScope();
        OwnerJudged(s.Director, s.Target, false);
        Assert.AreEqual(InterruptCameraCenterState.Idle, s.Director.CameraCenterState);
        s.Director.CameraCenterServerSucceeded(InterruptPresentationTier.Suppress, false, 0, s.Target.transform.position, Time.unscaledTime);
        Assert.AreEqual(InterruptCameraCenterState.Centering, s.Director.CameraCenterState);
    }

    [TestCase(InterruptPresentationTier.Normal)]
    [TestCase(InterruptPresentationTier.Dash)]
    public void 디렉터_팀원_화면_일반_대쉬_서버_사건은_안_움직임(InterruptPresentationTier tier)
    {
        using var s = new DirectorScope();
        s.Director.CameraCenterServerSucceeded(tier, false, 7, s.Target.transform.position, Time.unscaledTime);
        Assert.AreEqual(InterruptCameraCenterState.Idle, s.Director.CameraCenterState);
    }

    [Test]
    public void 디렉터_팀원_화면_제압_서버_사건은_적중_지점_쪽으로()
    {
        // 이 테스트에는 발동자 플레이어가 없어 대상을 못 찾는다 → 적중 지점(고정 점)을 본다.
        using var s = new DirectorScope();
        var point = new Vector3(-2f, 1f, 4f);
        s.Director.CameraCenterServerSucceeded(InterruptPresentationTier.Suppress, false, 7, point, Time.unscaledTime);
        Assert.AreEqual(InterruptCameraCenterState.Centering, s.Director.CameraCenterState);
        s.Step(0f);
        Near(new Vector3(-2f, 0f, 4f), s.Step(0.5f), "수평만");
    }

    [Test]
    public void 디렉터_대상이_파괴되면_복귀()
    {
        using var s = new DirectorScope();
        OwnerJudged(s.Director, s.Target, true);
        Object.DestroyImmediate(s.Target);
        s.Step(0f);
        Assert.AreEqual(InterruptCameraCenterState.Returning, s.Director.CameraCenterState);
    }

    [Test]
    public void 디렉터_서버_거부는_내_제압만_복귀()
    {
        using var s = new DirectorScope();
        s.Director.CameraCenterServerSucceeded(InterruptPresentationTier.Suppress, false, 7, s.Target.transform.position, Time.unscaledTime);
        ((IInterruptOwnerCueSink)s.Director).OwnerEnded(InterruptOwnerEndReason.ServerRejected);
        Assert.AreEqual(InterruptCameraCenterState.Centering, s.Director.CameraCenterState, "팀원(7) 제압으로 옮긴 중이면 그대로");

        OwnerJudged(s.Director, s.Target, true);
        ((IInterruptOwnerCueSink)s.Director).OwnerEnded(InterruptOwnerEndReason.ServerRejected);
        Assert.AreEqual(InterruptCameraCenterState.Returning, s.Director.CameraCenterState);
    }

    [Test]
    public void 디렉터_판정_실패_종료는_팀원_제압_중심을_건드리지_않음()
    {
        using var s = new DirectorScope();
        s.Director.CameraCenterServerSucceeded(InterruptPresentationTier.Suppress, false, 7, s.Target.transform.position, Time.unscaledTime);
        ((IInterruptOwnerCueSink)s.Director).OwnerEnded(InterruptOwnerEndReason.JudgeFailed);
        Assert.AreEqual(InterruptCameraCenterState.Centering, s.Director.CameraCenterState);
    }
}
