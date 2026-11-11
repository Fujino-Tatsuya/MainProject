using NUnit.Framework;

/// <summary>쿨다운 아이콘 연출(할 일 259) 순수 규칙 고정 — 끝남 판정·대시 충전 증가·어둡게 알파·펄스·번쩍 곡선.</summary>
public sealed class CooldownIconFeedbackRulesTests
{
    [Test]
    public void Ended_OnlyOnCoolingToReadyWithBaseline()
    {
        Assert.That(CooldownIconFeedbackRules.Ended(true, true, false), Is.True);
        Assert.That(CooldownIconFeedbackRules.Ended(false, true, false), Is.False, "첫 프레임(기준 없음)은 연출 안 함");
        Assert.That(CooldownIconFeedbackRules.Ended(true, false, false), Is.False, "계속 준비 상태");
        Assert.That(CooldownIconFeedbackRules.Ended(true, true, true), Is.False, "계속 쿨다운");
        Assert.That(CooldownIconFeedbackRules.Ended(true, false, true), Is.False, "쿨다운 시작");
    }

    [Test]
    public void ChargeGained_EachChargeIncrease()
    {
        Assert.That(CooldownIconFeedbackRules.ChargeGained(true, 0, 2, 1, 2), Is.True, "0→1");
        Assert.That(CooldownIconFeedbackRules.ChargeGained(true, 1, 2, 2, 2), Is.True, "1→2");
        Assert.That(CooldownIconFeedbackRules.ChargeGained(true, 1, 2, 0, 2), Is.False, "소비");
        Assert.That(CooldownIconFeedbackRules.ChargeGained(true, 2, 2, 2, 2), Is.False, "그대로");
        Assert.That(CooldownIconFeedbackRules.ChargeGained(false, 0, 2, 1, 2), Is.False, "기준 없음");
        Assert.That(CooldownIconFeedbackRules.ChargeGained(true, 0, 1, 2, 2), Is.False, "최대 충전 바뀐 프레임(재바인딩)");
        Assert.That(CooldownIconFeedbackRules.ChargeGained(true, 0, 0, 1, 0), Is.False, "대시 없음");
    }

    [Test]
    public void DimAlpha_IsOneMinusBrightness_Clamped()
    {
        Assert.That(CooldownIconFeedbackRules.DimAlpha(0.6f), Is.EqualTo(0.4f).Within(1e-5f));
        Assert.That(CooldownIconFeedbackRules.DimAlpha(1f), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(CooldownIconFeedbackRules.DimAlpha(-1f), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void PulseScale_OneAtEnds_PeakAtMiddle()
    {
        Assert.That(CooldownIconFeedbackRules.PulseScale(0f, 0.25f, 1.15f), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(CooldownIconFeedbackRules.PulseScale(0.125f, 0.25f, 1.15f), Is.EqualTo(1.15f).Within(1e-5f));
        Assert.That(CooldownIconFeedbackRules.PulseScale(0.25f, 0.25f, 1.15f), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(CooldownIconFeedbackRules.PulseScale(-0.1f, 0.25f, 1.15f), Is.EqualTo(1f));
        Assert.That(CooldownIconFeedbackRules.PulseScale(0.1f, 0f, 1.15f), Is.EqualTo(1f), "시간 0 = 펄스 끔");
    }

    [Test]
    public void FlashAlpha_PeakAtStart_FadesToZero()
    {
        Assert.That(CooldownIconFeedbackRules.FlashAlpha(0f, 0.3f, 0.85f), Is.EqualTo(0.85f).Within(1e-5f));
        Assert.That(CooldownIconFeedbackRules.FlashAlpha(0.15f, 0.3f, 0.85f), Is.EqualTo(0.85f * 0.25f).Within(1e-5f));
        Assert.That(CooldownIconFeedbackRules.FlashAlpha(0.3f, 0.3f, 0.85f), Is.EqualTo(0f));
        Assert.That(CooldownIconFeedbackRules.FlashAlpha(0.1f, 0f, 0.85f), Is.EqualTo(0f), "시간 0 = 번쩍 끔");

        float prev = float.MaxValue;
        for (float t = 0f; t < 0.3f; t += 0.01f)
        {
            float a = CooldownIconFeedbackRules.FlashAlpha(t, 0.3f, 0.85f);
            Assert.That(a, Is.LessThanOrEqualTo(prev));
            prev = a;
        }
    }
}
