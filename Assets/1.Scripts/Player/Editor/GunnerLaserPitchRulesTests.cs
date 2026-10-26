using NUnit.Framework;
using UnityEngine;

/// <summary>거너 평타 레이저 지면 조준(할 일 93) — 높이가 다른 지면만 위·아래로 기울이고 평지는 수평, 상한 60°.</summary>
public sealed class GunnerLaserPitchRulesTests
{
    private const float AimHeight = 1f;
    private const float Tolerance = 0.3f;
    private const float Eps = 0.0001f;

    // 발 높이 0, 총구 높이 1, +Z 를 본다.
    private static readonly Vector3 Origin = new Vector3(0f, 1f, 0f);

    private static Vector3 Aim(Vector3 flat, Vector3 ground) =>
        GunnerLaserPitchRules.AimDirection(flat, Origin, 0f, ground, AimHeight, Tolerance);

    private static float Pitch(Vector3 d) =>
        Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;

    [Test]
    public void LevelGround_StaysHorizontal_WithBodyDirection()
    {
        Vector3 body = new Vector3(1f, 0f, 1f).normalized;
        Vector3 d = Aim(body, new Vector3(5f, 0f, 5f));

        Assert.That(d.y, Is.EqualTo(0f).Within(Eps));
        Assert.That(Vector3.Distance(d, body), Is.LessThan(Eps), "수평 성분 = 입력 몸 방향");
    }

    [Test]
    public void HigherLedge_AimsUp_ThroughAimPoint()
    {
        Vector3 ground = new Vector3(0f, 2f, 6f);
        Vector3 d = Aim(Vector3.forward, ground);

        Assert.That(d.y, Is.GreaterThan(0f));
        Assert.That(d.magnitude, Is.EqualTo(1f).Within(Eps));

        // 출발점에서 조준점(지면 + 1)까지 수평 6m 를 가면 높이가 조준점과 같다.
        Vector3 aimPoint = ground + Vector3.up * AimHeight;
        Vector3 along = Origin + d * (6f / new Vector2(d.x, d.z).magnitude);
        Assert.That(Vector3.Distance(along, aimPoint), Is.LessThan(0.001f));
    }

    [Test]
    public void LowerGround_AimsDown()
    {
        Vector3 d = Aim(Vector3.forward, new Vector3(0f, -3f, 6f));

        Assert.That(d.y, Is.LessThan(0f));
        Assert.That(d.x, Is.EqualTo(0f).Within(Eps));
    }

    [Test]
    public void WithinTolerance_IsHorizontal_BeyondIsPitched()
    {
        Assert.That(Aim(Vector3.forward, new Vector3(0f, 0.3f, 6f)).y, Is.EqualTo(0f).Within(Eps), "Δ=0.3 은 같은 높이");
        Assert.That(Aim(Vector3.forward, new Vector3(0f, -0.3f, 6f)).y, Is.EqualTo(0f).Within(Eps), "Δ=-0.3 도 같은 높이");
        Assert.That(Aim(Vector3.forward, new Vector3(0f, 0.31f, 6f)).y, Is.GreaterThan(0f), "넘으면 기운다");
    }

    [Test]
    public void VeryCloseHighPoint_ClampsTo60Degrees()
    {
        Vector3 d = Aim(Vector3.forward, new Vector3(0f, 5f, 0.5f));

        Assert.That(Pitch(d), Is.EqualTo(GunnerLaserPitchRules.MaxPitchDegrees).Within(0.01f));
        Assert.That(d.magnitude, Is.EqualTo(1f).Within(Eps));
        Assert.That(d.z, Is.GreaterThan(0f), "수평 성분은 몸 방향 그대로");
    }

    [Test]
    public void VeryCloseLowPoint_ClampsToMinus60Degrees()
    {
        Vector3 d = Aim(Vector3.forward, new Vector3(0f, -6f, 0.5f));

        Assert.That(Pitch(d), Is.EqualTo(-GunnerLaserPitchRules.MaxPitchDegrees).Within(0.01f));
    }

    [Test]
    public void NearZeroHorizontalDistance_IsHorizontal()
    {
        Vector3 d = Aim(Vector3.forward, new Vector3(0.001f, 3f, 0.001f));

        Assert.That(d.y, Is.EqualTo(0f).Within(Eps));
        Assert.That(Vector3.Distance(d, Vector3.forward), Is.LessThan(Eps));
    }

    [Test]
    public void ClampPitch_ZeroVector_FallsBackToFlatFacing()
    {
        Vector3 d = GunnerLaserPitchRules.ClampPitch(Vector3.zero, new Vector3(0f, 0.5f, -2f));

        Assert.That(Vector3.Distance(d, Vector3.back), Is.LessThan(Eps));
    }

    [Test]
    public void ClampPitch_StraightUp_UsesFallbackAtMaxAngle()
    {
        Vector3 d = GunnerLaserPitchRules.ClampPitch(Vector3.up, Vector3.right);

        Assert.That(Pitch(d), Is.EqualTo(GunnerLaserPitchRules.MaxPitchDegrees).Within(0.01f));
        Assert.That(d.x, Is.GreaterThan(0f));
    }

    [Test]
    public void ClampPitch_WithinLimit_OnlyNormalizes()
    {
        Vector3 d = GunnerLaserPitchRules.ClampPitch(new Vector3(0f, 1f, 2f), Vector3.forward);

        Assert.That(Vector3.Distance(d, new Vector3(0f, 1f, 2f).normalized), Is.LessThan(Eps));
    }
}
