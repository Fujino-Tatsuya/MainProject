using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 할 일 57 S4a — 공격 예고 <b>채움</b>(BossAttackConeTelegraph: 부채꼴·띠·합집합·잡기 고리)을 SG_EnemyC3Decal 로 그린 결과가
/// 예전 CPU 256² 채움 텍스처 × 기존 데칼 식(SG_TelegraphDecal 의 Base_Map 샘플 → TelegraphShade)과 같은지 픽셀로 비교한다.
///
/// 비교 셰이더는 S2 의 Hidden/EnemyC3DecalCompare 그대로 — pass 0 = 기존 텍스처(_OldTex), pass 1 = EnemyC3DecalShade
/// (그래프 Custom Function 이 부르는 바로 그 함수, S2 의 GraphUsesSharedDecalFunction 이 연결을 보증). 값은 런타임과 같은
/// EnemyC3Visuals.SetArcFill/SetRectFill/SetUnionFill 이 넣는다.
/// 기존 텍스처: 부채꼴·고리 = LegacyDecalTextureReference.BuildArcTexture(S4c1 에서 BossDirectionIndicator 에서 옮긴 원본 그대로),
/// 띠·합집합 = 아래 OldRect/OldUnion(S4a 에서 지운 BossAttackConeTelegraph.BuildRectTexture/BuildUnionTexture 를 그대로 옮긴 기준).
/// 기준 = 채널 오차 ≤ 2/255 인 픽셀 99% 이상. 나란히 PNG(기존|셰이더|차이×16)는 _lane-artifacts 가 있을 때만 남긴다.
/// </summary>
public sealed class EnemyC3FillShaderTests
{
    const int N = 384;
    const int Tex = 256;   // 예전 채움 텍스처 크기(BossAttackConeTelegraph.TextureSize)
    const int Tolerance = 2;
    const float RequiredRatio = 0.99f;
    const int Steps = 24;  // BossAttackConeTelegraph.UnionFillSteps

    public struct Fill
    {
        public string label; public int kind;   // 3 부채꼴 · 4 띠 · 5 합집합
        public float halfAngle, inner;
        public Vector2 extent; public float pathRadius, pathLength, coneCenter, coneRadius, front;
        public override string ToString() => label;
    }

    // 채움 텍스처는 크기와 무관한 정규화 UV(부채꼴·띠) — 그래서 부채꼴은 각도, 고리는 진행 단계로 나눈다.
    static Fill Arc(float halfAngle) => new Fill { label = $"arc_a{halfAngle * 2}", kind = 3, halfAngle = halfAngle };
    static Fill Ring(float halfAngle, int step) => new Fill
    { label = $"ring_a{halfAngle * 2}_s{step:00}", kind = 3, halfAngle = halfAngle, inner = 1f - (float)step / Steps };
    static Fill Rect() => new Fill { label = "band", kind = 4 };

    // UnionExtent 와 같은 식. front = RebuildUnionFill 과 같은 u.y × step/24.
    static Fill Union(float pathRadius, float length, float coneRadius, float coneCenter, int step)
    {
        var extent = new Vector2(Mathf.Max(pathRadius, coneRadius) * 2, Mathf.Max(0.01f, Mathf.Max(length, coneCenter + coneRadius)));
        return new Fill
        {
            label = $"union_p{pathRadius * 2}x{length}_r{coneRadius}_s{step:00}", kind = 5, extent = extent,
            pathRadius = pathRadius, pathLength = length, coneCenter = coneCenter, coneRadius = coneRadius,
            front = extent.y * Mathf.Clamp01((float)step / Steps)
        };
    }

    static IEnumerable<TestCaseData> Cases()
    {
        foreach (var a in new[] { 45f, 90f, 180f }) yield return Case(Arc(a));
        // 잡기 고리 — 시작(빈 고리) · 첫 단계 · 중간 · 끝 직전 · 가득.
        foreach (var a in new[] { 90f, 180f })
            foreach (var s in new[] { 0, 1, 12, 23, 24 }) yield return Case(Ring(a, s));
        yield return Case(Rect());
        // 합집합 — S2 와 같은 세 모양 × 단계(0 · 1 · 중간 경계 12/13 · 끝 직전 · 끝).
        foreach (var s in new[] { 0, 1, 12, 13, 23, 24 })
        {
            yield return Case(Union(1.5f, 6f, 3f, 6f, s));
            yield return Case(Union(1f, 18f, 2.5f, 18f, s));
            yield return Case(Union(2f, 10f, 1.5f, 9f, s));
        }
    }

    static TestCaseData Case(Fill f) => new TestCaseData(f).SetName($"Fill_{f.label}");

    [OneTimeSetUp]
    public void ResetResults()
    {
        string dir = ArtifactDir();
        if (dir != null && File.Exists(Path.Combine(dir, "results.tsv"))) File.Delete(Path.Combine(dir, "results.tsv"));
    }

