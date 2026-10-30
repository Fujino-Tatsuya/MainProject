using BeaverLobby.Player.Dash;
using NUnit.Framework;

/// <summary>
/// 간파 예측 보정 씬(할 일 139) 단계 4 — 질문 흐름(<see cref="InterruptCalibrationQuestionFlow"/>: 대기 → 정지 조건·답·대기열)과
/// 정지 배율 보관(<see cref="CalibrationTimePause"/>)·질문 창 문구의 순수 규칙.
/// </summary>
public sealed class InterruptCalibrationQuestionTests
{
    const float Eps = 1e-5f;

    static InterruptCalibrationCompleted Completed(float judgeRealTime, bool succeeded = true, int tag = 0)
    {
        var sample = new InterruptCalibrationSample { ServerSucceeded = succeeded, PressAge = tag };
        return new InterruptCalibrationCompleted(sample, succeeded, judgeRealTime - 100f, judgeRealTime);
    }

    static InterruptCalibrationQuestionFlow AskingFlow(float judgeRealTime = 10f)
    {
        var flow = new InterruptCalibrationQuestionFlow();
        flow.Enqueue(Completed(judgeRealTime));
        Assert.IsTrue(flow.Tick(judgeRealTime + 1f, true));
        return flow;
    }

    // ─── 묻는 시점 ─────────────────────────────────────────────────────

    [Test]
    public void 기본값은_0_5초_3초_대기열_8()
    {
        var flow = new InterruptCalibrationQuestionFlow();
        Assert.AreEqual(0.5f, flow.SettleDelay, Eps);
        Assert.AreEqual(3f, flow.MaxWait, Eps);
        Assert.AreEqual(8, flow.QueueCapacity);
        Assert.AreEqual(CalibrationQuestionPhase.Idle, flow.Phase);
    }

    [TestCase(0.49f, true, ExpectedResult = false)]  // 결과 보는 중
    [TestCase(0.5f, true, ExpectedResult = true)]    // 0.5초 + 평소 상태
    [TestCase(0.5f, false, ExpectedResult = false)]  // 슬로우·히트스톱 진행 중
    [TestCase(2.99f, false, ExpectedResult = false)]
    [TestCase(3f, false, ExpectedResult = true)]     // 상한 — 평소로 안 돌아와도 묻는다
    [TestCase(10f, false, ExpectedResult = true)]
    public bool 판정_뒤_지연과_평소_상태로_묻는다(float since, bool settled)
    {
        return InterruptCalibrationQuestionFlow.ShouldAsk(100f + since, 100f, 0.5f, 3f, settled);
    }

    [Test]
    public void 상한이_지연보다_짧으면_지연이_먼저다()
    {
        Assert.IsFalse(InterruptCalibrationQuestionFlow.ShouldAsk(100.4f, 100f, 0.5f, 0.1f, false));
        Assert.IsTrue(InterruptCalibrationQuestionFlow.ShouldAsk(100.5f, 100f, 0.5f, 0.1f, false));
    }

    [Test]
    public void 지연_0_이면_평소_상태일_때_바로_묻는다()
    {
        Assert.IsTrue(InterruptCalibrationQuestionFlow.ShouldAsk(100f, 100f, 0f, 3f, true));
    }

    [Test]
    public void 표본이_오면_대기_조건이_차면_한_번만_묻기_시작한다()
    {
        var flow = new InterruptCalibrationQuestionFlow();
        flow.Enqueue(Completed(10f));
        Assert.AreEqual(CalibrationQuestionPhase.Waiting, flow.Phase);

        Assert.IsFalse(flow.Tick(10.2f, true));
        Assert.IsFalse(flow.Tick(10.6f, false));
        Assert.AreEqual(CalibrationQuestionPhase.Waiting, flow.Phase);

        Assert.IsTrue(flow.Tick(10.7f, true));
        Assert.IsTrue(flow.IsAsking);
        Assert.IsFalse(flow.Tick(10.8f, true), "묻는 중엔 다시 시작하지 않는다");
    }

    [Test]
    public void 표본_없으면_묻지_않는다()
    {
        var flow = new InterruptCalibrationQuestionFlow();
        Assert.IsFalse(flow.Tick(1000f, true));
        Assert.AreEqual(CalibrationQuestionPhase.Idle, flow.Phase);
    }

    [Test]
    public void 표본이_null_이면_무시한다()
    {
        var flow = new InterruptCalibrationQuestionFlow();
        flow.Enqueue(new InterruptCalibrationCompleted(null, true, 0f, 0f));
        Assert.AreEqual(CalibrationQuestionPhase.Idle, flow.Phase);
    }

