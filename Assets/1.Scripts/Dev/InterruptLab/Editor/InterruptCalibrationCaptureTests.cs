using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 간파 예측 보정 씬(할 일 139) 단계 3 — 시도 한 번 = 표본 하나 짝짓기(<see cref="InterruptCalibrationPairing"/>)와
/// 판정 시점 실제 창 상태(<see cref="DummyInterruptWindowActual"/>)의 순수 규칙.
/// </summary>
public sealed class InterruptCalibrationCaptureTests
{
    const float Eps = 1e-4f;

    static InterruptCalibrationOwnerCapture Owner(InterruptAttemptForecast forecast, bool viaDash = true, float pressAge = 0.05f)
    {
        var prediction = new DashInterruptPrediction
        {
            PredictedEndPosition = new Vector3(1f, 0f, 2f),
            PredictedResolveTime = 10.4f,
            WindowRemaining = 0.3f,
            PredictsSuccess = forecast == InterruptAttemptForecast.Strong,
            Reason = forecast == InterruptAttemptForecast.Strong ? DashInterruptPredictReason.Success
                : forecast == InterruptAttemptForecast.Weak ? DashInterruptPredictReason.WindowClosedNow
                : DashInterruptPredictReason.NoTargetInRange,
        };
        return new InterruptCalibrationOwnerCapture
        {
            ViaDash = viaDash,
            PressAge = pressAge,
            Input = new DashInterruptPredictInput { CastTime = 10f, DashRemaining = 0.2f, WindowOpenElapsed = -1f, WindowExpectedDuration = -1f },
            Prediction = prediction,
            TargetDistance = 1.5f,
            TargetAngle = 12f,
        };
    }

    static InterruptCalibrationJudgeCapture Judge(bool succeeded, float time, bool viaDash = true) =>
        new InterruptCalibrationJudgeCapture
        {
            ViaDash = viaDash,
            Succeeded = succeeded,
            Time = time,
            RealTime = time + 100f,
            WindowMode = CalibrationWindowMode.Periodic,
            WindowOpenLength = 0.6f,
            WindowClosedLength = 1.5f,
            ActualWindowOpen = succeeded,
            ActualOpenElapsed = succeeded ? 0.2f : (float?)null,
            ActualRemaining = succeeded ? 0.4f : (float?)null,
        };

    // ─── 짝짓기 ────────────────────────────────────────────────────────