    [TestCaseSource(nameof(Cases))]
    public void ShaderMatchesCpuFill(Fill f)
    {
        var shader = Shader.Find("Hidden/EnemyC3DecalCompare");
        Assert.IsNotNull(shader, "Hidden/EnemyC3DecalCompare");
        Assert.IsTrue(shader.isSupported, "compare shader not supported");
        var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        Texture2D old = null;
        try
        {
            // ApplyStyle 가 채움에 넣는 값과 같다(C3 채움 색, 마스크 꺼짐). 예전 텍스처는 흰색(_shaderColor 경로).
            mat.SetColor("_BaseColor", EnemyC3Visuals.FillColor("01"));
            mat.SetTexture("_MaskTex", Texture2D.whiteTexture);
            mat.SetVector("_MaskParams", new Vector4(0, 0, 1, 0));
            switch (f.kind)
            {
                case 3:
                    old = LegacyDecalTextureReference.BuildArcTexture(Color.white, f.halfAngle, isBack: false, inner: Mathf.Clamp01(f.inner), outer: 1f, size: Tex);
                    EnemyC3Visuals.SetArcFill(mat, f.halfAngle, f.inner);
                    break;
                case 4:
                    old = OldRect(Color.white);
                    EnemyC3Visuals.SetRectFill(mat);
                    break;
                default:
                    old = OldUnion(Color.white, f.extent, f.pathRadius, f.pathLength, f.coneCenter, f.coneRadius, f.front);
                    EnemyC3Visuals.SetUnionFill(mat, f.extent, f.pathRadius, f.pathLength, f.coneCenter, f.coneRadius, f.front);
                    break;
            }
            mat.SetTexture("_OldTex", old);

            Color32[] expected = Render(mat, 0), actual = Render(mat, 1);
            int ok = 0, maxDiff = 0;
            var diff = new Color32[N * N];
            for (int i = 0; i < expected.Length; i++)
            {
                Color32 e = expected[i], a = actual[i];
                int d = Mathf.Max(Mathf.Max(Mathf.Abs(e.r - a.r), Mathf.Abs(e.g - a.g)), Mathf.Max(Mathf.Abs(e.b - a.b), Mathf.Abs(e.a - a.a)));
                if (d <= Tolerance) ok++;
                maxDiff = Mathf.Max(maxDiff, d);
                byte v = (byte)Mathf.Min(255, d * 16);
                diff[i] = new Color32(v, v, v, 255);
            }
            float ratio = ok / (float)expected.Length;
            WriteArtifacts(f.label, expected, actual, diff, ratio, maxDiff);
            Assert.GreaterOrEqual(ratio, RequiredRatio, $"{f.label}: within {Tolerance}/255 = {ratio:P3}, max channel diff {maxDiff}");
        }
        finally
        {
            Object.DestroyImmediate(mat);
            if (old != null) Object.DestroyImmediate(old);
        }
    }

    // ── 런타임 동작: 채움 프로젝터가 텍스처 없이 C3 셰이더를 쓰고, 그리기 순서·25단계 진행이 예전과 같다 ──

    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static T Field<T>(BossAttackConeTelegraph t, string name) => (T)typeof(BossAttackConeTelegraph).GetField(name, Private).GetValue(t);

    static void Advance(BossAttackConeTelegraph t, float elapsed)
    {
        typeof(BossAttackConeTelegraph).GetField("_elapsed", Private).SetValue(t, elapsed);
        typeof(BossAttackConeTelegraph).GetMethod("ApplyFill", Private).Invoke(t, null);
    }

