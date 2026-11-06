using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 B 승리 연출 규칙(PLAN-boss-victory-cinematic §7) — 타임라인 경계(기획 v02 §1)·곡선·사망 표현 시계·서버 구도 계산.
/// </summary>
public sealed class BossVictoryRulesTests
{
    const float Eps = 0.0001f;

    [Test]
    public void Smooth_MatchesSpecCurve()
    {
        Assert.That(BossVictoryRules.Smooth(0f), Is.EqualTo(0f));
        Assert.That(BossVictoryRules.Smooth(1f), Is.EqualTo(1f));
        Assert.That(BossVictoryRules.Smooth(0.5f), Is.EqualTo(0.5f).Within(Eps));
        Assert.That(BossVictoryRules.Smooth(0.25f), Is.EqualTo(0.15625f).Within(Eps));   // u²(3−2u)
        Assert.That(BossVictoryRules.Smooth(-1f), Is.EqualTo(0f));
        Assert.That(BossVictoryRules.Smooth(2f), Is.EqualTo(1f));
    }

    // 정지 = 0.12 ≤ t < 2.00 (기획 §3 표).
    [TestCase(0.00f, false)]
    [TestCase(0.119f, false)]
    [TestCase(0.12f, true)]
    [TestCase(1.99f, true)]
    [TestCase(2.00f, false)]
    [TestCase(3.20f, false)]
    public void WorldFrozen_Boundaries(float t, bool frozen) =>
        Assert.That(BossVictoryRules.IsWorldFrozen(t), Is.EqualTo(frozen));

    // 카메라 하강 0.22–0.72: 0.12–0.22 는 고정(0), 0.72 이후 최종(1).
    [TestCase(0.12f, 0f)]
    [TestCase(0.22f, 0f)]
    [TestCase(0.47f, 0.5f)]
    [TestCase(0.72f, 1f)]
    [TestCase(1.50f, 1f)]
    public void DescendBlend_Boundaries(float t, float expected) =>
        Assert.That(BossVictoryRules.DescendBlend(t), Is.EqualTo(expected).Within(Eps));

    // 색: 0.72–0.80 들어옴 → 디졸브 전까지 1 유지(경석 10-27 — 2.00 원복 없음).
    [TestCase(0.71f, 0f)]
    [TestCase(0.72f, 0f)]
    [TestCase(0.76f, 0.5f)]
    [TestCase(0.80f, 1f)]
    [TestCase(2.12f, 1f)]
    [TestCase(9.00f, 1f)]
    public void ColorWeight_HoldsUntilDissolve(float t, float expected) =>
        Assert.That(BossVictoryRules.ColorWeight(t), Is.EqualTo(expected).Within(Eps));

    // 디졸브 시작(예: 6.5초) → 0.24초에 원래 색.
    [TestCase(6.49f, 1f)]
    [TestCase(6.50f, 1f)]
    [TestCase(6.62f, 0.5f)]
    [TestCase(6.74f, 0f)]
    [TestCase(8.00f, 0f)]
    public void ColorWeight_FadesOutFromDissolve(float t, float expected) =>
        Assert.That(BossVictoryRules.ColorWeight(t, 6.5f), Is.EqualTo(expected).Within(Eps));

    // 타이포(기본 진입 0.16초): 0.92 진입 → 1.08 완성 → 2.00 유지 → 3.20 소거(기획 §5).
    [TestCase(0.91f, 0f)]
    [TestCase(0.92f, 0f)]
    [TestCase(1.08f, 1f)]
    [TestCase(1.99f, 1f)]
    [TestCase(2.60f, 0.5f)]
    [TestCase(3.20f, 0f)]
    public void TypoAlpha_Boundaries(float t, float expected) =>
        Assert.That(BossVictoryRules.TypoAlpha(t), Is.EqualTo(expected).Within(Eps));

