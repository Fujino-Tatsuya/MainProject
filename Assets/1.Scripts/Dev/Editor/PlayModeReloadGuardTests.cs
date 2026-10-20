using NUnit.Framework;

// 순수 함수만 검사한다 — 실제 EditorPrefs 는 건드리지 않는다.
public sealed class PlayModeReloadGuardTests
{
    [Test]
    public void RecompileAndContinuePlaying_ChangesToRecompileAfterFinished()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeReloadGuard.RecompileAndContinuePlaying),
            Is.EqualTo(PlayModeReloadGuard.RecompileAfterFinishedPlaying));
    }

    [Test]
    public void RecompileAfterFinishedPlaying_Kept()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeReloadGuard.RecompileAfterFinishedPlaying),
            Is.EqualTo(PlayModeReloadGuard.RecompileAfterFinishedPlaying));
    }

    [Test]
    public void StopPlayingAndRecompile_Kept()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeReloadGuard.StopPlayingAndRecompile),
            Is.EqualTo(PlayModeReloadGuard.StopPlayingAndRecompile));
    }

    [Test]
    public void OutOfRange_ChangesToRecompileAfterFinished()
    {
        Assert.That(PlayModeReloadGuard.Decide(-1), Is.EqualTo(PlayModeReloadGuard.RecompileAfterFinishedPlaying));
        Assert.That(PlayModeReloadGuard.Decide(3), Is.EqualTo(PlayModeReloadGuard.RecompileAfterFinishedPlaying));
    }
}
