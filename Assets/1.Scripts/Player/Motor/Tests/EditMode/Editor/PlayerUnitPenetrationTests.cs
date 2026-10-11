using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// `PlayerMotionSweep.ComputeUnitPenetration` 통합 테스트 — 실제 `Physics.OverlapCapsule`·`ComputePenetration` 을 탄다.
/// 겹친 유닛에서 빠져나갈 수평 방향과, Player 레이어 중 몸 캡슐(PlayerMotor)이 아닌 콜라이더를 무시하는지 본다. (할 일 11)
/// </summary>
public sealed class PlayerUnitPenetrationTests
{
    private const float TestY = 200f;

    private readonly List<GameObject> createdObjects = new List<GameObject>();
    private readonly Collider[] overlapBuffer = new Collider[8];

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
                Object.DestroyImmediate(createdObject);
        }

        createdObjects.Clear();
        Physics.SyncTransforms();
    }

    [Test]
    public void OverlappingEnemy_PushesAwayHorizontally()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        Assume.That(enemyLayer, Is.GreaterThanOrEqualTo(0));

        CapsuleCollider player = CreateCapsule("Player", new Vector3(0f, TestY, 0f), 0);
        CreateCapsule("Enemy", new Vector3(0.6f, TestY, 0f), enemyLayer);
        Physics.SyncTransforms();

        Vector3 penetration = PlayerMotionSweep.ComputeUnitPenetration(player, Vector3.zero, 1 << enemyLayer, overlapBuffer);

        Assert.That(penetration.x, Is.LessThan(-0.3f));
        Assert.That(penetration.y, Is.EqualTo(0f));
        Assert.That(Mathf.Abs(penetration.z), Is.LessThan(0.001f));
    }

    [Test]
    public void SeparatedEnemy_NoPenetration()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        Assume.That(enemyLayer, Is.GreaterThanOrEqualTo(0));

        CapsuleCollider player = CreateCapsule("Player", new Vector3(0f, TestY, 0f), 0);
        CreateCapsule("Enemy", new Vector3(1.5f, TestY, 0f), enemyLayer);
        Physics.SyncTransforms();

        Vector3 penetration = PlayerMotionSweep.ComputeUnitPenetration(player, Vector3.zero, 1 << enemyLayer, overlapBuffer);

        Assert.That(penetration, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void PlayerLayerColliderWithoutMotor_IsIgnored()
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        Assume.That(playerLayer, Is.GreaterThanOrEqualTo(0));

        CapsuleCollider player = CreateCapsule("Player", new Vector3(0f, TestY, 0f), 0);
        // 가붕이 AAC 히트박스처럼 Player 레이어지만 몸 캡슐이 아닌 콜라이더.
        CreateCapsule("Hitbox", new Vector3(0.5f, TestY, 0f), playerLayer);
        Physics.SyncTransforms();

        Vector3 penetration = PlayerMotionSweep.ComputeUnitPenetration(player, Vector3.zero, 1 << playerLayer, overlapBuffer);

        Assert.That(penetration, Is.EqualTo(Vector3.zero));
    }

    private CapsuleCollider CreateCapsule(string name, Vector3 position, int layer)
    {
        GameObject gameObject = new GameObject(name);
        createdObjects.Add(gameObject);
        gameObject.layer = layer;
        gameObject.transform.position = position;

        CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = 0.5f;
        capsule.height = 2f;
        return capsule;
    }
}
