using NUnit.Framework;
using UnityEngine;

// 할 일 47 연장·정정 — 어쌔신 평타 다음 타 연결: 예약 창(가붕이 ComboWindowOpen~Close 와 같은 규칙, 클립 정규화 시간)
// + End 순간 눌림(거너 기본 공격과 같은 홀드).
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

    // 평타 1~4타 클립 이벤트(10-16 직접 배치): Hit 0, End 1타 0.4초 · 2~4타 0.333초 / 1.25초 클립.
    private const float Wave1End = 0.4f / WaveClipLength;          // 0.32
    private const float Wave2To4End = 0.33333334f / WaveClipLength; // ≈0.267
    private const float SpecialEnd = 0.97f;                        // 강타·변신 묶음 End(AssassinShellAuthoring.AttackEndTime)

    [Test]
    public void DataDefaultsOpenAfterHitAndCloseBeforeEnd()
    {
        AssassinBasicAttackData data = ScriptableObject.CreateInstance<AssassinBasicAttackData>();
        try
        {
            AssassinBasicAttackStepData[] steps = data.NormalSteps;
            AssertWindow(steps[0], 0.05f, 0.30f, Wave1End);
            for (int i = 1; i < 4; i++)
                AssertWindow(steps[i], 0.05f, 0.25f, Wave2To4End);
            AssertWindow(data.EnhancedStep, 0.4f, 0.95f, SpecialEnd);
            AssertWindow(data.TransformedStep, 0.75f, 0.95f, SpecialEnd);   // 묶음 마지막(4번째) 판정 뒤
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    // 1타 3.6배속: 창 0.05~0.30 = 약 0.017~0.104초, End 0.111초 — 창이 End 전에 닫히고 그 사이가 비지 않는다.
    [Test]
    public void FirstStepWindowFitsBeforeEnd()
    {
        float duration = WaveClipLength / 3.6f;

        Assert.That(InWindow(0.01f, duration, 0.05f, 0.30f), Is.False, "판정 프레임(0초) 직후는 아직 창 전");
        Assert.That(InWindow(0.05f, duration, 0.05f, 0.30f), Is.True);
        Assert.That(InWindow(0.10f, duration, 0.05f, 0.30f), Is.True);
        Assert.That(InWindow(Wave1End * duration, duration, 0.05f, 0.30f), Is.False, "End 순간엔 닫혀 있다");
    }

    // ── 할 일 47 정정 — End 순간 다음 타 = 창 예약 || 눌림(거너와 같은 홀드 + 창) ──

    private const float StaleAfter = 0.5f;      // AssassinBasicAttack.HoldStaleSeconds
    private const float HoldSignalGap = 0.1f;   // AssassinBasicAttack.HoldSignalInterval

    [Test]
    public void HeldAtEndContinues()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: false, held: true), Is.True);
    }

    [Test]
    public void WindowQueueContinuesEvenAfterRelease()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: true, held: false), Is.True);
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: true, held: true), Is.True);
    }

    [Test]
    public void NeitherQueuedNorHeldEnds()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: false, held: false), Is.False);
    }

    // 꾹 누르고만 있으면 1→2→3→4→1 이 계속 이어진다 — 유예 0 이어도 End 같은 순간 이어지니 4타 뒤 1타로 돈다.
    [Test]
    public void HoldingOnlyLoopsThroughFourStepsAndBackToFirst()
    {
        var model = new AssassinComboModel(4, 0f);
        float now = 0f;
        int step = model.Begin(now);

        for (int i = 0; i < 9; i++)
        {
            Assert.That(step, Is.EqualTo(i % 4), $"{i + 1}번째 타");
            now += 0.35f;
            model.Complete(step, now);

            bool held = AssassinComboWindowRules.IsHoldFresh(true, now - HoldSignalGap, now, StaleAfter);
            Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(false, held), Is.True, "눌림 — 다음 타");
            step = model.Begin(now);
        }
    }

    // 예전 홀드 방식의 "처음 누른 입력을 떼지 않아야" 조건은 없다 — 뗐다 다시 눌러 들고 있으면 End 순간 눌림으로 잇는다.
    [Test]
    public void ReleasedThenRepressedAndHeldContinues()
    {
        // 0.05 누름 → 0.12 뗌 → 0.2 다시 누름(각각 변경 즉시 신호). 서버가 아는 상태 = 마지막 변경(눌림, 0.2).
        const float end = 0.3f;
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(
            false, AssassinComboWindowRules.IsHoldFresh(true, 0.2f, end, StaleAfter)), Is.True);
    }

    [Test]
    public void ReleasedBeforeEndWithoutQueueEnds()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(
            false, AssassinComboWindowRules.IsHoldFresh(false, 0.29f, 0.3f, StaleAfter)), Is.False);
    }

    // 주기 신호가 끊기면(뗌 신호 유실 등) 안 눌림으로 본다 — 무한히 이어지지 않게. 경계는 포함.
    [Test]
    public void StaleHoldSignalCountsAsReleased()
    {
        Assert.That(AssassinComboWindowRules.IsHoldFresh(true, 1f, 1.5f, StaleAfter), Is.True, "경계");
        Assert.That(AssassinComboWindowRules.IsHoldFresh(true, 1f, 1.51f, StaleAfter), Is.False, "끊김");
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(
            false, AssassinComboWindowRules.IsHoldFresh(true, 1f, 2f, StaleAfter)), Is.False);
    }

    private static bool InWindow(float elapsed, float motionDuration, float open, float close) =>
        AssassinComboWindowRules.IsInside(
            AssassinComboWindowRules.NormalizedProgress(elapsed, motionDuration), open, close);

    private static void AssertWindow(AssassinBasicAttackStepData step, float open, float close, float end)
    {
        Assert.That(step.ComboWindowOpen, Is.EqualTo(open).Within(1e-5f));
        Assert.That(step.ComboWindowClose, Is.EqualTo(close).Within(1e-5f));
        Assert.That(step.ComboWindowClose, Is.LessThan(end), $"End({end:F3}) 전에 닫혀야 한다");
    }
}
