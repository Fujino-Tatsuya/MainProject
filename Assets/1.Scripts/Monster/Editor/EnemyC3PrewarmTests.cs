using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 할 일 57 R 예열 — <see cref="EnemyC3Visuals.Prewarm"/> 의 결정적 계약. 타이밍은 단언하지 않는다(측정 = EnemyC3TelegraphPerfProbe).
///   · 런타임 텍스처를 만들지 않는다(에셋이 아닌 Texture2D 가 늘지 않는다).
///   · 임시 더미(오브젝트·재질 인스턴스·코드 SVC)가 남지 않는다 — 57.3부터 의도한 비활성 드론 풀 1기만 남는다.
///   · 두 번째 호출은 아무것도 안 한다(false, 오브젝트·재질 수 그대로). Play 진입 리셋(ResetForPlay) 뒤엔 다시 돈다.
///   · 로딩 흐름(NetworkLoadingFlowController.HandleLoadStarted)이 부른다.
///   · 할 일 57.1 — 보스 예고 컴포넌트(BossAttackConeTelegraph·WellsDroneAttack) 예열 뒤에도 임시 컴포넌트·프로젝터·드론 뷰가 남지 않고,
///     게임이 새로 붙이는 예고는 프로젝터·재질 인스턴스 수와 그리기 순서가 그대로다.
///   · 할 일 57.3 — 실제 드론 모델의 같은 인스턴스·Animator·PlayableGraph를 반복 Lock/Cancel/Impact에서 재사용한다.
/// </summary>
public sealed class EnemyC3PrewarmTests
{
    static void ResetForPlay() =>
        typeof(EnemyC3Visuals).GetMethod("ResetForPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    static List<T> Runtime<T>() where T : Object =>
        Resources.FindObjectsOfTypeAll<T>().Where(o => o != null && !EditorUtility.IsPersistent(o)).ToList();

    static readonly System.Type PoolType = System.Type.GetType("WellsDroneVisualPool, Assembly-CSharp");
    static T Pool<T>(string name) => (T)PoolType.GetProperty(name, Any).GetValue(null);
    static WellsDroneDataSO ActualDroneData() => Resources.Load<WellsDronePrewarmCatalog>("Monster/WellsDronePrewarmCatalog").data;

    static WellsDroneAttack NewDrone(out GameObject owner)
    {
        owner = new GameObject("EnemyC3PrewarmTests_Drone");
        var drone = owner.AddComponent<WellsDroneAttack>();
        drone.Init(null, ActualDroneData());
        return drone;
    }

    static void DropDrone(GameObject owner, WellsDroneAttack drone)
    {
        Transform viewRoot = drone != null ? (Transform)typeof(WellsDroneAttack).GetField("_viewRoot", Any).GetValue(drone) : null;
        if (viewRoot != null) Object.DestroyImmediate(viewRoot.gameObject);
        if (owner != null) Object.DestroyImmediate(owner);
    }

    [TearDown]
    public void TearDown() => ResetForPlay();

    [Test]
    public void Prewarm_CreatesNoRuntimeTexture()
    {
        ResetForPlay();
        var before = new HashSet<Texture2D>(Runtime<Texture2D>());
        Assert.IsTrue(EnemyC3Visuals.Prewarm());
        var added = Runtime<Texture2D>().Where(t => !before.Contains(t)).Select(t => t.name).ToList();
        CollectionAssert.IsEmpty(added, "Prewarm created runtime textures: " + string.Join(", ", added));
    }

    [Test]
    public void Prewarm_LeavesNoDummies_AndSecondCallIsNoOp()
    {
        ResetForPlay();
        var goBefore = new HashSet<GameObject>(Runtime<GameObject>());
        var matBefore = new HashSet<Material>(Runtime<Material>());
        var meshBefore = new HashSet<Mesh>(Runtime<Mesh>());

        Assert.IsTrue(EnemyC3Visuals.Prewarm(), "first call after the Play reset runs");

        GameObject poolRoot = Runtime<GameObject>().Single(g => g.name == "WellsDroneVisualPool");
        CollectionAssert.IsEmpty(Runtime<GameObject>().Where(g => !goBefore.Contains(g) && g != poolRoot && !g.transform.IsChildOf(poolRoot.transform)).Select(g => g.name), "temporary dummy objects left behind");
        Assert.AreEqual(1, Pool<int>("VisualCount"), "one intentionally retained drone visual");
        Assert.AreEqual(1, Pool<int>("InactiveCount"), "prewarmed visual returned to the pool");
        Assert.AreEqual(0, Pool<int>("ActiveCount"));
        CollectionAssert.IsEmpty(Runtime<ShaderVariantCollection>().Where(s => s.name == "EnemyC3Prewarm"), "code SVC left behind");
        // 새 재질·메시는 BossPatternVisuals 의 공유 캐시(없었을 때만 1개씩)뿐 — C3 데칼 인스턴스는 남지 않는다.
        var newMats = Runtime<Material>().Where(m => !matBefore.Contains(m)).Select(m => m.name).ToList();
        Assert.That(newMats.Count, Is.LessThanOrEqualTo(1), "new materials: " + string.Join(", ", newMats));
        Assert.That(newMats.All(n => n == "BossPatternTelegraph" || n == "BossPatternTemp"), "new materials: " + string.Join(", ", newMats));
        var newMeshes = Runtime<Mesh>().Where(m => !meshBefore.Contains(m)).Select(m => m.name).ToList();
        Assert.That(newMeshes.Count, Is.LessThanOrEqualTo(1), "new meshes: " + string.Join(", ", newMeshes));
        Assert.That(newMeshes.All(n => n == "BossPatternQuad"), "new meshes: " + string.Join(", ", newMeshes));

        int go1 = Runtime<GameObject>().Count, mat1 = Runtime<Material>().Count, mesh1 = Runtime<Mesh>().Count, tex1 = Runtime<Texture2D>().Count;
        Assert.IsFalse(EnemyC3Visuals.Prewarm(), "second call is a no-op");
        Assert.AreEqual(go1, Runtime<GameObject>().Count, "objects after the second call");
        Assert.AreEqual(mat1, Runtime<Material>().Count, "materials after the second call");
        Assert.AreEqual(mesh1, Runtime<Mesh>().Count, "meshes after the second call");
        Assert.AreEqual(tex1, Runtime<Texture2D>().Count, "textures after the second call");

        ResetForPlay();
        Assert.IsTrue(EnemyC3Visuals.Prewarm(), "the Play reset re-arms the prewarm");
        Assert.AreEqual(mat1, Runtime<Material>().Count, "materials after the re-armed run (shared caches reused)");
        Assert.AreEqual(go1, Runtime<GameObject>().Count, "objects after the re-armed run");
    }

    [Test]
    public void Prewarm_FillsTheDataCaches()
    {
        ResetForPlay();
        EnemyC3Visuals.Prewarm();
        var blocks = (System.Collections.IDictionary)typeof(EnemyC3Visuals).GetField("plateBlocks", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Assert.That(blocks.Count, Is.GreaterThanOrEqualTo(10), "plate property blocks (12·14·15·16·06·07 layers)");
        Assert.IsNotNull(typeof(EnemyC3Visuals).GetField("entries", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "preset JSON loaded");
        Assert.IsNotNull(typeof(EnemyC3Visuals).GetField("smashSprite", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "smash 09 sprite loaded");
        Assert.IsNotNull(EnemyC3Visuals.PlateMaterial);
        Assert.IsNotNull(EnemyC3Visuals.DecalMaterial);
    }

    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly System.Type PrewarmType = System.Type.GetType("EnemyC3Prewarm, Assembly-CSharp");

    // 할 일 57.1/57.3 — 임시 보스 컴포넌트·프로젝터·재질 인스턴스·드론 뷰는 지우고, 실제 모델 비활성 풀 1기만 남긴다.
    [Test]
    public void Prewarm_BossComponents_LeaveNothingBehind()
    {
        ResetForPlay();
        var telBefore = new HashSet<BossAttackConeTelegraph>(Runtime<BossAttackConeTelegraph>());
        var droneBefore = new HashSet<WellsDroneAttack>(Runtime<WellsDroneAttack>());
        var decalBefore = new HashSet<UnityEngine.Rendering.Universal.DecalProjector>(Runtime<UnityEngine.Rendering.Universal.DecalProjector>());
        var dataBefore = new HashSet<WellsDroneDataSO>(Runtime<WellsDroneDataSO>());
        int viewsBefore = Runtime<GameObject>().Count(g => g.name == "WellsDroneView");

        Assert.IsTrue(EnemyC3Visuals.Prewarm());
        object last = PrewarmType.GetField("Last", Any).GetValue(null);
        Assert.That((double)last.GetType().GetField("boss", Any).GetValue(last), Is.GreaterThan(0.0), "boss component stage ran");

        CollectionAssert.IsEmpty(Runtime<BossAttackConeTelegraph>().Where(c => !telBefore.Contains(c)), "temporary telegraph left behind");
        CollectionAssert.IsEmpty(Runtime<WellsDroneAttack>().Where(c => !droneBefore.Contains(c)), "temporary drone attack left behind");
        CollectionAssert.IsEmpty(Runtime<UnityEngine.Rendering.Universal.DecalProjector>().Where(d => !decalBefore.Contains(d)), "telegraph projectors left behind");
        CollectionAssert.IsEmpty(Runtime<WellsDroneDataSO>().Where(d => !dataBefore.Contains(d)), "empty drone data left behind");
        Assert.AreEqual(viewsBefore, Runtime<GameObject>().Count(g => g.name == "WellsDroneView"), "drone view root left behind");
        Assert.AreEqual(1, Pool<int>("VisualCount"), "prewarmed drone visual count");
        Assert.AreEqual(1, Pool<int>("LiveGraphCount"), "one reusable PlayableGraph");

        int decals = Runtime<UnityEngine.Rendering.Universal.DecalProjector>().Count, mats = Runtime<Material>().Count;
        ResetForPlay();
        Assert.IsTrue(EnemyC3Visuals.Prewarm(), "re-armed run");
        Assert.AreEqual(decals, Runtime<UnityEngine.Rendering.Universal.DecalProjector>().Count, "projectors after the re-armed run");
        Assert.AreEqual(mats, Runtime<Material>().Count, "materials after the re-armed run (telegraph instances destroyed)");
    }

    [Test]
    public void DronePool_ReusesOneInstanceAnimatorAndGraph_WithoutLeaks()
    {
        ResetForPlay();
        Assert.IsTrue(EnemyC3Visuals.Prewarm());
        int created = Pool<int>("CreatedCount");
        int graphs = Pool<int>("GraphCreateCount");
        int liveGraphs = Pool<int>("LiveGraphCount");
        GameObject owner = null;
        WellsDroneAttack drone = null;
        try
        {
            drone = NewDrone(out owner);
            drone.ClientLock(Vector3.zero, 3f, 1f, true);
            GameObject first = (GameObject)typeof(WellsDroneAttack).GetField("_droneGo", Any).GetValue(drone);
            Assert.IsNotNull(first);
            Assert.AreEqual(created, Pool<int>("CreatedCount"), "prewarm means first use instantiates nothing");
            Assert.AreEqual(1, first.GetComponentsInChildren<Animator>(true).Length, "Animator is created once with the pooled instance");
            Assert.IsTrue(first.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "pooled visual never participates in collision");
            Assert.IsTrue(first.GetComponentsInChildren<Rigidbody>(true).All(r => r.isKinematic && !r.detectCollisions), "pooled visual never participates in physics");
            drone.ClientCancel();

            drone.ClientLock(Vector3.one, 4f, 1f, false);
            GameObject second = (GameObject)typeof(WellsDroneAttack).GetField("_droneGo", Any).GetValue(drone);
            Assert.AreSame(first, second, "same local visual instance");
            drone.ClientCancel();

            for (int i = 0; i < 8; i++)
            {
                drone.ClientLock(new Vector3(i, 0f, 0f), 3f, 1f, (i & 1) == 0);
                drone.ClientCancel();
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("EffectManager.*없습니다"));
                drone.ClientImpact(Vector3.zero, 3f);
            }

            Assert.AreEqual(1, Pool<int>("VisualCount"));
            Assert.AreEqual(1, Pool<int>("InactiveCount"));
            Assert.AreEqual(0, Pool<int>("ActiveCount"));
            Assert.AreEqual(created, Pool<int>("CreatedCount"), "no Instantiate after prewarm");
            Assert.AreEqual(graphs, Pool<int>("GraphCreateCount"), "no new PlayableGraph per attack");
            Assert.AreEqual(liveGraphs, Pool<int>("LiveGraphCount"), "graph count remains constant");
            Assert.AreEqual(1, first.GetComponentsInChildren<Animator>(true).Length, "Animator count remains constant");
        }
        finally { DropDrone(owner, drone); }
    }

    [Test]
    public void Prewarm_SecondCallDoesNothing_AndResetRecreatesThePool()
    {
        ResetForPlay();
        Assert.IsTrue(EnemyC3Visuals.Prewarm());
        GameObject first = Runtime<GameObject>().Single(g => g.name.EndsWith(" (Pooled)"));
        int created = Pool<int>("CreatedCount");
        Assert.IsFalse(EnemyC3Visuals.Prewarm());
        Assert.AreEqual(created, Pool<int>("CreatedCount"));
        Assert.AreSame(first, Runtime<GameObject>().Single(g => g.name.EndsWith(" (Pooled)")));

        ResetForPlay();
        Assert.IsTrue(first == null, "ResetForPlay destroys the previous pooled instance immediately in EditMode");
        Assert.IsTrue(EnemyC3Visuals.Prewarm());
        GameObject second = Runtime<GameObject>().Single(g => g.name.EndsWith(" (Pooled)"));
        Assert.AreNotSame(first, second);
        Assert.AreEqual(1, Pool<int>("CreatedCount"));
        Assert.AreEqual(1, Pool<int>("LiveGraphCount"));
    }

    // 예열 뒤 게임이 새로 붙이는 보스 예고도 그대로다 — 프로젝터 6개, 재질 인스턴스 6개(조각마다 따로), 그리기 순서 = 예고 재질 + 외곽선 1.
    [Test]
    public void Prewarm_ThenNewTelegraph_BuildsTheSameProjectorSet()
    {
        ResetForPlay();
        EnemyC3Visuals.Prewarm();
        var go = new GameObject("EnemyC3PrewarmTests_Telegraph");
        try
        {
            go.AddComponent<BossAttackConeTelegraph>().Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f);
            var decals = go.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true);
            Assert.AreEqual(6, decals.Length, "projectors");
            Assert.AreEqual(6, decals.Select(d => d.material).Distinct().Count(), "one material instance per piece");
            int baseQueue = Resources.Load<Material>("Telegraph/MA_TelegraphDecal").renderQueue;
            foreach (var d in decals)
            {
                int order = d.name.EndsWith("Outline") ? 1 : 0;
                Assert.AreEqual(baseQueue + order, d.material.renderQueue, d.name + " renderQueue");
                if (d.material.HasProperty("_DrawOrder")) Assert.AreEqual(order, d.material.GetInt("_DrawOrder"), d.name + " _DrawOrder");
                Assert.IsFalse(EditorUtility.IsPersistent(d.material), d.name + " uses an instance, not the asset");
            }
        }
        finally
        {
            foreach (var d in go.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true))
            {
                var m = d.material; d.material = null;
                if (m != null) Object.DestroyImmediate(m);
            }
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Prewarm_WarmsEveryListedVariant()
    {
        ResetForPlay();
        EnemyC3Visuals.Prewarm();
        var keywords = (string[][])PrewarmType.GetField("DecalKeywords", Any).GetValue(null);
        object last = PrewarmType.GetField("Last", Any).GetValue(null);
        int variants = (int)last.GetType().GetField("variants", Any).GetValue(last);
        Assert.AreEqual(2 + keywords.Length, variants, "plate + quad + every decal keyword set must exist in the shaders (none skipped)");
    }

    // 데칼 키워드 목록은 PC_Renderer 의 Decal 기능 설정에서 나온다 — 설정이 바뀌면 이 테스트가 목록 갱신을 요구한다.
    [Test]
    public void DecalKeywords_MatchTheProjectRendererDecalSettings()
    {
        var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/99.Settings/PC_Renderer.asset");
        Assert.IsNotNull(data, "PC_Renderer.asset");
        var feature = data.rendererFeatures.OfType<UnityEngine.Rendering.Universal.DecalRendererFeature>().FirstOrDefault();
        Assert.IsNotNull(feature, "Decal renderer feature");
        object settings = typeof(UnityEngine.Rendering.Universal.DecalRendererFeature).GetField("m_Settings", Any).GetValue(feature);
        object dbuffer = settings.GetType().GetField("dBufferSettings", Any).GetValue(settings);
        object screen = settings.GetType().GetField("screenSpaceSettings", Any).GetValue(settings);
        int surface = System.Convert.ToInt32(dbuffer.GetType().GetField("surfaceData", Any).GetValue(dbuffer));
        int blend = System.Convert.ToInt32(screen.GetType().GetField("normalBlend", Any).GetValue(screen));
        bool layers = (bool)settings.GetType().GetField("decalLayers", Any).GetValue(settings);

        string mrt = new[] { "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3" }[surface];
        string nb = new[] { "_DECAL_NORMAL_BLEND_LOW", "_DECAL_NORMAL_BLEND_MEDIUM", "_DECAL_NORMAL_BLEND_HIGH" }[blend];
        var keywords = ((string[][])PrewarmType.GetField("DecalKeywords", Any).GetValue(null)).Select(k => string.Join(" ", k.OrderBy(x => x))).ToList();
        string Set(params string[] k) => string.Join(" ", k.OrderBy(x => x));
        CollectionAssert.Contains(keywords, layers ? Set(mrt, "_DECAL_LAYERS") : Set(mrt), "DBuffer variant — update EnemyC3Prewarm.DecalKeywords");
        CollectionAssert.Contains(keywords, layers ? Set(nb, "_DECAL_LAYERS") : Set(nb), "ScreenSpace variant — update EnemyC3Prewarm.DecalKeywords");
    }

    [Test]
    public void LoadingFlow_CallsPrewarmOnceWhenTheTargetLoadStarts()
    {
        string src = File.ReadAllText("Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs");
        int at = src.IndexOf("private void HandleLoadStarted(", System.StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), "HandleLoadStarted not found");
        int end = src.IndexOf("private void HandleLoadComplete(", at, System.StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(at));
        StringAssert.Contains("EnemyC3Visuals.Prewarm();", src.Substring(at, end - at), "prewarm hook must sit in HandleLoadStarted");
        Assert.AreEqual(1, src.Split(new[] { "EnemyC3Visuals.Prewarm(" }, System.StringSplitOptions.None).Length - 1, "one prewarm call in the loading flow");
    }
}
