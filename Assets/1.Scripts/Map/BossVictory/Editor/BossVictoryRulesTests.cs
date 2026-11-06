using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 B 승리 연출 규칙(PLAN-boss-victory-cinematic §7) — 타임라인 경계(기획 v02 §1)·추가 연출 구간(할 일 200)·곡선·사망 표현 시계·서버 구도 계산·연출 벽 숨김.
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

    // 색: 0.72–0.80 들어옴 → 씬 끝까지 1 유지(할 일 200 D3 — 디졸브 원복 없음).
    [TestCase(-1.00f, 0f)]
    [TestCase(0.71f, 0f)]
    [TestCase(0.72f, 0f)]
    [TestCase(0.76f, 0.5f)]
    [TestCase(0.80f, 1f)]
    [TestCase(2.12f, 1f)]
    [TestCase(30.0f, 1f)]
    public void ColorWeight_HoldsToSceneEnd(float t, float expected) =>
        Assert.That(BossVictoryRules.ColorWeight(t), Is.EqualTo(expected).Within(Eps));

    // 추가 연출 구간(할 일 200): 단계 시각 = 경과 − 구간. 구간 0 이면 그대로, 음수 구간은 0 으로 본다.
    [TestCase(0.50f, 0f, 0.50f)]
    [TestCase(0.50f, 1.5f, -1.00f)]
    [TestCase(1.62f, 1.5f, 0.12f)]
    [TestCase(0.50f, -2f, 0.50f)]
    public void StageTime_SubtractsPrelude(float elapsed, float prelude, float expected) =>
        Assert.That(BossVictoryRules.StageTime(elapsed, prelude), Is.EqualTo(expected).Within(Eps));

    [Test]
    public void Prelude_ShiftsSlowMotionAndDescend()
    {
        const float prelude = 1.5f;
        // 처치 0.12초 뒤엔 아직 추가 연출 구간 — 슬로모션·하강 안 함.
        float early = BossVictoryRules.StageTime(0.12f, prelude);
        Assert.That(BossVictoryRules.IsPrelude(early), Is.True);
        Assert.That(BossVictoryRules.IsWorldFrozen(early), Is.False);
        Assert.That(BossVictoryRules.DescendBlend(early), Is.EqualTo(0f));
        // 구간 + 0.12 에 슬로모션 시작.
        Assert.That(BossVictoryRules.IsWorldFrozen(BossVictoryRules.StageTime(prelude + 0.12f, prelude)), Is.True);
        Assert.That(BossVictoryRules.IsWorldFrozen(BossVictoryRules.StageTime(prelude + 2.00f, prelude)), Is.False);
    }

    // 서버 사망 표현 배율: 추가 연출 구간 0(사망 애니는 그 뒤 — G) · 슬로모션 s · 그 밖 1.
    [TestCase(-0.5f, 0f)]
    [TestCase(0.00f, 1f)]
    [TestCase(0.119f, 1f)]
    [TestCase(0.12f, 0.1f)]
    [TestCase(1.99f, 0.1f)]
    [TestCase(2.00f, 1f)]
    public void DeathTimeScale_PreludeZeroThenSlow(float t, float expected) =>
        Assert.That(BossVictoryRules.DeathTimeScale(t, 0.1f), Is.EqualTo(expected).Within(Eps));

    // 결과 안전망 연장 = 추가 연출 구간 + 슬로모션 구간(1.88).
    [TestCase(0f, 1.88f)]
    [TestCase(1.5f, 3.38f)]
    [TestCase(-1f, 1.88f)]
    public void ResultSafetyDelay_IncludesPrelude(float prelude, float expected) =>
        Assert.That(BossVictoryRules.ResultSafetyDelay(prelude), Is.EqualTo(expected).Within(Eps));


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

    [TestCase(-0.5f, 0f)]
    public void DeathPresentationTime_PreludeIsZero(float t, float expected) =>
        Assert.That(BossVictoryRules.DeathPresentationTime(t, 0.1f), Is.EqualTo(expected).Within(Eps));

    // ── 연출 벽 숨김(할 일 200 추가) ──

    // 벽 바운즈의 얇은 축이 법선, 부호는 방 안 기준점 쪽. 모서리(정사각형)는 기준점 방향.
    [Test]
    public void WallInwardNormal_ThinAxisTowardInterior()
    {
        var southWall = new Bounds(new Vector3(0f, 2f, -10f), new Vector3(20f, 4f, 1f));
        Assert.That(BossVictoryRules.WallInwardNormal(southWall, Vector3.zero), Is.EqualTo(Vector3.forward));
        var eastWall = new Bounds(new Vector3(10f, 2f, 0f), new Vector3(1f, 4f, 20f));
        Assert.That(BossVictoryRules.WallInwardNormal(eastWall, Vector3.zero), Is.EqualTo(Vector3.left));
        var corner = new Bounds(new Vector3(10f, 2f, 10f), new Vector3(1f, 4f, 1f));
        Vector3 n = BossVictoryRules.WallInwardNormal(corner, Vector3.zero);
        Assert.That(n.x, Is.EqualTo(-0.7071f).Within(0.001f));
        Assert.That(n.z, Is.EqualTo(-0.7071f).Within(0.001f));
        Assert.That(n.y, Is.EqualTo(0f));
    }

    // 카메라가 방 남쪽에서 북쪽을 본다(24° 부감): 북쪽 벽(앞면이 카메라를 향함)만 남고 남쪽(뒷면)·동서(옆면)는 숨김.
    [Test]
    public void ShouldHideWall_KeepsOnlyClearlyFacingWalls()
    {
        Vector3 cam = new Vector3(0f, 6f, -6f);
        Vector3 fwd = Quaternion.Euler(24f, 0f, 0f) * Vector3.forward;
        const float threshold = 0.3f;
        Assert.That(BossVictoryRules.ShouldHideWall(Vector3.back, new Vector3(0f, 2f, 10f), cam, fwd, threshold), Is.False, "북쪽 벽 = 앞면");
        Assert.That(BossVictoryRules.ShouldHideWall(Vector3.forward, new Vector3(0f, 2f, -10f), cam, fwd, threshold), Is.True, "남쪽 벽 = 뒷면");
        Assert.That(BossVictoryRules.ShouldHideWall(Vector3.left, new Vector3(10f, 2f, 0f), cam, fwd, threshold), Is.True, "동쪽 벽 = 옆면(내적 0)");
        Assert.That(BossVictoryRules.ShouldHideWall(Vector3.right, new Vector3(-10f, 2f, 0f), cam, fwd, threshold), Is.True, "서쪽 벽 = 옆면(내적 0)");
    }

    // 비스듬한 시선: 내적이 임계값 위면 남기고 아래면 숨긴다. 카메라가 벽 바깥이면 앞면 쪽을 봐도 숨긴다.
    [Test]
    public void ShouldHideWall_ThresholdAndOutsideCamera()
    {
        Vector3 wallN = Vector3.back;                     // 북쪽 벽 안쪽 법선
        Vector3 wallC = new Vector3(0f, 2f, 10f);
        Vector3 cam = new Vector3(0f, 6f, -6f);
        Vector3 fwd60 = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward;   // dot = cos60 = 0.5
        Vector3 fwd80 = Quaternion.Euler(0f, 80f, 0f) * Vector3.forward;   // dot ≈ 0.17
        Assert.That(BossVictoryRules.ShouldHideWall(wallN, wallC, cam, fwd60, 0.3f), Is.False);
        Assert.That(BossVictoryRules.ShouldHideWall(wallN, wallC, cam, fwd80, 0.3f), Is.True);
        Vector3 outside = new Vector3(0f, 6f, 14f);       // 벽 너머(바깥)
        Assert.That(BossVictoryRules.ShouldHideWall(wallN, wallC, outside, Vector3.forward, 0.3f), Is.True);
        Assert.That(BossVictoryRules.ShouldHideWall(Vector3.zero, wallC, cam, Vector3.back, 0.3f), Is.False, "법선 퇴화면 그대로");
    }

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
