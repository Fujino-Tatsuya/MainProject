using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 할 일 148 — 복도는 늘 몬스터 출입 금지. 배리어(<see cref="MapSceneBarriers.CreateBarrierObject"/>)를 열어
/// 큐브가 비활성돼도 몬스터 카브(<c>MonsterBlock_G*</c>)는 켜진 채 배리어 자리를 덮는다. 원본: Docs/tech/tutorial-director.md §7.
/// </summary>
public sealed class MapSceneBarrierMonsterBlockTests
{
    GameObject _root;

    [SetUp] public void SetUp() => _root = new GameObject("MonsterBlockTestRoot");
    [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

    static MapSceneBarrierSpec Spec() =>
        new MapSceneBarrierSpec(MapSceneGate.G3_Corridor2Entry, 2, true, MapSceneRect.FromMinMax(10f, -2.3f, 10.4f, 2.3f));

    NavMeshObstacle Block()
    {
        NavMeshObstacle[] all = _root.GetComponentsInChildren<NavMeshObstacle>(true);
        Assert.AreEqual(1, all.Length, "카브는 하나");
        return all[0];
    }

    [Test]
    public void 카브는_큐브와_따로다_큐브를_꺼도_남는다()
    {
        GameObject cube = MapSceneBarriers.CreateBarrierObject(Spec(), _root.transform, 0f, 3f, null, Color.white);
        NavMeshObstacle block = Block();
        Assert.IsFalse(block.transform.IsChildOf(cube.transform), "큐브 자식이면 열 때 같이 꺼진다");
        Assert.IsNull(cube.GetComponent<NavMeshObstacle>());

        // 열기 = 큐브 콜라이더 끄기 + 페이드(스케일) 뒤 비활성
        cube.GetComponent<Collider>().enabled = false;
        cube.transform.localScale = new Vector3(cube.transform.localScale.x, 0f, cube.transform.localScale.z);
        cube.SetActive(false);

        Assert.IsTrue(block.enabled && block.gameObject.activeInHierarchy, "열려도 몬스터 카브는 켜져 있다");
        Assert.IsTrue(block.carving);
        Assert.IsFalse(block.carveOnlyStationary);
        Assert.AreEqual(3f + 2f, block.size.y, 1e-4f, "위아래 +2m 그대로");
    }

    [Test]
    public void 카브는_배리어_자리를_덮는다()
    {
        MapSceneBarriers.CreateBarrierObject(Spec(), _root.transform, 1f, 3f, null, Color.white);
        NavMeshObstacle block = Block();
        Vector3 c = block.transform.TransformPoint(block.center);
        Assert.AreEqual(10.2f, c.x, 1e-4f);
        Assert.AreEqual(0f, c.z, 1e-4f);
        Assert.AreEqual(0.4f, block.size.x, 1e-4f);
        Assert.AreEqual(4.6f, block.size.z, 1e-4f);
        Assert.Less(c.y - block.size.y * 0.5f, 1f, "바닥(1m) 아래까지 내려와 표면을 판다");
        Assert.AreEqual(Vector3.one, block.transform.lossyScale);
    }

    [Test]
    public void 뒤_배리어는_카브가_없다()
    {
        MapSceneBarriers.CreateBarrierObject(Spec(), _root.transform, 0f, 3f, null, Color.white, carve: false);
        Assert.AreEqual(0, _root.GetComponentsInChildren<NavMeshObstacle>(true).Length);
    }
}
