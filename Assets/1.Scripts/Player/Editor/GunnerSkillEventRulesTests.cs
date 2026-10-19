using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 거너 R 레이저 생성 위치·대상 규칙 고정 (PLAN-anim-event-timing P3 ② — 생성이 Custom0 로 늦어진 뒤),
/// 우클릭 간파 이벤트 방식·후폭풍 출발 규칙 (P3-2 ①).
/// </summary>
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

    // ── 우클릭 간파 (P3-2 ①) — 판정·종료 = 클립 Hit·End, 후폭풍 출발 = 이 피어의 Hit ──

    [Test]
    public void Interrupt_UsesClipHitAndEnd()
    {
        var go = new GameObject("GunnerInterruptTest");
        try
        {
            var skill = go.AddComponent<GunnerInterruptSkill>();
            Assert.That(skill.RequiredAnimationEvents,
                Is.EquivalentTo(new[] { SkillAnimationEventType.Hit, SkillAnimationEventType.End }));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Recoil_DoesNotMoveBeforeHit()
    {
        var recoil = new GunnerRecoilModel();
        recoil.Arm(1.5f, 0.2f);

        Assert.That(recoil.IsMoving, Is.False);
        for (int i = 0; i < 50; i++)
            Assert.That(recoil.Step(0.02f), Is.EqualTo(0f), "Hit 전 — 시간이 지나도 출발하지 않는다(타이머 없음)");
    }

    [Test]
    public void Recoil_AfterHit_CoversDistanceInDuration()
    {
        var recoil = new GunnerRecoilModel();
        recoil.Arm(1.5f, 0.2f);
        recoil.Release();

        Assert.That(recoil.Step(0.02f), Is.EqualTo(0.15f).Within(1e-5f), "속도 = 거리 / 시간");
        float total = 0.15f;
        for (int i = 1; i < 10; i++) // 0.2s = 0.02s × 10틱
            total += recoil.Step(0.02f);

        Assert.That(total, Is.EqualTo(1.5f).Within(1e-4f));
        Assert.That(recoil.Step(0.02f), Is.LessThan(1e-4f), "거리를 넘겨 밀리지 않는다");
    }

    [Test]
    public void Recoil_IgnoresHitWithoutCastOrAfterEnd()
    {
        var recoil = new GunnerRecoilModel();
        recoil.Release();
        Assert.That(recoil.IsMoving, Is.False, "시전 전 Hit");

        recoil.Arm(1.5f, 0.2f);
        recoil.Clear();
        recoil.Release();
        Assert.That(recoil.IsMoving, Is.False, "종료 뒤 늦은 Hit");
        Assert.That(recoil.Step(0.02f), Is.EqualTo(0f));
    }

    [Test]
    public void Recoil_DuplicateHit_DoesNotRestart()
    {
        var recoil = new GunnerRecoilModel();
        recoil.Arm(1f, 0.1f);
        recoil.Release();
        recoil.Step(0.05f);
        recoil.Release();

        Assert.That(recoil.Step(1f), Is.EqualTo(0.5f).Within(1e-5f), "남은 거리만 — 처음부터 다시 밀리지 않는다");
    }
}
