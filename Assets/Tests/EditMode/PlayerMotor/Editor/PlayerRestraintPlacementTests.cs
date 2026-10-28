using NUnit.Framework;
using UnityEngine;

// 할 일 136 — 23호 잡기 구속 포즈 목표점(Push 정면점·Carry 손 소켓)·놓는 자리를 시전자 중심 → 목표점 정적 스윕으로 벽 앞에 자른다.
public sealed class PlayerRestraintPlacementTests
{
    private const float Skin = 0.02f;

    private GameObject wall;
    private GameObject body;

    [TearDown]
    public void TearDown()
    {
        if (wall != null) Object.DestroyImmediate(wall);
        if (body != null) Object.DestroyImmediate(body);
    }

    [Test]
    public void ClampDistance_Unblocked_KeepsRequested()
    {
        Assert.That(PlayerUnitCollisionRules.ClampPlacementDistance(2.24f, false, 0f, Skin), Is.EqualTo(2.24f));
    }

    [Test]
    public void ClampDistance_Blocked_StopsSkinBeforeWall_NeverNegativeOrBeyond()
    {
        Assert.That(PlayerUnitCollisionRules.ClampPlacementDistance(2.24f, true, 1.0f, Skin), Is.EqualTo(0.98f).Within(1e-5f));
        Assert.That(PlayerUnitCollisionRules.ClampPlacementDistance(2.24f, true, 0f, Skin), Is.EqualTo(0f), "시작 겹침 → 시전자 자리");
        Assert.That(PlayerUnitCollisionRules.ClampPlacementDistance(0.5f, true, 3f, Skin), Is.EqualTo(0.5f), "요청보다 멀리 놓지 않는다");
    }

    [Test]
    public void ClampPlacement_TargetBehindWall_PlacedInFrontOfWall()
    {
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(2f, 1f, 0f);
        wall.transform.localScale = new Vector3(1f, 4f, 4f); // 안쪽 면 x = 1.5

        body = new GameObject("Body136Grab");
        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.radius = 0.38f;
        capsule.height = 1.8f;
        capsule.center = new Vector3(0f, 0.9f, 0f);
        body.transform.position = new Vector3(0f, 0f, 5f); // 몸 위치는 결과와 무관(오프셋으로만 스윕)
        Physics.SyncTransforms();

        LayerMask mask = 1 << wall.layer;
        var buffer = new RaycastHit[8];
        Vector3 origin = Vector3.zero;              // 보스 중심(목표 높이)
        Vector3 target = new Vector3(2.24f, 0f, 0f); // grabRadius × 0.6 — 벽 안

        Vector3 placed = PlayerMotionSweep.ClampPlacement(capsule, origin, target, mask, 60f, Skin, buffer);
        Assert.That(placed.x + capsule.radius, Is.LessThanOrEqualTo(1.5f + 1e-3f), "몸이 벽 안쪽 면을 넘지 않는다");
        Assert.That(placed.x, Is.GreaterThan(1.0f), "벽 바로 앞까지는 간다");
        Assert.That(placed.y, Is.EqualTo(0f).Within(1e-5f));

        Vector3 free = PlayerMotionSweep.ClampPlacement(capsule, origin, new Vector3(-2.24f, 0f, 0f), mask, 60f, Skin, buffer);
        Assert.That(free, Is.EqualTo(new Vector3(-2.24f, 0f, 0f)), "막는 것이 없으면 목표점 그대로");
    }
}
