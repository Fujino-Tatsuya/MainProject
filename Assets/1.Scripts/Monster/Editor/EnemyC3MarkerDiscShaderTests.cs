using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 할 일 57 S4c1 — 보스 방향 표식(BossDirectionIndicator 데칼)과 장판 원판(AoeTelegraph 코드 원판)을 SG_EnemyC3Decal 의
/// 채움 모드(부채꼴 3 + 후방 플래그 · 원판 6)로 그린 결과가 예전 CPU 텍스처 × 예전 재질과 같은지 픽셀로 비교한다.
///
/// 예전 재질 = SG_DecalFloorOnly(MA_BossMarkerDecal · MA_AoeDecal_Red) — 색 입력이 없어 Base_Map 의 색·알파가 그대로 나간다.
/// 그래서 pass 0(기존: _OldTex → TelegraphShade)은 _BaseColor 흰색 · 마스크 꺼짐으로 그린다(= 텍스처 그대로).
/// pass 1(새것: EnemyC3DecalShade — 그래프 Custom Function 이 부르는 바로 그 함수, S2 의 GraphUsesSharedDecalFunction 이 연결 보증)은
/// 런타임과 같은 값 — _BaseColor = 예전에 텍스처에 굽던 색 · EnemyC3Visuals.SetArcFill/SetDiscFill.
/// 기존 텍스처 = Editor/LegacyDecalTextureReference(런타임에서 옮긴 원본 그대로). 비교 셰이더 = S2 의 Hidden/EnemyC3DecalCompare.
/// 기준 = 채널 오차 ≤ 2/255 인 픽셀 99% 이상. 나란히 PNG(기존|셰이더|차이×16)는 _lane-artifacts 가 있을 때만 남긴다.
/// </summary>
public sealed class EnemyC3MarkerDiscShaderTests
{
    const int N = 384;
    const int ArcTex = 256;   // 예전 BossDirectionIndicator.DecalTextureSize
    const int DiscTex = 128;  // 예전 AoeTelegraph 호출 크기
    const int Tolerance = 2;
    const float RequiredRatio = 0.99f;
    const float DecalAlpha = 0.85f;   // BossDirectionIndicator.decalAlpha(프리팹 값 0.85)

    const string FrontPath = "Assets/3.Materials/MA_BossDirection_Front.mat";
    const string BackPath = "Assets/3.Materials/MA_BossDirection_Back.mat";
    const string MarkerDecalPath = "Assets/3.Materials/MA_BossMarkerDecal.mat";
    const string AoeDecalPath = "Assets/3.Materials/MA_AoeDecal_Red.mat";

    public struct Case
    {
        public string label;
        public bool disc, isBack;
        public float halfAngle, inner, outer;   // 방향 표식
        public float fill, rim; public Color color;   // 원판
        public override string ToString() => label;
    }

