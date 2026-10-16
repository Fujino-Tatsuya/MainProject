using NUnit.Framework;
using UnityEngine;

// 할 일 47 연장 — 어쌔신 평타 다음 타 예약 창(가붕이 ComboWindowOpen~Close 와 같은 규칙, 클립 정규화 시간).
public sealed class AssassinComboWindowRulesTests
{
    // Combo_Attack_Wave_0N 클립 길이(1.25초)와 단별 재생 속도(3.6/3.6/3.6/3).
    private const float WaveClipLength = 1.25f;

    [Test]
    public void ProgressIsElapsedOverMotionDuration()
    {
        Assert.That(AssassinComboWindowRules.NormalizedProgress(0.25f, 0.5f), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(AssassinComboWindowRules.NormalizedProgress(0f, 0.5f), Is.EqualTo(0f));
    }

    [Test]
    public void ZeroMotionDurationIsOutsideWindow()
    {
        float progress = AssassinComboWindowRules.NormalizedProgress(0.1f, 0f);

        Assert.That(AssassinComboWindowRules.IsInside(progress, 0f, 1f), Is.False);
    }

    [Test]
    public void BoundariesAreInclusive()
    {
        Assert.That(AssassinComboWindowRules.IsInside(0.4f, 0.4f, 0.95f), Is.True, "열림 경계");
        Assert.That(AssassinComboWindowRules.IsInside(0.95f, 0.4f, 0.95f), Is.True, "닫힘 경계");
    }

    [Test]
    public void OutsideWindowIsRejected()
    {
        Assert.That(AssassinComboWindowRules.IsInside(0.39f, 0.4f, 0.95f), Is.False, "판정 전 클릭");
        Assert.That(AssassinComboWindowRules.IsInside(0.96f, 0.4f, 0.95f), Is.False, "닫힘 뒤 ~ End 전 클릭");
        Assert.That(AssassinComboWindowRules.IsInside(-1f, 0.4f, 0.95f), Is.False, "회전 대기 중(클립 시작 전)");
    }

    [Test]
    public void InvertedWindowNeverOpens()
    {
        Assert.That(AssassinComboWindowRules.IsInside(0.5f, 0.8f, 0.2f), Is.False);
    }

    // 정규화 창은 재생 속도에 비례한다 — 같은 창 값이 3.6배속 단과 3배속 단에서 서로 다른 초가 된다.
    [Test]
    public void SameNormalizedWindowScalesWithPlaybackSpeed()
    {
        float fast = WaveClipLength / 3.6f;   // 1~3타 ≈ 0.347초
        float slow = WaveClipLength / 3f;     // 4타 ≈ 0.417초
        const float elapsed = 0.35f;

        Assert.That(InWindow(elapsed, fast, 0.4f, 0.95f), Is.False, "1~3타는 0.35초면 이미 끝(창 밖)");
        Assert.That(InWindow(elapsed, slow, 0.4f, 0.95f), Is.True, "4타는 0.35초가 창 안(≈0.84)");
    }

    [Test]
    public void DataDefaultsOpenAfterHitAndCloseBeforeEnd()
    {
        AssassinBasicAttackData data = ScriptableObject.CreateInstance<AssassinBasicAttackData>();
        try
        {
            foreach (AssassinBasicAttackStepData step in data.NormalSteps)
                AssertWindow(step, 0.4f, 0.95f);
            AssertWindow(data.EnhancedStep, 0.4f, 0.95f);
            AssertWindow(data.TransformedStep, 0.75f, 0.95f);   // 묶음 마지막(4번째) 판정 뒤
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    private static bool InWindow(float elapsed, float motionDuration, float open, float close) =>
        AssassinComboWindowRules.IsInside(
            AssassinComboWindowRules.NormalizedProgress(elapsed, motionDuration), open, close);

    private static void AssertWindow(AssassinBasicAttackStepData step, float open, float close)
    {
        Assert.That(step.ComboWindowOpen, Is.EqualTo(open).Within(1e-5f));
        Assert.That(step.ComboWindowClose, Is.EqualTo(close).Within(1e-5f));
        Assert.That(step.ComboWindowClose, Is.LessThan(0.97f), "End(0.97) 전에 닫혀야 한다");
    }
}
