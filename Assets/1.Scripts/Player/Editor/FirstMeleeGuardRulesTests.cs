using NUnit.Framework;
using UnityEngine;

// PLAN-paladin-counter-e C2 — 팔라딘 E 방어 방향(전방 180°)·반격 부채꼴(전방 120°·3m) 순수 규칙.
public sealed class FirstMeleeGuardRulesTests
{
    const float GuardAngle = 180f;   // FirstMeleeSubSkillData.guardAngle 기본값 — 좌우 90°
    const float CounterAngle = 120f; // counterAngle 기본값 — 좌우 60°
    const float CounterRange = 3f;   // counterRange 기본값

    static Vector3 Yaw(float degrees, float distance) => Quaternion.Euler(0f, degrees, 0f) * Vector3.forward * distance;

    static bool Guard(Vector3 attacker) =>
        FirstMeleeGuardRules.IsInsideGuardArc(Vector3.forward, Vector3.zero, attacker, GuardAngle);

    static bool Sector(Vector3 closest, Vector3 center) =>
        FirstMeleeGuardRules.IsInsideCounterSector(Vector3.zero, Vector3.forward, closest, center, CounterRange, CounterAngle);

    static bool Sector(Vector3 closest) => Sector(closest, closest);

    // ── 방어 방향(플레이어 원점·방어 방향 +Z) ──

    [Test]
    public void GuardArc_AttackerStraightAhead_True() => Assert.That(Guard(new Vector3(0f, 0f, 5f)), Is.True);

    [Test]
    public void GuardArc_At90Degrees_True()
    {
        // 경계 포함 — 정측면은 좌우 모두 방어 범위.
        Assert.That(Guard(new Vector3(5f, 0f, 0f)), Is.True);
        Assert.That(Guard(new Vector3(-5f, 0f, 0f)), Is.True);
    }

    [Test]
    public void GuardArc_At91Degrees_False()
    {
        Assert.That(Guard(Yaw(91f, 5f)), Is.False);
        Assert.That(Guard(Yaw(-91f, 5f)), Is.False);
    }

    [Test]
    public void GuardArc_Behind_False() => Assert.That(Guard(new Vector3(0f, 0f, -5f)), Is.False);

    [Test]
    public void GuardArc_HighAttackerAhead_AngleIgnoresHeight_True()
    {
        // 3D 로 재면 정면에서 80° 넘게 위지만, 바닥 평면에서는 정면이다.
        Assert.That(Guard(new Vector3(0f, 10f, 1f)), Is.True);
        // 높이가 있어도 평면상 뒤면 실패.
        Assert.That(Guard(new Vector3(0f, 10f, -1f)), Is.False);
    }

    [Test]
    public void GuardArc_PlanarOverlap_TreatedAsFront()
    {
        Assert.That(Guard(Vector3.zero), Is.True);
        Assert.That(Guard(new Vector3(0.005f, 3f, -0.005f)), Is.True); // XZ 0.01m 미만 — 높이 무관
    }

    [Test]
    public void GuardArc_ZeroForward_False()
    {
        Assert.That(FirstMeleeGuardRules.IsInsideGuardArc(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 5f), GuardAngle), Is.False);
        // 수직 방향만 있는 forward 도 평면 기준 0 이다. 겹친 공격자도 정면이 없으니 실패.
        Assert.That(FirstMeleeGuardRules.IsInsideGuardArc(Vector3.up, Vector3.zero, Vector3.zero, GuardAngle), Is.False);
    }

    // ── 반격 부채꼴(원점·방향 +Z·3m·120°) ──

    [Test]
    public void CounterSector_WithinRangeAndAngle_True() => Assert.That(Sector(new Vector3(0.5f, 0f, 2f)), Is.True);

    [Test]
    public void CounterSector_BeyondRange_False()
    {
        Assert.That(Sector(new Vector3(0f, 0f, 3.01f)), Is.False);
        Assert.That(Sector(new Vector3(0f, 0f, 3f)), Is.True); // 거리 경계 포함
    }

    [Test]
    public void CounterSector_At60Degrees_True()
    {
        Assert.That(Sector(Yaw(60f, 2f)), Is.True);
        Assert.That(Sector(Yaw(-60f, 2f)), Is.True);
    }

    [Test]
    public void CounterSector_At61Degrees_False()
    {
        Assert.That(Sector(Yaw(61f, 2f)), Is.False);
        Assert.That(Sector(Yaw(-61f, 2f)), Is.False);
    }

    [Test]
    public void CounterSector_LargeColliderSurfaceInRange_CenterFar_True() =>
        // 큰 보스 — 중심은 10m 밖이어도 표면이 3m 안이면 맞는다.
        Assert.That(Sector(new Vector3(0f, 0f, 2.9f), new Vector3(0f, 0f, 10f)), Is.True);

    [Test]
    public void CounterSector_Angle_IgnoresHeight()
    {
        // 3D 로 재면 정면에서 68° 위(> 60°)지만 평면 각도는 0° — 각도만 평면이라 안이다.
        // 거리는 3D(≈2.69m)로도 3m 안인 점을 골라, 런타임 3D 구 브로드페이즈와 무관하게 각도만 확인한다.
        Assert.That(Sector(new Vector3(0f, 2.5f, 1f)), Is.True);
    }

    [Test]
    public void CounterSector_ClosestPointAtOrigin_UsesBoundsCenter()
    {
        // 발밑을 포함하는 콜라이더 — 표면이 원점이면 중심 방향으로 각도를 다시 잰다.
        Assert.That(Sector(Vector3.zero, new Vector3(0f, 0f, 2f)), Is.True);
        Assert.That(Sector(Vector3.zero, new Vector3(0f, 0f, -2f)), Is.False);
        // 중심까지 겹치면 방향을 정할 수 없어 안으로 본다(AssassinConeAttack.IsInsideCone 과 같다).
        Assert.That(Sector(Vector3.zero, new Vector3(0f, 1f, 0f)), Is.True);
    }
}