    static Color MarkerColor(bool isBack)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(isBack ? BackPath : FrontPath);
        Color c = m != null ? m.GetColor("_BaseColor") : Color.red;
        c.a = DecalAlpha;
        return c;
    }

    static IEnumerable<TestCaseData> Cases()
    {
        // 방향 표식 — 앞/뒤 × 반각 3개(기본 60 · 좁게 30 · 넓게 100) × inner/outer 2개(프리팹 기본 1.6/2.6 · 두꺼운 1/3).
        foreach (bool back in new[] { false, true })
            foreach (float half in new[] { 30f, 60f, 100f })
                foreach (var (inner, outer) in new[] { (1.6f, 2.6f), (1f, 3f) })
                    yield return Data(new Case
                    {
                        label = $"arc_{(back ? "back" : "front")}_h{half}_i{inner}_o{outer}",
                        isBack = back, halfAngle = half, inner = inner, outer = outer
                    });
        // 장판 원판 — 프리팹 기본(0.45/0.12, 빨강 0.35) · 테두리 넓게 · 테두리 없음 꽉 찬 원판(다른 색).
        foreach (var (fill, rim, color) in new[]
                 {
                     (0.45f, 0.12f, new Color(1f, 0f, 0f, 0.35f)),
                     (0.2f, 0.3f, new Color(1f, 0f, 0f, 0.85f)),
                     (1f, 0f, new Color(1f, 0.55f, 0.1f, 0.6f)),
                 })
            yield return Data(new Case { label = $"disc_f{fill}_r{rim}_a{color.a}", disc = true, fill = fill, rim = rim, color = color });
    }

    static TestCaseData Data(Case c) => new TestCaseData(c).SetName($"MarkerDisc_{c.label}");

    [OneTimeSetUp]
    public void ResetResults()
    {
        string dir = ArtifactDir();
        if (dir != null && File.Exists(Path.Combine(dir, "results.tsv"))) File.Delete(Path.Combine(dir, "results.tsv"));
    }

    [Test]
    public void OldMaterials_AreColorlessFloorDecals()
    {
        // pass 0 의 전제 — 예전 재질은 색 입력 없이 Base_Map 을 그대로 내보냈다(SG_DecalFloorOnly).
        foreach (var path in new[] { MarkerDecalPath, AoeDecalPath })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(m, path);
            Assert.AreEqual("Shader Graphs/SG_DecalFloorOnly", m.shader.name, path);
            Assert.IsTrue(m.HasProperty("Base_Map"), path + " Base_Map");
            Assert.IsFalse(m.HasProperty("_BaseColor"), path + " must have no color input");
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void ShaderMatchesOldTexture(Case c)
    {
        var shader = Shader.Find("Hidden/EnemyC3DecalCompare");
        Assert.IsNotNull(shader, "Hidden/EnemyC3DecalCompare");
        Assert.IsTrue(shader.isSupported, "compare shader not supported");
        var oldMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        var newMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        Texture2D old = null;
        try
        {
            foreach (var m in new[] { oldMat, newMat })
            {
                m.SetTexture("_MaskTex", Texture2D.whiteTexture);
                m.SetVector("_MaskParams", new Vector4(0, 0, 1, 0));
            }
            oldMat.SetColor("_BaseColor", Color.white);   // SG_DecalFloorOnly = 텍스처 그대로

            if (c.disc)
            {
                old = LegacyDecalTextureReference.BuildDiscTexture(c.color, DiscTex, c.fill, c.rim);
                newMat.SetColor("_BaseColor", c.color);
                EnemyC3Visuals.SetDiscFill(newMat, c.fill, c.rim);
            }
            else
            {
                Color color = MarkerColor(c.isBack);
                old = LegacyDecalTextureReference.BuildArcTexture(color, c.halfAngle, c.isBack, c.inner, c.outer, ArcTex);
                newMat.SetColor("_BaseColor", color);
                EnemyC3Visuals.SetArcFill(newMat, c.halfAngle, c.inner / c.outer, c.isBack);
            }
            old.hideFlags = HideFlags.HideAndDontSave;
            oldMat.SetTexture("_OldTex", old);

            Color32[] expected = Render(oldMat, 0), actual = Render(newMat, 1);
            int ok = 0, maxDiff = 0, covered = 0;
            var diff = new Color32[N * N];
            for (int i = 0; i < expected.Length; i++)
            {
                Color32 e = expected[i], a = actual[i];
                int d = Mathf.Max(Mathf.Max(Mathf.Abs(e.r - a.r), Mathf.Abs(e.g - a.g)), Mathf.Max(Mathf.Abs(e.b - a.b), Mathf.Abs(e.a - a.a)));
                if (d <= Tolerance) ok++;
                if (e.a > 0) covered++;
                maxDiff = Mathf.Max(maxDiff, d);
                byte v = (byte)Mathf.Min(255, d * 16);
                diff[i] = new Color32(v, v, v, 255);
            }
            float ratio = ok / (float)expected.Length;
            WriteArtifacts(c.label, expected, actual, diff, ratio, maxDiff);
            Assert.Greater(covered, N * N / 50, $"{c.label}: old texture drew almost nothing — wrong case?");
            Assert.GreaterOrEqual(ratio, RequiredRatio, $"{c.label}: within {Tolerance}/255 = {ratio:P3}, max channel diff {maxDiff}");
        }
        finally
        {
            Object.DestroyImmediate(oldMat);
            Object.DestroyImmediate(newMat);
            if (old != null) Object.DestroyImmediate(old);
        }
    }

    // ── 런타임 동작: 재질 교체가 예전 재질의 그리기 순서·데칼 설정을 지키고, 값만 바뀌며, 애셋을 건드리지 않는다 ──

    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    static T Get<T>(object o, string field) => (T)o.GetType().GetField(field, Private).GetValue(o);
    static void Call(object o, string method) => o.GetType().GetMethod(method, Private).Invoke(o, null);

    static void AssertSameDecalSettings(Material source, Material m, string label)
    {
        Assert.AreSame(EnemyC3Visuals.DecalMaterial.shader, m.shader, label + " shader");
        Assert.AreNotSame(EnemyC3Visuals.DecalMaterial, m, label + " must be an instance");
        Assert.AreEqual(source.renderQueue, m.renderQueue, label + " renderQueue");
        foreach (var p in new[] { "_DrawOrder", "Normal_Blend", "_DecalAngleFadeSupported", "_DecalMeshBiasType", "_DecalMeshDepthBias", "_DecalMeshViewBias" })
            Assert.AreEqual(source.GetFloat(p), m.GetFloat(p), label + " " + p);
        Assert.AreEqual(0f, m.GetVector("_MaskParams").y, label + " mask off");
        Assert.IsFalse(m.HasProperty("Base_Map"), label + " must not carry a baked texture");
    }

    [Test]
    public void DirectionMarker_UsesC3Decal_ValuesOnly()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(MarkerDecalPath);
        var front = AssetDatabase.LoadAssetAtPath<Material>(FrontPath);
        var back = AssetDatabase.LoadAssetAtPath<Material>(BackPath);
        string sourceBefore = File.ReadAllText(MarkerDecalPath);
        var go = new GameObject("EnemyC3MarkerTest");
        try
        {
            var bdi = go.AddComponent<BossDirectionIndicator>();
            Set(bdi, "frontMaterial", front);
            Set(bdi, "backMaterial", back);
            Set(bdi, "decalMaterial", source);
            Call(bdi, "Awake");

            var fd = Get<DecalProjector>(bdi, "_frontDecal");
            var bd = Get<DecalProjector>(bdi, "_backDecal");
            Assert.IsNotNull(fd); Assert.IsNotNull(bd);
            Material fm = fd.material, bm = bd.material;
            AssertSameDecalSettings(source, fm, "front");
            AssertSameDecalSettings(source, bm, "back");
            Assert.AreNotSame(fm, bm, "front/back own their instances");

            // 기본(SO 없음) = 반각 60/60, inner 1.6 / outer 2.6.
            Assert.AreEqual(new Vector4(3, 0, 60, 1.6f / 2.6f), fm.GetVector("_C3Shape"));
            Assert.AreEqual(new Vector4(3, 1, 60, 1.6f / 2.6f), bm.GetVector("_C3Shape"));
            AssertColor(MarkerColor(false), fm.GetColor("_BaseColor"), "front color");
            AssertColor(MarkerColor(true), bm.GetColor("_BaseColor"), "back color");
            Assert.AreEqual(new Vector3(5.2f, 5.2f, 4f), fd.size);

            // 크기가 바뀌어도 재질을 새로 만들지 않는다 — 값만.
            Set(bdi, "innerRadius", 1f);
            Set(bdi, "outerRadius", 3f);
            Call(bdi, "RebuildMeshesIfNeeded");
            Assert.AreSame(fm, fd.material, "front material reused");
            Assert.AreSame(bm, bd.material, "back material reused");
            Assert.AreEqual(1f / 3f, fm.GetVector("_C3Shape").w, 1e-6f);
            Assert.AreEqual(new Vector3(6f, 6f, 4f), fd.size);

            Assert.AreEqual(sourceBefore, File.ReadAllText(MarkerDecalPath), "marker decal asset must not change");
            Assert.IsFalse(EditorUtility.IsDirty(source), "marker decal asset must not be dirtied");
        }
        finally { Cleanup(go); }
    }

    [Test]
    public void AoeDisc_UsesC3Decal_AndKeepsPrefabMaterial()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(AoeDecalPath);
        var go = new GameObject("EnemyC3DiscTest");
        try
        {
            var decal = go.AddComponent<DecalProjector>();
            decal.material = source;
            var aoe = go.AddComponent<AoeTelegraph>();   // shapeTexture 비움 = 코드 원판
            aoe.SetAlpha(0.6f);                          // 첫 사용에서 모양을 정한다(TryDecal)

            Material m = decal.material;
            AssertSameDecalSettings(source, m, "disc");
            Assert.AreEqual(new Vector4(6, 0, 0.45f, 0.12f), m.GetVector("_C3Shape"));
            AssertColor(new Color(1f, 0f, 0f, 0.35f), m.GetColor("_BaseColor"), "disc color");
            Assert.AreEqual(0.6f, decal.fadeFactor, 1e-6f, "alpha still goes through fadeFactor");
            Assert.AreEqual("Shader Graphs/SG_DecalFloorOnly", source.shader.name, "prefab material untouched");
            Assert.IsFalse(EditorUtility.IsDirty(source), "prefab material must not be dirtied");

            // 바닥 전용 재질이었으므로 스타일은 예전처럼 먹지 않는다.
            aoe.ApplyStyle(new TelegraphLayer(Color.cyan));
            AssertColor(new Color(1f, 0f, 0f, 0.35f), m.GetColor("_BaseColor"), "style must stay a no-op for a colorless prefab material");
        }
        finally { Cleanup(go); }
    }

    static void AssertColor(Color expected, Color actual, string label)
    {
        for (int i = 0; i < 4; i++) Assert.AreEqual(expected[i], actual[i], 1e-4f, $"{label} [{i}]");
    }

    static void Cleanup(GameObject go)
    {
        foreach (var d in go.GetComponentsInChildren<DecalProjector>(true))
        {
            var m = d.material;
            d.material = null;
            if (m != null && !AssetDatabase.Contains(m)) Object.DestroyImmediate(m);
        }
        Object.DestroyImmediate(go);
    }

    // ── 렌더 · 산출물(S2·S4a 와 같은 방식) ──

    static Mesh _quad;
    static Mesh Quad
    {
        get
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "EnemyC3MarkerDiscTestQuad", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return _quad;
        }
    }

    static Color32[] Render(Material mat, int pass)
    {
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGB32, 0) { sRGB = true });
        var cb = new CommandBuffer { name = "EnemyC3MarkerDiscTest" };
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(false, true, Color.clear);
        cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-.5f, .5f, -.5f, .5f, -1, 1));
        cb.DrawMesh(Quad, Matrix4x4.identity, mat, 0, pass);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(N, N, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, N, N), 0, 0, false);
        read.Apply(false, false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = read.GetPixels32();
        Object.DestroyImmediate(read);
        return pixels;
    }

    static string ArtifactDir()
    {
        // <repo>/../_lane-artifacts/<repo 폴더 이름>/57-shader/S4c — 레포 밖, 사람이 볼 산출물 자리. 없으면 남기지 않는다.
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "S4c") : null;
    }

    static void WriteArtifacts(string label, Color32[] expected, Color32[] actual, Color32[] diff, float ratio, int maxDiff)
    {
        string dir = ArtifactDir();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        var sheet = new Texture2D(N * 3, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * 3 * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                px[y * N * 3 + x] = OverGray(expected[i]);
                px[y * N * 3 + N + x] = OverGray(actual[i]);
                px[y * N * 3 + 2 * N + x] = diff[i];
            }
        sheet.SetPixels32(px);
        sheet.Apply(false, false);
        File.WriteAllBytes(Path.Combine(dir, label + ".png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        File.AppendAllText(Path.Combine(dir, "results.tsv"), $"{label}\t{ratio:F5}\t{maxDiff}\n");
    }

    static Color32 OverGray(Color32 premul)
    {
        const int bg = 40;
        int k = 255 - premul.a;
        return new Color32((byte)Mathf.Min(255, premul.r + bg * k / 255), (byte)Mathf.Min(255, premul.g + bg * k / 255),
                           (byte)Mathf.Min(255, premul.b + bg * k / 255), 255);
    }
}
