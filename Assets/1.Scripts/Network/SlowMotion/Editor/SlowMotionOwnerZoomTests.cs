using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// 오너 줌 한 번(<see cref="SlowMotionOwnerZoom"/>, 할 일 132) — 실제 슬로우 세션(<see cref="SlowMotionSession"/>)을 1/64초 프레임으로 돌려
/// 오프셋 배율 시퀀스의 줌인(배율이 줄어드는 구간)·줌아웃(늘어나는 구간) 횟수를 센다. 평평한 구간은 구간을 끊지 않는다.
/// 프로필은 디렉터 프리팹 기본값(강 0.15/0.05/0.5/0.25, 약 0.5/0.05/0.2/0.25, 성공 0.25 + 히트스톱 0.05@0.02/0.05/1.2/0.4), 배율 강 0.7·약 0.93·성공 0.85.
/// </summary>
public sealed class SlowMotionOwnerZoomTests
{
    const ulong Me = 1;
    const ulong Other = 2;
    const double Frame = 1.0 / 64.0;
    const double End = 3.0;
    const float Eps = 1e-4f;

    static SlowMotionProfile Strong() => new SlowMotionProfile
        { scale = 0.15f, enterDuration = 0.05f, holdDuration = 0.5f, exitDuration = 0.25f, failExitDuration = 0.1f };
    static SlowMotionProfile Weak() => new SlowMotionProfile
        { scale = 0.5f, enterDuration = 0.05f, holdDuration = 0.2f, exitDuration = 0.25f, failExitDuration = 0.1f };
    static SlowMotionProfile Success(bool hitStop = true) => new SlowMotionProfile
    {
        scale = 0.25f, hitStopDuration = hitStop ? 0.05f : 0f, hitStopScale = 0.02f,
        enterDuration = 0.05f, holdDuration = 1.2f, exitDuration = 0.4f, failExitDuration = 0.1f,
    };

    sealed class Run
    {
        public readonly List<float> Multipliers = new List<float>();
        public readonly List<float> Targets = new List<float>();
        public readonly List<double> Times = new List<double>();

        public int ZoomIns => CountRuns(Multipliers, -1);
        public int ZoomOuts => CountRuns(Multipliers, +1);
        public int TargetDrops => CountRuns(Targets, -1);
        public float Min => Multipliers.Count == 0 ? 1f : Multipliers.Min();
        public float Last => Multipliers[Multipliers.Count - 1];

        public float At(double t)
        {
            int i = Times.FindIndex(x => x >= t - 1e-9);
            return Multipliers[i < 0 ? Multipliers.Count - 1 : i];
        }

        public float MinBetween(double from, double to)
        {
            float min = 1f;
            for (int i = 0; i < Times.Count; i++)
                if (Times[i] >= from && Times[i] <= to)
                    min = Math.Min(min, Multipliers[i]);
            return min;
        }

        static int CountRuns(List<float> values, int sign)
        {
            int runs = 0;
            int last = 0;
            for (int i = 1; i < values.Count; i++)
            {
                float d = values[i] - values[i - 1];
                if (Math.Abs(d) <= 1e-6f)
                    continue; // 평평한 구간은 방향을 끊지 않는다
                int s = d > 0f ? 1 : -1;
                if (s == sign && last != sign)
                    runs++;
                last = s;
            }
            return runs;
        }
    }

    /// <summary>events: (시각, 이벤트). awaiting: 그 시각 오너 상태가 강 시도 결론 전인가.</summary>
    static Run Simulate((double time, SlowMotionEvent e)[] events, Func<double, bool> awaiting)
    {
        var session = new SlowMotionSession();
        var zoom = new SlowMotionOwnerZoom();
        var run = new Run();
        int next = 0;

        for (int frame = 0; frame * Frame <= End; frame++)
        {
            double t = frame * Frame;
            while (next < events.Length && events[next].time <= t + 1e-9)
                session.Apply(events[next++].e);

            bool active = session.IsActiveAt(t);
            var input = new SlowMotionOwnerZoomInput { Mine = active && session.CurrentTriggerClientId == Me };
            if (input.Mine)
            {
                input.TriggerId = session.CurrentTriggerId;
                input.Purpose = session.CurrentPurpose;
                input.Phase = session.PhaseAt(t);
                input.CurrentScale = session.ScaleAt(t);
                input.ProfileScale = session.CurrentProfileScale;
                input.ZoomFactor = InterruptProductionRules.SelectOwnerZoomFactor(input.Purpose, 0.7f, 0.93f, 0.85f);
                input.AwaitingStrongResult = awaiting(t);
            }

            run.Multipliers.Add(zoom.Step(input, (float)Frame));
            run.Targets.Add(zoom.TargetDepth);
            run.Times.Add(t);
        }

        return run;
    }

