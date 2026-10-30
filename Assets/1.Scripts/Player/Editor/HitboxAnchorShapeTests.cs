using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 139-3 — 간파 시도 예측의 대상 검사 = 판정 앵커를 공격 방향으로 돌려 판정 위치에 놓은 오버랩(<see cref="HitboxAnchorShape"/>,
/// 서버 판정 <c>OverlapHitboxAnchor</c> 와 같은 질의). 허수아비(캡슐 r 0.5 · 높이 2)를 진짜 콜라이더로 두고 같은 질의로 본다.
/// 앵커 값 = 프리팹 저작값: 어쌔신·거너 상자 중심 (0, 1.3, 0.9) · 반크기 (0.7, 1.2, 0.7) → 정면 도달 1.6 · 옆 0.7 · 전방위 1.746,
/// 팔라딘 (0, 1.34, 0.9) · (0.8, 1.2, 0.8) → 정면 1.7 · 옆 0.8 · 전방위 1.879. 높이 띠 = 발 +0.1 ~ +2.5(할 일 163 — 웰즈 & 23호 몸에 맞춤).
/// 거리 = 판정 위치 → 허수아비 표면 수평 거리.
/// 이전 규칙(전방위 도달 + 공격 방향 반평면)은 아래 '대상 없음' 경우를 전부 대상 있음으로 봤다.
/// </summary>
public sealed class HitboxAnchorShapeTests
{
    private const float DummyRadius = 0.5f;
    // 씬의 다른 콜라이더와 떨어진 곳에서 잰다.
    private static readonly Vector3 Pivot = new Vector3(5000f, 0f, 5000f);

    private static readonly Vector3 GunnerCenter = new Vector3(0f, 1.3f, 0.9f);
    private static readonly Vector3 GunnerHalf = new Vector3(0.7f, 1.2f, 0.7f);
    private static readonly Vector3 PaladinCenter = new Vector3(0f, 1.34f, 0.9f);
    private static readonly Vector3 PaladinHalf = new Vector3(0.8f, 1.2f, 0.8f);

    private GameObject dummy;
    private CapsuleCollider dummyCollider;
    private readonly Collider[] results = new Collider[16];

    [SetUp]
    public void SetUp()
    {
        dummy = new GameObject("HitboxAnchorShapeTests_Dummy");
        dummyCollider = dummy.AddComponent<CapsuleCollider>();
        dummyCollider.radius = DummyRadius;
        dummyCollider.height = 2f;
        dummyCollider.center = new Vector3(0f, 1f, 0f);
        dummyCollider.isTrigger = true; // Hurtbox 와 같다
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(dummy);
    }

    // 지금 바라보는 방향(currentForward)의 앵커 형상 — 예측 때처럼 공격 방향으로 돌리는 경로를 함께 탄다.
    private static HitboxAnchorShape AnchorFacing(Vector3 pivot, Vector3 currentForward, Vector3 localCenter, Vector3 halfExtents)
    {
        Quaternion facing = Quaternion.LookRotation(currentForward, Vector3.up);
        return HitboxAnchorShape.FromBox(pivot + facing * localCenter, halfExtents, facing);
    }

    // 판정 위치 resolve·공격 방향 attackDirection 에서, 공격 방향과 angle 도 · 표면 거리 surface 에 둔 허수아비에 닿는가.
    private bool Hits(Vector3 localCenter, Vector3 halfExtents, Vector3 resolve, Vector3 attackDirection, float angle, float surface)
    {
        Vector3 toDummy = Quaternion.Euler(0f, angle, 0f) * attackDirection.normalized;
        PlaceDummy(resolve + toDummy * (surface + DummyRadius));
        return HitsPlaced(localCenter, halfExtents, resolve, attackDirection);
    }

    private void PlaceDummy(Vector3 position)
    {
        position.y = 0f;
        dummy.transform.position = position;
        Physics.SyncTransforms();
    }

    // 지금은 -X 를 보다가(근접 = 회전 스냅 전) 공격 방향으로 돌려 놓는다. 판정 위치는 시전 위치에서 옮긴다(대쉬 = 예상 대시 끝).
    private bool HitsPlaced(Vector3 localCenter, Vector3 halfExtents, Vector3 resolve, Vector3 attackDirection)
    {
        HitboxAnchorShape shape = AnchorFacing(Pivot, Vector3.left, localCenter, halfExtents)
            .Aimed(Pivot, Vector3.left, attackDirection)
            .Offset(resolve - Pivot);
        int count = shape.Overlap(results, ~0);
        for (int i = 0; i < count; i++)
            if (results[i] == dummyCollider)
                return true;
        return false;
    }

    // ─── 어쌔신·거너 ───

    [Test]
    public void 거너_정면_1점65_대상_없음_1점5_대상_있음()
    {
        Assert.IsFalse(Hits(GunnerCenter, GunnerHalf, Pivot, Vector3.forward, 0f, 1.65f), "정면 도달 1.6 밖(전방위 1.746 안)");
        Assert.IsTrue(Hits(GunnerCenter, GunnerHalf, Pivot, Vector3.forward, 0f, 1.5f));
    }

    [Test]
    public void 거너_표본8_옆_75도_1점31_대상_없음()
    {
        Assert.IsFalse(Hits(GunnerCenter, GunnerHalf, Pivot, Vector3.forward, 75f, 1.31f), "옆 도달 0.7 — 이전 규칙은 강 + 서버 실패");
    }

    [Test]
    public void 거너_표본7_옆_75도_0점55_대상_있음()
    {
        Assert.IsTrue(Hits(GunnerCenter, GunnerHalf, Pivot, Vector3.forward, 75f, 0.55f));
    }

    [Test]
    public void 표본10_판정_위치가_허수아비와_겹치면_대시_방향이_등져도_대상_있음()
    {
        // CSV seq 10: 랩 허수아비 (0, 0, 5), 예상 대시 끝 (0.38058, 4.99015), 대시 방향 (0.707, 0.707) — 각도 133.5°·표면 거리 0, 서버 성공.
        Vector3 dummyPos = Pivot + new Vector3(0f, 0f, 5f);
        Vector3 resolve = Pivot + new Vector3(0.38058f, 0.015f, 4.99015f);
        Vector3 dash = new Vector3(0.70711f, 0f, 0.70711f);
        PlaceDummy(dummyPos);

        Assert.IsTrue(HitsPlaced(GunnerCenter, GunnerHalf, resolve, dash), "어쌔신·거너 상자");
        Assert.IsTrue(HitsPlaced(PaladinCenter, PaladinHalf, resolve, dash), "팔라딘 상자");
    }

    [Test]
    public void 거너_대쉬_예상_대시_끝_기준()
    {
        // 시전 위치에서 대상과 4m — 대시 끝(+3m)에선 정면 표면 0.5m.
        Vector3 end = Pivot + Vector3.forward * 3f;
        PlaceDummy(Pivot + Vector3.forward * (3f + 0.5f + DummyRadius));
        Assert.IsTrue(HitsPlaced(GunnerCenter, GunnerHalf, end, Vector3.forward));
        Assert.IsFalse(HitsPlaced(GunnerCenter, GunnerHalf, Pivot, Vector3.forward), "시전 위치 기준이면 밖");
        Assert.IsFalse(HitsPlaced(GunnerCenter, GunnerHalf, end, Vector3.back), "대시 끝에서 등지면 대상 없음");
    }

    // ─── 팔라딘 ───

    [Test]
    public void 팔라딘_정면_1점75_대상_없음_1점65_대상_있음()
    {
        Assert.IsFalse(Hits(PaladinCenter, PaladinHalf, Pivot, Vector3.forward, 0f, 1.75f), "정면 도달 1.7 밖(전방위 1.879 안)");
        Assert.IsTrue(Hits(PaladinCenter, PaladinHalf, Pivot, Vector3.forward, 0f, 1.65f));
    }

    [Test]
    public void 팔라딘_옆_75도_1점31_대상_없음_0점55_대상_있음()
    {
        Assert.IsFalse(Hits(PaladinCenter, PaladinHalf, Pivot, Vector3.forward, 75f, 1.31f));
        Assert.IsTrue(Hits(PaladinCenter, PaladinHalf, Pivot, Vector3.forward, 75f, 0.55f));
    }

    // ─── 보스 몸(웰즈 & 23호 HurtBox 캡슐 r 1.7 · 높이 5.78, 바닥부터) ───

    [Test]
    public void 웰즈_23호_가장_넓은_단면_기준_정면_도달_그대로_닿는다()
    {
        dummyCollider.radius = 1.7f;
        dummyCollider.height = 5.78f;
        dummyCollider.center = new Vector3(0f, 2.89f, 0f);
        // 표면 거리 = 가장 넓은 단면(r 1.7) 기준. 상자 높이 띠가 y 1.7 위로 걸쳐야 정면 도달이 그대로 나온다(할 일 163 전 팔라딘 = 1.24 까지만).
        PlaceDummy(Pivot + Vector3.forward * (1.65f + 1.7f));
        Assert.IsTrue(HitsPlaced(PaladinCenter, PaladinHalf, Pivot, Vector3.forward), "팔라딘 정면 1.65");
        PlaceDummy(Pivot + Vector3.forward * (1.55f + 1.7f));
        Assert.IsTrue(HitsPlaced(GunnerCenter, GunnerHalf, Pivot, Vector3.forward), "어쌔신·거너 정면 1.55");
    }

    // ─── 형상 계산 ───

    [Test]
    public void 같은_방향_0_이동이면_형상이_그대로다_서버_판정_불변()
    {
        HitboxAnchorShape shape = AnchorFacing(Pivot, Vector3.right, GunnerCenter, GunnerHalf);
        HitboxAnchorShape same = shape.Aimed(Pivot, Vector3.right, Vector3.right).Offset(Vector3.zero);
        Assert.That(Vector3.Distance(shape.Box.center, same.Box.center), Is.LessThan(1e-5f));
        Assert.That(Quaternion.Angle(shape.Box.orientation, same.Box.orientation), Is.LessThan(1e-3f));
    }

    [Test]
    public void 반대_방향으로_돌려도_수직축으로만_돈다()
    {
        HitboxAnchorShape shape = AnchorFacing(Pivot, Vector3.forward, GunnerCenter, GunnerHalf)
            .Aimed(Pivot, Vector3.forward, Vector3.back);
        Vector3 expected = Pivot + new Vector3(0f, 1.3f, -0.9f);
        Assert.That(Vector3.Distance(expected, shape.Box.center), Is.LessThan(1e-4f));
        Assert.That(Vector3.Angle(Vector3.back, shape.Box.orientation * Vector3.forward), Is.LessThan(0.01f));
        Assert.That(Vector3.Angle(Vector3.up, shape.Box.orientation * Vector3.up), Is.LessThan(0.01f));
    }

    [Test]
    public void 방향을_모르면_돌리지_않는다()
    {
        HitboxAnchorShape shape = AnchorFacing(Pivot, Vector3.forward, GunnerCenter, GunnerHalf);
        HitboxAnchorShape kept = shape.Aimed(Pivot, Vector3.forward, Vector3.zero);
        Assert.That(Vector3.Distance(shape.Box.center, kept.Box.center), Is.LessThan(1e-6f));
    }

    [Test]
    public void 구_캡슐도_같은_규칙으로_돌고_옮긴다()
    {
        Vector3 offset = new Vector3(1f, 0f, 2f);
        HitboxAnchorShape sphere = HitboxAnchorShape.FromSphere(Pivot + Vector3.forward, 0.5f)
            .Aimed(Pivot, Vector3.forward, Vector3.right).Offset(offset);
        Assert.That(Vector3.Distance(Pivot + Vector3.right + offset, sphere.Sphere.center), Is.LessThan(1e-4f));

        HitboxAnchorShape capsule = HitboxAnchorShape.FromCapsule(Pivot + Vector3.forward, Pivot + Vector3.forward * 2f, 0.3f)
            .Aimed(Pivot, Vector3.forward, Vector3.left);
        Assert.That(Vector3.Distance(Pivot + Vector3.left, capsule.Capsule.point0), Is.LessThan(1e-4f));
        Assert.That(Vector3.Distance(Pivot + Vector3.left * 2f, capsule.Capsule.point1), Is.LessThan(1e-4f));
        Assert.AreEqual(0.3f, capsule.Capsule.radius);
    }
}
