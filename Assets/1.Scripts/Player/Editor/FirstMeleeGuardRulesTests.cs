using NUnit.Framework;
using UnityEngine;

// PLAN-paladin-counter-e C2 — 팔라딘 E 방어 방향(전방 180°) 순수 규칙. 반격 부채꼴은 판정 도형으로 옮겼다(PaladinSkillAreaTests, 할 일 153).
public sealed class FirstMeleeGuardRulesTests
{
    const float GuardAngle = 180f;   // FirstMeleeSubSkillData.guardAngle 기본값 — 좌우 90°

    static Vector3 Yaw(float degrees, float distance) => Quaternion.Euler(0f, degrees, 0f) * Vector3.forward * distance;

    static bool Guard(Vector3 attacker) =>
        FirstMeleeGuardRules.IsInsideGuardArc(Vector3.forward, Vector3.zero, attacker, GuardAngle);

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
}