    // 진입을 늦춘 값(경석 10-27 "너무 빠르다") — 0.4초 페이드 · 0.45초 안착.
    [Test]
    public void Typo_SlowerTiming()
    {
        Assert.That(BossVictoryRules.TypoAlpha(1.12f, 0.4f), Is.EqualTo(0.5f).Within(Eps));
        Assert.That(BossVictoryRules.TypoAlpha(1.32f, 0.4f), Is.EqualTo(1f).Within(Eps));
        Assert.That(BossVictoryRules.TypoSettle(1.37f, 0.45f), Is.EqualTo(1f).Within(Eps));
        Assert.That(BossVictoryRules.TypoSettle(1.07f, 0.45f), Is.LessThan(1f));
    }

    // 글리치 구간 — 밖은 0, 시작 0.03초에 차오르고 끝 0.06초에 빠진다.
    [TestCase(0.70f, 0f)]
    [TestCase(0.72f, 0f)]
    [TestCase(0.735f, 0.5f)]
    [TestCase(0.80f, 1f)]
    [TestCase(0.87f, 0.5f)]
    [TestCase(0.90f, 0f)]
    [TestCase(0.95f, 0f)]
    public void GlitchBurst_Envelope(float t, float expected) =>
        Assert.That(BossVictoryRules.GlitchBurst(t, 0.72f, 0.90f), Is.EqualTo(expected).Within(Eps));

    [Test]
    public void TypoSettle_EndsAt107()
    {
        Assert.That(BossVictoryRules.TypoSettle(0.92f), Is.EqualTo(0f).Within(Eps));
        Assert.That(BossVictoryRules.TypoSettle(1.07f), Is.EqualTo(1f).Within(Eps));
        Assert.That(BossVictoryRules.TypoSettle(1.08f), Is.EqualTo(1f).Within(Eps));
    }

    // 사망 표현 시계 — 완전 정지(s=0)면 1.88초를 뺀다(Codex C6). 서버 대기·클라 애니가 같은 값.
    [TestCase(0.05f, 0.05f)]
    [TestCase(0.12f, 0.12f)]
    [TestCase(1.50f, 0.12f)]
    [TestCase(2.00f, 0.12f)]
    [TestCase(3.00f, 1.12f)]
    public void DeathPresentationTime_ExcludesFreeze(float t, float expected) =>
        Assert.That(BossVictoryRules.DeathPresentationTime(t), Is.EqualTo(expected).Within(Eps));

    // 슬로모션(s=0.1) — 정지 구간 1.88초가 0.188초만큼만 흐른다(경석 10-27 "완전 정지 말고 슬로모션").
    [TestCase(1.12f, 0.22f)]
    [TestCase(2.00f, 0.308f)]
    [TestCase(3.00f, 1.308f)]
    public void DeathPresentationTime_SlowMotion(float t, float expected) =>
        Assert.That(BossVictoryRules.DeathPresentationTime(t, 0.1f), Is.EqualTo(expected).Within(Eps));

    [Test]
    public void FreezeDuration_Is188() =>
        Assert.That(BossVictoryRules.FreezeDuration, Is.EqualTo(1.88f).Within(Eps));

    // ── 구도 ──

    static BossVictoryRules.Framing Framing() => new BossVictoryRules.Framing
    {
        FinalPitch = 24f, EndPitch = 32f, Fov = 34f, Aspect = 16f / 9f,
        MinDistance = 4f, MaxDistance = 22f, DistanceStep = 0.5f, EndDistanceScale = 1.25f, CharacterHeight = 1.8f,
        YawCandidates = new[] { -25f, -35f, -15f, -45f, -55f, -5f, 15f, 30f },
    };

    static BossVictoryRules.Subjects Melee(params Vector3[] mates) => new BossVictoryRules.Subjects
    {
        Killer = new Vector3(0f, 0f, 0f),
        KillerForward = Vector3.forward,
        HitPoint = new Vector3(0f, 1.5f, 2f),
        Boss = new Vector3(0f, 0f, 3.5f),
        BossRadius = 1.5f,
        BossHeight = 4f,
        Teammates = new List<Vector3>(mates),
    };

