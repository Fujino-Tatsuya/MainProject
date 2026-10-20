using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 57 R 예열 — <see cref="EnemyC3Visuals.Prewarm"/> 의 결정적 계약. 타이밍은 단언하지 않는다(측정 = EnemyC3TelegraphPerfProbe).
///   · 런타임 텍스처를 만들지 않는다(에셋이 아닌 Texture2D 가 늘지 않는다).
///   · 더미(오브젝트·재질 인스턴스·코드 SVC)가 남지 않는다 — 남는 건 게임이 원래 만드는 공유 캐시(BossPatternVisuals 판 재질·쿼드)뿐.
///   · 두 번째 호출은 아무것도 안 한다(false, 오브젝트·재질 수 그대로). Play 진입 리셋(ResetForPlay) 뒤엔 다시 돈다.
///   · 로딩 흐름(NetworkLoadingFlowController.HandleLoadStarted)이 부른다.
/// </summary>
public sealed class EnemyC3PrewarmTests
{
    static void ResetForPlay() =>
        typeof(EnemyC3Visuals).GetMethod("ResetForPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    static List<T> Runtime<T>() where T : Object =>
        Resources.FindObjectsOfTypeAll<T>().Where(o => o != null && !EditorUtility.IsPersistent(o)).ToList();

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

        CollectionAssert.IsEmpty(Runtime<GameObject>().Where(g => !goBefore.Contains(g)).Select(g => g.name), "dummy objects left behind");
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
