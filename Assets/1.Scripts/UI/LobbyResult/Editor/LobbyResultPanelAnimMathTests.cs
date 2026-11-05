using EuniTween;
using UnityEngine;
using NUnit.Framework;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215·220) 계산 — 진행도·카운트업 지연·투명도/크기·카운트업 숫자·감속 곡선·끝 펀치.
/// </summary>
public sealed class LobbyResultPanelAnimMathTests
{
    [Test]
    public void Progress_ClampsAndZeroDurationIsInstant()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.Progress(0f, 0.2f), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.Progress(0.1f, 0.2f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Progress(5f, 0.2f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.Progress(-1f, 0.2f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Progress(0f, 0f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Progress(0f, -1f), 1e-6f);
    }

    [Test]
    public void CountUpProgress_WaitsForDelay()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpProgress(0.1f, 0.15f, 0.8f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpProgress(0.15f, 0.15f, 0.8f), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.CountUpProgress(0.55f, 0.15f, 0.8f), 1e-5f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CountUpProgress(2f, 0.15f, 0.8f), 1e-6f);
        // 지연 음수 = 0 취급, 지속 0 = 지연 뒤 즉시 1
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.CountUpProgress(0.4f, -1f, 0.8f), 1e-5f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CountUpProgress(0.2f, 0.15f, 0f), 1e-6f);
    }

    [Test]
    public void AlphaAndScale_FromVisibility()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.Alpha(-0.2f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Alpha(1.1f), 1e-6f);
        Assert.AreEqual(0.95f, LobbyResultPanelAnimMath.Scale(0f, 0.95f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.Scale(1f, 0.95f), 1e-6f);
        Assert.AreEqual(0.975f, LobbyResultPanelAnimMath.Scale(0.5f, 0.95f), 1e-6f);
        // 곡선 넘침(Back 류)은 크기에 그대로
        Assert.Greater(LobbyResultPanelAnimMath.Scale(1.1f, 0.95f), 1f);
    }

    [Test]
    public void CountUpValue_ZeroToFinal_NeverOvershoots()
    {
        Assert.AreEqual(0, LobbyResultPanelAnimMath.CountUpValue(48210, 0f));
        Assert.AreEqual(0, LobbyResultPanelAnimMath.CountUpValue(48210, -0.5f));
        Assert.AreEqual(24105, LobbyResultPanelAnimMath.CountUpValue(48210, 0.5f));
        Assert.AreEqual(48210, LobbyResultPanelAnimMath.CountUpValue(48210, 1f));
        Assert.AreEqual(48210, LobbyResultPanelAnimMath.CountUpValue(48210, 1.3f));
        Assert.AreEqual(0, LobbyResultPanelAnimMath.CountUpValue(0, 0.7f));
        Assert.AreEqual(int.MaxValue, LobbyResultPanelAnimMath.CountUpValue(int.MaxValue, 1f));
        Assert.LessOrEqual(LobbyResultPanelAnimMath.CountUpValue(int.MaxValue, 0.999999f), int.MaxValue);
        // 220: 소수 버림 — 최종값은 진행도 1 에서만(작은 값이 감속 꼬리 동안 미리 최종값에 멈추지 않게)
        Assert.AreEqual(11, LobbyResultPanelAnimMath.CountUpValue(12, 0.99f));
        Assert.AreEqual(11, LobbyResultPanelAnimMath.CountUpValue(12, 0.9999f));
        Assert.AreEqual(12, LobbyResultPanelAnimMath.CountUpValue(12, 1f));
        Assert.AreEqual(48209, LobbyResultPanelAnimMath.CountUpValue(48210, 0.99999f));
        for (float p = 0f; p < 1f; p += 0.001f)
            Assert.Less(LobbyResultPanelAnimMath.CountUpValue(12, p), 12);
    }

