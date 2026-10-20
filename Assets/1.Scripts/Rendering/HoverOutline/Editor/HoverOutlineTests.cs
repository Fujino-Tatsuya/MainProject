using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 몬스터 외곽선(할 일 58) — 대상 고르기 규칙, 전환, 마스크에 그릴 렌더러 고르기(원본 머티리얼 무수정),
/// 그릴 목록(꺼진 렌더러 제외·색 인덱스·굵기 클램프), 마스크/합성 셰이더.
/// </summary>
public sealed class HoverOutlineTests
{
    readonly List<Object> _cleanup = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        EnemyOutlineDrawList.Clear();
    }

    T Track<T>(T obj) where T : Object
    {
        _cleanup.Add(obj);
        return obj;
    }

    Material NewMaterial(string name, int renderQueue = -1)
    {
        var m = Track(new Material(Shader.Find("Hidden/InternalErrorShader")) { name = name });
        if (renderQueue >= 0)
            m.renderQueue = renderQueue;
        return m;
    }

    MeshRenderer NewMeshRenderer(Transform parent, string name, Material material)
    {
        var go = new GameObject(name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        else
            Track(go);
        go.AddComponent<MeshFilter>().sharedMesh = Track(NewMesh(1));
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        return r;
    }

    static UnityEngine.Mesh NewMesh(int subMeshes)
    {
        var mesh = new UnityEngine.Mesh
        {
            vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
            subMeshCount = subMeshes,
        };
        for (int i = 0; i < subMeshes; i++)
            mesh.SetTriangles(new[] { 0, 1, 2 }, i);
        return mesh;
    }

    List<Renderer> Collect(Transform root)
    {
        var into = new List<Renderer>();
        EnemyOutlineRenderers.Collect(root, into, new List<Renderer>());
        return into;
    }

    // ── 대상 고르기 ──

    [Test]
    public void 살아있는_적은_고를_수_있다()
    {
        Assert.IsTrue(CursorUnitPick.IsSelectable(false, 1));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void HP_0_이하_사망처리중은_제외(int hp)
    {
        Assert.IsFalse(CursorUnitPick.IsSelectable(false, hp));
    }

    [Test]
    public void 자기_자신은_제외()
    {
        Assert.IsFalse(CursorUnitPick.IsSelectable(true, 100));
    }

    [Test]
    public void 유닛이_없으면_제외()
    {
        Assert.IsFalse(CursorUnitPick.IsSelectable(null, null));
        Assert.IsNull(CursorUnitPick.ResolveUnit(null));
    }

    [Test]
    public void 자식_콜라이더는_상위_유닛으로_해석()
    {
        var root = Track(new GameObject("Monster"));
        var unit = root.AddComponent<Unit>();
        var part = new GameObject("Part");
        part.transform.SetParent(root.transform, false);
        var col = part.AddComponent<BoxCollider>();

        Assert.AreSame(unit, CursorUnitPick.ResolveUnit(col));
    }

    [Test]
    public void 적_마스크는_Enemy와_EnemyHurtBox()
    {
        int expected = LayerMask.GetMask("Enemy", "EnemyHurtBox");
        Assert.AreNotEqual(0, expected);
        Assert.AreEqual(expected, GameLayers.EnemyTargetMask);
    }

    // ── 전환 ──

    [Test]
    public void 같은_대상이면_전환_없음()
    {
        var t = new HoverOutlineTracker();
        var a = new object();
        Assert.IsTrue(t.Set(a, out _));
        Assert.IsFalse(t.Set(a, out object prev));
        Assert.AreSame(a, prev);
    }

    [Test]
    public void 대상이_바뀌면_직전_대상을_돌려준다()
    {
        var t = new HoverOutlineTracker();
        var a = new object();
        var b = new object();
        t.Set(a, out _);

        Assert.IsTrue(t.Set(b, out object prev));
        Assert.AreSame(a, prev);
        Assert.AreSame(b, t.Current);

        Assert.IsTrue(t.Set(null, out prev));
        Assert.AreSame(b, prev);
        Assert.IsNull(t.Current);
        Assert.IsFalse(t.Set(null, out _));
    }

    // ── 마스크에 그릴 렌더러 ──

    [Test]
    public void 렌더러_모으기_원본_머티리얼_계층은_그대로()
    {
        Material body = NewMaterial("Body");
        var root = Track(new GameObject("Monster"));
        MeshRenderer r = NewMeshRenderer(root.transform, "Body", body);
        int layer = r.gameObject.layer;

        List<Renderer> found = Collect(root.transform);

        CollectionAssert.AreEqual(new Renderer[] { r }, found);
        CollectionAssert.AreEqual(new[] { body }, r.sharedMaterials);
        Assert.AreEqual(0, r.transform.childCount);
        Assert.AreEqual(layer, r.gameObject.layer);
    }

    [Test]
    public void 스키닝_여러_서브메시는_서브메시_수만큼_그린다()
    {
        var root = Track(new GameObject("Boss"));
        var go = new GameObject("Body");
        go.transform.SetParent(root.transform, false);
        var skin = go.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = Track(NewMesh(3));
        Material[] originals = { NewMaterial("A"), NewMaterial("B"), NewMaterial("C") };
        skin.sharedMaterials = originals;

        List<Renderer> found = Collect(root.transform);
        Assert.AreEqual(1, found.Count);
        Assert.AreEqual(3, EnemyOutlineRenderers.SubMeshCount(skin));

        EnemyOutlineDrawList.Begin(Color.red, Color.yellow, 3);
        Assert.IsTrue(EnemyOutlineDrawList.Add(skin, EnemyOutlineDrawList.PrimaryColorIndex));
        Assert.AreEqual(3, EnemyOutlineDrawList.Get(0).SubMeshCount);
        CollectionAssert.AreEqual(originals, skin.sharedMaterials);
    }

    [Test]
    public void 연출용_반투명_렌더러는_제외()
    {
        var root = Track(new GameObject("Monster"));
        NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));
        MeshRenderer marked = NewMeshRenderer(root.transform, "Arc", NewMaterial("Arc"));
        marked.gameObject.AddComponent<NoHitFlash>();
        NewMeshRenderer(root.transform, "Glass", NewMaterial("Glass", (int)RenderQueue.Transparent));

        List<Renderer> found = Collect(root.transform);

        Assert.AreEqual(1, found.Count);
        Assert.AreEqual("Body", found[0].name);
    }

    [Test]
    public void 꺼진_렌더러는_그릴_목록에서_빠진다()
    {
        var root = Track(new GameObject("Monster"));
        MeshRenderer r = NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));

        EnemyOutlineDrawList.Begin(Color.red, Color.yellow, 3);
        r.enabled = false;
        Assert.IsFalse(EnemyOutlineDrawList.Add(r, EnemyOutlineDrawList.PrimaryColorIndex));
        r.enabled = true;
        r.forceRenderingOff = true;
        Assert.IsFalse(EnemyOutlineDrawList.Add(r, EnemyOutlineDrawList.PrimaryColorIndex));
        r.forceRenderingOff = false;
        Assert.IsTrue(EnemyOutlineDrawList.Add(r, EnemyOutlineDrawList.AggroColorIndex));

        Assert.AreEqual(1, EnemyOutlineDrawList.Count);
        Assert.AreEqual(EnemyOutlineDrawList.AggroColorIndex, EnemyOutlineDrawList.Get(0).ColorIndex);
    }

    [Test]
    public void 그릴_목록은_프레임마다_새로_시작()
    {
        var root = Track(new GameObject("Monster"));
        MeshRenderer r = NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));

        EnemyOutlineDrawList.Begin(Color.red, Color.yellow, 3);
        EnemyOutlineDrawList.Add(r, EnemyOutlineDrawList.PrimaryColorIndex);
        EnemyOutlineDrawList.Begin(Color.red, Color.yellow, 3);
        Assert.AreEqual(0, EnemyOutlineDrawList.Count);
        Assert.IsFalse(EnemyOutlineDrawList.Add(null, 0));
    }

    [TestCase(0, 1)]
    [TestCase(3, 3)]
    [TestCase(99, 8)]
    public void 굵기는_1에서_8픽셀로_클램프(int raw, int expected)
    {
        EnemyOutlineDrawList.Begin(Color.red, Color.yellow, raw);
        Assert.AreEqual(expected, EnemyOutlineDrawList.WidthPixels);
    }

    [Test]
    public void 대상이_먼저_파괴돼도_목록은_안전()
    {
        var root = new GameObject("Monster");
        MeshRenderer r = NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));
        EnemyOutlineDrawList.Begin(Color.red, Color.yellow, 3);
        EnemyOutlineDrawList.Add(r, 0);

        Object.DestroyImmediate(root);
        Assert.IsTrue(EnemyOutlineDrawList.Get(0).Renderer == null);
        Assert.IsFalse(EnemyOutlineDrawList.Add(r, 0));
    }

    // ── 셰이더 ──

    [TestCase("Hidden/Rendering/EnemyOutlineMask")]
    [TestCase("Hidden/Rendering/EnemyOutlineComposite")]
    public void 외곽선_셰이더가_있고_지원된다(string name)
    {
        Shader shader = Shader.Find(name);
        Assert.IsNotNull(shader, name);
        Assert.IsTrue(shader.isSupported, name);
    }

    [Test]
    public void 마스크_셰이더는_색_틴트_프로퍼티가_없다()
    {
        // HitFlash·사망 틴트(MPB _BaseColor/_Color)가 덮어쓰기 머티리얼에 영향을 주지 않는다.
        var m = Track(new Material(Shader.Find("Hidden/Rendering/EnemyOutlineMask")));
        Assert.IsFalse(m.HasProperty("_BaseColor"));
        Assert.IsFalse(m.HasProperty("_Color"));
        Assert.IsTrue(m.HasProperty("_OutlineColorIndex"));
    }
}
