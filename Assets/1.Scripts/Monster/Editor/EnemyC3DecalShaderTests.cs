using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 할 일 57 S2 — 공격 데칼 외곽선(01~05·08·10·11: 부채꼴·띠·합집합)을 SG_EnemyC3Decal 로 그린 결과가
/// 예전 CPU Decal() 텍스처 × 기존 데칼 식(TelegraphShade)과 같은지 픽셀로 비교한다(PLAN-57-enemy-c3-shader §6).
///
/// DecalProjector 는 RT 로 직접 찍기 어려워 Hidden/EnemyC3DecalCompare 로 데칼 표면 출력(색·알파)을 평면에 그린다.
/// 그 셰이더의 새 패스는 SG_EnemyC3Decal 의 Custom Function 과 <b>같은 hlsl 함수</b>(EnemyC3DecalShade)를 부르고,
/// 값은 런타임과 <b>같은 C# 함수</b>(EnemyC3Visuals.SetDecalProperties)가 넣는다 — 그래프가 그 함수를 쓰는지는 따로 확인한다.
/// 기준 = 채널 오차 ≤ 2/255 인 픽셀 99% 이상. 나란히 PNG(기존|셰이더|차이×16)는 _lane-artifacts 가 있을 때만 남긴다.
/// </summary>
public sealed class EnemyC3DecalShaderTests
{
    const int N = 384;
    const int Tolerance = 2;
    const float RequiredRatio = 0.99f;
    const string GraphPath = "Assets/1.Scripts/Rendering/Telegraph/SG_EnemyC3Decal.shadergraph";
    const string HlslPath = "Assets/1.Scripts/Rendering/Telegraph/EnemyC3Decal.hlsl";

    // BossAttackConeTelegraph 가 Decal() 을 부르던 인자 그대로 — (모양 크기, 부채꼴 반경, 끝 원 중심, 반각, 경로 폭, 경로 길이).
    public struct Shape
    {
        public string label; public Vector2 extent; public float coneRadius, coneCenter, halfAngle, pathWidth, pathLength;
        public override string ToString() => label;
    }

    static Shape Band(float pathRadius, float length) => new Shape
    { label = $"band_{pathRadius * 2}x{length}m", extent = new Vector2(pathRadius * 2, length), pathWidth = pathRadius * 2, pathLength = length };

    static Shape Cone(float radius, float halfAngle) => new Shape
    { label = $"cone_r{radius}_a{halfAngle * 2}", extent = new Vector2(radius * 2, radius * 2), coneRadius = radius, halfAngle = halfAngle };

    // UnionExtent 와 같은 식(폭 = 큰 반경 ×2, 길이 = max(경로, 끝 원 끝)).
    static Shape Union(float pathRadius, float length, float coneRadius, float coneCenter) => new Shape
    {
        label = $"union_p{pathRadius * 2}x{length}_r{coneRadius}",
        extent = new Vector2(Mathf.Max(pathRadius, coneRadius) * 2, Mathf.Max(length, coneCenter + coneRadius)),
        coneRadius = coneRadius, coneCenter = coneCenter, halfAngle = 180, pathWidth = pathRadius * 2, pathLength = length
    };

    static IEnumerable<TestCaseData> Cases()
    {
        // 돌진·직선 띠 — 짧은 것 / 중간 / 긴 돌진 경로.
        var bands = new[] { Band(1f, 4f), Band(1.5f, 12f), Band(0.75f, 25f) };
        foreach (var id in new[] { "01", "04", "05", "08", "10", "11" })
            foreach (var s in bands) yield return Case(id, s);
        // 03 부채꼴 — 좁은 각 / 반원 / 원(반각 180 → 원 띠 그라데이션).
        foreach (var s in new[] { Cone(3f, 45f), Cone(6f, 90f), Cone(4f, 180f) }) yield return Case("03", s);
        // 02 합집합 — 짧은 경로 + 큰 끝 원 / 긴 돌진 + 작은 끝 원 / 원이 경로보다 좁은 경우.
        foreach (var s in new[] { Union(1.5f, 6f, 3f, 6f), Union(1f, 18f, 2.5f, 18f), Union(2f, 10f, 1.5f, 9f) }) yield return Case("02", s);
    }