    // ─── 답 ────────────────────────────────────────────────────────────

    [TestCase(CalibrationAnswer.ShouldSucceed, true, true)]
    [TestCase(CalibrationAnswer.ShouldFail, true, false)]
    public void 참거짓_답은_기록한다(CalibrationAnswer answer, bool commit, bool value)
    {
        var flow = AskingFlow();
        InterruptCalibrationSample asked = flow.Current.Sample;

        Assert.IsTrue(flow.Answer(answer, out CalibrationAnswerOutcome outcome));
        Assert.AreEqual(commit, outcome.Commit);
        Assert.AreEqual(value, outcome.Value);
        Assert.AreSame(asked, outcome.Answered.Sample);
        Assert.AreEqual(1, flow.AnsweredCount);
        Assert.AreEqual(0, flow.SkippedCount);
        Assert.AreEqual(CalibrationQuestionPhase.Idle, flow.Phase);
    }

    [Test]
    public void 건너뛰기는_기록하지_않고_센다()
    {
        var flow = AskingFlow();
        Assert.IsTrue(flow.Answer(CalibrationAnswer.Skip, out CalibrationAnswerOutcome outcome));
        Assert.IsFalse(outcome.Commit);
        Assert.AreEqual(0, flow.AnsweredCount);
        Assert.AreEqual(1, flow.SkippedCount);
        Assert.AreEqual(CalibrationQuestionPhase.Idle, flow.Phase);
    }

    [Test]
    public void 묻는_중이_아니면_답을_무시한다()
    {
        var flow = new InterruptCalibrationQuestionFlow();
        Assert.IsFalse(flow.Answer(CalibrationAnswer.ShouldSucceed, out _));

        flow.Enqueue(Completed(10f));
        Assert.IsFalse(flow.Answer(CalibrationAnswer.ShouldSucceed, out _), "결과 보는 중(대기)엔 답을 받지 않는다");
        Assert.AreEqual(0, flow.AnsweredCount);
        Assert.AreEqual(CalibrationQuestionPhase.Waiting, flow.Phase);
    }

    [TestCase(CalibrationAnswer.ShouldSucceed, ExpectedResult = true)]
    [TestCase(CalibrationAnswer.ShouldFail, ExpectedResult = false)]
    [TestCase(CalibrationAnswer.Skip, ExpectedResult = null)]
    public bool? 답_매핑(CalibrationAnswer answer) => InterruptCalibrationQuestionFlow.ToUserAnswer(answer);

    // ─── 대기열 ────────────────────────────────────────────────────────

    [Test]
    public void 묻는_중에_온_표본은_줄_서고_답하면_바로_다시_묻는다()
    {
        var flow = AskingFlow(10f);
        flow.Enqueue(Completed(10.3f, tag: 2));
        Assert.AreEqual(1, flow.QueuedCount);
        Assert.IsTrue(flow.IsAsking, "줄 선 표본이 지금 질문을 바꾸지 않는다");

        Assert.IsTrue(flow.Answer(CalibrationAnswer.ShouldFail, out _));
        Assert.AreEqual(CalibrationQuestionPhase.Waiting, flow.Phase);
        Assert.AreEqual(0, flow.QueuedCount);
        Assert.AreEqual(2f, flow.Current.Sample.PressAge.Value, Eps);

        // 판정이 이미 오래전 — 평소 상태면 바로 묻는다
        Assert.IsTrue(flow.Tick(30f, true));
    }

    [Test]
    public void 결과_보는_중에_온_표본도_순서대로_묻는다()
    {
        var flow = new InterruptCalibrationQuestionFlow();
        flow.Enqueue(Completed(10f, tag: 1));
        flow.Enqueue(Completed(10.2f, tag: 2));
        flow.Enqueue(Completed(10.4f, tag: 3));
        Assert.AreEqual(2, flow.QueuedCount);

        for (int expected = 1; expected <= 3; expected++)
        {
            Assert.IsTrue(flow.Tick(20f, true));
            Assert.AreEqual(expected, flow.Current.Sample.PressAge.Value, Eps);
            Assert.IsTrue(flow.Answer(CalibrationAnswer.ShouldSucceed, out _));
        }

        Assert.AreEqual(CalibrationQuestionPhase.Idle, flow.Phase);
        Assert.AreEqual(3, flow.AnsweredCount);
    }

