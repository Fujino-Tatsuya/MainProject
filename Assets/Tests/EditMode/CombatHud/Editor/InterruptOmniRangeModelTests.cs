using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 인터럽트 강조 전방위 사거리(할 일 5)와 대상까지 거리(할 일 139-4 — 실제 콜라이더 표면, <see cref="InterruptOmniRangeModel.HorizontalDistanceToCollider"/>).
/// 이전 AABB XZ 사각형 거리는 대상이 월드 대각선에 있으면 짧게 나왔다 — 23호 피격 캡슐(r 1.7 · 높이 5.78 · 중심 y 2.89, 트리거)에서
/// 최대 r(√2−1) = 0.70m. 같은 대상으로 HUD 강조(<see cref="InterruptOmniRangeModel.IsInReach"/>)와 예측 앵커 오버랩(<see cref="HitboxAnchorShape"/>)을 본다.
/// </summary>
public sealed class InterruptOmniRangeModelTests
{
    private const float Tolerance = 1e-4f;

    // 플레이어 중심, 정면(+Z)으로 1.5 나간 박스 판정(반폭 0.5, 반높이 0.5, 반길이 1) — 가장 먼 꼭짓점 (±0.5, ·, 2.5).
    // 씬의 다른 콜라이더와 떨어진 곳에서 잰다(예측 오버랩 테스트가 같은 자리를 쓴다).
    private static readonly Vector3 Pivot = new Vector3(6000f, 0f, 6000f);
    private static readonly Vector3 BoxCenter = Pivot + new Vector3(0f, 0.5f, 1.5f);
    private static readonly Vector3 BoxHalfExtents = new Vector3(0.5f, 0.5f, 1f);
    private static readonly float BoxReach = Mathf.Sqrt(0.5f * 0.5f + 2.5f * 2.5f);

    // 23호 피격 캡슐 · 거너 앵커(상자 중심 (0, 1, 0.9) · 반크기 (0.7, 0.9, 0.7) → 정면 도달 1.6 · 전방위 1.746).
    private const float BossRadius = 1.7f;
    private static readonly Vector3 Diagonal = new Vector3(1f, 0f, 1f).normalized;
    private static readonly Vector3 GunnerCenter = new Vector3(0f, 1f, 0.9f);
    private static readonly Vector3 GunnerHalf = new Vector3(0.7f, 0.9f, 0.7f);