    [Test]
    public void Compose_Melee_KillerLeftForegroundAndFramed()
    {
        BossVictoryRules.Framing f = Framing();
        BossVictoryRules.Composition c = BossVictoryRules.Compose(Melee(new Vector3(-4f, 0f, 2f), new Vector3(4f, 0f, 5f)), f);

        Assert.That(float.IsNegativeInfinity(c.Score), Is.False, "필수 조건(막타·타격점·보스 화면 안)을 만족하는 후보가 있어야 한다");
        Vector3 killer = BossVictoryRules.ToViewport(c.Final, Vector3.zero, f.Fov, f.Aspect);
        Vector3 hit = BossVictoryRules.ToViewport(c.Final, new Vector3(0f, 1.5f, 2f), f.Fov, f.Aspect);
        Vector3 boss = BossVictoryRules.ToViewport(c.Final, new Vector3(0f, 2f, 3.5f), f.Fov, f.Aspect);
        Assert.That(killer.x, Is.LessThan(0.5f), "막타 = 화면 왼쪽");
        Assert.That(hit.x, Is.GreaterThan(killer.x), "타격점은 막타보다 오른쪽");
        Assert.That(killer.z, Is.LessThan(boss.z), "막타가 보스보다 앞(전경)");

        // 24° 부감 · 32° 종료는 더 높고 더 멀다.
        Vector3 toLook = new Vector3(0f, 1.5f, 2f) - c.Final.Position;
        float pitch = Mathf.Asin(-toLook.normalized.y) * Mathf.Rad2Deg;
        Assert.That(pitch, Is.EqualTo(24f).Within(0.5f));
        Assert.That(c.EndPose.Position.y, Is.GreaterThan(c.Final.Position.y));
        Assert.That((c.EndPose.Position - new Vector3(0f, 1.5f, 2f)).magnitude, Is.GreaterThan(c.Distance));
    }

    [Test]
    public void Compose_IsDeterministic()
    {
        BossVictoryRules.Subjects s = Melee(new Vector3(-4f, 0f, 2f), new Vector3(4f, 0f, 5f));
        BossVictoryRules.Composition a = BossVictoryRules.Compose(s, Framing());
        BossVictoryRules.Composition b = BossVictoryRules.Compose(s, Framing());
        Assert.That(a.Final.Position, Is.EqualTo(b.Final.Position));
        Assert.That(a.Yaw, Is.EqualTo(b.Yaw));
        Assert.That(a.Distance, Is.EqualTo(b.Distance));
    }

    [Test]
    public void Compose_Solo_StillFramesKillerAndBoss()
    {
        BossVictoryRules.Composition c = BossVictoryRules.Compose(Melee(), Framing());
        Assert.That(float.IsNegativeInfinity(c.Score), Is.False);
    }

    // 원거리 막타(거너 12m) — 막타가 카메라 뒤로 빠지지 않고 화면 안에 들어오는 거리를 고른다.
    [Test]
    public void Compose_Ranged_KillerStaysInFrame()
    {
        BossVictoryRules.Framing f = Framing();
        var s = new BossVictoryRules.Subjects
        {
            Killer = new Vector3(0f, 0f, -12f),
            KillerForward = Vector3.forward,
            HitPoint = new Vector3(0f, 1.5f, 2f),
            Boss = new Vector3(0f, 0f, 3.5f),
            BossRadius = 1.5f,
            BossHeight = 4f,
            Teammates = new List<Vector3>(),
        };
        BossVictoryRules.Composition c = BossVictoryRules.Compose(s, f);
        Assert.That(float.IsNegativeInfinity(c.Score), Is.False);
        Vector3 killer = BossVictoryRules.ToViewport(c.Final, s.Killer, f.Fov, f.Aspect);
        Assert.That(killer.z, Is.GreaterThan(0f), "막타가 카메라 앞");
        Assert.That(killer.x, Is.InRange(0f, 1f));
    }

    // 막타와 타격점이 같은 자리(퇴화) — 막타가 보던 방향으로 대신 잡는다(NaN 금지).
    [Test]
    public void Compose_DegenerateDirection_UsesKillerForward()
    {
        BossVictoryRules.Subjects s = Melee();
        s.HitPoint = s.Killer;
        BossVictoryRules.Composition c = BossVictoryRules.Compose(s, Framing());
        Assert.That(float.IsNaN(c.Final.Position.x), Is.False);
        Assert.That(float.IsNaN(c.Final.Rotation.x), Is.False);
    }
}
