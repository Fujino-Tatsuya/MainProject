using NUnit.Framework;
using UnityEditor;

public sealed class PlayModeReloadGuardTests
{
    [Test]
    public void EnteredPlayMode_Unlocked_Locks()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.EnteredPlayMode, false),
            Is.EqualTo(PlayModeReloadGuard.LockAction.Lock));
    }

    [Test]
    public void EnteredPlayMode_AlreadyLocked_DoesNotLockTwice()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.EnteredPlayMode, true),
            Is.EqualTo(PlayModeReloadGuard.LockAction.None));
    }

    [Test]
    public void ExitingPlayMode_Locked_Unlocks()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.ExitingPlayMode, true),
            Is.EqualTo(PlayModeReloadGuard.LockAction.Unlock));
    }

    [Test]
    public void EnteredEditMode_StillLocked_UnlocksAsSafetyNet()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.EnteredEditMode, true),
            Is.EqualTo(PlayModeReloadGuard.LockAction.Unlock));
    }

    [Test]
    public void Unlocked_ExitOrEditMode_DoesNotUnlockTwice()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.ExitingPlayMode, false),
            Is.EqualTo(PlayModeReloadGuard.LockAction.None));
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.EnteredEditMode, false),
            Is.EqualTo(PlayModeReloadGuard.LockAction.None));
    }

    [Test]
    public void ExitingEditMode_NeverChangesLock()
    {
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.ExitingEditMode, false),
            Is.EqualTo(PlayModeReloadGuard.LockAction.None));
        Assert.That(PlayModeReloadGuard.Decide(PlayModeStateChange.ExitingEditMode, true),
            Is.EqualTo(PlayModeReloadGuard.LockAction.None));
    }

    [Test]
    public void FullPlaySession_LocksOnceAndUnlocksOnce()
    {
        bool locked = false;
        int locks = 0, unlocks = 0;
        foreach (PlayModeStateChange change in new[]
                 {
                     PlayModeStateChange.ExitingEditMode, PlayModeStateChange.EnteredPlayMode,
                     PlayModeStateChange.ExitingPlayMode, PlayModeStateChange.EnteredEditMode,
                 })
        {
            switch (PlayModeReloadGuard.Decide(change, locked))
            {
                case PlayModeReloadGuard.LockAction.Lock: locks++; locked = true; break;
                case PlayModeReloadGuard.LockAction.Unlock: unlocks++; locked = false; break;
            }
        }

        Assert.That(locks, Is.EqualTo(1));
        Assert.That(unlocks, Is.EqualTo(1));
        Assert.That(locked, Is.False);
    }
}
