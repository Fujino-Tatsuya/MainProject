using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 간파 보정 랩(할 일 139 → 191 5단계 재연결) — QA 기록 한 줄(<see cref="InterruptQaTracker"/>) + 판정 순간 캡처 = 표본 하나 짝짓기
/// (<see cref="InterruptCalibrationPairing"/>)와 판정 시점 실제 창 상태(<see cref="DummyInterruptWindowActual"/>)의 순수 규칙.
/// </summary>
public sealed class InterruptCalibrationCaptureTests
{
    const float Eps = 1e-4f;
    const ulong Actor = 11;
    const ulong Target = 7;
    const uint Window = 3;

    static InterruptJudgeResult Result(InterruptJudgeOutcome outcome, InterruptJudgeFailReason reason = InterruptJudgeFailReason.None) =>
        new InterruptJudgeResult { Outcome = outcome, FailReason = reason, TargetId = Target, WindowId = Window, JudgeTime = 10.4 };

    static InterruptCalibrationJudgeCapture Capture(float time, bool open) =>
        new InterruptCalibrationJudgeCapture
        {
            Time = time,
            RealTime = time + 100f,
            WindowMode = CalibrationWindowMode.Periodic,
            WindowOpenLength = 0.6f,
            WindowClosedLength = 1.5f,
            ActualWindowOpen = open,
            ActualOpenElapsed = open ? 0.2f : (float?)null,
            ActualRemaining = open ? 0.4f : (float?)null,
            TargetDistance = 1.5f,
            TargetAngle = 12f,
        };

    // 매니저 이벤트 순서 그대로: 예측 → (판정 순간 캡처) → 판정 → 서버.
    sealed class Rig
    {
        public readonly InterruptQaTracker Tracker = new InterruptQaTracker();
        public readonly InterruptCalibrationPairing Pairing = new InterruptCalibrationPairing();
        public InterruptCalibrationCompleted? Last;
        public int Samples;

        public Rig()
        {
            Tracker.Completed += r =>
            {
                if (Pairing.OnRecord(r, out InterruptCalibrationCompleted done))
                {
                    Last = done;
                    Samples++;
                }
            };
        }

        public void Press(InterruptJudgeOutcome predicted, bool viaDash = true, ulong actor = Actor, double at = 10.0) =>
            Tracker.Predicted(actor, 1, "Paladin", viaDash, at, Result(predicted,
                predicted == InterruptJudgeOutcome.Failure ? InterruptJudgeFailReason.WindowClosed : InterruptJudgeFailReason.None), at);

        public void Judge(InterruptJudgeOutcome judged, bool open, ulong actor = Actor, InterruptJudgeFailReason reason = InterruptJudgeFailReason.None)
        {
            Pairing.NoteJudged(actor, Capture(10.4f, open));
            Tracker.Judged(actor, Result(judged, reason), 10.4);
        }

        public void Approve(params ulong[] actors) =>
            Tracker.Approved(new InterruptClaimApproval(Target, Window, actors[0], actors[actors.Length - 1], actors));
    }

    // ─── 짝짓기 ────────────────────────────────────────────────────────

    [Test]
    public void 강_예측_판정_성공_승인이_표본_하나가_된다()
    {
        var rig = new Rig();
        rig.Press(InterruptJudgeOutcome.Success);
        rig.Judge(InterruptJudgeOutcome.Success, open: true);
        Assert.AreEqual(0, rig.Samples, "판정 성공은 서버 결과까지 기다린다");

        rig.Approve(Actor);
        Assert.AreEqual(1, rig.Samples);
        InterruptCalibrationCompleted done = rig.Last.Value;
        InterruptCalibrationSample s = done.Sample;

        Assert.IsTrue(done.Succeeded);
        Assert.AreEqual(10.4f, done.JudgeTime, Eps);
        Assert.AreEqual(110.4f, done.JudgeRealTime, Eps);
        Assert.AreEqual(110.4f, s.RealTime.Value, Eps);
        Assert.AreEqual(InterruptAttemptForecast.Strong, s.Forecast);
        Assert.AreEqual(true, s.JudgedSuccess);
        Assert.AreEqual(true, s.ServerSucceeded);
        Assert.AreEqual(InterruptQaServer.ApprovedFirst, s.Record.Server);
        Assert.AreEqual(InterruptQaMismatch.None, s.Record.Mismatch);
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
        Assert.AreEqual(0, rig.Pairing.DroppedCount);
    }

    [Test]
    public void 약_예측_판정_실패는_서버를_기다리지_않고_신고_안_함으로_완성된다()
    {
        var rig = new Rig();
        rig.Press(InterruptJudgeOutcome.Failure);
        rig.Judge(InterruptJudgeOutcome.Failure, open: false, reason: InterruptJudgeFailReason.WindowClosed);

        Assert.AreEqual(1, rig.Samples);
        InterruptCalibrationSample s = rig.Last.Value.Sample;
        Assert.IsFalse(rig.Last.Value.Succeeded);
        Assert.AreEqual(InterruptAttemptForecast.Weak, s.Forecast);
        Assert.AreEqual(false, s.JudgedSuccess);
        Assert.AreEqual(false, s.ServerSucceeded);
        Assert.AreEqual(InterruptQaServer.NotClaimed, s.Record.Server);
        Assert.AreEqual(false, s.ActualWindowOpen);
        Assert.IsNull(s.ActualRemaining);
    }

