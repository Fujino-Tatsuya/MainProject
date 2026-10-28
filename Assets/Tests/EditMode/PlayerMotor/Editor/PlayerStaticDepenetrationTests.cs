using NUnit.Framework;
using UnityEngine;

// 할 일 136 — 플레이어 모터 정적 지형 디페네트레이션(벽 시작 겹침 탈출)과 유닛 밀어내기 벽 미끄러짐.
public sealed class PlayerStaticDepenetrationTests
{
    private const float MaxWalkable = 60f;
    private const float Speed = 5f;
    private const float Dt = 0.02f;

    private GameObject wall;
    private GameObject body;
    private GameObject ground;

    [TearDown]
    public void TearDown()
    {
        if (wall != null) Object.DestroyImmediate(wall);
        if (body != null) Object.DestroyImmediate(body);
        if (ground != null) Object.DestroyImmediate(ground);
    }

    [Test]
    public void EscapeStep_PlanarCancelled_FallsBackToFullThenDeepest_OrderIndependent()
    {
        // 양쪽 벽이 수평으로 상쇄 + 아래로 미는 관통 → 전체 합(수직)으로 푼다.
        var acc = new PlayerUnitCollisionRules.StaticPenetration();
        acc = PlayerUnitCollisionRules.AccumulateStaticEscape(acc, Vector3.left, 0.3f, MaxWalkable);
        acc = PlayerUnitCollisionRules.AccumulateStaticEscape(acc, Vector3.right, 0.3f, MaxWalkable);
        acc = PlayerUnitCollisionRules.AccumulateStaticEscape(acc, Vector3.down, 0.2f, MaxWalkable);
        Vector3 vertical = PlayerUnitCollisionRules.ComputeStaticEscapeStep(acc, Speed, Dt);
        Assert.That(vertical.y, Is.LessThan(0f), "수평이 0 이면 전체 벡터(수직)로 푼다");
        Assert.That(vertical.magnitude, Is.LessThanOrEqualTo(Speed * Dt + 1e-5f), "순간이동 아님");

        // 전체 합도 0(완전 대칭) → 가장 깊은 관통 하나. 깊이가 같으면 누적 순서와 무관하게 같은 방향.
        var a = new PlayerUnitCollisionRules.StaticPenetration();
        a = PlayerUnitCollisionRules.AccumulateStaticEscape(a, Vector3.left, 0.4f, MaxWalkable);
        a = PlayerUnitCollisionRules.AccumulateStaticEscape(a, Vector3.right, 0.4f, MaxWalkable);
        var b = new PlayerUnitCollisionRules.StaticPenetration();
        b = PlayerUnitCollisionRules.AccumulateStaticEscape(b, Vector3.right, 0.4f, MaxWalkable);
        b = PlayerUnitCollisionRules.AccumulateStaticEscape(b, Vector3.left, 0.4f, MaxWalkable);
        Vector3 stepA = PlayerUnitCollisionRules.ComputeStaticEscapeStep(a, Speed, Dt);
        Vector3 stepB = PlayerUnitCollisionRules.ComputeStaticEscapeStep(b, Speed, Dt);
        Assert.That(stepA.magnitude, Is.EqualTo(Speed * Dt).Within(1e-5f), "애매해도 멈춰 있지 않는다");
        Assert.That(stepA, Is.EqualTo(stepB), "버퍼 순서와 무관하게 한 방향 — 서버·오너가 같은 답");

        // 수평으로 풀리는 평소 경우는 종전 그대로(수직 0).
        var wallOnly = PlayerUnitCollisionRules.AccumulateStaticEscape(
            new PlayerUnitCollisionRules.StaticPenetration(), new Vector3(-0.8f, -0.6f, 0f), 0.5f, MaxWalkable);
        Vector3 planar = PlayerUnitCollisionRules.ComputeStaticEscapeStep(wallOnly, Speed, Dt);
        Assert.That(planar.y, Is.EqualTo(0f));
        Assert.That(planar.x, Is.LessThan(0f));
    }

    [Test]
    public void ReleasedInsideTallWallMidair_EscapesThenLands_Deterministic()
    {
        Vector3 first = SimulateReleaseInsideWall(out int escapeTicks, out int landTicks);
        Vector3 second = SimulateReleaseInsideWall(out _, out _);
        Assert.That(second, Is.EqualTo(first), "같은 입력이면 같은 결과 — 서버·오너 같은 시뮬레이션");
        TestContext.WriteLine($"탈출 {escapeTicks}틱, 착지 {landTicks}틱, 최종 {first}");
    }