    static void Cleanup(GameObject go)
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
    public void FillProjectors_UseC3Shader_WithOldDrawOrder()
    {
        var go = new GameObject("EnemyC3FillTest");
        try
        {
            var t = go.AddComponent<BossAttackConeTelegraph>();
            t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f);   // 합집합(02)
            var source = Resources.Load<Material>("Telegraph/MA_TelegraphDecal");
            Assert.IsNotNull(source, "MA_TelegraphDecal");
            foreach (var name in new[] { "_coneFill", "_bandFill", "_unionFill" })
            {
                var d = Field<DecalProjector>(t, name);
                Assert.AreSame(EnemyC3Visuals.DecalMaterial.shader, d.material.shader, name);
                Assert.AreEqual(source.renderQueue, d.material.renderQueue, name + " renderQueue");
                Assert.AreEqual(0, d.material.GetInt("_DrawOrder"), name + " _DrawOrder");
                Assert.IsFalse(d.material.HasProperty("Base_Map") && d.material.GetTexture("Base_Map") != null, name + " must not carry a baked texture");
            }
            Assert.AreEqual(4f, Field<DecalProjector>(t, "_bandFill").material.GetVector("_C3Shape").x, "band fill mode");
        }
        finally { Cleanup(go); }
    }

    [Test]
    public void UnionFill_Advances_In25Steps()
    {
        var go = new GameObject("EnemyC3FillTest");
        try
        {
            var t = go.AddComponent<BossAttackConeTelegraph>();
            const float grow = 2f;
            t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, grow);
            var m = Field<DecalProjector>(t, "_unionFill").material;
            float len = m.GetVector("_C3Extent").y;
            Assert.AreEqual(20.5f, len, 1e-4f, "union extent length");
            // 경계: Mathf.RoundToInt(t×24) — 12.49 → 12, 12.51 → 13, 끝 = 24.
            foreach (var (frac, step) in new[] { (0f, 0), (0.4f / Steps, 0), (12.49f / Steps, 12), (12.51f / Steps, 13), (1f, 24) })
            {
                Advance(t, grow * frac);
                Vector4 s = m.GetVector("_C3Shape");
                Assert.AreEqual(5f, s.x, "union fill mode");
                Assert.AreEqual(len * step / Steps, s.w, 1e-4f, $"front at t={frac}");
            }
        }
        finally { Cleanup(go); }
    }

    [Test]
    public void GrabRing_ClosesIn25Steps_AndResetsForNextAttack()
    {
        var go = new GameObject("EnemyC3FillTest");
        try
        {
            var t = go.AddComponent<BossAttackConeTelegraph>();
            const float grow = 1.5f;
            t.Show(4f, 180f, 0f, 0f, 0f, 0f, grow, fillInward: true);
            var m = Field<DecalProjector>(t, "_coneFill").material;
            foreach (var (frac, step) in new[] { (0f, 0), (6.49f / Steps, 6), (6.51f / Steps, 7), (1f, 24) })
            {
                Advance(t, grow * frac);
                Vector4 s = m.GetVector("_C3Shape");
                Assert.AreEqual(3f, s.x, "arc fill mode");
                Assert.AreEqual(90f, s.z, 1e-4f, "half angle");
                Assert.AreEqual(1f - (float)step / Steps, s.w, 1e-5f, $"inner at t={frac}");
            }
            // 잡기가 반쯤에서 끊기고 같은 각도의 일반 부채꼴이 오면 고리가 남으면 안 된다.
            Advance(t, grow * 0.5f);
            t.Show(4f, 180f, 0f, 0f, 0f, 0f, grow);
            Assert.AreEqual(0f, m.GetVector("_C3Shape").w, "normal cone fill must be a full sector");
        }
        finally { Cleanup(go); }
    }

    // ── 기준(예전 CPU 굽기) — S4a 에서 지운 BossAttackConeTelegraph 코드를 그대로 옮겼다(테두리 없는 채움 호출 인자 고정) ──

    static Texture2D NewTex(string name) => new Texture2D(Tex, Tex, TextureFormat.RGBA32, false)
    { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };

    // BuildRectTexture(color, 256, rimU 0, rimV 0, interiorAlpha 1)
    static Texture2D OldRect(Color color)
    {
        var tex = NewTex("OldBossPathBandDecal");
        float feather = 1.5f / Tex;
        var pixels = new Color32[Tex * Tex];
        for (int y = 0; y < Tex; y++)
            for (int x = 0; x < Tex; x++)
            {
                float u = (x + 0.5f) / Tex, v = (y + 0.5f) / Tex;
                float du = Mathf.Min(u, 1f - u), dv = Mathf.Min(v, 1f - v);
                float a = color.a * Mathf.Clamp01(du / feather) * Mathf.Clamp01(dv / feather);
                pixels[y * Tex + x] = new Color(color.r, color.g, color.b, Mathf.Clamp01(a));
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    // BuildUnionTexture(color, extent, rimWorld 0, interiorAlpha 1, front)
    static Texture2D OldUnion(Color color, Vector2 extent, float pathRadius, float pathLength, float coneCenter, float coneRadius, float front)
    {
        var tex = NewTex("OldBossAttackUnionDecal");
        float len = extent.y;
        float feather = 1.5f * Mathf.Max(extent.x, len) / Tex;
        var pixels = new Color32[Tex * Tex];
        for (int y = 0; y < Tex; y++)
        {
            float along = (y + 0.5f) / Tex * len;
            for (int x = 0; x < Tex; x++)
            {
                float lat = ((x + 0.5f) / Tex - 0.5f) * extent.x;
                float dBand = pathRadius > 0f && pathLength > 0f
                    ? Mathf.Max(Mathf.Abs(lat) - pathRadius, Mathf.Max(-along, along - pathLength))
                    : float.MaxValue;
                float dz = along - coneCenter;
                float dCone = coneRadius > 0f ? Mathf.Sqrt(dz * dz + lat * lat) - coneRadius : float.MaxValue;
                float d = Mathf.Min(dBand, dCone);
                float a = color.a * Mathf.Clamp01(-d / feather);
                if (front >= 0f && along > front) a = 0f;
                pixels[y * Tex + x] = new Color(color.r, color.g, color.b, Mathf.Clamp01(a));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    // ── 렌더 · 산출물(S2 와 같은 방식) ──

    static Mesh _quad;
    static Mesh Quad
    {
        get
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "EnemyC3FillTestQuad", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return _quad;
        }
    }

    static Color32[] Render(Material mat, int pass)
    {
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGB32, 0) { sRGB = true });
        var cb = new CommandBuffer { name = "EnemyC3FillTest" };
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
        // <repo>/../_lane-artifacts/<repo 폴더 이름>/57-shader/S4a — 레포 밖, 사람이 볼 산출물 자리. 없으면 남기지 않는다.
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "S4a") : null;
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
