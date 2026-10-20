using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

/// <summary>
/// 할 일 57 S5 — 예열 후 예고를 되풀이해도 재질·텍스처·스프라이트 인스턴스가 늘지 않는다(결정적 단언만, 타이밍 없음).
/// 예전 C3 는 크기가 바뀔 때마다 384² 텍스처를 새로 굽고 해제 없이 쌓았다(PLAN-57 §0). 시간 측정은
/// Tools/Enemy C3/Measure shader telegraph cost(EnemyC3TelegraphPerfProbe) — 머신마다 흔들려 여기 넣지 않는다.
/// </summary>
public sealed class EnemyC3TelegraphInstanceTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static (int materials, int textures, int sprites) Count() =>
        (Resources.FindObjectsOfTypeAll<Material>().Length,
         Resources.FindObjectsOfTypeAll<Texture2D>().Length,
         Resources.FindObjectsOfTypeAll<Sprite>().Length);

    static void AssertNoGrowth((int materials, int textures, int sprites) before, string what)
    {
        var after = Count();
        Assert.AreEqual(before.materials, after.materials, what + ": material instances grew");
        Assert.AreEqual(before.textures, after.textures, what + ": Texture2D instances grew");
        Assert.AreEqual(before.sprites, after.sprites, what + ": Sprite instances grew");
    }

    static void DestroyTelegraph(GameObject go)
    {
        foreach (var d in go.GetComponentsInChildren<DecalProjector>(true))
        {
            var m = d.material;
            d.material = null;
            if (m != null) Object.DestroyImmediate(m);
        }
        Object.DestroyImmediate(go);
    }

    [Test]
    public void AttackTelegraph_RepeatedShowWithChangingSizes_CreatesNoInstances()
    {
        var go = new GameObject("EnemyC3InstanceTest");
        try
        {
            var t = go.AddComponent<BossAttackConeTelegraph>();
            // 예열 — 프로젝터 6개·재질 인스턴스 6개는 처음 한 번만 만든다(EnsureBuilt).
            t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f);
            var projectors = go.GetComponentsInChildren<DecalProjector>(true);
            var materials = projectors.Select(p => p.material).ToArray();
            var before = Count();

            var applyFill = typeof(BossAttackConeTelegraph).GetMethod("ApplyFill", Private);
            var elapsed = typeof(BossAttackConeTelegraph).GetField("_elapsed", Private);
            for (int i = 0; i < 20; i++)
            {
                float len = 10f + i * 0.73f;   // 예전 Decal() 캐시 키(F4 크기)가 매번 미스 나던 경로
                t.Show(2.5f, 360f, 0f, len, 1f, len, 1f);                         // 합집합(02)
                t.Show(0f, 0f, 0f, 0f, 1.2f, len, 1f);                             // 띠(01)
                t.Show(3f + i * 0.1f, 60f + i * 5f, 0f, 0f, 0f, 0f, 1f);           // 부채꼴(03)
                t.Show(4f, 180f, 0f, 0f, 0f, 0f, 1f, fillInward: true);            // 잡기 고리
                elapsed.SetValue(t, i / 20f);
                applyFill.Invoke(t, null);                                         // 진행 단계 변경
            }

            AssertNoGrowth(before, "BossAttackConeTelegraph.Show ×80");
            CollectionAssert.AreEqual(materials, projectors.Select(p => p.material).ToArray(), "projector material instances must be reused");
        }
        finally { DestroyTelegraph(go); }
    }

    // 할 일 57 R(A) — 공격 예고 데칼 인스턴스는 예고 재질(source)의 그리기 순서·데칼 설정을 그대로 물려받고,
    // 조각별 순서(외곽선 +1)만 덮는다. 데칼 설정 = EnemyC3Visuals.NewDecalInstance 가 복사하는 목록.
    static readonly string[] DecalSettings = { "Normal_Blend", "_DecalAngleFadeSupported", "_DecalMeshBiasType", "_DecalMeshDepthBias", "_DecalMeshViewBias" };

    static void AssertInheritsSource(GameObject go, Material source, string what)
    {
        var projectors = go.GetComponentsInChildren<DecalProjector>(true);
        Assert.AreEqual(6, projectors.Length, what + ": 6 projectors");
        foreach (var p in projectors)
        {
            var m = p.material;
            Assert.AreSame(EnemyC3Visuals.DecalMaterial.shader, m.shader, what + "/" + p.name + ": C3 decal shader");
            int artOrder = p.name.EndsWith("Outline") ? 1 : 0;
            Assert.AreEqual(source.renderQueue + artOrder, m.renderQueue, what + "/" + p.name + ": renderQueue");
            Assert.AreEqual(artOrder, m.GetInt("_DrawOrder"), what + "/" + p.name + ": _DrawOrder");
            foreach (var s in DecalSettings)
                if (source.HasProperty(s) && m.HasProperty(s))
                    Assert.AreEqual(source.GetFloat(s), m.GetFloat(s), 1e-6f, what + "/" + p.name + ": " + s);
        }
    }

    [Test]
    public void AttackTelegraph_DecalInstances_InheritSourceDrawOrderAndDecalSettings()
    {
        Assume.That(EnemyC3Visuals.DecalMaterial, Is.Not.Null);
        var source = Resources.Load<Material>("Telegraph/MA_TelegraphDecal");
        Assume.That(source, Is.Not.Null, "Resources/Telegraph/MA_TelegraphDecal");
        var go = new GameObject("EnemyC3InstanceTest_Inherit");
        try
        {
            go.AddComponent<BossAttackConeTelegraph>().Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f);
            AssertInheritsSource(go, source, "MA_TelegraphDecal");
        }
        finally { DestroyTelegraph(go); }
    }

    [Test]
    public void AttackTelegraph_MaterialOverride_InheritsOverrideDrawOrderAndDecalSettings()
    {
        Assume.That(EnemyC3Visuals.DecalMaterial, Is.Not.Null);
        var baseSource = Resources.Load<Material>("Telegraph/MA_TelegraphDecal");
        Assume.That(baseSource, Is.Not.Null);
        var t = typeof(BossAttackConeTelegraph);
        var matField = t.GetField("_telegraphMaterial", BindingFlags.Static | BindingFlags.NonPublic);
        var triedField = t.GetField("_triedLoadMaterial", BindingFlags.Static | BindingFlags.NonPublic);
        object savedMat = matField.GetValue(null), savedTried = triedField.GetValue(null);
        // 기본값과 다른 값으로 바꾼 대체 재질 — 복사가 실제로 일어나야 통과한다.
        var over = new Material(baseSource) { renderQueue = baseSource.renderQueue + 7 };
        foreach (var s in DecalSettings) if (over.HasProperty(s)) over.SetFloat(s, over.GetFloat(s) == 0f ? 1f : 0f);
        var go = new GameObject("EnemyC3InstanceTest_Override");
        try
        {
            matField.SetValue(null, null);
            triedField.SetValue(null, true);   // Resources 예고 재질이 없는 경로 → MaterialOverride
            var tel = go.AddComponent<BossAttackConeTelegraph>();
            tel.MaterialOverride = over;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("예고 재질"));
            tel.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f);
            AssertInheritsSource(go, over, "MaterialOverride");
        }
        finally
        {
            matField.SetValue(null, savedMat);
            triedField.SetValue(null, savedTried);
            DestroyTelegraph(go);
            Object.DestroyImmediate(over);
        }
    }

    [Test]
    public void FloorPlates_RepeatedPaint_ShareOneMaterialAndCreateNoInstances()
    {
        var root = new GameObject("EnemyC3InstanceTest_Plate").transform;
        try
        {
            var outer = BossPatternVisuals.CreateFlat("Outer", root);
            var fill = BossPatternVisuals.CreateFlat("Fill", root);
            var flat = BossPatternVisuals.CreateFlat("Flat", root);
            // 예열 — 판 재질(Resources)·id 별 MPB 는 처음 한 번만.
            foreach (string id in new[] { "06", "12", "14", "15", "16" }) EnemyC3Visuals.PaintPlate(outer, fill, id, id == "12" || id == "15");
            EnemyC3Visuals.PaintActive(outer, .5f);
            BossPatternVisuals.Paint(flat, Color.red, BossPatternVisuals.CodeShape.Disc);
            var before = Count();

            for (int i = 0; i < 20; i++)
            {
                foreach (string id in new[] { "06", "12", "14", "15", "16" }) EnemyC3Visuals.PaintPlate(outer, fill, id, id == "12" || id == "15");
                EnemyC3Visuals.PaintActive(outer, i / 20f);
                BossPatternVisuals.Paint(flat, Color.red, (BossPatternVisuals.CodeShape)(1 + i % 3), 0.05f + (i % 5) * 0.02f);
            }

            AssertNoGrowth(before, "PaintPlate/PaintActive/Paint(CodeShape) ×20");
            Assert.AreSame(EnemyC3Visuals.PlateMaterial, outer.sharedMaterial, "outer plate uses the shared EnemyC3Plate material");
            Assert.AreSame(EnemyC3Visuals.PlateMaterial, fill.sharedMaterial, "fill plate uses the shared EnemyC3Plate material");
        }
        finally { Object.DestroyImmediate(root.gameObject); }
    }

    [Test]
    public void Jump_CreateAndDestroy_LeavesNoInstances()
    {
        // 예열 1회(공유 재질·쿼드·MPB) 뒤에는 만들고 지운 만큼만 — 텍스처·재질이 남지 않는다.
        Object.DestroyImmediate(EnemyC3Visuals.CreateJump(Vector3.zero, 4f, 1.2f));
        var before = Count();
        var made = new List<GameObject>();
        for (int i = 0; i < 10; i++) made.Add(EnemyC3Visuals.CreateJump(new Vector3(i, 0f, 0f), 3f + i * 0.5f, 1.2f));
        var during = Count();
        Assert.AreEqual(before.materials, during.materials, "CreateJump must not create materials");
        Assert.AreEqual(before.textures, during.textures, "CreateJump must not create textures");
        foreach (var go in made) Object.DestroyImmediate(go);
        AssertNoGrowth(before, "CreateJump ×10 + destroy");
    }

    [Test]
    public void SmashSprite_RepeatedApply_ReusesThePrebakedSprite()
    {
        var go = new GameObject("EnemyC3InstanceTest_Smash");
        try
        {
            var sr = new GameObject("L_circle").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(go.transform, false);
            sr.sprite = EnemyC3Visuals.SmashSprite;
            Assume.That(sr.sprite, Is.Not.Null, "Resources/" + EnemyC3Visuals.SmashSpritePath);
            EnemyC3Visuals.ApplySmash(go);
            var before = Count();
            for (int i = 0; i < 20; i++) EnemyC3Visuals.ApplySmash(go);
            AssertNoGrowth(before, "ApplySmash ×20");
            Assert.AreSame(EnemyC3Visuals.SmashSprite, sr.sprite);
        }
        finally { Object.DestroyImmediate(go); }
    }
}