    [Test]
    public void 대기열이_차면_새로_온_표본을_버리고_센다()
    {
        var flow = AskingFlow();
        flow.QueueCapacity = 2;
        flow.Enqueue(Completed(11f, tag: 1));
        flow.Enqueue(Completed(11f, tag: 2));
        flow.Enqueue(Completed(11f, tag: 3));

        Assert.AreEqual(2, flow.QueuedCount);
        Assert.AreEqual(1, flow.OverflowDroppedCount);

        flow.Answer(CalibrationAnswer.Skip, out _);
        Assert.AreEqual(1f, flow.Current.Sample.PressAge.Value, Eps, "앞에 선 표본이 남는다");
    }

    [Test]
    public void 대기열_0_이면_묻는_중_표본만_둔다()
    {
        var flow = AskingFlow();
        flow.QueueCapacity = 0;
        flow.Enqueue(Completed(11f));
        Assert.AreEqual(0, flow.QueuedCount);
        Assert.AreEqual(1, flow.OverflowDroppedCount);
    }

    [Test]
    public void 정리는_기록_없이_비우고_개수는_둔다()
    {
        var flow = AskingFlow();
        flow.Enqueue(Completed(11f));
        flow.Answer(CalibrationAnswer.Skip, out _);
        flow.Tick(30f, true);

        flow.Clear();
        Assert.AreEqual(CalibrationQuestionPhase.Idle, flow.Phase);
        Assert.AreEqual(0, flow.QueuedCount);
        Assert.AreEqual(1, flow.SkippedCount);
        Assert.IsFalse(flow.Answer(CalibrationAnswer.ShouldSucceed, out _));
    }

    // ─── 정지 배율 ─────────────────────────────────────────────────────

    [Test]
    public void 정지는_멈춘_순간_배율로_돌아간다()
    {
        var pause = new CalibrationTimePause();
        Assert.IsTrue(pause.Begin(0.25f));
        Assert.IsTrue(pause.IsPaused);
        Assert.AreEqual(0.25f, pause.SavedScale, Eps);

        Assert.IsFalse(pause.Begin(0f), "멈춘 중 다시 멈춰도 처음 배율을 지킨다");
        Assert.AreEqual(0.25f, pause.End().Value, Eps);
        Assert.IsFalse(pause.IsPaused);
    }

    [Test]
    public void 멈추지_않았으면_풀_배율이_없다()
    {
        var pause = new CalibrationTimePause();
        Assert.IsNull(pause.End());
        pause.Begin(1f);
        pause.End();
        Assert.IsNull(pause.End(), "두 번 풀지 않는다");
    }

    [Test]
    public void 이미_0_인_배율을_기억하면_1_로_돌려놓는다()
    {
        var pause = new CalibrationTimePause();
        pause.Begin(0f);
        Assert.AreEqual(1f, pause.End().Value, Eps);
        Assert.AreEqual(0f, CalibrationTimePause.HeldScale, Eps);
    }

    // ─── 문구 ──────────────────────────────────────────────────────────

    [Test]
    public void 질문_창_요약은_예측_결과_실제_창을_보인다()
    {
        var s = new InterruptCalibrationSample
        {
            OwnerForecast = InterruptAttemptForecast.Weak,
            OwnerReason = DashInterruptPredictReason.WindowClosesBeforeResolve,
            ServerForecast = InterruptAttemptForecast.Strong,
            ServerSucceeded = true,
            ActualWindowOpen = true,
            ActualOpenElapsed = 0.25f,
            ActualRemaining = null,
        };
        string text = InterruptCalibrationQuestionPanel.DescribeSample(s);

        StringAssert.Contains("오너 예측: 약 (WindowClosesBeforeResolve)", text);
        StringAssert.Contains("서버 예측: 강", text);
        StringAssert.Contains("서버 결과: 성공", text);
        StringAssert.Contains("실제 창: 열림 · 경과 0.250s · 남음 -", text);
    }

    [Test]
    public void 값_없는_표본_요약은_빈_칸_표시()
    {
        string text = InterruptCalibrationQuestionPanel.DescribeSample(new InterruptCalibrationSample());
        StringAssert.Contains("오너 예측: - (-)", text);
        StringAssert.Contains("서버 결과: -", text);
        Assert.AreEqual("-", InterruptCalibrationQuestionPanel.DescribeSample(null));
        Assert.AreEqual("대상 없음", InterruptCalibrationQuestionPanel.DescribeForecast(InterruptAttemptForecast.NoTarget));
    }
}
