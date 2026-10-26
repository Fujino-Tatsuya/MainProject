using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 로비 문짝 다음 자세 × 몸 캡슐 겹침 통합 테스트 — 실제 BoxCollider·비볼록 MeshCollider·CapsuleCollider 로
/// `LobbyProximityDoor.LeafBox`·`BodyOverlapsLeafBox` 를 탄다. (할 일 99)
/// 문짝은 힌지(원점)에서 로컬 -X 로 1.27m, 두께 Z -0.22~0.06(PF_Room_DoorSingle_01_v003 과 같은 모양).
/// +90° 로 열면 문짝이 +Z 쪽으로 넘어가 그 자리에 선 몸 캡슐(반경 0.38 — 문짝 두께보다 굵다)을 관통한다.
/// `Physics.ComputePenetration` 은 비볼록 메시를 캡슐이 관통하면 양면이 상쇄돼 false 라 상자로 판정한다.
/// </summary>
public sealed class LobbyProximityDoorPenetrationTests
{
    private const float TestY = 200f;
    private const float FrontFaceZ = 0.06f;

    private static readonly Vector3 LeafCenter = new Vector3(-0.635f, 1f, -0.08f);
    private static readonly Vector3 LeafSize = new Vector3(1.27f, 2f, 0.28f);
    private static readonly Vector3 BodyInSwing = new Vector3(-0.1f, 0.9f, 0.7f);

    private readonly List<GameObject> createdObjects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
                Object.DestroyImmediate(createdObject);
        }

        createdObjects.Clear();
    }

    [Test]
    public void BoxLeaf_SwingThroughBody_Overlaps()
    {
        Transform leaf = CreateLeaf(Quaternion.identity);
        BoxCollider box = leaf.gameObject.AddComponent<BoxCollider>();
        box.center = LeafCenter;
        box.size = LeafSize;
        CapsuleCollider body = CreateBody(leaf.TransformPoint(BodyInSwing));

        Assert.That(Overlaps(leaf, body, 0f), Is.False, "닫힌 자세");
        Assert.That(Overlaps(leaf, body, -90f), Is.False, "반대쪽으로 열린 자세");
        Assert.That(Overlaps(leaf, body, 90f), Is.True, "몸 쪽으로 열린 자세");
    }

    [Test]
    public void NonConvexMeshChildLeaf_SwingThroughBody_Overlaps()
    {
        Transform leaf = CreateLeaf(Quaternion.identity);
        // 실제 문처럼 비볼록 MeshCollider. 자식 + 배율로 두어 문짝 기준 상자 환산도 탄다.
        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(mesh.GetComponent<BoxCollider>());
        mesh.transform.SetParent(leaf, false);
        mesh.transform.localPosition = LeafCenter;
        mesh.transform.localScale = LeafSize;
        MeshCollider meshCollider = mesh.AddComponent<MeshCollider>();
        meshCollider.convex = false;
        meshCollider.sharedMesh = mesh.GetComponent<MeshFilter>().sharedMesh;
        CapsuleCollider body = CreateBody(leaf.TransformPoint(BodyInSwing));

        Assert.That(Overlaps(leaf, body, 0f), Is.False, "닫힌 자세");
        Assert.That(Overlaps(leaf, body, 90f), Is.True, "몸 쪽으로 열린 자세");
    }

    [Test]
    public void RotatedDoor_SameResultAsUnrotated()
    {
        // 씬의 문은 Y 270° 처럼 돌아 있다 — 상자는 문짝 회전 기준이라 결과가 같아야 한다.
        Transform leaf = CreateLeaf(Quaternion.Euler(0f, 270f, 0f));
        BoxCollider box = leaf.gameObject.AddComponent<BoxCollider>();
        box.center = LeafCenter;
        box.size = LeafSize;
        CapsuleCollider body = CreateBody(leaf.TransformPoint(BodyInSwing));

        Assert.That(Overlaps(leaf, body, 0f), Is.False, "닫힌 자세");
        Assert.That(Overlaps(leaf, body, 90f), Is.True, "몸 쪽으로 열린 자세");
    }

    [Test]
    public void BodyAwayFromSwing_NeverOverlaps()
    {
        Transform leaf = CreateLeaf(Quaternion.identity);
        BoxCollider box = leaf.gameObject.AddComponent<BoxCollider>();
        box.center = LeafCenter;
        box.size = LeafSize;
        CapsuleCollider body = CreateBody(leaf.TransformPoint(new Vector3(-0.6f, 0.9f, 2.2f)));

        foreach (float angle in new[] { 0f, 30f, 60f, 90f })
            Assert.That(Overlaps(leaf, body, angle), Is.False, $"angle {angle}");
    }

    [Test]
    public void TriggerOnlyLeaf_HasNoBox()
    {
        Transform leaf = CreateLeaf(Quaternion.identity);
        BoxCollider box = leaf.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;

        Assert.That(LobbyProximityDoor.LeafBox(leaf, leaf.GetComponentsInChildren<Collider>(true), out _), Is.False);
    }

    private static bool Overlaps(Transform leaf, CapsuleCollider body, float angle)
    {
        Assert.That(LobbyProximityDoor.LeafBox(leaf, leaf.GetComponentsInChildren<Collider>(true), out Bounds leafBox), Is.True);
        LobbyProximityDoorRules.LeafPose(leaf.position, leaf.rotation, angle, new Vector3(0f, 0f, FrontFaceZ),
            out Vector3 position, out Quaternion rotation);
        return LobbyProximityDoor.BodyOverlapsLeafBox(leafBox, position, rotation, body);
    }

    private Transform CreateLeaf(Quaternion rotation)
    {
        GameObject gameObject = new GameObject("Leaf");
        createdObjects.Add(gameObject);
        gameObject.transform.SetPositionAndRotation(new Vector3(3f, TestY, -2f), rotation);
        return gameObject.transform;
    }

    private CapsuleCollider CreateBody(Vector3 position)
    {
        GameObject gameObject = new GameObject("Body");
        createdObjects.Add(gameObject);
        gameObject.transform.position = position;

        // Player.prefab 몸 캡슐과 같은 크기.
        CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = 0.38f;
        capsule.height = 1.79f;
        return capsule;
    }
}