    private readonly List<GameObject> created = new List<GameObject>();
    private readonly Collider[] results = new Collider[16];

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            if (go != null)
                Object.DestroyImmediate(go);
        created.Clear();
    }

    private static float Reach() =>
        InterruptOmniRangeModel.BoxReach(Pivot, BoxCenter, BoxHalfExtents, Quaternion.identity);

    private GameObject Create(string name, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject("InterruptOmniRangeModelTests_" + name);
        go.transform.SetPositionAndRotation(position, rotation);
        created.Add(go);
        return go;
    }

    // pivot 기준 (x, z) 에 선 축 정렬 상자 대상(가로 size · 높이 2).
    private BoxCollider Target(float x, float z, float size = 1f)
    {
        var box = Create("Target", Pivot + new Vector3(x, 0f, z), Quaternion.identity).AddComponent<BoxCollider>();
        box.size = new Vector3(size, 2f, size);
        box.center = new Vector3(0f, 1f, 0f);
        Physics.SyncTransforms();
        return box;
    }

    // 23호 피격 캡슐 — Y축 45° 돌려 pivot 에서 direction 쪽 실제 표면 거리 surface 에 세운다.
    private CapsuleCollider PlaceBoss(Vector3 direction, float surface)
    {
        var capsule = Create("Boss", Pivot + direction * (surface + BossRadius), Quaternion.Euler(0f, 45f, 0f))
            .AddComponent<CapsuleCollider>();
        capsule.radius = BossRadius;
        capsule.height = 5.78f;
        capsule.center = new Vector3(0f, 2.89f, 0f);
        capsule.direction = 1;
        capsule.isTrigger = true;
        Physics.SyncTransforms();
        return capsule;
    }

    // 이전 거리(AABB XZ 사각형) — 비교용.
    private static float AabbDistance(Vector3 pivot, Bounds b)
    {
        float dx = Mathf.Max(0f, Mathf.Max(b.min.x - pivot.x, pivot.x - b.max.x));
        float dz = Mathf.Max(0f, Mathf.Max(b.min.z - pivot.z, pivot.z - b.max.z));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float GunnerOmniReach() =>
        InterruptOmniRangeModel.BoxReach(Pivot, Pivot + GunnerCenter, GunnerHalf, Quaternion.identity);

    // ─── 전방위 사거리 ───

    [Test]
    public void BoxReach_IsFarthestCornerIncludingForwardOffset()
    {
        Assert.That(Reach(), Is.EqualTo(BoxReach).Within(Tolerance));
    }

    [Test]
    public void BoxReach_SameWhicheverWayPlayerFaces()
    {
        // 판정이 플레이어와 함께 돈다 — 옆(+X)·뒤(-Z)를 보고 있어도 사거리는 같다.
        Quaternion right = Quaternion.Euler(0f, 90f, 0f);
        Quaternion back = Quaternion.Euler(0f, 180f, 0f);

        Assert.That(InterruptOmniRangeModel.BoxReach(Pivot, Pivot + right * (BoxCenter - Pivot), BoxHalfExtents, right),
            Is.EqualTo(BoxReach).Within(Tolerance));
        Assert.That(InterruptOmniRangeModel.BoxReach(Pivot, Pivot + back * (BoxCenter - Pivot), BoxHalfExtents, back),
            Is.EqualTo(BoxReach).Within(Tolerance));
    }

    [Test]
    public void SphereAndCapsuleReach_AddRadiusToFarthestPoint()
    {
        Assert.That(InterruptOmniRangeModel.SphereReach(Pivot, Pivot + new Vector3(0f, 1f, 2f), 0.5f),
            Is.EqualTo(2.5f).Within(Tolerance));
        Assert.That(InterruptOmniRangeModel.CapsuleReach(Pivot, Pivot + new Vector3(0f, 1f, 1f), Pivot + new Vector3(0f, 1f, 3f), 0.4f),
            Is.EqualTo(3.4f).Within(Tolerance), "더 먼 끝점 + 반경");
    }

    // ─── HUD 강조 판정 ───

    [Test]
    public void IsInReach_FrontBackAndSideAllCount()
    {
        float reach = Reach();

        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 2.8f), reach), Is.True, "앞");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -2.8f), reach), Is.True, "뒤 — 바라보는 방향과 무관");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(2.8f, 0f), reach), Is.True, "오른쪽");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(-2.8f, 0f), reach), Is.True, "왼쪽");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(2f, -2f), reach), Is.True, "대각선 뒤");
    }

    [Test]
    public void IsInReach_OutsideRadiusIsOff()
    {
        float reach = Reach();

        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 3.2f), reach), Is.False, "앞 — 표면 2.7 > 사거리");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -3.2f), reach), Is.False, "뒤");
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(3.2f, 0f), reach), Is.False, "옆");
    }

    [Test]
    public void IsInReach_UsesTargetColliderSurfaceNotCenter()
    {
        float reach = Reach();

        // 중심은 3.5 로 같다 — 작은 대상(표면 3.0)은 밖, 큰 대상(반폭 1.1 → 표면 2.4)은 안.
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -3.5f, 1f), reach), Is.False);
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, -3.5f, 2.2f), reach), Is.True);

        // 표면이 사거리 경계 바로 안/밖.
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(reach + 0.49f, 0f), reach), Is.True);
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(reach + 0.51f, 0f), reach), Is.False);
    }

    [Test]
    public void IsInReach_ZeroWhenStandingInsideAndOffWithoutShape()
    {
        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot, Target(0.2f, -0.2f, 3f)), Is.EqualTo(0f).Within(Tolerance));
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 0f, 3f), Reach()), Is.True);
        Assert.That(InterruptOmniRangeModel.IsInReach(Pivot, Target(0f, 0f, 3f), 0f), Is.False, "판정 형상이 없으면(사거리 0) 끈다");
    }

    [Test]
    public void HUD_거너_대각선_실제_2점1이면_강조_안_함_1점7이면_강조()
    {
        float reach = GunnerOmniReach();
        Assert.That(reach, Is.EqualTo(1.746f).Within(0.01f));

        Assert.IsFalse(InterruptOmniRangeModel.IsInReach(Pivot, PlaceBoss(Diagonal, 2.1f), reach), "AABB 1.40 이면 강조됐다");
        TearDown();
        Assert.IsTrue(InterruptOmniRangeModel.IsInReach(Pivot, PlaceBoss(Diagonal, 1.7f), reach));
    }

    // ─── 실제 표면 거리 vs AABB(할 일 139-4) ───

    [Test]
    public void 캡슐_대각선_실제_2점1_AABB는_1점4()
    {
        CapsuleCollider boss = PlaceBoss(Diagonal, 2.1f);
        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot, boss), Is.EqualTo(2.1f).Within(0.01f));
        Assert.That(AabbDistance(Pivot, boss.bounds), Is.EqualTo(2.1f - BossRadius * (Mathf.Sqrt(2f) - 1f)).Within(0.01f),
            "이전 거리 = 0.70m 짧다");
    }

    [Test]
    public void 캡슐_축_방향이면_AABB와_같다()
    {
        CapsuleCollider boss = PlaceBoss(Vector3.forward, 2.1f);
        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot, boss), Is.EqualTo(2.1f).Within(0.01f));
        Assert.That(AabbDistance(Pivot, boss.bounds), Is.EqualTo(2.1f).Within(0.01f));
    }

    [Test]
    public void 높이와_상관없이_캡슐_몸통_기준_수평거리다()
    {
        // pivot y 0 = 캡슐 바닥 — 높이를 맞추지 않으면 아래 반구 쪽 점으로 가 수평 거리가 커진다.
        CapsuleCollider boss = PlaceBoss(Vector3.right, 0.3f);
        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot, boss), Is.EqualTo(0.3f).Within(0.01f));
        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot + Vector3.up * 10f, boss), Is.EqualTo(0.3f).Within(0.01f));
    }

    [Test]
    public void pivot이_캡슐_안이면_0()
    {
        CapsuleCollider boss = PlaceBoss(Diagonal, -1f);
        Assert.AreEqual(0f, InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot, boss), Tolerance);
    }

    [Test]
    public void 상자_Y축_45도_대각선_면까지_실제_1점5_AABB는_0점5()
    {
        // 반크기 1 상자를 45° 돌리면 월드 대각선 쪽은 면(중심에서 1), AABB 는 모서리(중심에서 2).
        var box = Create("Box", Pivot + Diagonal * 2.5f, Quaternion.Euler(0f, 45f, 0f)).AddComponent<BoxCollider>();
        box.size = new Vector3(2f, 2f, 2f);
        box.center = new Vector3(0f, 1f, 0f);
        Physics.SyncTransforms();

        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot, box), Is.EqualTo(1.5f).Within(0.01f));
        Assert.That(AabbDistance(Pivot, box.bounds), Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void 비볼록_메시는_AABB_표면으로_근사한다()
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(cube);

        var meshCollider = Create("Mesh", Pivot + Diagonal * 3f + Vector3.up * 0.5f, Quaternion.Euler(0f, 45f, 0f))
            .AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = false;
        Physics.SyncTransforms();

        Assert.That(InterruptOmniRangeModel.HorizontalDistanceToCollider(Pivot, meshCollider),
            Is.EqualTo(AabbDistance(Pivot, meshCollider.bounds)).Within(0.01f));
    }

    // ─── 예측(판정 앵커 오버랩 — FindPredictTarget 과 같은 질의, AABB 를 안 쓴다) ───

    private bool GunnerAnchorHits(Vector3 direction, Collider collider)
    {
        Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
        HitboxAnchorShape shape = HitboxAnchorShape.FromBox(Pivot + facing * GunnerCenter, GunnerHalf, facing);
        int count = shape.Overlap(results, ~0);
        for (int i = 0; i < count; i++)
            if (results[i] == collider)
                return true;
        return false;
    }

    [Test]
    public void 예측_거너_대각선_실제_2점1이면_대상_없음_1점5면_대상_있음()
    {
        Assert.IsFalse(GunnerAnchorHits(Diagonal, PlaceBoss(Diagonal, 2.1f)), "정면 도달 1.6 밖");
        TearDown();
        Assert.IsTrue(GunnerAnchorHits(Diagonal, PlaceBoss(Diagonal, 1.5f)));
    }
}
