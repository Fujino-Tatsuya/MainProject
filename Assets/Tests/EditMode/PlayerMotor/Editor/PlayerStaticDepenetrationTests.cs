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

    [TearDown]
    public void TearDown()
    {
        if (wall != null) Object.DestroyImmediate(wall);
        if (body != null) Object.DestroyImmediate(body);
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