    [Test]
    public void CountUpSeconds_ZeroToFinal_FormattedNeverExceedsFinal()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpSeconds(754.4f, 0f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpSeconds(754.4f, -1f), 1e-6f);
        Assert.AreEqual(377.2f, LobbyResultPanelAnimMath.CountUpSeconds(754.4f, 0.5f), 1e-3f);
        Assert.AreEqual(754.4f, LobbyResultPanelAnimMath.CountUpSeconds(754.4f, 1f), 1e-6f);
        Assert.AreEqual(754.4f, LobbyResultPanelAnimMath.CountUpSeconds(754.4f, 1.4f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpSeconds(-3f, 0.5f), 1e-6f);

        // 표기 = 기존 mm:ss(초 아래 버림). 진행 중 어떤 값도 최종 표기보다 크지 않다.
        Assert.AreEqual("12:34", SessionResult.FormatTime(754.4f));
        Assert.AreEqual("00:00", SessionResult.FormatTime(-1f));
        Assert.AreEqual("06:17", SessionResult.FormatTime(LobbyResultPanelAnimMath.CountUpSeconds(754.4f, 0.5f)));
        for (float p = 0f; p < 1f; p += 0.01f)
            Assert.LessOrEqual(LobbyResultPanelAnimMath.CountUpSeconds(754.4f, p), 754.4f);
    }