    static (double, SlowMotionEvent) Start(double t, uint id, ulong caster, SlowMotionProfile p, SlowMotionPurpose purpose) =>
        (t, SlowMotionEvent.CreateStart(id, t, caster, p, purpose));

    static void AssertOnce(Run run, string label)
    {
        Assert.AreEqual(1, run.ZoomIns, $"{label}: 줌인 횟수");
        Assert.AreEqual(1, run.ZoomOuts, $"{label}: 줌아웃 횟수");
        Assert.AreEqual(1, run.TargetDrops, $"{label}: 목표 깊이는 마지막 복귀에서만 준다");
        Assert.AreEqual(1f, run.Last, $"{label}: 끝나면 정확히 1");
    }

    // (a) 강 시도 → 유지 중 성공
    [Test]
    public void StrongAttempt_SuccessDuringHold_ZoomsOnce_KeepsStrongFactor()
    {
        const double hit = 20 * Frame; // 시도 유지(0.05~0.55) 안
        Run run = Simulate(new[]
        {
            Start(0, 1, Me, Strong(), SlowMotionPurpose.Attempt),
            Start(hit, 2, Me, Success(), SlowMotionPurpose.Success),
        }, t => t < hit);

        AssertOnce(run, "강 → 유지 중 성공");
        Assert.AreEqual(0.7f, run.Min, Eps, "성공은 강 시도의 더 깊은 배율을 이어받는다");
        Assert.AreEqual(0.7f, run.At(1.5), Eps, "성공 유지 중에도 0.7");
    }

    // (b) 강 시도 → 유지가 끝나 복귀 중 성공 — 다시 줌인하지 않는다
    [TestCase(true)]
    [TestCase(false)]
    public void StrongAttempt_SuccessDuringExit_NoSecondZoomIn(bool hitStop)
    {
        const double hit = 42 * Frame; // 시도 복귀(0.55~0.8) 안
        Run run = Simulate(new[]
        {
            Start(0, 1, Me, Strong(), SlowMotionPurpose.Attempt),
            Start(hit, 2, Me, Success(hitStop), SlowMotionPurpose.Success),
        }, t => t < hit);

        AssertOnce(run, $"강 → 복귀 중 성공 (히트스톱 {hitStop})");
        Assert.AreEqual(0.7f, run.MinBetween(0.55, hit), Eps, "강 시도 결론 전이면 복귀 중에도 줌을 붙잡는다");
    }

    // (c) 약 시도 → 성공 — 줌인 한 번, 성공 배율로 더 깊게만
    [Test]
    public void WeakAttempt_Success_ZoomsDeeperOnly()
    {
        const double hit = 9 * Frame; // 약 유지(0.05~0.25) 안
        Run run = Simulate(new[]
        {
            Start(0, 1, Me, Weak(), SlowMotionPurpose.AttemptWeak),
            Start(hit, 2, Me, Success(), SlowMotionPurpose.Success),
        }, t => false);

        AssertOnce(run, "약 → 성공");
        Assert.AreEqual(0.93f, run.At(hit - Frame), Eps, "약 시도 깊이");
        Assert.AreEqual(0.85f, run.Min, Eps, "성공 배율(더 깊은 쪽)로 이동");
    }

    // (d) 강 시도 → 실패
    [Test]
    public void StrongAttempt_Fail_ZoomsOnce()
    {
        const double hit = 20 * Frame;
        Run run = Simulate(new[]
        {
            Start(0, 1, Me, Strong(), SlowMotionPurpose.Attempt),
            (hit, SlowMotionEvent.CreateFail(1, hit)),
        }, t => t < hit);

        AssertOnce(run, "강 → 실패");
        Assert.AreEqual(1f, run.At(hit + 0.1 + 0.12 + Frame), "실패 복귀 + 감쇠 뒤 원래 거리");
    }

