using NUnit.Framework;
using UnityEngine;

/// <summary>거너 R 레이저 생성 위치·대상 규칙 고정 (PLAN-anim-event-timing P3 ② — 생성이 Custom0 로 늦어진 뒤).</summary>
public sealed class GunnerSkillEventRulesTests
{
    static readonly Vector3 Aim = new Vector3(1f, 0f, 1f);
    static readonly Vector3 Now = new Vector3(5f, 0f, 5f);
    static readonly Vector3 AtCast = new Vector3(3f, 0f, 3f);

    [Test]
    public void AliveTarget_SpawnsOnCurrentPosition_KeepsTarget()
    {
        Vector3 p = GunnerSkillEventRules.ResolveLaserSpawn(false, Aim, true, Now, AtCast, out bool keep);
        Assert.That(p, Is.EqualTo(Now));
        Assert.That(keep, Is.True);
    }

    [Test]
    public void TargetDiedBeforeSpawn_UsesCastPosition_WithoutTarget()
    {
        Vector3 p = GunnerSkillEventRules.ResolveLaserSpawn(false, Aim, false, Now, AtCast, out bool keep);
        Assert.That(p, Is.EqualTo(AtCast), "시전 순간 위치에 생성 → 레이저가 재탐색");
        Assert.That(keep, Is.False);
    }

    [Test]
    public void GroundPoint_AlwaysAimPoint_WithoutTarget()
    {
        Vector3 p = GunnerSkillEventRules.ResolveLaserSpawn(true, Aim, true, Now, AtCast, out bool keep);
        Assert.That(p, Is.EqualTo(Aim));
        Assert.That(keep, Is.False);
    }
}
