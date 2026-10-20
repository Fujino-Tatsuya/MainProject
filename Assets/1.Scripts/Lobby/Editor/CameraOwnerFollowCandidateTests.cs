using NUnit.Framework;

/// <summary>로비 캐릭터 교체(할 일 48 C12, R4) — 카메라 오너 추적 후보는 스폰된 내 오브젝트만(디스폰 중인 옛 PlayerObject 건너뜀).</summary>
public sealed class CameraOwnerFollowCandidateTests
{
    [Test]
    public void 스폰된_내_오브젝트만_후보()
    {
        Assert.That(CameraTargetSwitcher.IsOwnerFollowCandidate(isSpawned: true, isOwner: true), Is.True);
    }

    [Test]
    public void 디스폰된_옛_오브젝트는_내_것이어도_건너뛴다()
    {
        Assert.That(CameraTargetSwitcher.IsOwnerFollowCandidate(isSpawned: false, isOwner: true), Is.False);
    }

    [Test]
    public void 남의_오브젝트는_후보가_아니다()
    {
        Assert.That(CameraTargetSwitcher.IsOwnerFollowCandidate(isSpawned: true, isOwner: false), Is.False);
        Assert.That(CameraTargetSwitcher.IsOwnerFollowCandidate(isSpawned: false, isOwner: false), Is.False);
    }
}