    [Test]
    public void 정상_짝_오너_서버_판정이_표본_하나가_된다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong), 10f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Weak, 10f);
        Assert.IsTrue(pairing.HasPending);

        Assert.IsTrue(pairing.OnJudged(Judge(true, 10.4f), out InterruptCalibrationCompleted done));
        Assert.IsFalse(pairing.HasPending);
        Assert.AreEqual(0, pairing.DroppedCount);

        InterruptCalibrationSample s = done.Sample;
        Assert.IsTrue(done.Succeeded);
        Assert.AreEqual(10.4f, done.JudgeTime, Eps);
        Assert.AreEqual(110.4f, done.JudgeRealTime, Eps);
        Assert.AreEqual(110.4f, s.RealTime.Value, Eps);
        Assert.AreEqual(InterruptAttemptForecast.Strong, s.OwnerForecast);
        Assert.AreEqual(DashInterruptPredictReason.Success, s.OwnerReason);
        Assert.AreEqual(InterruptAttemptForecast.Weak, s.ServerForecast);
        Assert.AreEqual(true, s.ServerSucceeded);
        Assert.AreEqual(0.05f, s.PressAge.Value, Eps);
        Assert.AreEqual(0.2f, s.PredictInput.Value.DashRemaining, Eps);
        Assert.AreEqual(new Vector3(1f, 0f, 2f), s.ResolvePosition.Value);
        Assert.AreEqual(10.4f, s.PredictedResolveTime.Value, Eps);
        Assert.AreEqual(0.3f, s.PredictedWindowRemaining.Value, Eps);
        Assert.AreEqual(1.5f, s.TargetDistance.Value, Eps);
        Assert.AreEqual(12f, s.TargetAngle.Value, Eps);
        Assert.AreEqual(CalibrationWindowMode.Periodic, s.WindowMode);
        Assert.AreEqual(0.6f, s.WindowOpenLength.Value, Eps);
        Assert.AreEqual(1.5f, s.WindowClosedLength.Value, Eps);
        Assert.AreEqual(true, s.ActualWindowOpen);
        Assert.AreEqual(0.2f, s.ActualOpenElapsed.Value, Eps);
        Assert.AreEqual(0.4f, s.ActualRemaining.Value, Eps);
        Assert.IsNull(s.UserAnswer, "답은 질문 창이 Commit 때 채운다");
        Assert.AreEqual(0, s.Seq);
    }

    [Test]
    public void 순서가_뒤바뀌어도_서버_예측_뒤_오너_예측이_같은_표본에_붙는다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Strong, 10f);
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Weak), 10.01f);

        Assert.IsTrue(pairing.OnJudged(Judge(false, 10.5f), out InterruptCalibrationCompleted done));
        Assert.AreEqual(InterruptAttemptForecast.Weak, done.Sample.OwnerForecast);
        Assert.AreEqual(InterruptAttemptForecast.Strong, done.Sample.ServerForecast);
        Assert.AreEqual(false, done.Sample.ServerSucceeded);
        Assert.AreEqual(0, pairing.DroppedCount);
    }

    [Test]
    public void 판정이_안_오면_대기_시간_뒤_버린다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong), 10f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Strong, 10f);

        pairing.Tick(10f + InterruptCalibrationPairing.DefaultPendingTimeout);
        Assert.IsTrue(pairing.HasPending, "경계 시각까지는 기다린다");

        pairing.Tick(10f + InterruptCalibrationPairing.DefaultPendingTimeout + 0.01f);
        Assert.IsFalse(pairing.HasPending);
        Assert.AreEqual(1, pairing.DroppedCount);

        // 늦게 온 판정은 대기 표본이 없어 버린다
        Assert.IsFalse(pairing.OnJudged(Judge(true, 14f), out _));
        Assert.AreEqual(2, pairing.DroppedCount);
    }

    [Test]
    public void 판정이_빠진_채_다음_시도가_오면_앞_표본을_버리고_새로_연다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong), 10f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Strong, 10f);
        // 판정 전 종료 — 판정 없음

        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Weak), 11f);
        Assert.AreEqual(1, pairing.DroppedCount);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Weak, 11f);

        Assert.IsTrue(pairing.OnJudged(Judge(false, 11.4f), out InterruptCalibrationCompleted done));
        Assert.AreEqual(InterruptAttemptForecast.Weak, done.Sample.OwnerForecast);
        Assert.AreEqual(InterruptAttemptForecast.Weak, done.Sample.ServerForecast);
        Assert.AreEqual(1, pairing.DroppedCount);
    }

    [Test]
    public void 오너_예측이_빠진_판정은_오너_칸을_비운_채_완성된다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnServerPredicted(true, InterruptAttemptForecast.NoTarget, 10f);

        Assert.IsTrue(pairing.OnJudged(Judge(false, 10.3f), out InterruptCalibrationCompleted done));
        Assert.IsNull(done.Sample.OwnerForecast);
        Assert.IsNull(done.Sample.PredictInput);
        Assert.IsNull(done.Sample.PressAge);
        Assert.AreEqual(InterruptAttemptForecast.NoTarget, done.Sample.ServerForecast);
    }

    [Test]
    public void 근접_간파는_기본으로_무시하고_대기_표본도_건드리지_않는다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong, viaDash: false), 10f);
        pairing.OnServerPredicted(false, InterruptAttemptForecast.Strong, 10f);
        Assert.IsFalse(pairing.HasPending);
        Assert.IsFalse(pairing.OnJudged(Judge(true, 10.3f, viaDash: false), out _));
        Assert.AreEqual(0, pairing.DroppedCount, "무시한 근접 판정은 버림 수에도 안 든다");

        // 대쉬 시도 사이에 근접 사건이 끼어도 대쉬 표본은 그대로
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong), 11f);
        pairing.OnServerPredicted(false, InterruptAttemptForecast.Weak, 11f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Strong, 11f);
        Assert.IsTrue(pairing.OnJudged(Judge(true, 11.3f), out InterruptCalibrationCompleted done));
        Assert.AreEqual(InterruptAttemptForecast.Strong, done.Sample.ServerForecast);
        Assert.AreEqual(0, pairing.DroppedCount);
    }

    [Test]
    public void 근접_플래그를_켜면_근접_간파도_짝짓는다()
    {
        var pairing = new InterruptCalibrationPairing { IncludeMelee = true };
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Weak, viaDash: false, pressAge: 0f), 10f);
        pairing.OnServerPredicted(false, InterruptAttemptForecast.Weak, 10f);
        Assert.IsTrue(pairing.OnJudged(Judge(false, 10.3f, viaDash: false), out InterruptCalibrationCompleted done));
        Assert.AreEqual(InterruptAttemptForecast.Weak, done.Sample.OwnerForecast);
    }

    [Test]
    public void 연속_두_번은_표본_두_개로_섞이지_않는다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong), 10f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Strong, 10f);
        Assert.IsTrue(pairing.OnJudged(Judge(true, 10.4f), out InterruptCalibrationCompleted first));

        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Weak), 10.5f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Weak, 10.5f);
        Assert.IsTrue(pairing.OnJudged(Judge(false, 10.9f), out InterruptCalibrationCompleted second));

        Assert.AreNotSame(first.Sample, second.Sample);
        Assert.AreEqual(InterruptAttemptForecast.Strong, first.Sample.OwnerForecast);
        Assert.AreEqual(true, first.Sample.ServerSucceeded);
        Assert.AreEqual(InterruptAttemptForecast.Weak, second.Sample.OwnerForecast);
        Assert.AreEqual(false, second.Sample.ServerSucceeded);
        Assert.AreEqual(0, pairing.DroppedCount);
    }

    [Test]
    public void 재예측은_대기_표본에_덧붙고_원래_예측을_남긴다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Weak), 10f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Weak, 10f);
        pairing.OnOwnerRepredicted(true, InterruptAttemptForecast.Strong, 10.05f);
        pairing.OnServerRepredicted(true, InterruptAttemptForecast.Strong, 10.06f);

        Assert.IsTrue(pairing.OnJudged(Judge(true, 10.35f), out InterruptCalibrationCompleted done));
        InterruptCalibrationSample s = done.Sample;
        Assert.AreEqual(InterruptAttemptForecast.Weak, s.OwnerForecast, "원래 예측");
        Assert.AreEqual(DashInterruptPredictReason.WindowClosedNow, s.OwnerReason);
        Assert.AreEqual(true, s.OwnerRepredicted);
        Assert.AreEqual(InterruptAttemptForecast.Strong, s.OwnerFinalForecast);
        Assert.AreEqual(InterruptAttemptForecast.Weak, s.ServerForecast);
        Assert.AreEqual(true, s.ServerRepredicted);
        Assert.AreEqual(InterruptAttemptForecast.Strong, s.ServerFinalForecast);
        Assert.AreEqual(0, pairing.DroppedCount, "재예측은 새 표본을 열지 않는다");
    }

    [Test]
    public void 재예측이_없으면_최종은_원래와_같고_짝없는_재예측은_무시한다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerRepredicted(true, InterruptAttemptForecast.Strong, 9f);
        pairing.OnServerRepredicted(true, InterruptAttemptForecast.Strong, 9f);
        Assert.IsFalse(pairing.HasPending, "대기 표본 없이 온 재예측은 표본을 열지 않는다");

        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong), 10f);
        pairing.OnServerRepredicted(true, InterruptAttemptForecast.Strong, 10.01f); // 서버 예측이 아직 없다 — 무시
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Strong, 10.02f);
        Assert.IsTrue(pairing.OnJudged(Judge(true, 10.35f), out InterruptCalibrationCompleted done));
        Assert.AreEqual(false, done.Sample.OwnerRepredicted);
        Assert.AreEqual(InterruptAttemptForecast.Strong, done.Sample.OwnerFinalForecast);
        Assert.AreEqual(false, done.Sample.ServerRepredicted);
        Assert.AreEqual(InterruptAttemptForecast.Strong, done.Sample.ServerFinalForecast);
    }

    [Test]
    public void 요약에_재예측_수와_오너_최종_예측_일치율()
    {
        var stats = new InterruptCalibrationStats();
        var pairing = new InterruptCalibrationPairing();
        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Weak), 10f);
        pairing.OnServerPredicted(true, InterruptAttemptForecast.Weak, 10f);
        pairing.OnOwnerRepredicted(true, InterruptAttemptForecast.Strong, 10.05f);
        pairing.OnJudged(Judge(true, 10.35f), out InterruptCalibrationCompleted done);
        done.Sample.UserAnswer = true;
        stats.Add(done.Sample);

        Assert.AreEqual(1, stats.OwnerRepredictedCount);
        Assert.AreEqual(0, stats.ServerRepredictedCount);
        string summary = stats.BuildSummary(null);
        StringAssert.Contains("오너 예측 ↔ 답: 0/1 (0.0%)", summary, "누른 순간 예측 기준은 그대로");
        StringAssert.Contains("재예측: 오너 1 · 서버 0", summary);
        StringAssert.Contains("오너 최종 예측 ↔ 답: 1/1 (100.0%)", summary);
    }

    [Test]
    public void Clear_는_대기_표본을_버린다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.Clear();
        Assert.AreEqual(0, pairing.DroppedCount);

        pairing.OnOwnerPredicted(Owner(InterruptAttemptForecast.Strong), 10f);
        pairing.Clear();
        Assert.IsFalse(pairing.HasPending);
        Assert.AreEqual(1, pairing.DroppedCount);
    }

    [Test]
    public void 창_모드와_모름_값_변환()
    {
        Assert.AreEqual(CalibrationWindowMode.Indefinite, InterruptCalibrationPairing.ToCalibrationMode(DummyInterruptWindowMode.Unbounded));
        Assert.AreEqual(CalibrationWindowMode.Periodic, InterruptCalibrationPairing.ToCalibrationMode(DummyInterruptWindowMode.Periodic));
        Assert.IsNull(InterruptCalibrationPairing.KnownOrNull(-1f));
        Assert.AreEqual(0f, InterruptCalibrationPairing.KnownOrNull(0f));
    }

    // ─── 판정 시점 실제 창 상태 ────────────────────────────────────────

    [Test]
    public void 주기_모드_열림_중엔_경과와_남은_시간을_스케줄로_잰다()
    {
        var schedule = new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Periodic, 0.6f, 1.5f);
        var actual = new DummyInterruptWindowActual();

        schedule.Tick(0.2f, false);
        actual.Observe(5.2f, false, schedule);
        Assert.IsTrue(actual.IsOpen);
        Assert.AreEqual(5.0f, actual.OpenSince, Eps);
        Assert.AreEqual(0.25f, actual.OpenElapsed(5.25f), Eps);
        Assert.AreEqual(0.35f, actual.Remaining(5.25f), Eps);

        schedule.Tick(0.5f, false); // 열림 0.7 → 닫힘 0.1
        actual.Observe(5.7f, false, schedule);
        Assert.IsFalse(actual.IsOpen);
        Assert.AreEqual(-1f, actual.OpenElapsed(5.7f));
        Assert.AreEqual(-1f, actual.Remaining(5.7f));
    }

    [Test]
    public void 회복_중엔_닫힘이고_회복_뒤_닫힘부터_다시_연다()
    {
        var schedule = new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Periodic, 0.6f, 1.5f);
        var actual = new DummyInterruptWindowActual();

        schedule.Tick(0.1f, true);
        actual.Observe(1f, true, schedule);
        Assert.IsFalse(actual.IsOpen);

        schedule.Tick(0.1f, false); // 회복 끝 → 닫힘 처음
        actual.Observe(1.1f, false, schedule);
        Assert.IsFalse(actual.IsOpen);

        schedule.Tick(1.6f, false); // 닫힘 1.5 넘김 → 열림 0.1
        actual.Observe(2.7f, false, schedule);
        Assert.IsTrue(actual.IsOpen);
        Assert.AreEqual(2.6f, actual.OpenSince, Eps);
        Assert.AreEqual(0.5f, actual.Remaining(2.7f), Eps);
    }

    [Test]
    public void 무기한_모드는_열린_틱부터_재고_남은_시간은_모름()
    {
        var schedule = new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Unbounded);
        var actual = new DummyInterruptWindowActual();

        actual.Observe(1f, true, schedule);
        Assert.IsFalse(actual.IsOpen);

        actual.Observe(3f, false, schedule);
        actual.Observe(3.5f, false, schedule);
        Assert.IsTrue(actual.IsOpen);
        Assert.AreEqual(3f, actual.OpenSince, Eps, "열린 첫 틱 시각을 유지한다");
        Assert.AreEqual(1f, actual.OpenElapsed(4f), Eps);
        Assert.AreEqual(-1f, actual.Remaining(4f));
    }

    [Test]
    public void 닫힘_0인_주기는_계속_열림이라_남은_시간_모름()
    {
        var schedule = new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Periodic, 0.6f, 0f);
        var actual = new DummyInterruptWindowActual();
        schedule.Tick(0.1f, false);
        actual.Observe(1f, false, schedule);
        schedule.Tick(2f, false);
        actual.Observe(3f, false, schedule);
        Assert.IsTrue(actual.IsOpen);
        Assert.AreEqual(1f, actual.OpenSince, Eps);
        Assert.AreEqual(-1f, actual.Remaining(3f));
    }
}
