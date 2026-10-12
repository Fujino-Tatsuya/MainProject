using NUnit.Framework;
using UnityEngine;

/// <summary>거너 평타 방향 고정(할 일 27) — 원인별 회전 잠금 토큰과 시작 조준 고정 규칙.</summary>
public sealed class GunnerAttackFacingModelTests
{
    // ── PlayerFacingLock ────────────────────────────────────────────────

    [Test]
    public void FacingLock_StartsUnlocked()
    {
        Assert.That(new PlayerFacingLock().IsLocked, Is.False);
    }

    [Test]
    public void FacingLock_CausesAreIndependent()
    {
        var facingLock = new PlayerFacingLock();
        object gunner = new object();
        object assassin = new object();

        facingLock.Acquire(gunner);
        facingLock.Acquire(assassin);
        facingLock.Release(gunner);

        Assert.That(facingLock.IsLocked, Is.True, "다른 원인의 잠금은 남는다");
        Assert.That(facingLock.IsHeldBy(assassin), Is.True);

        facingLock.Release(assassin);
        Assert.That(facingLock.IsLocked, Is.False);
    }

    [Test]
    public void FacingLock_ReleaseByNonHolder_DoesNothing()
    {
        var facingLock = new PlayerFacingLock();
        object gunner = new object();

        facingLock.Acquire(gunner);

        Assert.That(facingLock.Release(new object()), Is.False);
        Assert.That(facingLock.IsLocked, Is.True, "안 잡은 원인이 남의 잠금을 풀지 않는다");
    }

    [Test]
    public void FacingLock_DoubleAcquire_CountsOnce()
    {
        var facingLock = new PlayerFacingLock();
        object gunner = new object();

        Assert.That(facingLock.Acquire(gunner), Is.True);
        Assert.That(facingLock.Acquire(gunner), Is.False);
        facingLock.Release(gunner);

        Assert.That(facingLock.IsLocked, Is.False, "같은 원인을 두 번 잡아도 한 번 풀면 끝 — 고착 없음");
    }

    [Test]
    public void FacingLock_NullCause_IsIgnored()
    {
        var facingLock = new PlayerFacingLock();

        Assert.That(facingLock.Acquire(null), Is.False);
        Assert.That(facingLock.IsLocked, Is.False);
        Assert.That(facingLock.Release(null), Is.False);
    }

    // ── GunnerAttackFacingModel ─────────────────────────────────────────

    [Test]
    public void Begin_FlattensAndLocksStartAim()
    {
        var model = new GunnerAttackFacingModel();
        Vector3 locked = model.Begin(new Vector3(3f, 5f, 0f), Vector3.forward);

        Assert.That(model.IsLocked, Is.True);
        AssertDirection(locked, Vector3.right);
        AssertDirection(model.Direction, Vector3.right);
    }

    [Test]
    public void Begin_WithoutAim_UsesCurrentFacing()
    {
        var model = new GunnerAttackFacingModel();
        Vector3 locked = model.Begin(Vector3.up, Vector3.left);

        AssertDirection(locked, Vector3.left);
    }

    [Test]
    public void ShotDirection_WhileLocked_IgnoresLaterAim()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.right, Vector3.forward);

        // 홀드·입력 창으로 이어지는 발도, 몸이 딴 데를 보고 있어도 시작 방향이다.
        AssertDirection(model.ShotDirection(Vector3.back), Vector3.right);
        AssertDirection(model.ShotDirection(Vector3.left), Vector3.right);
    }

    [Test]
    public void End_ReleasesOnce()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.right, Vector3.forward);

        Assert.That(model.End(), Is.True);
        Assert.That(model.IsLocked, Is.False);
        Assert.That(model.End(), Is.False, "끊김·정상 종료가 겹쳐도 두 번째는 아무것도 안 한다");
        AssertDirection(model.ShotDirection(Vector3.back), Vector3.back);
    }

    [Test]
    public void Restart_LocksNewStartAim()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.right, Vector3.forward);
        model.End();
        model.Begin(Vector3.back, Vector3.forward);

        AssertDirection(model.ShotDirection(Vector3.right), Vector3.back);
    }

    static void AssertDirection(Vector3 actual, Vector3 expected)
    {
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-4f), $"기대 {expected}, 실제 {actual}");
    }
}
