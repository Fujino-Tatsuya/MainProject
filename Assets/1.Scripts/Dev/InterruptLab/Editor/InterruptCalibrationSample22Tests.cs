using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 139-5 → 139-6 — 보정 표본 22번(세션 139calib-20261029_170741) '무기한 창 추정 오류' 재현과 해결.
/// <list type="bullet">
/// <item>무기한 허수아비(<see cref="TrainingDummyInterruptCycle"/>, 게이트 Unbounded)는 시간으로 닫히지 않는다 — 닫힘 전이는 간파 성공뿐이다.</item>
/// <item>창 기록(<see cref="InterruptWindowHistory"/>)은 '열림 → 성공으로 닫힘'을 창 길이로 센다 — 관측 중앙값 = 지난 성공들까지 걸린 시간의 중앙값(1.24323).</item>
/// <item>139-5(관측 중앙값만): 열린 뒤 0.924s 에 누르면 추정 남은 0.319s &lt; 판정까지 0.345s → 약(WindowClosesBeforeResolve). 서버 창은 판정 때도 열려 있어 성공.</item>
/// <item>139-6(해결): 창 길이는 로컬로 아는 값(<see cref="InterruptWindowLengthResolver"/>) 우선 — 랩 무기한 게이트 = +∞ → 남은 창 +∞ → 강.</item>
/// </list>
/// 이전 열림 길이 3개는 관측 중앙값 1.24323 이 나오게 고른 값이다(그 세션의 창 전이 원본 기록은 남아 있지 않다).
/// 조사 보고: _lane-artifacts/MainProject-Worktree3/139-5/window-inventory.md
/// </summary>
public sealed class InterruptCalibrationSample22Tests
{
    // CSV seq 22 오너 예측 입력 칸.
    const float CastTime = 84.99547f;
    const float DashRemaining = 0.22197f;
    const float PrepDelay = 0.34473f;
    const float OpenElapsedAtPress = 0.92383f;
    const float ExpectedDurationInCsv = 1.24323f;

    // 랩 허수아비 회복 2초 = 취약 2·idle 0(DebugDummyInterruptRecovery.SplitRecovery).
    static TrainingDummyInterruptCycle LabCycle() => new TrainingDummyInterruptCycle(2f, 0f);

    // TrainingDummyInterrupt.OnStateChanged 와 같은 기록 — 복제값이 바뀔 때만 '열림 여부'를 적는다.
    static void RecordIfChanged(InterruptWindowHistory history, TrainingDummyInterruptCycle cycle, double serverTime, bool changed)
    {
        if (changed)
            history.Record(serverTime, cycle.IsInterruptible);
    }

    // 랩 허수아비 게이트(DebugDummyInterruptWindowMode)와 같은 길이 — 스케줄이 정한다.
    sealed class ScheduleGate : ITrainingDummyWindowGate
    {
        readonly DummyInterruptWindowSchedule _schedule;
        public ScheduleGate(DummyInterruptWindowSchedule schedule) => _schedule = schedule;
        public bool TickWindowOpen(float deltaTime, bool recovering) => _schedule.Tick(deltaTime, recovering);
        public float OpenWindowLength => _schedule.OpenWindowLength;
    }

