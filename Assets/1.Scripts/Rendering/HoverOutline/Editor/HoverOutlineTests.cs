using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 몬스터 호버 외곽선(할 일 58) — 대상 고르기 규칙, 전환 시 떼기/붙이기, 겹 렌더러가 원본 머티리얼을 안 건드리는지,
/// 림 설정 범위·셰이더 식.
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

    // ── 겹 렌더러 ──

    [Test]
    public void 붙이기_원본_머티리얼_배열은_그대로()
    {
        Material body = NewMaterial("Body");
        Material outline = NewMaterial("Outline");
        var root = Track(new GameObject("Monster"));
        MeshRenderer r = NewMeshRenderer(root.transform, "Body", body);

        var set = new HoverOutlineOverlaySet();
        set.Attach(root.transform, outline);

        Assert.AreEqual(1, set.Count);
        CollectionAssert.AreEqual(new[] { body }, r.sharedMaterials);
        Renderer overlay = set.GetOverlay(0);
        Assert.AreSame(r.transform, overlay.transform.parent);
        Assert.AreEqual(HoverOutlineOverlaySet.OverlayName, overlay.name);
        CollectionAssert.AreEqual(new[] { outline }, overlay.sharedMaterials);
        Assert.AreEqual(ShadowCastingMode.Off, overlay.shadowCastingMode);

        set.Clear();
        Assert.AreEqual(0, set.Count);
        Assert.AreEqual(0, r.transform.childCount);
        CollectionAssert.AreEqual(new[] { body }, r.sharedMaterials);
    }

    [Test]
    public void 스키닝_여러_서브메시_전부_덮는다()
    {
        Material outline = NewMaterial("Outline");
        var root = Track(new GameObject("Boss"));
        var go = new GameObject("Body");
        go.transform.SetParent(root.transform, false);
        var skin = go.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = Track(NewMesh(3));
        Material[] originals = { NewMaterial("A"), NewMaterial("B"), NewMaterial("C") };
        skin.sharedMaterials = originals;

        var set = new HoverOutlineOverlaySet();
        set.Attach(root.transform, outline);

        Assert.AreEqual(1, set.Count);
        var overlay = set.GetOverlay(0) as SkinnedMeshRenderer;
        Assert.IsNotNull(overlay);
        Assert.AreSame(skin.sharedMesh, overlay.sharedMesh);
        CollectionAssert.AreEqual(new[] { outline, outline, outline }, overlay.sharedMaterials);
        CollectionAssert.AreEqual(originals, skin.sharedMaterials);
    }

    [Test]
    public void 다시_붙여도_겹이_쌓이지_않는다()
    {
        Material outline = NewMaterial("Outline");
        var root = Track(new GameObject("Monster"));
        MeshRenderer r = NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));

        var set = new HoverOutlineOverlaySet();
        set.Attach(root.transform, outline);
        set.Attach(root.transform, outline);

        Assert.AreEqual(1, set.Count);
        Assert.AreEqual(1, r.transform.childCount);
    }

    [Test]
    public void 연출용_반투명_렌더러는_제외()
    {
        Material outline = NewMaterial("Outline");
        var root = Track(new GameObject("Monster"));
        NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));
        MeshRenderer marked = NewMeshRenderer(root.transform, "Arc", NewMaterial("Arc"));
        marked.gameObject.AddComponent<NoHitFlash>();
        NewMeshRenderer(root.transform, "Glass", NewMaterial("Glass", (int)RenderQueue.Transparent));

        var set = new HoverOutlineOverlaySet();
        set.Attach(root.transform, outline);

        Assert.AreEqual(1, set.Count);
        Assert.AreEqual("Body", set.GetSource(0).name);
    }

    [Test]
    public void 원본이_꺼지면_겹도_꺼진다()
    {
        var root = Track(new GameObject("Monster"));
        MeshRenderer r = NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));
        var set = new HoverOutlineOverlaySet();
        set.Attach(root.transform, NewMaterial("Outline"));

        r.enabled = false;
        set.SyncVisibility();
        Assert.IsFalse(set.GetOverlay(0).enabled);

        r.enabled = true;
        set.SyncVisibility();
        Assert.IsTrue(set.GetOverlay(0).enabled);
    }

    // ── 림 설정 ──

    [Test]
    public void 림_설정_범위_밖은_클램프()
    {
        var s = new HoverRimSettings(new Color(1f, 0f, 0f, 3f), 1.5f, -0.2f, 99f).Sanitized();
        Assert.AreEqual(1f, s.Threshold);
        Assert.AreEqual(0f, s.Softness);
        Assert.AreEqual(HoverRimSettings.MaxIntensity, s.Intensity);
        Assert.AreEqual(1f, s.Color.a);

        s = new HoverRimSettings(Color.red, -1f, 5f, -3f).Sanitized();
        Assert.AreEqual(0f, s.Threshold);
        Assert.AreEqual(HoverRimSettings.MaxSoftness, s.Softness);
        Assert.AreEqual(0f, s.Intensity);
    }

    [Test]
    public void 림_설정_NaN은_최소값()
    {
        var s = new HoverRimSettings(Color.red, float.NaN, float.NaN, float.NaN).Sanitized();
        Assert.AreEqual(0f, s.Threshold);
        Assert.AreEqual(0f, s.Softness);
        Assert.AreEqual(0f, s.Intensity);
    }

    [Test]
    public void 림_설정_범위_안은_그대로()
    {
        var raw = new HoverRimSettings(Color.red, 0.25f, 0.1f, 1f);
        Assert.AreEqual(raw, raw.Sanitized());
    }

    [Test]
    public void 림은_실루엣에서_1_정면에서_0()
    {
        Assert.AreEqual(1f, HoverRimSettings.RimAlpha(0f, 0.25f, 0.1f), 1e-5f);
        Assert.AreEqual(0f, HoverRimSettings.RimAlpha(1f, 0.25f, 0.1f), 1e-5f);
        Assert.AreEqual(0.5f, HoverRimSettings.RimAlpha(0.25f, 0.25f, 0.1f), 1e-5f);
    }

    [Test]
    public void 림_부드러움_0이면_임계값에서_딱_잘린다()
    {
        Assert.AreEqual(1f, HoverRimSettings.RimAlpha(0.249f, 0.25f, 0f), 1e-5f);
        Assert.AreEqual(0f, HoverRimSettings.RimAlpha(0.251f, 0.25f, 0f), 1e-5f);
    }

    [Test]
    public void 림_셰이더는_색_틴트_프로퍼티가_없다()
    {
        Shader shader = Shader.Find("Project/Enemy Hover Rim");
        Assert.IsNotNull(shader);
        var m = Track(new Material(shader));
        Assert.IsFalse(m.HasProperty("_BaseColor"));
        Assert.IsFalse(m.HasProperty("_Color"));
        Assert.IsTrue(m.HasProperty(HoverRimSettings.ColorId));
        Assert.IsTrue(m.HasProperty(HoverRimSettings.ThresholdId));
        Assert.IsTrue(m.HasProperty(HoverRimSettings.SoftnessId));
        Assert.IsTrue(m.HasProperty(HoverRimSettings.IntensityId));
        Assert.GreaterOrEqual(m.renderQueue, (int)RenderQueue.Transparent);
    }

    [Test]
    public void 대상이_먼저_파괴돼도_떼기는_안전()
    {
        var root = new GameObject("Monster");
        NewMeshRenderer(root.transform, "Body", NewMaterial("Body"));
        var set = new HoverOutlineOverlaySet();
        set.Attach(root.transform, NewMaterial("Outline"));

        Object.DestroyImmediate(root);
        Assert.DoesNotThrow(() => set.SyncVisibility());
        Assert.DoesNotThrow(() => set.Clear());
        Assert.AreEqual(0, set.Count);
    }
}