    [Test]
    public void 판정_성공_뒤_거부면_서버_실패_표본이고_어긋난_이유가_남는다()
    {
        var rig = new Rig();
        rig.Press(InterruptJudgeOutcome.Success);
        rig.Judge(InterruptJudgeOutcome.Success, open: true);
        rig.Tracker.Rejected(new InterruptClaimRejection(Actor, Target, Window, InterruptClaimRejectReason.FirstComeLost, InterruptJudgeFailReason.None));

        Assert.AreEqual(1, rig.Samples);
        InterruptCalibrationSample s = rig.Last.Value.Sample;
        Assert.IsFalse(rig.Last.Value.Succeeded);
        Assert.AreEqual(true, s.JudgedSuccess);
        Assert.AreEqual(false, s.ServerSucceeded);
        Assert.AreEqual(InterruptQaMismatch.FirstCome, s.Record.Mismatch);
    }

    [Test]
    public void 판정_전에_끝난_시도는_표본이_아니고_버림_수로_센다()
    {
        var rig = new Rig();
        rig.Press(InterruptJudgeOutcome.Success);
        rig.Tracker.Abandoned(Actor, InterruptQaJudge.Cut, nameof(SkillEndReason.Cancelled));
        Assert.AreEqual(0, rig.Samples);
        Assert.AreEqual(1, rig.Pairing.DroppedCount);

        // 판정이 빠진 채 다음 시도가 오면 앞 줄은 '교체'로 버려진다
        rig.Press(InterruptJudgeOutcome.Success, at: 11.0);
        rig.Press(InterruptJudgeOutcome.Failure, at: 11.2);
        Assert.AreEqual(2, rig.Pairing.DroppedCount);

        rig.Judge(InterruptJudgeOutcome.Failure, open: false);
        Assert.AreEqual(1, rig.Samples);
        Assert.AreEqual(InterruptAttemptForecast.Weak, rig.Last.Value.Sample.Forecast);
    }

    [Test]
    public void 판정_순간_캡처가_없으면_랩_칸을_비운_채_완성된다()
    {
        var rig = new Rig();
        rig.Press(InterruptJudgeOutcome.NoTarget);
        rig.Tracker.Judged(Actor, Result(InterruptJudgeOutcome.NoTarget, InterruptJudgeFailReason.OutOfRange), 10.4); // NoteJudged 없음

        Assert.AreEqual(1, rig.Samples);
        InterruptCalibrationSample s = rig.Last.Value.Sample;
        Assert.AreEqual(InterruptAttemptForecast.NoTarget, s.Forecast);
        Assert.IsNull(s.JudgedSuccess, "판정 대상 없음 = 일치율 분모 밖");
        Assert.IsNull(s.RealTime);
        Assert.IsNull(s.WindowMode);
        Assert.IsNull(s.TargetDistance);
    }

    [Test]
    public void 근접_간파는_기본으로_무시하고_버림_수에도_안_든다()
    {
        var rig = new Rig();
        rig.Press(InterruptJudgeOutcome.Success, viaDash: false);
        rig.Judge(InterruptJudgeOutcome.Failure, open: false);
        Assert.AreEqual(0, rig.Samples);
        Assert.AreEqual(0, rig.Pairing.DroppedCount);

        rig.Pairing.IncludeMelee = true;
        rig.Press(InterruptJudgeOutcome.Failure, viaDash: false, at: 11.0);
        rig.Judge(InterruptJudgeOutcome.Failure, open: false);
        Assert.AreEqual(1, rig.Samples, "근접 플래그를 켜면 근접 간파도 표본");
    }

    [Test]
    public void 시전자가_다르면_캡처와_줄이_섞이지_않는다()
    {
        var rig = new Rig();
        rig.Press(InterruptJudgeOutcome.Success, actor: 11);
        rig.Press(InterruptJudgeOutcome.Success, actor: 12);
        rig.Judge(InterruptJudgeOutcome.Success, open: true, actor: 11);
        rig.Judge(InterruptJudgeOutcome.Success, open: true, actor: 12);
        rig.Approve(11, 12);

        Assert.AreEqual(2, rig.Samples);
        Assert.AreEqual(12UL, rig.Last.Value.Sample.Record.ActorId);
        Assert.AreEqual(InterruptQaServer.ApprovedSimultaneous, rig.Last.Value.Sample.Record.Server);
        Assert.AreEqual(0, rig.Pairing.DroppedCount);
    }

    [Test]
    public void Clear_는_판정_캡처를_버린다()
    {
        var pairing = new InterruptCalibrationPairing();
        pairing.NoteJudged(Actor, Capture(10.4f, true));
        pairing.Clear();

        var record = new InterruptQaRecord
        {
            ActorId = Actor, ViaDash = true, HasPrediction = true,
            Prediction = Result(InterruptJudgeOutcome.Failure), Judge = InterruptQaJudge.Failure,
            JudgeResult = Result(InterruptJudgeOutcome.Failure), Server = InterruptQaServer.NotClaimed,
        };
        Assert.IsTrue(pairing.OnRecord(record, out InterruptCalibrationCompleted done));
        Assert.IsNull(done.Sample.ActualWindowOpen, "지운 캡처는 붙지 않는다");
        Assert.AreEqual(0, pairing.DroppedCount);
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