    // (d') 강 시도 → 결론 없이 시도 슬로우가 끝남 — 복귀 중엔 붙잡고, 슬로우가 끝나면 한 번 빠진다
    [Test]
    public void StrongAttempt_NoResult_ReleasesWhenSlowEnds()
    {
        Run run = Simulate(new[] { Start(0, 1, Me, Strong(), SlowMotionPurpose.Attempt) }, t => true);

        AssertOnce(run, "강 → 결론 없음");
        Assert.AreEqual(0.7f, run.At(0.75), Eps, "복귀 중엔 붙잡는다");
        Assert.AreEqual(1f, run.At(0.8 + 0.12 + Frame), "슬로우 끝 + 감쇠 뒤 원래 거리");
    }

    // 약 시도 → 실패 — 기존대로 슬로우 곡선을 따라 한 번
    [Test]
    public void WeakAttempt_Fail_ZoomsOnce()
    {
        Run run = Simulate(new[] { Start(0, 1, Me, Weak(), SlowMotionPurpose.AttemptWeak) }, t => false);

        AssertOnce(run, "약 → 실패");
        Assert.AreEqual(0.93f, run.Min, Eps);
    }

    // (e) 대상 없음(슬로우 없음)·남의 슬로우 — 줌 없음
    [Test]
    public void NoTarget_NoZoom()
    {
        Run run = Simulate(new (double, SlowMotionEvent)[0], t => false);
        Assert.IsTrue(run.Multipliers.TrueForAll(m => m == 1f));
    }

    [Test]
    public void OthersSlow_NoZoom()
    {
        Run run = Simulate(new[]
        {
            Start(0, 1, Other, Strong(), SlowMotionPurpose.Attempt),
            Start(20 * Frame, 2, Other, Success(), SlowMotionPurpose.Success),
        }, t => true);
        Assert.IsTrue(run.Multipliers.TrueForAll(m => m == 1f));
    }

    // (f) 남의 슬로우가 덮음 — 세션이 끝나 한 번 빠지고, 뒤에 온 내 성공은 이어받지 않는다(새 세션)
    [Test]
    public void OthersSlowOverrides_EndsSession()
    {
        const double other = 20 * Frame;
        const double mySuccess = 80 * Frame;
        Run run = Simulate(new[]
        {
            Start(0, 1, Me, Strong(), SlowMotionPurpose.Attempt),
            Start(other, 2, Other, Strong(), SlowMotionPurpose.Attempt),
            Start(mySuccess, 3, Me, Success(), SlowMotionPurpose.Success),
        }, t => t < other);

        Assert.AreEqual(1f, run.At(other + 0.12 + Frame), "남의 슬로우가 덮으면 원래 거리로");
        Assert.AreEqual(0.85f, run.MinBetween(mySuccess, End), Eps, "끊긴 뒤의 성공은 새 세션 — 성공 배율");
        Assert.AreEqual(1f, run.Last);
    }

    // (f) 강제 리셋 — 한 번 빠진다
    [Test]
    public void ForcedReset_ZoomsOutOnce()
    {
        const double reset = 20 * Frame;
        Run run = Simulate(new[]
        {
            Start(0, 1, Me, Strong(), SlowMotionPurpose.Attempt),
            (reset, SlowMotionEvent.CreateReset(1, reset)),
        }, t => t < reset);

        AssertOnce(run, "강제 리셋");
        Assert.AreEqual(1f, run.At(reset + 0.12 + Frame));
    }

    // 내 새 시도가 앞 세션을 덮으면(성공 아님) 배율을 물려받지 않는다
    [Test]
    public void NewAttemptOfMine_StartsNewChain()
    {
        const double second = 42 * Frame; // 강 시도 복귀 중 내 새 시도
        Run run = Simulate(new[]
        {
            Start(0, 1, Me, Strong(), SlowMotionPurpose.Attempt),
            Start(second, 2, Me, Weak(), SlowMotionPurpose.AttemptWeak),
        }, t => t < second);

        Assert.AreEqual(0.93f, run.At(second + 0.24), 1e-3f, "약 시도 배율로 옮겨 간다");
        Assert.AreEqual(1f, run.Last);
    }
}
