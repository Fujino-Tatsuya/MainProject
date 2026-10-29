using NUnit.Framework;

/// <summary>보스 HP 바 잠김 표시(할 일 147) 고정 — 잠김 즉시 덮개·자물쇠, 해제 애니(≤0.2초), 해제 중 재잠김, 리셋.</summary>
public sealed class BossHpLockDisplayTests
{
    [Test]
    public void NotLocked_ShowsNormalBar()
    {
        var d = new BossHpLockDisplay();
        BossHpLockDisplay.Frame f = d.Evaluate(false, 0.016f, 0.15f);

        Assert.That(f.Tint01, Is.EqualTo(0f));
        Assert.That(f.ShowLock, Is.False);
        Assert.That(d.Locked, Is.False);
    }

    [Test]
    public void Locked_ShowsTintAndLockImmediately()
    {
        var d = new BossHpLockDisplay();
        BossHpLockDisplay.Frame f = d.Evaluate(true, 0.016f, 0.15f);

        Assert.That(f.Tint01, Is.EqualTo(1f));
        Assert.That(f.ShowLock, Is.True);
        Assert.That(f.LockAlpha, Is.EqualTo(1f));
        Assert.That(f.LockScale, Is.EqualTo(1f));
        Assert.That(d.Locked, Is.True);
    }

    [Test]
    public void Release_FadesOutOverReleaseSeconds()
    {
        var d = new BossHpLockDisplay();
        d.Evaluate(true, 0.016f, 0.2f);

        BossHpLockDisplay.Frame first = d.Evaluate(false, 0.016f, 0.2f);   // 해제 첫 프레임 = 아직 가득
        Assert.That(first.Tint01, Is.EqualTo(1f));
        Assert.That(first.ShowLock, Is.True);

        BossHpLockDisplay.Frame mid = d.Evaluate(false, 0.1f, 0.2f);
        Assert.That(mid.Tint01, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(mid.LockAlpha, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(mid.LockScale, Is.GreaterThan(1f));
        Assert.That(mid.ShowLock, Is.True);

        BossHpLockDisplay.Frame end = d.Evaluate(false, 0.1f, 0.2f);
        Assert.That(end.Tint01, Is.EqualTo(0f));
        Assert.That(end.ShowLock, Is.False);
    }

    [Test]
    public void ReleaseSeconds_ClampedToMax()
    {
        var d = new BossHpLockDisplay();
        d.Evaluate(true, 0.016f, 5f);
        d.Evaluate(false, 0f, 5f);

        // 5초를 줘도 0.2초에 끝난다.
        BossHpLockDisplay.Frame f = d.Evaluate(false, BossHpLockDisplay.MaxReleaseSeconds, 5f);
        Assert.That(f.ShowLock, Is.False);
        Assert.That(f.Tint01, Is.EqualTo(0f));
    }

    [Test]
    public void ZeroReleaseSeconds_UnlocksImmediately()
    {
        var d = new BossHpLockDisplay();
        d.Evaluate(true, 0.016f, 0f);
        BossHpLockDisplay.Frame f = d.Evaluate(false, 0.016f, 0f);

        Assert.That(f.Tint01, Is.EqualTo(0f));
        Assert.That(f.ShowLock, Is.False);
    }

    [Test]
    public void Relock_DuringRelease_ReturnsToLocked()
    {
        var d = new BossHpLockDisplay();
        d.Evaluate(true, 0.016f, 0.2f);
        d.Evaluate(false, 0.016f, 0.2f);
        d.Evaluate(false, 0.1f, 0.2f);

        BossHpLockDisplay.Frame f = d.Evaluate(true, 0.016f, 0.2f);
        Assert.That(f.Tint01, Is.EqualTo(1f));
        Assert.That(f.LockAlpha, Is.EqualTo(1f));
        Assert.That(f.LockScale, Is.EqualTo(1f));

        // 다시 풀면 해제 애니가 처음부터.
        Assert.That(d.Evaluate(false, 0.016f, 0.2f).Tint01, Is.EqualTo(1f));
    }

    [Test]
    public void Reset_DropsReleaseAnimation()
    {
        var d = new BossHpLockDisplay();
        d.Evaluate(true, 0.016f, 0.2f);
        d.Reset();

        BossHpLockDisplay.Frame f = d.Evaluate(false, 0.016f, 0.2f);
        Assert.That(f.ShowLock, Is.False);
        Assert.That(f.Tint01, Is.EqualTo(0f));
    }
}
