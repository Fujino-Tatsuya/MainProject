using System;
using NUnit.Framework;

/// <summary>
/// 슬로우 모션 산식(<see cref="SlowMotionTimeline"/>)의 단계·last-wins·실패 복귀·누적 손실 규칙.
///
/// 🔴 기본 수치(0.3 / 0.05 / 0.35 …)는 튜닝 대상이라 잠그지 않는다 — 테스트는 읽기 쉬운 값의 프로필을 직접 만든다.
///    길이·시각은 2 진수로 딱 떨어지는 값(0.125 …)을 쓴다 — 0.1f 같은 값은 경계에서 float→double 오차로 단계가 갈린다.
/// </summary>
public sealed class SlowMotionTimelineTests
{
    const double T0 = 8.0; // 0 이 아닌 시작 시각 — 절대 시각과 경과 시간을 섞어 쓴 버그를 잡는다
    const float Eps = 1e-5f;

    // 배율 0.5, 진입 0.125, 유지 0.25, 복귀 0.25, 실패 복귀 0.125 → 정상 종료 = 0.625s.
    static SlowMotionProfile P(float scale = 0.5f, float enter = 0.125f, float hold = 0.25f, float exit = 0.25f,
                               float failExit = 0.125f) =>
        new SlowMotionProfile
        {
            scale = scale,
            enterDuration = enter,
            holdDuration = hold,
            exitDuration = exit,
            failExitDuration = failExit,
        };

    // P() 한 번이 끝까지 갔을 때의 손실: 진입 ∫(1−linear) + 유지 + 복귀 ∫(1−easeOut).
    const double FullLostOfP = 0.125 * 0.5 / 2.0 + 0.25 * 0.5 + 0.5 * 0.25 / 3.0;

    static SlowMotionTimeline Started(SlowMotionProfile p = null)
    {
        var tl = new SlowMotionTimeline();
        tl.Start(T0, p ?? P());
        return tl;
    }

    static double EaseOut(double u) => 1.0 - (1.0 - u) * (1.0 - u);

    // 중점 규칙 수치 적분 — 해석식과 독립적인 기준값.
    static double NumericLost(SlowMotionTimeline tl, double from, double to, double step = 1e-4)
    {
        double sum = 0.0;
        int n = (int)Math.Round((to - from) / step);
        for (int i = 0; i < n; i++)
            sum += (1.0 - tl.ScaleAt(from + (i + 0.5) * step)) * step;
        return sum;
    }

    // ─── 유휴 ─────────────────────────────────────────────────────────