    // 경계벽(두께 1m·높이 8m) 한가운데, 바닥 위 3.5m 에 놓인 몸 → 벽 밖으로 빠져나온 뒤 중력으로 떨어져 착지.
    private Vector3 SimulateReleaseInsideWall(out int escapeTicks, out int landTicks)
    {
        TearDown();
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground136";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(30f, 1f, 30f); // 윗면 y = 0

        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "TallWall136";
        wall.transform.position = new Vector3(0f, 4f, 0f);
        wall.transform.localScale = new Vector3(1f, 8f, 10f); // x ∈ [-0.5, 0.5], y ∈ [0, 8]

        body = new GameObject("Body136");
        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.radius = 0.38f;
        capsule.height = 1.8f;
        capsule.center = new Vector3(0f, 0.9f, 0f);
        body.transform.position = new Vector3(0f, 3.5f, 0f); // 두께 한가운데 — 나갈 방향이 애매
        Physics.SyncTransforms();

        LayerMask mask = 1 << wall.layer;
        var overlap = new Collider[8];
        var cast = new RaycastHit[8];
        Collider wallCollider = wall.GetComponent<Collider>();

        const int maxTicks = 150; // 탈출 ≈ 0.88m ÷ 0.1m = 9틱, 낙하 3.5m ≈ 43틱
        float verticalVelocity = 0f;
        escapeTicks = -1;
        landTicks = -1;
        for (int i = 0; i < maxTicks; i++)
        {
            Vector3 step = PlayerMotionSweep.ComputeStaticSeparationStep(
                capsule, Vector3.zero, mask, MaxWalkable, Speed, Dt, overlap);
            Assert.That(step.magnitude, Is.LessThanOrEqualTo(Speed * Dt + 1e-5f), "틱당 이동속도 × dt 상한");
            if (step != Vector3.zero)
            {
                body.transform.position += step;
                Physics.SyncTransforms();
                continue;
            }

            if (escapeTicks < 0)
                escapeTicks = i;

            // 모터와 같은 스윕 경로로 중력 낙하.
            verticalVelocity -= 9.81f * Dt;
            Vector3 fall = new Vector3(0f, verticalVelocity * Dt, 0f);
            Vector3 resolved = PlayerMotionSweep.Resolve(
                capsule, fall, Vector3.zero, false, 0.3f, MaxWalkable, mask, 0.02f, 3, cast);
            body.transform.position += resolved;
            Physics.SyncTransforms();
            if (resolved.y > fall.y + 1e-4f)
            {
                landTicks = i;
                break;
            }
        }

        Assert.That(escapeTicks, Is.GreaterThan(0), $"{maxTicks} 틱 안에 벽에서 빠져나와야 한다");
        Assert.That(landTicks, Is.GreaterThan(escapeTicks), $"{maxTicks} 틱 안에 바닥에 착지해야 한다");

        Vector3 pos = body.transform.position;
        Assert.That(Mathf.Abs(pos.x), Is.GreaterThanOrEqualTo(0.5f + capsule.radius - 0.01f), "벽 두께 밖");
        Assert.That(pos.y, Is.EqualTo(0f).Within(0.05f), "바닥 위에 선다");
        GetCapsuleEnds(capsule, out Vector3 p1, out Vector3 p2);
        Collider[] hits = Physics.OverlapCapsule(p1, p2, capsule.radius - 0.01f, mask, QueryTriggerInteraction.Ignore);
        Assert.That(System.Array.IndexOf(hits, wallCollider), Is.EqualTo(-1), "벽과 겹치지 않는다");
        return pos;
    }

    private static void GetCapsuleEnds(CapsuleCollider capsule, out Vector3 p1, out Vector3 p2)
    {
        Vector3 center = capsule.transform.TransformPoint(capsule.center);
        float half = capsule.height * 0.5f - capsule.radius;
        p1 = center + Vector3.up * half;
        p2 = center - Vector3.up * half;
    }

    [Test]
    public void AccumulateStatic_IgnoresWalkableGround_KeepsWallPlanar()
    {
        Vector3 ground = PlayerUnitCollisionRules.AccumulateStaticPenetration(Vector3.zero, Vector3.up, 0.02f, MaxWalkable);
        Assert.That(ground, Is.EqualTo(Vector3.zero), "바닥 접지 겹침은 밀지 않는다");

        Vector3 slope = PlayerUnitCollisionRules.AccumulateStaticPenetration(
            Vector3.zero, Quaternion.Euler(30f, 0f, 0f) * Vector3.up, 0.02f, MaxWalkable);
        Assert.That(slope, Is.EqualTo(Vector3.zero), "완경사 접지도 제외 — 경사에서 미끄러지지 않게");

        Vector3 wallPush = PlayerUnitCollisionRules.AccumulateStaticPenetration(Vector3.zero, Vector3.left, 0.3f, MaxWalkable);
        Assert.That(wallPush.x, Is.EqualTo(-0.3f).Within(1e-5f));
        Assert.That(wallPush.y, Is.EqualTo(0f));
    }

