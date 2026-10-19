using NUnit.Framework;

// 서버 안전망 시각 규칙(PlayerSkillSafetyNet) — 타이머 방식은 maxActiveDuration + padding 그대로,
// 이벤트 방식은 클립 실제 재생 시간 + padding, 클립·속도를 못 구하면 타이머 방식으로 대체.
public sealed class PlayerSkillSafetyNetTests
{
    private const float Padding = 0.1f;

    [Test]
    public void TimerSkill_UsesMaxActiveDurationPlusPadding()
    {
        float seconds = PlayerSkillSafetyNet.Seconds(false, 1.5f, Padding, true, 5.666667f, 1f, 1.2f, 1f, out bool usedClip);

        Assert.That(usedClip, Is.False, "타이머 방식은 클립이 있어도 클립 길이를 쓰지 않는다");
        Assert.That(seconds, Is.EqualTo(1.6f).Within(1e-5f));
        Assert.That(PlayerSkillSafetyNet.TimerSeconds(1.5f, Padding), Is.EqualTo(1.6f).Within(1e-5f));
    }

    [Test]
    public void EventSkill_UsesClipLengthOverPlaybackSpeed()
    {
        // 팔라딘 R: 클립 5.667s, 상태 speed 1 × UltimateSpeed 1.2 → 4.722s + 0.1
        float seconds = PlayerSkillSafetyNet.Seconds(true, 1.5f, Padding, true, 5.666667f, 1f, 1.2f, 1f, out bool usedClip);

        Assert.That(usedClip, Is.True);
        Assert.That(seconds, Is.EqualTo(5.666667f / 1.2f + Padding).Within(1e-4f));
    }

    [Test]
    public void EventSkill_MultipliesStateSpeedParameterAndAnimatorSpeed()
    {
        Assert.That(PlayerSkillSafetyNet.TryGetPlaybackSeconds(2f, 2f, 0.5f, 2f, out float seconds), Is.True);
        Assert.That(seconds, Is.EqualTo(1f).Within(1e-5f), "2s ÷ (2 × 0.5 × 2)");

        Assert.That(PlayerSkillSafetyNet.TryGetPlaybackSeconds(2f, -1f, 1f, 1f, out float reversed), Is.True);
        Assert.That(reversed, Is.EqualTo(2f).Within(1e-5f), "역재생도 길이는 같다");
    }

    [Test]
    public void EventSkill_ZeroSpeedFallsBackToTimer()
    {
        float seconds = PlayerSkillSafetyNet.Seconds(true, 1.5f, Padding, true, 5f, 1f, 0f, 1f, out bool usedClip);

        Assert.That(usedClip, Is.False);
        Assert.That(seconds, Is.EqualTo(1.6f).Within(1e-5f));
    }

    [Test]
    public void EventSkill_MissingClipFallsBackToTimer()
    {
        float noClip = PlayerSkillSafetyNet.Seconds(true, 1.5f, Padding, false, 5f, 1f, 1f, 1f, out bool usedNoClip);
        float zeroLength = PlayerSkillSafetyNet.Seconds(true, 1.5f, Padding, true, 0f, 1f, 1f, 1f, out bool usedZero);

        Assert.That(usedNoClip, Is.False);
        Assert.That(noClip, Is.EqualTo(1.6f).Within(1e-5f));
        Assert.That(usedZero, Is.False);
        Assert.That(zeroLength, Is.EqualTo(1.6f).Within(1e-5f));
    }

    [Test]
    public void NegativePadding_IsClampedToZero()
    {
        Assert.That(PlayerSkillSafetyNet.TimerSeconds(1.5f, -1f), Is.EqualTo(1.5f).Within(1e-5f));
    }

    [Test]
    public void RemainingSeconds_CountsFromNormalizedTime()
    {
        Assert.That(PlayerSkillSafetyNet.RemainingSeconds(4f, 0f), Is.EqualTo(4f).Within(1e-5f));
        Assert.That(PlayerSkillSafetyNet.RemainingSeconds(4f, 0.25f), Is.EqualTo(3f).Within(1e-5f));
        Assert.That(PlayerSkillSafetyNet.RemainingSeconds(4f, 1.5f), Is.EqualTo(0f), "클립 끝을 넘겼으면(끝 프레임에 머묾) 0");
    }
}
