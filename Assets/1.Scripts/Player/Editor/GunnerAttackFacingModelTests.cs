using NUnit.Framework;
using UnityEngine;

/// <summary>거너 평타 방향(할 일 27) — 원인별 회전 잠금 토큰과 발 단위 잠금·발 사이 제한 회전 규칙.</summary>
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

        Assert.That(model.IsActive, Is.True);
        Assert.That(model.IsShotLocked, Is.True, "첫 발은 준비 동작부터 잠긴다");
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
    public void ShotLocked_DoesNotTurnTowardCursor()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.right, Vector3.forward);

        // 준비~사격 끝: 커서가 반대편이어도, 시간이 많이 지나도 돌지 않는다.
        AssertDirection(model.TurnBetweenShots(Vector3.right, Vector3.left, 360f, 10f), Vector3.right);
    }

    [Test]
    public void BetweenShots_TurnsAtLimitedSpeed()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.forward, Vector3.forward);
        model.EndShot();

        // 360°/s × 0.125s = 45° — 커서(오른쪽, 90°)까지 다 돌지 않는다. 즉시 스냅 아님.
        Vector3 turned = model.TurnBetweenShots(Vector3.forward, Vector3.right, 360f, 0.125f);
        AssertDirection(turned, new Vector3(1f, 0f, 1f).normalized);
    }

    [Test]
    public void BetweenShots_StopsAtCursor()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.forward, Vector3.forward);
        model.EndShot();

        // 남은 각도보다 많이 돌 수 있으면 커서에서 멈춘다(넘어가지 않는다). 왼쪽으로도 짧은 쪽으로 돈다.
        AssertDirection(model.TurnBetweenShots(Vector3.forward, Vector3.right, 360f, 1f), Vector3.right);
        AssertDirection(model.TurnBetweenShots(Vector3.forward, Vector3.left, 360f, 0.125f),
                        new Vector3(-1f, 0f, 1f).normalized);
    }

    [Test]
    public void BetweenShots_ZeroSpeed_StaysFixed()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.forward, Vector3.forward);
        model.EndShot();

        // 0 = 사이에도 고정 — 이어지는 발이 전부 시작 방향(예전 동작)이 된다.
        AssertDirection(model.TurnBetweenShots(Vector3.forward, Vector3.back, 0f, 1f), Vector3.forward);
    }

    [Test]
    public void NextShot_LocksBodyFacing_NotCursor()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.forward, Vector3.forward);
        model.EndShot();

        // 커서는 오른쪽(90°)인데 몸이 45° 까지만 돌았다 — 다음 발은 덜 돈 몸 방향으로 잠긴다.
        Vector3 body = model.TurnBetweenShots(Vector3.forward, Vector3.right, 360f, 0.125f);
        Vector3 locked = model.BeginShot(body);

        Assert.That(model.IsShotLocked, Is.True);
        AssertDirection(locked, new Vector3(1f, 0f, 1f).normalized);
        AssertDirection(model.TurnBetweenShots(locked, Vector3.right, 360f, 1f), locked);
    }

    [Test]
    public void MaxTurnAngle_IsSpeedTimesTime()
    {
        // 기본 360°/s 기준: 발사 간격 0.35s → 126°, 후딜 0.2s → 72°.
        Assert.That(GunnerAttackFacingModel.MaxTurnAngle(360f, 0.35f), Is.EqualTo(126f).Within(1e-3f));
        Assert.That(GunnerAttackFacingModel.MaxTurnAngle(360f, 0.2f), Is.EqualTo(72f).Within(1e-3f));
        Assert.That(GunnerAttackFacingModel.MaxTurnAngle(-10f, 1f), Is.EqualTo(0f));
    }

    [Test]
    public void NotActive_DoesNotTurn()
    {
        var model = new GunnerAttackFacingModel();

        AssertDirection(model.TurnBetweenShots(Vector3.forward, Vector3.right, 360f, 1f), Vector3.forward);
    }

    [Test]
    public void End_ReleasesOnce()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.right, Vector3.forward);

        Assert.That(model.End(), Is.True);
        Assert.That(model.IsActive, Is.False);
        Assert.That(model.IsShotLocked, Is.False);
        Assert.That(model.End(), Is.False, "끊김·정상 종료가 겹쳐도 두 번째는 아무것도 안 한다");
    }

    [Test]
    public void Restart_LocksNewStartAim()
    {
        var model = new GunnerAttackFacingModel();
        model.Begin(Vector3.right, Vector3.forward);
        model.End();

        AssertDirection(model.Begin(Vector3.back, Vector3.forward), Vector3.back);
        Assert.That(model.IsShotLocked, Is.True);
    }

    static void AssertDirection(Vector3 actual, Vector3 expected)
    {
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-4f), $"기대 {expected}, 실제 {actual}");
    }
}