    [Test]
    public void CrownCycle_FirstRightAfterCountUp_ThenEveryInterval()
    {
        // 숫자 지연 0.15 + 숫자 0.8 + 펀치 0(꺼짐) + 왕관 지연 0.05 = 1.0 초에 첫 점프, 0.6 초 동안
        float first = LobbyResultPanelAnimMath.CrownFirstStart(0.15f, 0.8f, 0f, 0.05f);
        Assert.AreEqual(1f, first, 1e-5f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CrownFirstStart(-1f, -1f, -1f, -1f), 1e-6f);
        // 220 기본: 0.15 + 1.5 + 펀치 0.28 + 0.05 = 1.98 초 — 펀치가 끝난 직후
        Assert.AreEqual(1.98f, LobbyResultPanelAnimMath.CrownFirstStart(0.15f, 1.5f, 0.28f, 0.05f), 1e-5f);

        Assert.AreEqual(3f, LobbyResultPanelAnimMath.CrownPeriod(0.6f, 3f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CrownPeriod(0.6f, 0f), 1e-6f);
        Assert.AreEqual(0.6f, LobbyResultPanelAnimMath.CrownPeriod(0.6f, 0.2f), 1e-6f); // 점프보다 짧은 간격 = 점프 시간

        // 1회만(간격 0)
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CrownCycleProgress(0.9f, first, 0.6f, 0f), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.CrownCycleProgress(1.3f, first, 0.6f, 0f), 1e-4f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CrownCycleProgress(10f, first, 0.6f, 0f), 1e-6f);
        // 3초 주기: 1.0 · 4.0 · 7.0 초에 다시 시작, 사이엔 제자리(1)
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CrownCycleProgress(2f, first, 0.6f, 3f), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.CrownCycleProgress(4.3f, first, 0.6f, 3f), 1e-3f);
        Assert.AreEqual(0.25f, LobbyResultPanelAnimMath.CrownCycleProgress(7.15f, first, 0.6f, 3f), 1e-3f);
        // 점프 시간 0 = 늘 제자리
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CrownCycleProgress(1.3f, first, 0f, 3f), 1e-6f);

        // 마지막 착지부터 — 착지 1.6 · 4.6 · 7.6 초
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SinceLastCrownLand(1.5f, first, 0.6f, 3f));
        Assert.AreEqual(0.4f, LobbyResultPanelAnimMath.SinceLastCrownLand(2f, first, 0.6f, 3f), 1e-4f);
        Assert.AreEqual(2.7f, LobbyResultPanelAnimMath.SinceLastCrownLand(4.3f, first, 0.6f, 3f), 1e-3f); // 다음 점프 중(착지 전)
        Assert.AreEqual(0.1f, LobbyResultPanelAnimMath.SinceLastCrownLand(4.7f, first, 0.6f, 3f), 1e-3f);
        Assert.AreEqual(3.4f, LobbyResultPanelAnimMath.SinceLastCrownLand(5f, first, 0.6f, 0f), 1e-4f); // 1회만이면 계속 늘어남
    }

    [Test]
    public void CrownJumpAndSpin_ReturnToRestAtEnds()
    {
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CrownJumpOffset(0f, 28f, Ease.OutQuad), 1e-6f);
        Assert.AreEqual(28f, LobbyResultPanelAnimMath.CrownJumpOffset(0.5f, 28f, Ease.OutQuad), 1e-4f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CrownJumpOffset(1f, 28f, Ease.OutQuad), 1e-6f);
        float rising = LobbyResultPanelAnimMath.CrownJumpOffset(0.25f, 28f, Ease.OutQuad);
        Assert.Greater(rising, 0f);
        Assert.Less(rising, 28f);

        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CrownSpinAngle(0f, 360f, Ease.InOutCubic), 1e-6f);
        Assert.AreEqual(180f, LobbyResultPanelAnimMath.CrownSpinAngle(0.5f, 360f, Ease.InOutCubic), 1e-3f);
        Assert.AreEqual(360f, LobbyResultPanelAnimMath.CrownSpinAngle(1f, 360f, Ease.InOutCubic), 1e-6f);
        Assert.AreEqual(360f, LobbyResultPanelAnimMath.CrownSpinAngle(2f, 360f, Ease.InOutCubic), 1e-6f);
    }

    [Test]
    public void Sparkle_BurstAtLandingThenRepeatsOneAtATime()
    {
        const int count = 3;
        const float stagger = 0.08f, duration = 0.4f, interval = 1.6f;

        // 착지 전 = 없음
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SparkleProgress(-0.01f, 0, count, stagger, duration, interval));
        // 몰아치기: 0 번은 0 초, 2 번은 0.16 초에 시작
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.SparkleProgress(0.2f, 0, count, stagger, duration, interval), 1e-5f);
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SparkleProgress(0.1f, 2, count, stagger, duration, interval));
        Assert.AreEqual(0.1f, LobbyResultPanelAnimMath.SparkleProgress(0.2f, 2, count, stagger, duration, interval), 1e-5f);

        // 몰아치기 끝 = 0.56 초, 한 간격 쉬고 2.16 초부터 0 → 1 → 2 차례로
        float firstRepeat = (count - 1) * stagger + duration + interval;
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SparkleProgress(firstRepeat - 0.01f, 0, count, stagger, duration, interval));
        Assert.AreEqual(0.25f, LobbyResultPanelAnimMath.SparkleProgress(firstRepeat + 0.1f, 0, count, stagger, duration, interval), 1e-4f);
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SparkleProgress(firstRepeat + 0.1f, 1, count, stagger, duration, interval));
        Assert.AreEqual(0.25f, LobbyResultPanelAnimMath.SparkleProgress(firstRepeat + interval + 0.1f, 1, count, stagger, duration, interval), 1e-4f);
        Assert.AreEqual(0.25f, LobbyResultPanelAnimMath.SparkleProgress(firstRepeat + 3f * interval + 0.1f, 0, count, stagger, duration, interval), 1e-4f);

        // 반복 간격 0 = 몰아치기 뒤 끝
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SparkleProgress(5f, 0, count, stagger, duration, 0f));
        // 잘못된 입력
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SparkleProgress(0.1f, 3, count, stagger, duration, interval));
        Assert.AreEqual(-1f, LobbyResultPanelAnimMath.SparkleProgress(0.1f, 0, count, stagger, 0f, interval));

        Assert.AreEqual(0f, LobbyResultPanelAnimMath.SparklePulse(-1f, Ease.OutCubic), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.SparklePulse(0.5f, Ease.OutCubic), 1e-5f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.SparklePulse(1f, Ease.OutCubic), 1e-6f);
    }
    [Test]
    public void DefaultCountUpCurve_StrongEaseOut_NeverOvershoots()
    {
        // 220: 30% 시간에 82%, 남은 70% 시간 동안 마지막 18% — 끝으로 갈수록 느려지고 1 을 넘지 않는다.
        AnimationCurve curve = LobbyResultPanelAnimator.DefaultCountUpCurve();
        Assert.AreEqual(0f, curve.Evaluate(0f), 1e-5f);
        Assert.AreEqual(0.82f, curve.Evaluate(0.3f), 1e-4f);
        Assert.AreEqual(1f, curve.Evaluate(1f), 1e-5f);
        Assert.Greater(curve.Evaluate(0.65f), 0.93f); // 중반 이후는 거의 다 왔다 — 남은 자리가 천천히

        float previous = 0f;
        float previousStep = float.MaxValue;
        for (int i = 1; i <= 100; i++)
        {
            float t = i / 100f;
            float v = curve.Evaluate(t);
            Assert.LessOrEqual(v, 1f + 1e-5f, $"t={t}");
            Assert.GreaterOrEqual(v, previous - 1e-6f, $"t={t}"); // 되돌아가지 않음
            if (t > 0.3f)
            {
                float step = v - previous;
                Assert.LessOrEqual(step, previousStep + 1e-5f, $"t={t}"); // 감속 구간: 한 칸 증가량이 줄기만
                previousStep = step;
            }
            previous = v;
        }
    }

    [Test]
    public void PanelPunch_OneToPeakToOne_AtCountUpEnd()
    {
        Assert.AreEqual(1.65f, LobbyResultPanelAnimMath.CountUpEnd(0.15f, 1.5f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpEnd(-1f, -1f), 1e-6f);

        // 끝나기 전·끝난 뒤 = 정확히 1
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.PanelPunchScale(-0.1f, 0.28f, 1.06f, Ease.OutQuad), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.PanelPunchScale(0f, 0.28f, 1.06f, Ease.OutQuad), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.PanelPunchScale(0.28f, 0.28f, 1.06f, Ease.OutQuad), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.PanelPunchScale(5f, 0.28f, 1.06f, Ease.OutQuad), 1e-6f);
        // 가운데 = 최대
        Assert.AreEqual(1.06f, LobbyResultPanelAnimMath.PanelPunchScale(0.14f, 0.28f, 1.06f, Ease.OutQuad), 1e-4f);
        float rising = LobbyResultPanelAnimMath.PanelPunchScale(0.07f, 0.28f, 1.06f, Ease.OutQuad);
        Assert.Greater(rising, 1f);
        Assert.Less(rising, 1.06f);
        // 지속 0 = 펀치 없음
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.PanelPunchScale(0.1f, 0f, 1.06f, Ease.OutQuad), 1e-6f);
    }

    [Test]
    public void CountUpCurveWeight_SmallLinear_LargeCurve_LogBlend()
    {
        // 기본 기준 10 ~ 1000: 10 이하 선형, 1000 이상 곡선 그대로, 100 = 반반(자릿수 보간)
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpCurveWeight(1, 10f, 1000f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpCurveWeight(3, 10f, 1000f), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpCurveWeight(10, 10f, 1000f), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.CountUpCurveWeight(100, 10f, 1000f), 1e-5f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CountUpCurveWeight(1000, 10f, 1000f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CountUpCurveWeight(1234567, 10f, 1000f), 1e-6f);
        // 기준이 뒤집히면 선형 기준 하나로 자른다
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CountUpCurveWeight(50, 100f, 10f), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.CountUpCurveWeight(500, 100f, 10f), 1e-6f);
    }

    [Test]
    public void CountUpFrame_AllValuesArriveTogether_SmallEven_LargeDramatic()
    {
        AnimationCurve curve = LobbyResultPanelAnimator.DefaultCountUpCurve();
        LobbyResultCountUpFrame At(float p) => new LobbyResultCountUpFrame(p, p >= 1f ? 1f : Mathf.Clamp01(curve.Evaluate(p)), 10f, 1000f);

        int[] finals = { 1, 3, 12, 100, 754, 48210, 1234567 };
        // 같은 끝: 끝 직전엔 모두 최종값 전, 끝(1)에서 모두 최종값. 도중 어느 값도 최종값을 넘지 않는다.
        foreach (int final in finals)
        {
            Assert.Less(At(0.99f).Value(final), final, $"final={final}");
            Assert.AreEqual(final, At(1f).Value(final), $"final={final}");
            int previous = 0;
            for (float p = 0f; p < 1f; p += 0.005f)
            {
                int v = At(p).Value(final);
                Assert.LessOrEqual(v, final, $"final={final} p={p}");
                Assert.GreaterOrEqual(v, previous, $"final={final} p={p}"); // 되돌아가지 않음
                previous = v;
            }
        }
        Assert.AreEqual(754.4f, At(1f).Seconds(754.4f), 1e-4f);
        Assert.Less(At(0.99f).Seconds(754.4f), 754f); // 표기 12:34 는 끝에만
        Assert.AreEqual(1f, LobbyResultCountUpFrame.Final.ProgressFor(3), 1e-6f);

        // 작은 값(3) = 선형 — 1/3·2/3 지점에서 고르게 한 칸씩
        Assert.AreEqual(0, At(0.33f).Value(3));
        Assert.AreEqual(1, At(0.34f).Value(3));
        Assert.AreEqual(1, At(0.66f).Value(3));
        Assert.AreEqual(2, At(0.67f).Value(3));
        // 큰 값 = 곡선 그대로(30% 시간에 82%)
        Assert.AreEqual(curve.Evaluate(0.3f), At(0.3f).ProgressFor(48210), 1e-5f);
        Assert.Greater(At(0.3f).Value(48210), 48210 * 0.8f);
        // 중간 값(100) = 반반 — 선형보다 빠르고 곡선보다 느리다
        float mid = At(0.3f).ProgressFor(100);
        Assert.Greater(mid, 0.3f);
        Assert.Less(mid, curve.Evaluate(0.3f));
    }

    [Test]
    public void PunchFlash_QuickOnThenFades()
    {
        const float duration = 0.45f, rise = 0.1f;
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.FlashAlpha(-0.01f, duration, rise, Ease.OutCubic), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.FlashAlpha(0f, duration, rise, Ease.OutCubic), 1e-6f);
        Assert.AreEqual(0.5f, LobbyResultPanelAnimMath.FlashAlpha(duration * 0.05f, duration, rise, Ease.OutCubic), 1e-4f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.FlashAlpha(duration * rise, duration, rise, Ease.OutCubic), 1e-4f);
        float fading = LobbyResultPanelAnimMath.FlashAlpha(duration * 0.5f, duration, rise, Ease.OutCubic);
        Assert.Greater(fading, 0f);
        Assert.Less(fading, 1f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.FlashAlpha(duration, duration, rise, Ease.OutCubic), 1e-6f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.FlashAlpha(0.1f, 0f, rise, Ease.OutCubic), 1e-6f);
        // 켜지는 비율 0 = 첫 순간 최대
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.FlashAlpha(0f, duration, 0f, Ease.OutCubic), 1e-6f);

        Assert.AreEqual(0f, LobbyResultPanelAnimMath.FlashSpread(0f, duration, Ease.OutCubic), 1e-6f);
        Assert.AreEqual(1f, LobbyResultPanelAnimMath.FlashSpread(duration, duration, Ease.OutCubic), 1e-6f);
        float spreading = LobbyResultPanelAnimMath.FlashSpread(duration * 0.5f, duration, Ease.OutCubic);
        Assert.Greater(spreading, 0.5f); // OutCubic = 앞에서 빨리 퍼짐
        Assert.Less(spreading, 1f);
    }
}
