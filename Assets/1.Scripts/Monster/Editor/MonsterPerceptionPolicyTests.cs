using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 인지 높이 제한과 차징 방향(화면 남쪽) 판정.
///
/// 🔴 값(허용 2m)은 튜닝 대상이라 잠그지 않는다 — 고정하는 것은 관계뿐이다.
/// </summary>
public sealed class MonsterPerceptionPolicyTests
{
    const float Tol = 2f;

    // ─── 인지 높이 ─────────────────────────────────────────────────────

    [Test]
    public void 같은_높이는_인지()
    {
        Assert.IsTrue(MonsterPerceptionPolicy.WithinHeight(0f, 0f, Tol));
    }

    [Test]
    public void 경계까지는_인지()
    {
        Assert.IsTrue(MonsterPerceptionPolicy.WithinHeight(0f, 2f, Tol));
        Assert.IsTrue(MonsterPerceptionPolicy.WithinHeight(0f, -2f, Tol));
    }

    [Test]
    public void 경계를_넘으면_모른다()
    {
        Assert.IsFalse(MonsterPerceptionPolicy.WithinHeight(0f, 2.01f, Tol));
        Assert.IsFalse(MonsterPerceptionPolicy.WithinHeight(0f, -2.01f, Tol));
    }

    // 위층 몹이 아래를, 아래층 몹이 위를 — 양쪽 다 막혀야 한다(대칭).
    [Test]
    public void 위아래_대칭으로_막힌다()
    {
        Assert.IsFalse(MonsterPerceptionPolicy.WithinHeight(3f, 0f, Tol), "위층 몹 → 아래 플레이어");
        Assert.IsFalse(MonsterPerceptionPolicy.WithinHeight(0f, 3f, Tol), "아래층 몹 → 위 플레이어");
    }

    // 경사를 절반 올라온 상태(1m)는 인지돼야 한다 — 그래야 올라오는 도중 전투가 붙는다.
    [Test]
    public void 경사_중간은_인지된다()
    {
        Assert.IsTrue(MonsterPerceptionPolicy.WithinHeight(0f, 1f, Tol));
    }

    // 0 은 "제한 끔"이다 — 예전 동작으로 되돌리는 값이라 반드시 통과해야 한다.
    [Test]
    public void 허용_0이면_제한이_없다()
    {
        Assert.IsTrue(MonsterPerceptionPolicy.WithinHeight(0f, 100f, 0f));
        Assert.IsTrue(MonsterPerceptionPolicy.WithinHeight(0f, 100f, -1f));
    }

    // ─── 차징 방향 = 화면 남쪽 ─────────────────────────────────────────

    static void AssertDir(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(0f, actual.y, 1e-4f, "XZ 평면이어야 한다");
        Assert.AreEqual(1f, actual.magnitude, 1e-3f, "정규화돼야 한다");
        Assert.Greater(Vector3.Dot(expected.normalized, actual), 0.999f,
            $"기대 {expected.normalized} 실제 {actual}");
    }

    // 요 0°(월드 +Z 를 향해 내려보는 카메라) → 화면 아래 = 월드 −Z.
    [Test]
    public void 요_0도_기울어진_카메라()
    {
        Quaternion rot = Quaternion.Euler(45f, 0f, 0f);
        AssertDir(Vector3.back, BossChargeFacingPolicy.ScreenSouth(
            rot * Vector3.forward, rot * Vector3.up, BossChargeFacingPolicy.DefaultFallback));
    }

    [Test]
    public void 요_180도면_반대쪽()
    {
        Quaternion rot = Quaternion.Euler(45f, 180f, 0f);
        AssertDir(Vector3.forward, BossChargeFacingPolicy.ScreenSouth(
            rot * Vector3.forward, rot * Vector3.up, BossChargeFacingPolicy.DefaultFallback));
    }

    [Test]
    public void 요_45도면_대각선()
    {
        Quaternion rot = Quaternion.Euler(45f, 45f, 0f);
        Vector3 expected = Quaternion.Euler(0f, 45f, 0f) * Vector3.back;
        AssertDir(expected, BossChargeFacingPolicy.ScreenSouth(
            rot * Vector3.forward, rot * Vector3.up, BossChargeFacingPolicy.DefaultFallback));
    }

    // 🔴 이 케이스가 정책을 따로 만든 이유다 — forward 의 XZ 투영이 0 이라 up 으로 넘어가야 한다.
    [Test]
    public void 완전_수직_카메라는_up_으로_판정()
    {
        Quaternion rot = Quaternion.Euler(90f, 0f, 0f);
        AssertDir(Vector3.back, BossChargeFacingPolicy.ScreenSouth(
            rot * Vector3.forward, rot * Vector3.up, BossChargeFacingPolicy.DefaultFallback));
    }

    [Test]
    public void 완전_수직에서_요_90도()
    {
        Quaternion rot = Quaternion.Euler(90f, 90f, 0f);
        Vector3 expected = Quaternion.Euler(0f, 90f, 0f) * Vector3.back;
        AssertDir(expected, BossChargeFacingPolicy.ScreenSouth(
            rot * Vector3.forward, rot * Vector3.up, BossChargeFacingPolicy.DefaultFallback));
    }

    // 둘 다 쓸 수 없으면(수학적으로는 불가하지만 방어) 폴백으로 떨어진다.
    [Test]
    public void 둘_다_못_쓰면_폴백()
    {
        AssertDir(Vector3.back, BossChargeFacingPolicy.ScreenSouth(
            Vector3.up, Vector3.up, BossChargeFacingPolicy.DefaultFallback));
    }

    [Test]
    public void 폴백도_XZ_로_눕힌다()
    {
        AssertDir(Vector3.right, BossChargeFacingPolicy.ScreenSouth(
            Vector3.up, Vector3.up, new Vector3(5f, 9f, 0f)));
    }
}