    [Test]
    public void SeparationStep_CappedBySpeedTimesDt()
    {
        Vector3 step = PlayerUnitCollisionRules.ComputeSeparationStep(new Vector3(-0.5f, 0f, 0f), Speed, Dt);
        Assert.That(step.magnitude, Is.EqualTo(Speed * Dt).Within(1e-5f), "순간이동 아님 — 틱당 속도 × dt");
        Assert.That(step.x, Is.LessThan(0f));
    }

    [Test]
    public void WallSlide_FollowsTangentSide_HeadOnPicksFixedSide()
    {
        Vector3 normal = Vector3.left; // 벽이 +x 쪽, 법선은 플레이어 쪽(−x)
        Vector3 oblique = PlayerUnitCollisionRules.ComputeWallSlideStep(new Vector3(0.1f, 0f, -0.05f), normal);
        Assert.That(oblique.x, Is.EqualTo(0f).Within(1e-5f), "벽 법선 방향 성분 없음");
        Assert.That(oblique.z, Is.LessThan(0f), "접선 성분 부호를 따른다");
        Assert.That(oblique.magnitude, Is.EqualTo(new Vector3(0.1f, 0f, -0.05f).magnitude).Within(1e-5f));

        Vector3 headOnA = PlayerUnitCollisionRules.ComputeWallSlideStep(new Vector3(0.1f, 0f, 0f), normal);
        Vector3 headOnB = PlayerUnitCollisionRules.ComputeWallSlideStep(new Vector3(0.1f, 0f, 0f), normal);
        Assert.That(headOnA.magnitude, Is.EqualTo(0.1f).Within(1e-5f));
        Assert.That(headOnA, Is.EqualTo(headOnB), "정면이면 고정 방향 — 서버·오너가 같은 답");

        Assert.That(PlayerUnitCollisionRules.ComputeWallSlideStep(new Vector3(0.1f, 0f, 0f), Vector3.up), Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void CapsuleInsideWall_EscapesWithinTicks_StepNeverExceedsCap()
    {
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall136";
        wall.transform.position = new Vector3(1f, 1f, 0f);
        wall.transform.localScale = new Vector3(1f, 4f, 4f); // x ∈ [0.5, 1.5]

        body = new GameObject("Body136");
        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.radius = 0.38f;
        capsule.height = 1.8f;
        capsule.center = new Vector3(0f, 0.9f, 0f);
        body.transform.position = new Vector3(0.4f, 0.1f, 0f); // 0.28m 파고듦(시작 겹침)
        Physics.SyncTransforms();

        LayerMask mask = 1 << wall.layer;
        var overlap = new Collider[8];
        Assert.That(PlayerMotionSweep.ComputeStaticPenetration(capsule, Vector3.zero, mask, MaxWalkable, overlap).x,
            Is.LessThan(-0.1f), "시작 겹침을 −x(벽 밖)로 푼다");

        const int maxTicks = 10; // 0.28m ÷ (5m/s × 0.02s) ≈ 3 틱
        bool escaped = false;
        for (int i = 0; i < maxTicks; i++)
        {
            Vector3 step = PlayerMotionSweep.ComputeStaticSeparationStep(
                capsule, Vector3.zero, mask, MaxWalkable, Speed, Dt, overlap);
            Assert.That(step.magnitude, Is.LessThanOrEqualTo(Speed * Dt + 1e-5f));
            if (step == Vector3.zero)
            {
                escaped = true;
                break;
            }
            body.transform.position += step;
            Physics.SyncTransforms();
        }

        Assert.That(escaped, Is.True, $"{maxTicks} 틱 안에 벽에서 빠져나와야 한다");
        Assert.That(body.transform.position.x + capsule.radius, Is.LessThanOrEqualTo(0.5f + PlayerUnitCollisionRules.MinSeparationDistance + 1e-3f));
        Assert.That(body.transform.position.y, Is.EqualTo(0.1f).Within(1e-5f), "수평으로만 민다");
    }
}
