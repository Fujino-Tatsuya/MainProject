using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 할 일 160 — 맵씬 게이트 레이저 시각(<see cref="MapGateLaserView"/>·<see cref="MapSceneBarriers.CreateGateLaser"/>).
/// 실제 프리팹(FBX bounds)으로 맞춤 결과를 보고, 열림 뒤 빔·콜라이더는 꺼지고 발사대·카브는 남는지 본다. 원본: Docs/tech/tutorial-director.md §7.
/// </summary>
public sealed class MapGateLaserViewTests
{
    const string PrefabPath = "Assets/2.Prefabs/Environment/MapScene/MapGateLaser.prefab";
    const float Tol = 0.02f;

    GameObject _root;

    [SetUp] public void SetUp() => _root = new GameObject("GateLaserTestRoot");
    [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

    static MapGateLaserView Prefab()
    {
        var p = AssetDatabase.LoadAssetAtPath<MapGateLaserView>(PrefabPath);
        Assert.IsNotNull(p, PrefabPath);
        return p;
    }

    MapGateLaserView Spawn() => Object.Instantiate(Prefab(), _root.transform, false);

    // 레이캐스트(벽 재기)가 열린 씬의 벽에 닿지 않게 멀리 둔다.
    static MapSceneBarrierSpec Spec() =>
        new MapSceneBarrierSpec(MapSceneGate.G3_Corridor2Entry, 2, true, MapSceneRect.FromMinMax(5000f, -2.3f, 5000.4f, 2.3f));

    static Bounds Bounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    [Test]
    public void 프리팹은_발사대와_빔_템플릿이_있고_콜라이더가_없다()
    {
        MapGateLaserView p = Prefab();
        Assert.IsNotNull(p.Posts, "발사대 칸");
        Assert.AreEqual(1, p.BeamCount, "빔 템플릿 하나");
        Assert.AreEqual(0, p.GetComponentsInChildren<Collider>(true).Length, "막기는 배리어 큐브가 한다");
    }

    [TestCase(2.76f, false)]
    [TestCase(4.6f, true)]
    public void Fit_빔_길이와_발사대_폭은_게이트_폭이다(float width, bool alongX)
    {
        MapGateLaserView v = Spawn();
        v.Fit(width, 3f, alongX);

        Assert.GreaterOrEqual(v.BeamCount, 2);
        Bounds posts = Bounds(v.Posts.gameObject);
        Assert.AreEqual(width, alongX ? posts.size.z : posts.size.x, Tol, "발사대 바깥 끝 = 폭");
        Assert.AreEqual(0f, posts.min.y, Tol, "바닥에 선다");

        float prevY = float.NegativeInfinity;
        for (int i = 0; i < v.BeamCount; i++)
        {
            Bounds b = Bounds(v.GetBeam(i));
            Assert.AreEqual(width, alongX ? b.size.z : b.size.x, Tol, $"빔 {i} 길이 = 폭");
            Assert.Less(alongX ? b.size.x : b.size.z, 0.2f, "빔은 진행 방향으로 얇다");
            Assert.Greater(b.center.y, prevY, "줄은 아래→위");
            Assert.LessOrEqual(b.max.y, posts.max.y + Tol, "빔은 발사대 높이 안");
            prevY = b.center.y;
        }
    }

    [Test]
    public void Fit_다시_불러도_줄이_불어나지_않는다()
    {
        MapGateLaserView v = Spawn();
        v.Fit(2.76f, 3f, false);
        int n = v.BeamCount;
        v.Fit(4.6f, 3f, true);
        Assert.AreEqual(n, v.BeamCount, "폭이 달라도 개수는 높이 기준");
        Assert.AreEqual(n, v.GetBeam(0).transform.parent.childCount, "복제 줄이 남지 않는다");
    }

    [Test]
    public void 줄_수는_높이와_간격으로_정하고_상한을_넘지_않는다()
    {
        Assert.AreEqual(7, MapGateLaserView.BeamRows(2.122f, 0.25f, 0.3f, 8));
        Assert.AreEqual(3, MapGateLaserView.BeamRows(2.122f, 0.25f, 0.3f, 3));
        Assert.AreEqual(1, MapGateLaserView.BeamRows(0.2f, 0.25f, 0.3f, 8), "낮아도 한 줄은 있다");
    }

    [Test]
    public void Open_빔만_즉시_꺼지고_발사대는_남는다_다시_맞춰도_꺼진_채()
    {
        MapGateLaserView v = Spawn();
        v.Fit(2.76f, 3f, false);
        v.Open();

        Assert.IsTrue(v.IsOpen);
        Assert.IsTrue(v.Posts.gameObject.activeInHierarchy, "발사대는 장식으로 남는다");
        for (int i = 0; i < v.BeamCount; i++) Assert.IsFalse(v.GetBeam(i).activeSelf, $"빔 {i}");

        v.Fit(2.76f, 3f, false);   // 늦게 맞춰도 같은 결과
        for (int i = 0; i < v.BeamCount; i++) Assert.IsFalse(v.GetBeam(i).activeSelf, $"재맞춤 빔 {i}");
    }

    [Test]
    public void 배리어_레이저는_큐브_렌더러만_끄고_콜라이더는_그대로다()
    {
        GameObject cube = MapSceneBarriers.CreateBarrierObject(Spec(), _root.transform, 0f, 3f, null, Color.white);
        MapGateLaserView laser = MapSceneBarriers.CreateGateLaser(Spec(), cube, 0f, 3f, Prefab());

        Assert.IsNotNull(laser);
        Assert.IsFalse(cube.GetComponent<MeshRenderer>().enabled, "큐브는 안 보인다");
        var box = cube.GetComponent<BoxCollider>();
        Assert.IsTrue(box.enabled && !box.isTrigger);
        Assert.AreEqual(GameLayers.BarrierLayer, cube.layer);
        Assert.Less(Vector3.Distance(new Vector3(0.4f, 3f, 4.6f), cube.transform.localScale), 1e-3f, "콜라이더 크기 그대로");
        Assert.AreSame(cube.transform.parent, laser.transform.parent, "큐브 형제");

        // 벽이 없으면 배리어 가로(4.6m) 그대로, 가운데·바닥에 놓인다.
        Bounds b = Bounds(laser.GetBeam(0));
        Assert.AreEqual(4.6f, b.size.z, Tol);
        Assert.AreEqual(5000.2f, laser.transform.position.x, 1e-3f);
        Assert.AreEqual(0f, laser.transform.position.z, 1e-3f);
    }

    [Test]
    public void 열면_콜라이더와_빔은_즉시_꺼지고_발사대와_카브는_남는다()
    {
        GameObject cube = MapSceneBarriers.CreateBarrierObject(Spec(), _root.transform, 0f, 3f, null, Color.white);
        MapGateLaserView laser = MapSceneBarriers.CreateGateLaser(Spec(), cube, 0f, 3f, Prefab());
        NavMeshObstacle block = _root.GetComponentInChildren<NavMeshObstacle>(true);

        MapSceneBarriers.OpenInstant(cube, laser);

        Assert.IsFalse(cube.GetComponent<Collider>().enabled, "플레이어 막기 해제");
        Assert.IsFalse(cube.activeSelf);
        Assert.IsTrue(laser.gameObject.activeInHierarchy && laser.Posts.gameObject.activeInHierarchy, "발사대 남음");
        for (int i = 0; i < laser.BeamCount; i++) Assert.IsFalse(laser.GetBeam(i).activeSelf, $"빔 {i}");
        Assert.IsTrue(block.enabled && block.gameObject.activeInHierarchy && block.carving, "몬스터 카브 유지(148)");
    }

    [Test]
    public void 프리팹이_없으면_큐브_그대로다()
    {
        GameObject cube = MapSceneBarriers.CreateBarrierObject(Spec(), _root.transform, 0f, 3f, null, Color.white, carve: false);
        Assert.IsNull(MapSceneBarriers.CreateGateLaser(Spec(), cube, 0f, 3f, null));
        Assert.IsTrue(cube.GetComponent<MeshRenderer>().enabled);
    }
}