    [Test]
    public void 유휴면_배율_1_단계_None_손실_0()
    {
        var tl = new SlowMotionTimeline();
        Assert.AreEqual(1f, tl.ScaleAt(T0));
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0));
        Assert.IsFalse(tl.IsActiveAt(T0));
        Assert.AreEqual(0.0, tl.LostTimeUntil(T0));
    }

    [Test]
    public void 시작_이전_시각은_유휴()
    {
        var tl = Started();
        Assert.AreEqual(1f, tl.ScaleAt(T0 - 0.0625));
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 - 0.0625));
        Assert.AreEqual(0.0, tl.LostTimeUntil(T0 - 0.0625));
    }

    // ─── 단계 경계 ────────────────────────────────────────────────────

    [Test]
    public void 단계_경계별_배율과_단계()
    {
        var tl = Started();

        Assert.AreEqual(SlowMotionPhase.Enter, tl.PhaseAt(T0));
        Assert.AreEqual(1f, tl.ScaleAt(T0), Eps, "유휴에서 시작하면 1.0 에서 출발");

        Assert.AreEqual(SlowMotionPhase.Enter, tl.PhaseAt(T0 + 0.0625));
        Assert.AreEqual(0.75f, tl.ScaleAt(T0 + 0.0625), Eps, "진입은 linear");

        Assert.AreEqual(SlowMotionPhase.Hold, tl.PhaseAt(T0 + 0.125));
        Assert.AreEqual(0.5f, tl.ScaleAt(T0 + 0.125), Eps);
        Assert.AreEqual(0.5f, tl.ScaleAt(T0 + 0.25), Eps);

        Assert.AreEqual(SlowMotionPhase.Exit, tl.PhaseAt(T0 + 0.375));
        Assert.AreEqual(0.5f, tl.ScaleAt(T0 + 0.375), Eps);
        Assert.AreEqual((float)(0.5 + 0.5 * EaseOut(0.5)), tl.ScaleAt(T0 + 0.5), Eps, "복귀는 ease-out");

        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 0.625));
        Assert.AreEqual(1f, tl.ScaleAt(T0 + 0.625), Eps);
        Assert.IsFalse(tl.IsActiveAt(T0 + 5.0));
        Assert.AreEqual(1f, tl.ScaleAt(T0 + 5.0));
    }

    [Test]
    public void 복귀는_ease_out_이라_중간에_linear_보다_높다()
    {
        var tl = Started();
        const double linearMid = 0.75;
        Assert.Greater(tl.ScaleAt(T0 + 0.5), linearMid);
    }

    // ─── last-wins 재시작 ─────────────────────────────────────────────

    [TestCase(0.0625, SlowMotionPhase.Enter)]
    [TestCase(0.25, SlowMotionPhase.Hold)]
    [TestCase(0.5, SlowMotionPhase.Exit)]
    public void 진행_중_재시작은_현재_배율에서_진입부터(double at, SlowMotionPhase expectedBefore)
    {
        var tl = Started();
        double t = T0 + at;
        Assert.AreEqual(expectedBefore, tl.PhaseAt(t));
        float before = tl.ScaleAt(t);

        tl.Start(t, P(scale: 0.25f));

        Assert.AreEqual(SlowMotionPhase.Enter, tl.PhaseAt(t));
        Assert.AreEqual(before, tl.ScaleAt(t), Eps, "배율이 튀지 않는다");
        Assert.AreEqual(SlowMotionPhase.Hold, tl.PhaseAt(t + 0.125));
        Assert.AreEqual(0.25f, tl.ScaleAt(t + 0.125), Eps, "새 프로필의 배율로 간다");
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(t + 0.625), "새 프로필 길이로 끝난다");
    }

    [Test]
    public void 실패_복귀_중_재시작도_현재_배율에서()
    {
        var tl = Started();
        tl.Fail(T0 + 0.25);
        double t = T0 + 0.3125;
        Assert.AreEqual(SlowMotionPhase.FailExit, tl.PhaseAt(t));
        float before = tl.ScaleAt(t);

        tl.Start(t, P());

        Assert.AreEqual(SlowMotionPhase.Enter, tl.PhaseAt(t));
        Assert.AreEqual(before, tl.ScaleAt(t), Eps);
    }

    [Test]
    public void 종료_후_재시작은_1에서()
    {
        var tl = Started();
        tl.Start(T0 + 1.0, P());
        Assert.AreEqual(1f, tl.ScaleAt(T0 + 1.0), Eps);
        Assert.AreEqual(SlowMotionPhase.Enter, tl.PhaseAt(T0 + 1.0));
    }

    // ─── 실패 복귀 ────────────────────────────────────────────────────

    [TestCase(0.0625)]
    [TestCase(0.25)]
    [TestCase(0.5)]
    public void 진행_중_Fail_은_현재_배율에서_실패_복귀(double at)
    {
        var tl = Started();
        double t = T0 + at;
        float before = tl.ScaleAt(t);

        tl.Fail(t);

        Assert.AreEqual(SlowMotionPhase.FailExit, tl.PhaseAt(t));
        Assert.AreEqual(before, tl.ScaleAt(t), Eps);
        Assert.AreEqual((float)(before + (1.0 - before) * EaseOut(0.5)), tl.ScaleAt(t + 0.0625), Eps, "실패 복귀는 ease-out");
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(t + 0.125), "실패 복귀 길이로 끝난다");
        Assert.AreEqual(1f, tl.ScaleAt(t + 0.125), Eps);
    }

    [Test]
    public void 유휴에서_Fail_은_무시()
    {
        var tl = new SlowMotionTimeline();
        tl.Fail(T0);
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0));
        Assert.AreEqual(0.0, tl.LostTimeUntil(T0 + 1.0));
    }

    [Test]
    public void 종료_후_Fail_은_무시()
    {
        var tl = Started();
        double lost = tl.LostTimeUntil(T0 + 2.0);

        tl.Fail(T0 + 1.0);

        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 1.0));
        Assert.AreEqual(1f, tl.ScaleAt(T0 + 1.0));
        Assert.AreEqual(lost, tl.LostTimeUntil(T0 + 2.0), 1e-12);
    }

    [Test]
    public void 실패_복귀_중_Fail_은_멱등()
    {
        var tl = Started();
        tl.Fail(T0 + 0.25);
        float at = tl.ScaleAt(T0 + 0.34375);

        tl.Fail(T0 + 0.3125);

        Assert.AreEqual(at, tl.ScaleAt(T0 + 0.34375), Eps, "곡선이 다시 시작되지 않는다");
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 0.375));
    }

    // ─── ForceReset ──────────────────────────────────────────────────

    [Test]
    public void ForceReset_은_즉시_1_유휴_누적_보존()
    {
        var tl = Started();
        double t = T0 + 0.25;
        double lostAtReset = tl.LostTimeUntil(t);
        Assert.Greater(lostAtReset, 0.0);

        tl.ForceReset(t);

        Assert.AreEqual(1f, tl.ScaleAt(t));
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(t));
        Assert.AreEqual(lostAtReset, tl.LostTimeUntil(t), 1e-12);
        Assert.AreEqual(lostAtReset, tl.LostTimeUntil(t + 3.0), 1e-12, "리셋 뒤엔 더 쌓이지 않는다");
        Assert.AreEqual(0.5f, tl.ScaleAt(T0 + 0.1875), Eps, "리셋 이전 시각 조회는 그대로");
    }

    [Test]
    public void ForceReset_뒤_Fail_은_무시()
    {
        var tl = Started();
        tl.ForceReset(T0 + 0.25);
        tl.Fail(T0 + 0.3125);
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 0.3125));
    }

    // ─── 누적 손실 ────────────────────────────────────────────────────

    [Test]
    public void 단일_슬로우_손실은_닫힌_식과_같다()
    {
        var tl = Started();
        Assert.AreEqual(FullLostOfP, tl.LostTimeUntil(T0 + 0.625), 1e-9);
        Assert.AreEqual(FullLostOfP, tl.LostTimeUntil(T0 + 9.0), 1e-9);
    }

    [Test]
    public void 단일_슬로우_손실은_수치_적분과_일치()
    {
        var tl = Started();
        foreach (double d in new[] { 0.03, 0.1, 0.17, 0.3, 0.41, 0.6, 1.0 })
            Assert.AreEqual(NumericLost(tl, T0 - 0.1, T0 + d), tl.LostTimeUntil(T0 + d), 1e-5, $"t=T0+{d}");
    }

    [Test]
    public void 재시작_실패_리셋_섞인_시나리오도_수치_적분과_일치()
    {
        var tl = new SlowMotionTimeline();
        tl.Start(T0, P());
        tl.Start(T0 + 0.05, P(scale: 0.2f, enter: 0.07f));   // 진입 중 재시작
        tl.Start(T0 + 0.2, P(scale: 0.6f));                   // 유지 중 재시작(배율이 올라가는 진입)
        tl.Fail(T0 + 0.33);                                   // 유지 중 실패
        tl.Start(T0 + 0.38, P(failExit: 0.2f));               // 실패 복귀 중 재시작
        tl.Start(T0 + 0.85, P());                             // 복귀 중 재시작
        tl.Fail(T0 + 0.9);                                    // 진입 중 실패
        tl.Start(T0 + 1.2, P());                              // 유휴에서 다시
        tl.ForceReset(T0 + 1.45);                             // 유지 중 강제 리셋
        tl.Start(T0 + 1.6, P(enter: 0f, hold: 0.1f));          // 진입 0

        foreach (double d in new[] { 0.02, 0.05, 0.19, 0.33, 0.4, 0.84, 0.95, 1.3, 1.45, 1.5, 1.65, 2.5 })
            Assert.AreEqual(NumericLost(tl, T0, T0 + d), tl.LostTimeUntil(T0 + d), 1e-5, $"t=T0+{d}");
    }

    [Test]
    public void 손실은_단조_비감소()
    {
        var tl = new SlowMotionTimeline();
        tl.Start(T0, P());
        tl.Start(T0 + 0.15, P(scale: 0.9f));
        tl.Fail(T0 + 0.2);
        tl.Start(T0 + 0.25, P(scale: 0.01f));
        tl.ForceReset(T0 + 0.5);
        tl.Start(T0 + 0.7, P());

        double prev = tl.LostTimeUntil(T0 - 1.0);
        for (int i = 0; i <= 3000; i++)
        {
            double t = T0 - 1.0 + i * 1e-3;
            double cur = tl.LostTimeUntil(t);
            Assert.GreaterOrEqual(cur, prev, $"t={t}");
            prev = cur;
        }
    }

    [Test]
    public void 손실은_경과_시간을_넘지_않는다()
    {
        var tl = Started(P(scale: 0.01f, hold: 1f));
        for (int i = 0; i <= 200; i++)
        {
            double d = i * 0.01;
            Assert.LessOrEqual(tl.LostTimeUntil(T0 + d), d + 1e-12);
        }
    }

    // ─── 이벤트 순서 ──────────────────────────────────────────────────

    [Test]
    public void 마지막_이벤트보다_이른_이벤트는_무시()
    {
        var tl = Started();
        tl.Start(T0 + 0.25, P(scale: 0.25f));
        double lost = tl.LostTimeUntil(T0 + 2.0);

        tl.Start(T0 + 0.125, P(scale: 0.9f));
        tl.Fail(T0 + 0.1875);
        tl.ForceReset(T0 + 0.24);

        Assert.AreEqual(SlowMotionPhase.Enter, tl.PhaseAt(T0 + 0.25));
        Assert.AreEqual(lost, tl.LostTimeUntil(T0 + 2.0), 1e-12);
    }

    [Test]
    public void 과거_시각_조회는_그_시각의_구간으로_계산()
    {
        var tl = Started();
        double lostBefore = tl.LostTimeUntil(T0 + 0.1875);
        float scaleBefore = tl.ScaleAt(T0 + 0.1875);

        tl.Start(T0 + 0.25, P(scale: 0.25f));

        Assert.AreEqual(lostBefore, tl.LostTimeUntil(T0 + 0.1875), 1e-12, "나중 이벤트가 과거 값을 바꾸지 않는다");
        Assert.AreEqual(scaleBefore, tl.ScaleAt(T0 + 0.1875));
    }

    [Test]
    public void 같은_시각_이벤트는_나중_것이_이긴다()
    {
        var tl = Started();
        tl.Fail(T0);
        Assert.AreEqual(SlowMotionPhase.FailExit, tl.PhaseAt(T0));
        Assert.AreEqual(1f, tl.ScaleAt(T0), Eps);
    }

    [Test]
    public void 이벤트가_많이_쌓여도_현재_값은_정확()
    {
        var tl = new SlowMotionTimeline();
        for (int i = 0; i < 200; i++)
            tl.Start(T0 + i, P());

        Assert.AreEqual(200 * FullLostOfP, tl.LostTimeUntil(T0 + 500.0), 1e-9);
    }

    // ─── 길이 0 단계 · 클램프 ──────────────────────────────────────────

    [Test]
    public void 진입_0이면_바로_유지()
    {
        var tl = Started(P(enter: 0f));
        Assert.AreEqual(SlowMotionPhase.Hold, tl.PhaseAt(T0));
        Assert.AreEqual(0.5f, tl.ScaleAt(T0), Eps);
    }

    [Test]
    public void 유지_0이면_진입_뒤_바로_복귀()
    {
        var tl = Started(P(hold: 0f));
        Assert.AreEqual(SlowMotionPhase.Exit, tl.PhaseAt(T0 + 0.125));
        Assert.AreEqual(0.5f, tl.ScaleAt(T0 + 0.125), Eps);
    }

    [Test]
    public void 복귀_0이면_유지_끝에_바로_1()
    {
        var tl = Started(P(exit: 0f));
        Assert.AreEqual(SlowMotionPhase.Hold, tl.PhaseAt(T0 + 0.3125));
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 0.375));
        Assert.AreEqual(1f, tl.ScaleAt(T0 + 0.375));
    }

    [Test]
    public void 실패_복귀_0이면_Fail_즉시_1()
    {
        var tl = Started(P(failExit: 0f));
        double lost = tl.LostTimeUntil(T0 + 0.25);

        tl.Fail(T0 + 0.25);

        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 0.25));
        Assert.AreEqual(1f, tl.ScaleAt(T0 + 0.25));
        Assert.AreEqual(lost, tl.LostTimeUntil(T0 + 1.0), 1e-12);
    }

    [Test]
    public void 모든_단계_0이면_시작해도_즉시_유휴()
    {
        var tl = Started(P(enter: 0f, hold: 0f, exit: 0f, failExit: 0f));
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0));
        Assert.IsFalse(tl.IsActiveAt(T0));
        Assert.AreEqual(1f, tl.ScaleAt(T0));
        Assert.AreEqual(0.0, tl.LostTimeUntil(T0 + 1.0));
    }

    [Test]
    public void 음수_길이는_0으로_본다()
    {
        var tl = Started(P(enter: -1f, hold: 0.25f, exit: -0.5f));
        Assert.AreEqual(SlowMotionPhase.Hold, tl.PhaseAt(T0));
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 0.25));
    }

    [TestCase(0f, SlowMotionProfile.MinScale)]
    [TestCase(-3f, SlowMotionProfile.MinScale)]
    [TestCase(2f, 1f)]
    [TestCase(float.NaN, 1f)]
    [TestCase(0.4f, 0.4f)]
    public void 배율은_0_초과_1_이하로_클램프(float authored, float expected)
    {
        var p = P(scale: authored);
        Assert.AreEqual(expected, p.ClampedScale, Eps);

        var tl = Started(p);
        Assert.AreEqual(expected, tl.ScaleAt(T0 + 0.1875), Eps);
    }

    [Test]
    public void 시작_뒤_프로필을_바꿔도_진행_중인_슬로우는_그대로()
    {
        var p = P();
        var tl = Started(p);
        p.scale = 0.1f;
        p.holdDuration = 5f;

        Assert.AreEqual(0.5f, tl.ScaleAt(T0 + 0.1875), Eps);
        Assert.AreEqual(SlowMotionPhase.None, tl.PhaseAt(T0 + 0.625));
    }
}