    [Test]
    public void 표본22_무기한_창_해결_로컬_길이_무기한이면_관측_중앙값을_안_써서_강()
    {
        TrainingDummyInterruptCycle cycle = LabCycle();
        var history = new InterruptWindowHistory();
        double t = 0d;
        history.Record(t, cycle.IsInterruptible); // 스폰 시작 상태(열림)

        // 이전 세 번: 열린 뒤 이만큼 지나 간파 성공 → 취약 2초 → 다시 무기한 열림.
        float[] untilSuccess = { 1.27088f, 1.24323f, 1.21558f };
        foreach (float len in untilSuccess)
        {
            // 성공 전까지는 아무리 시간을 흘려도 닫히지 않는다(무기한).
            Assert.IsFalse(cycle.Tick(len), "무기한 창은 시간으로 닫히지 않는다");
            Assert.AreEqual(0f, cycle.Remaining, "무기한 열림 = 남은 시간 없음");
            t += len;

            Assert.IsTrue(cycle.TryInterrupt());
            RecordIfChanged(history, cycle, t, true); // 성공 = 닫힘 기록

            t += 2d;
            RecordIfChanged(history, cycle, t, cycle.Tick(2f)); // 취약 → idle 0 → 열림이 한 틱에 끝 상태 하나로
            Assert.IsTrue(cycle.IsInterruptible);
        }

        Assert.AreEqual(7, history.Count, "열림 1 + (닫힘·열림) × 3 — 회복 중 열림 기록 없음");
        Assert.IsTrue(history.TryGetTypicalOpenDuration(out double typical));
        Assert.AreEqual(ExpectedDurationInCsv, typical, 1e-5, "추정 길이 = 성공까지 걸린 시간의 중앙값");

        // seq 22: 다시 열린 뒤 0.924s 에 누름.
        double press = t + OpenElapsedAtPress;

        // 139-5 동작(관측 중앙값만): 추정 남은 0.3194 < 판정까지 0.34473 → 약.
        history.Estimate(press, cycle.IsInterruptible, out float openElapsed, out float observed);
        Assert.AreEqual(OpenElapsedAtPress, openElapsed, 1e-4f);
        Assert.AreEqual(ExpectedDurationInCsv, observed, 1e-5f);
        DashInterruptPrediction before = DashInterruptPredictor.Predict(Seq22Input(openElapsed, observed));
        Assert.AreEqual(0.3194f, before.WindowRemaining, 1e-3f);
        Assert.AreEqual(PrepDelay, before.TimeToResolve(CastTime), 1e-4f);
        Assert.AreEqual(InterruptAttemptForecast.Weak, before.Forecast);

        // 139-6: 서버(솔로 호스트 = 오너) 예측은 랩 무기한 게이트 값을 직접 읽는다 → 길이 +∞.
        var gate = new ScheduleGate(new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Unbounded));
        float local = InterruptWindowLengthResolver.ForTrainingDummy(true, gate);
        Assert.IsTrue(float.IsPositiveInfinity(local), "랩 무기한 = 마감 없음");
        history.Estimate(press, cycle.IsInterruptible, local, out openElapsed, out float expected);
        Assert.IsTrue(float.IsPositiveInfinity(expected), "로컬 길이가 관측 중앙값보다 우선");

        DashInterruptPrediction p = DashInterruptPredictor.Predict(Seq22Input(openElapsed, expected));
        Assert.IsTrue(float.IsPositiveInfinity(p.WindowRemaining), "무기한 = 남은 창 무한");
        Assert.AreEqual(DashInterruptPredictReason.Success, p.Reason);
        Assert.AreEqual(InterruptAttemptForecast.Strong, p.Forecast);

        // 실제 서버 창: 판정까지 시간을 흘려도 그대로 열림·남은 시간 없음 → 성공(CSV server_succeeded=true·actual_remaining 빈 칸).
        Assert.IsFalse(cycle.Tick(PrepDelay));
        Assert.IsTrue(cycle.IsInterruptible);
        Assert.AreEqual(0f, cycle.Remaining);
        Assert.IsTrue(cycle.TryInterrupt(), "서버 판정은 성공");
    }

    static DashInterruptPredictInput Seq22Input(float openElapsed, float expected) => new DashInterruptPredictInput
    {
        CastTime = CastTime,
        CastPosition = new Vector3(0.41469f, 0.015f, 1.14153f),
        DashDirection = new Vector3(0f, 0f, 1f),
        DashSpeed = 16f,
        DashRemaining = DashRemaining,
        SkillStartTime = CastTime,
        PrepDelay = PrepDelay,
        HasTargetInRange = true,
        TargetWindowOpen = true,
        WindowOpenElapsed = openElapsed,
        WindowExpectedDuration = expected,
    };

    [Test]
    public void 표본22_무기한_창_추정_오류_기본_허수아비_순환도_회복_중_닫힘_중복은_길이로_세지_않는다()
    {
        // 본게임 허수아비 기본값(취약 3·idle 1): 취약 → idle 이 '닫힘 → 닫힘'으로 한 번 더 적힌다. 길이는 열림 → 닫힘만 센다.
        var cycle = new TrainingDummyInterruptCycle(3f, 1f);
        var history = new InterruptWindowHistory();
        history.Record(0d, cycle.IsInterruptible);

        Assert.IsTrue(cycle.TryInterrupt());
        RecordIfChanged(history, cycle, 0.5d, true);
        RecordIfChanged(history, cycle, 3.5d, cycle.Tick(3f)); // 취약 → idle
        Assert.AreEqual(TrainingDummyInterruptState.Idle, cycle.State);
        RecordIfChanged(history, cycle, 4.5d, cycle.Tick(1f)); // idle → 열림

        Assert.AreEqual(4, history.Count);
        Assert.IsTrue(history.TryGetTypicalOpenDuration(out double typical));
        Assert.AreEqual(0.5d, typical, 1e-9, "창 길이 = 성공까지 걸린 시간 하나뿐");
    }
}
