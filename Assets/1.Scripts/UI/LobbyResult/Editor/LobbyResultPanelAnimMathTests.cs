using EuniTween;
using NUnit.Framework;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215) 계산 — 진행도·카운트업 지연·투명도/크기·카운트업 숫자.
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
        // 숫자 지연 0.15 + 숫자 0.8 + 왕관 지연 0.05 = 1.0 초에 첫 점프, 0.6 초 동안
        float first = LobbyResultPanelAnimMath.CrownFirstStart(0.15f, 0.8f, 0.05f);
        Assert.AreEqual(1f, first, 1e-5f);
        Assert.AreEqual(0f, LobbyResultPanelAnimMath.CrownFirstStart(-1f, -1f, -1f), 1e-6f);

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
}
