using NUnit.Framework;

public sealed class SoundPlaybackLimiterTests
{
    [Test]
    public void TryAcquire_RejectsAtSimultaneousLimit()
    {
        var limiter = new SoundPlaybackLimiter();

        Assert.That(limiter.TryAcquire("player.hit", 0d, 2, 0d), Is.True);
        Assert.That(limiter.TryAcquire("player.hit", 0d, 2, 0d), Is.True);
        Assert.That(limiter.TryAcquire("player.hit", 0d, 2, 0d), Is.False);
        Assert.That(limiter.ActiveCount("player.hit"), Is.EqualTo(2));
    }

    [Test]
    public void Release_FreesOneSimultaneousSlot()
    {
        var limiter = new SoundPlaybackLimiter();
        Assert.That(limiter.TryAcquire("player.hit", 0d, 1, 0d), Is.True);

        limiter.Release("player.hit");

        Assert.That(limiter.TryAcquire("player.hit", 0d, 1, 0d), Is.True);
        Assert.That(limiter.ActiveCount("player.hit"), Is.EqualTo(1));
    }

    [Test]
    public void TryAcquire_RejectsBeforeCooldownBoundary()
    {
        var limiter = new SoundPlaybackLimiter();
        Assert.That(limiter.TryAcquire("player.hit", 10d, 0, 1d), Is.True);
        limiter.Release("player.hit");

        Assert.That(limiter.TryAcquire("player.hit", 10.999d, 0, 1d), Is.False);
    }

    [Test]
    public void TryAcquire_AllowsExactlyAtCooldownBoundary()
    {
        var limiter = new SoundPlaybackLimiter();
        Assert.That(limiter.TryAcquire("player.hit", 10d, 0, 1d), Is.True);
        limiter.Release("player.hit");

        Assert.That(limiter.TryAcquire("player.hit", 11d, 0, 1d), Is.True);
    }

    [Test]
    public void TryAcquire_TracksDifferentKeysIndependently()
    {
        var limiter = new SoundPlaybackLimiter();

        Assert.That(limiter.TryAcquire("player.hit", 5d, 1, 10d), Is.True);
        Assert.That(limiter.TryAcquire("enemy.hit", 5d, 1, 10d), Is.True);
    }
}