    static TestCaseData Case(string id, Shape s) => new TestCaseData(id, s).SetName($"Decal_{id}_{s.label}");

    [OneTimeSetUp]
    public void ResetResults()
    {
        string dir = ArtifactDir();
        if (dir != null && File.Exists(Path.Combine(dir, "results.tsv"))) File.Delete(Path.Combine(dir, "results.tsv"));
    }

    [Test]
    public void GraphUsesSharedDecalFunction()
    {
        // 비교 셰이더와 실제 데칼 그래프가 같은 hlsl·같은 함수를 부른다는 보증.
        string graph = File.ReadAllText(GraphPath);
        StringAssert.Contains("\"m_FunctionName\": \"EnemyC3DecalShade\"", graph);
        StringAssert.Contains($"\"m_FunctionSource\": \"{AssetDatabase.AssetPathToGUID(HlslPath)}\"", graph);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
        Assert.IsNotNull(shader, "SG_EnemyC3Decal");
        Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "SG_EnemyC3Decal has errors");
        Assert.AreSame(shader, EnemyC3Visuals.DecalMaterial.shader, "MA_EnemyC3Decal must use SG_EnemyC3Decal");
    }

    static Mesh _quad;
    static Mesh Quad
    {
        get
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "EnemyC3DecalTestQuad", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return _quad;
        }
    }

    static Color32[] Render(Material mat, int pass)
    {
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGB32, 0) { sRGB = true });
        var cb = new CommandBuffer { name = "EnemyC3DecalTest" };
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

    [TestCaseSource(nameof(Cases))]
    public void ShaderMatchesCpuDecal(string id, Shape s)
    {
        var shader = Shader.Find("Hidden/EnemyC3DecalCompare");
        Assert.IsNotNull(shader, "Hidden/EnemyC3DecalCompare");
        Assert.IsTrue(shader.isSupported, "compare shader not supported");
        var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            // ApplyStyle 가 C3 외곽선에 넣는 값과 같다(흰색, 마스크 꺼짐).
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_MaskTex", Texture2D.whiteTexture);
            mat.SetVector("_MaskParams", new Vector4(0, 0, 1, 0));
            // 기존: Decal() 텍스처. 새것: 같은 인자로 런타임 주입 함수.
            mat.SetTexture("_OldTex", EnemyC3Visuals.Decal(id, s.extent, s.coneRadius, s.coneCenter, s.halfAngle, s.pathWidth, s.pathLength));
            EnemyC3Visuals.SetDecalProperties(mat, id, s.extent, s.coneRadius, s.coneCenter, s.halfAngle, s.pathWidth, s.pathLength);

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
            string label = $"{id}_{s.label}";
            WriteArtifacts(label, expected, actual, diff, ratio, maxDiff);
            Assert.GreaterOrEqual(ratio, RequiredRatio, $"{label}: within {Tolerance}/255 = {ratio:P3}, max channel diff {maxDiff}");
        }
        finally { Object.DestroyImmediate(mat); }
    }

    static string ArtifactDir()
    {
        // <repo>/../_lane-artifacts/<repo 폴더 이름>/57-shader/S2 — 레포 밖, 사람이 볼 산출물 자리. 없으면 남기지 않는다.
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "S2") : null;
    }

    static void WriteArtifacts(string label, Color32[] expected, Color32[] actual, Color32[] diff, float ratio, int maxDiff)
    {
        string dir = ArtifactDir();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        // 기존 | 셰이더 | 차이×16. 앞의 둘은 프리멀티 값을 어두운 회색 위에 얹어 보이게 한다.
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
