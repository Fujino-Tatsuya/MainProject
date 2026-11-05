using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 할 일 221 후속 — 확인 씬(DebugDecalFloorBlendLab)을 Play 없이 세워 변형별 비교 이미지를 남긴다.
/// batchmode 진입점: <c>-executeMethod DecalFloorBlendLabCapture.Capture</c>(선택 <c>-captureOut &lt;폴더&gt;</c>).
/// 기본 저장 위치 = 레포 밖 <c>_lane-artifacts/Work2/221/compare</c>(없으면 만든다). 전역 값은 캡처 동안만 넣고 끝나면 지운다.
/// 변형 = 끔(221 이전) · 1차(채움만) · 후속 밝기 보존 · 후속 곱하기 / 시점 = 전체 + 바닥 3종 × 예고·스킬 근접.
/// 시트(<c>sheet_*.png</c>)는 한 시점의 변형 4개를 왼쪽부터 위 순서로 붙인 것.
/// </summary>
public static class DecalFloorBlendLabCapture
{
    const string Tag = "[DecalFloorBlendCapture]";
    const int W = 960, H = 600;

    struct Variant
    {
        public string name;
        public bool off;
        public Vector4 fill, edge;
    }

    public static readonly Vector4 FirstPassFill = DecalFloorBlend.ToGlobal(0.6f, 0.15f, 1f);

    static Variant[] Variants()
    {
        Vector4 fill = DecalFloorBlend.ToGlobal(0.6f, 0.5f, 1f);
        return new[]
        {
            new Variant { name = "0_off", off = true },
            new Variant { name = "1_first", fill = FirstPassFill, edge = DecalFloorBlend.EdgeIdentity },
            new Variant { name = "2_luminance", fill = fill, edge = DecalFloorBlend.ToEdgeGlobal(0.9f, 0.7f, DecalFloorStainMode.LuminanceKeep) },
            new Variant { name = "3_multiply", fill = fill, edge = DecalFloorBlend.ToEdgeGlobal(0.9f, 0.7f, DecalFloorStainMode.Multiply) },
        };
    }

    public static void Capture()
    {
        string outDir = Arg("-captureOut") ?? DefaultDir();
        Directory.CreateDirectory(outDir);
        bool async = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            EditorSceneManager.OpenScene(DecalFloorBlendLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var lab = Object.FindFirstObjectByType<DebugDecalFloorBlendLab>();
            if (lab == null) { Debug.LogError($"{Tag} 랩 컴포넌트가 없다"); return; }
            lab.Build();
            Camera cam = Camera.main;
            if (cam == null) { Debug.LogError($"{Tag} 카메라 없음"); return; }

            var views = new List<(string name, Vector3 pos, Vector3 look)>
            {
                ("all", cam.transform.position, cam.transform.position + cam.transform.forward),
            };
            string[] floors = { "bright", "dark", "pattern" };
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 16f;
                views.Add(($"{floors[i]}_telegraph", new Vector3(x, 12f, -15.5f), new Vector3(x, 0f, -6f)));
                views.Add(($"{floors[i]}_skill", new Vector3(x, 10f, -1.5f), new Vector3(x, 0f, 6f)));
            }

            var rt = new RenderTexture(new RenderTextureDescriptor(W, H, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 4 });
            cam.targetTexture = rt;
            Variant[] variants = Variants();
            foreach (var v in views)
            {
                cam.transform.position = v.pos;
                cam.transform.LookAt(v.look);
                var shots = new List<Color32[]>();
                foreach (Variant variant in variants)
                {
                    Apply(variant);
                    Color32[] px = Shot(cam, rt);
                    shots.Add(px);
                    Save(Path.Combine(outDir, $"{v.name}_{variant.name}.png"), px, W, H);
                }
                var sheet = new Color32[W * shots.Count * H];
                for (int s = 0; s < shots.Count; s++)
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                            sheet[y * W * shots.Count + s * W + x] = shots[s][y * W + x];
                Save(Path.Combine(outDir, $"sheet_{v.name}.png"), sheet, W * shots.Count, H);
            }
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            File.WriteAllText(Path.Combine(outDir, "README.txt"),
                "221 바닥 블렌딩 비교 — sheet_<시점>.png = 왼쪽부터 0 끔(221 이전) | 1 1차(채움 0.6·소프트 0.15) | " +
                "2 후속 밝기 보존(채움 0.6·소프트 0.5·외곽선 알파 0.9·물들임 0.7) | 3 후속 곱하기(같은 값, 모드만 곱하기)\n" +
                "만든 곳: Assets/1.Scripts/Dev/DecalFloorBlendLab/Editor/DecalFloorBlendLabCapture.cs (batchmode -executeMethod DecalFloorBlendLabCapture.Capture)\n");
            Debug.Log($"{Tag} {views.Count} 시점 × {variants.Length} 변형 → {outDir}");
        }
        finally
        {
            DecalFloorBlendDriver.Clear();
            ShaderUtil.allowAsyncCompilation = async;
        }
    }

    static void Apply(Variant v)
    {
        if (v.off) { DecalFloorBlendDriver.Clear(); return; }
        Shader.SetGlobalVector(DecalFloorBlend.SkillGlobalId, v.fill);
        Shader.SetGlobalVector(DecalFloorBlend.TelegraphGlobalId, v.fill);
        Shader.SetGlobalVector(DecalFloorBlend.SkillEdgeGlobalId, v.edge);
        Shader.SetGlobalVector(DecalFloorBlend.TelegraphEdgeGlobalId, v.edge);
    }

    static Color32[] Shot(Camera cam, RenderTexture rt)
    {
        cam.Render();
        cam.Render();   // 첫 변형 컴파일·데칼 등록 뒤 한 번 더
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(W, H, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
        read.Apply(false, false);
        RenderTexture.active = prev;
        Color32[] px = read.GetPixels32();
        Object.DestroyImmediate(read);
        return px;
    }

    static void Save(string path, Color32[] px, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply(false, false);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static string DefaultDir()
    {
        var project = Directory.GetParent(Application.dataPath);
        return Path.Combine(project.Parent.FullName, "_lane-artifacts", "Work2", "221", "compare");
    }

    static string Arg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
