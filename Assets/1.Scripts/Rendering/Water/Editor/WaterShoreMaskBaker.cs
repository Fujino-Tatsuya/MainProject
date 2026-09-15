using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;


// ----------------------------------------------------------------------------
//  WaterShoreMaskBaker.cs — 물가까지의 거리(signed distance) 굽기 · 에디터 전용
//
//  🔴 왜 필요한가: 셰이더가 물가를 "얕은 곳"으로 추론하면 두 가지가 같이 망가진다.
//     ① 벽 옆 띠 폭이 카메라 각도를 탄다 — `waterDepth` 는 수면 바로 아래 수심이 아니라
//        **시선이 만난 표면과의 Y 차**라서 수평 거리와 안정적으로 대응하지 않는다.
//     ② 열린 수면에서도 깊이 노이즈가 수심을 깎으면 물가색이 칠해진다(한가운데 흰 덩어리).
//     거리를 텍스처로 주면 둘 다 사라진다. Unity 공식 물 샘플(WaterSimple_FoamMask)도
//     같은 결론으로 "거품 위치를 칠하는 마스크"를 쓴다 — 우리는 손으로 안 칠하고 굽는다.
//
//  🔴 왜 런타임이 아니라 에디터인가(2026-09-15 팀장): 시연 맵은 **고정 맵**이다.
//     셔플 맵이 스테이지 1 로 갈 때는 이 로직을 `MapGenerator.OnGenerated` 에 걸면 된다
//     (`MinimapController.BakeTerrain()` 이 그 전례). 지금은 런타임 비용 0 이 낫다.
//
//  🔴 마스크 한 장이 물 세 장을 전부 덮는다 — 머티리얼(`WaterDark.mat`)이 공유라 물마다
//     다른 텍스처를 줄 수 없다. 수면 높이가 달라도(−3.1 / −19) XZ 로 멀리 떨어져 있어서
//     (x 0 vs 500) 한 장에 담긴다. **물별로 마스크를 나누면 공유 머티리얼에서 마지막 것만 살아남는다.**
//
//  🔴 "무엇이 물가인가" = **수면보다 위로 올라온 렌더러 전부**.
//     카메라 클립으로 "수면 근처만" 고르려던 초안은 교차검증에서 깨졌다 — 상자의 윗면을
//     near 평면으로 자르면 뚜껑이 안 생겨서, 탑다운에서 남은 수직 측면은 투영 면적이 0 이라
//     점유가 통째로 누락된다.
//     대가: 물 위를 지나는 공중 다리도 물가로 잡힌다. 고정 맵이라 결과를 눈으로 보고
//     문제가 되면 그때 프록시로 가른다.
// ----------------------------------------------------------------------------
public static class WaterShoreMaskBaker
{
    private const string WaterMaterialPath = "Assets/3.Materials/Water/WaterDark.mat";
    // 🔴 PNG 다. R16 `Texture2D` 를 스크립트로 만들어 `.asset` 으로 저장했더니 셰이더에서
    //    **검게 샘플링**됐다(2026-09-15 실측: `_DeepColor` 를 빨강으로 바꿔도 빨강이 0픽셀).
    //    PNG + 명시적 임포터 설정은 검증된 경로다. 8비트지만 범위가 ±8m 라 간격이 3cm 로,
    //    처오름 진폭(1~3m)에 비해 충분하다. 16비트를 R·G 로 쪼개면 **bilinear 가 상위/하위를
    //    따로 보간해서 경계마다 튄다** — 그래서 안 쓴다.
    private const string MaskPath = "Assets/3.Materials/Water/WaterShoreMask.png";

    // 텍셀 목표 크기(m). 물가 효과 폭보다 충분히 작아야 한다.
    private const float TargetTexel = 0.35f;
    private const int MaxRes = 2048;

    // 🔴 인코딩 범위(m). 여기 담기는 건 "효과가 닿는 거리"지 맵 전체 거리가 아니다.
    //    가장 넓은 효과 범위 + 처오름 이동량 + 미분 여유보다 크기만 하면 된다.
    //    🔴 색 전환 거리(`_GradientDist`)가 이 값을 넘으면 그 너머는 **평평해진다** — 인게임에서
    //    "주변만 일정 거리 퍼지고 끝"으로 보이는 게 정확히 이 포화다(2026-09-15 팀장).
    //    그래서 `_GradientDist` 보다 넉넉히 크게 잡는다.
    //    ±48m 를 8비트로 담으면 간격이 0.38m — 텍셀 크기와 같아서 공간 해상도가 지배한다(손해 없음).
    private const float MaskRange = 48f;

    private const float Margin = 8f;   // 물 경계 바깥 여유(m)

    [MenuItem("Tools/Rendering/Look/Bake Water Shore Mask (open scene)")]
    public static void Bake()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning("[ShoreMask] 활성 씬이 없다.");
            return;
        }

        var waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (waterMaterial == null)
        {
            Debug.LogWarning($"[ShoreMask] 물 머티리얼이 없다: {WaterMaterialPath}");
            return;
        }

        List<Renderer> waters = FindWaters(waterMaterial);
        if (waters.Count == 0)
        {
            Debug.LogWarning("[ShoreMask] 이 씬에서 물 렌더러를 못 찾았다.");   // 조용히 성공하지 않는다
            return;
        }

        // ── 1. 모든 물을 덮는 월드 XZ 사각형 ───────────────────────────────
        Bounds b = waters[0].bounds;
        float topWaterY = waters[0].transform.position.y;
        float lowWaterY = topWaterY;
        foreach (Renderer r in waters)
        {
            b.Encapsulate(r.bounds);
            topWaterY = Mathf.Max(topWaterY, r.transform.position.y);
            lowWaterY = Mathf.Min(lowWaterY, r.transform.position.y);
        }

        float minX = b.min.x - Margin, maxX = b.max.x + Margin;
        float minZ = b.min.z - Margin, maxZ = b.max.z + Margin;
        float worldW = maxX - minX, worldH = maxZ - minZ;

        // 🔴 텍셀을 정사각으로 유지한다. 가로세로 간격이 다르면 거리 변환이 왜곡되고,
        //    나중에 ∇d 를 뽑을 때 방향이 틀어진다.
        float texel = Mathf.Max(TargetTexel, Mathf.Max(worldW, worldH) / MaxRes);
        int resX = Mathf.Clamp(Mathf.RoundToInt(worldW / texel), 16, MaxRes);
        int resY = Mathf.Clamp(Mathf.RoundToInt(worldH / texel), 16, MaxRes);

        // ── 2. 점유(= 물 위에 있는 것) 래스터화 ────────────────────────────
        // 🔴 카메라 렌더를 쓰지 않는다(2026-09-15에 두 번 밟음).
        //    ① 배경 알파로 빈 곳을 가렸더니 URP 가 알파를 1 로 채워 **전 픽셀이 육지**가 됐다.
        //    ② 마젠타 배경으로 바꿨더니 한 번은 맞았는데 다음 베이크에서 **통째로 뒤집혔다**
        //       (스카이박스가 그려진 것으로 보인다). 렌더 결과의 색으로 점유를 판정하는 한
        //       파이프라인 상태에 계속 끌려다닌다.
        //    렌더러 바운즈를 CPU 로 찍으면 결정적이다. 대가: 축에 안 맞는 벽이 AABB 만큼 뚱뚱해진다.
        //    이 맵은 복도가 축 정렬이라 문제되지 않는다.
        int n = resX * resY;
        var land = new bool[n];
        int landCount = 0;
        int skippedHuge = 0;
        float invTexelX = resX / worldW, invTexelZ = resY / worldH;

        var allRenderers = Object.FindObjectsByType<MeshRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var r in allRenderers)
        {
            if (waters.Contains(r)) continue;                      // 물 자신
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds rb = r.bounds;
            if (rb.max.y <= topWaterY + 0.01f) continue;           // 수면 아래 = 물가 아님(바닥 포함)

            // 🔴 거대 렌더러 한 장이 전체를 육지로 만들 수 있다. 넓이로 걸러내고 **로그로 남긴다**.
            float areaFrac = (rb.size.x * rb.size.z) / (worldW * worldH);
            if (areaFrac > 0.25f) { skippedHuge++; continue; }

            int x0 = Mathf.Clamp(Mathf.FloorToInt((rb.min.x - minX) * invTexelX), 0, resX - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((rb.max.x - minX) * invTexelX), 0, resX - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((rb.min.z - minZ) * invTexelZ), 0, resY - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((rb.max.z - minZ) * invTexelZ), 0, resY - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = z * resX + x;
                    if (!land[i]) { land[i] = true; landCount++; }
                }
        }

        // 🔴 "0장 성공"으로 넘어가지 않는다. 전부 0 이거나 전부 1 인 마스크는 무의미하다.
        if (landCount == 0 || landCount == n)
        {
            Debug.LogError($"[ShoreMask] 점유가 {(landCount == 0 ? "0" : "전부")} 다 — 마스크가 무의미하다. " +
                           $"수면 {topWaterY:0.##} 위 렌더러를 못 찾았거나 전부 찾았다.");
            return;
        }

        // ── 4. signed distance ─────────────────────────────────────────────
        // 물 쪽 양수 / 육지 쪽 음수. 경계에서 미분이 끊기지 않아 ∇d 가 안정적이다.
        float[] dOut = EdtSquared(land, resX, resY);     // 육지가 씨앗 → 물에서의 거리
        var water = new bool[n];
        for (int i = 0; i < n; i++) water[i] = !land[i];
        float[] dIn = EdtSquared(water, resX, resY);     // 물이 씨앗 → 육지에서의 거리

        // ── 5. 인코딩 (PNG · 8비트 · 선형) ─────────────────────────────────
        var cols = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            float sd = (land[i] ? -Mathf.Sqrt(dIn[i]) : Mathf.Sqrt(dOut[i])) * texel;
            float u = 0.5f + 0.5f * Mathf.Clamp(sd / MaskRange, -1f, 1f);
            byte v = (byte)Mathf.RoundToInt(u * 255f);
            cols[i] = new Color32(v, v, v, 255);
        }

        var tmp = new Texture2D(resX, resY, TextureFormat.RGBA32, false, true);
        tmp.SetPixels32(cols);
        tmp.Apply();
        System.IO.File.WriteAllBytes(MaskPath, tmp.EncodeToPNG());
        Object.DestroyImmediate(tmp);

        AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
        ConfigureImporter(MaskPath);
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        if (mask == null)
        {
            Debug.LogError($"[ShoreMask] PNG 임포트 실패: {MaskPath}");
            return;
        }

        // ── 6. 머티리얼에 물리기 ───────────────────────────────────────────
        waterMaterial.SetTexture("_ShoreMask", mask);
        // xy = 최소 모서리, zw = 1/크기. 셰이더: uv = (worldXZ - xy) * zw
        waterMaterial.SetVector("_ShoreMaskRect", new Vector4(minX, minZ, 1f / worldW, 1f / worldH));
        waterMaterial.SetFloat("_ShoreMaskRange", MaskRange);
        waterMaterial.SetFloat("_ShoreMaskTexel", texel);
        EditorUtility.SetDirty(waterMaterial);
        AssetDatabase.SaveAssets();

        // ── 7. 사람이 볼 수 있는 미리보기 (레포 밖) ────────────────────────
        WritePreview(land, dOut, dIn, resX, resY, texel);

        Debug.Log($"[ShoreMask] 구웠다 — {resX}x{resY}, 텍셀 {texel:0.###}m, 범위 ±{MaskRange}m\n" +
                  $"  점유(수면 위) {(100f * landCount / n):0.#}%\n" +
                  $"  월드 x[{minX:0.#}, {maxX:0.#}] z[{minZ:0.#}, {maxZ:0.#}]\n" +
                  $"  기준 수면 {topWaterY:0.##} 위의 렌더러만 (가장 낮은 수면 {lowWaterY:0.##})\n" +
                  $"  물 {waters.Count}장 · 너무 커서 건너뛴 렌더러 {skippedHuge}개");
    }

    /// <summary>미리보기 PNG. 🔴 마스크가 맞는지 **눈으로** 확인하기 전에 셰이더를 고치지 않는다.</summary>
    private static void WritePreview(bool[] land, float[] dOut, float[] dIn, int w, int h, float texel)
    {
        var prev = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        var cols = new Color32[w * h];
        for (int i = 0; i < cols.Length; i++)
        {
            if (land[i])
            {
                cols[i] = new Color32(200, 60, 60, 255);           // 육지 = 빨강
            }
            else
            {
                float m = Mathf.Sqrt(dOut[i]) * texel;
                byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(m / MaskRange) * 255f);
                cols[i] = new Color32(0, (byte)(255 - v), (byte)(60 + v * 0.7f), 255); // 물가=밝음 → 먼 물=어두움
            }
        }
        prev.SetPixels32(cols);
        prev.Apply();
        string dir = System.Environment.GetEnvironmentVariable("TEMP");
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "shore_mask_preview.png"), prev.EncodeToPNG());
        Object.DestroyImmediate(prev);
    }

    private static void ConfigureImporter(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        // 🔴 sRGB 를 켜면 거리값이 감마로 휘어 벽에서 멀어질수록 틀려진다. 반드시 끈다.
        imp.sRGBTexture = false;
        imp.textureType = TextureImporterType.Default;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;
        imp.SaveAndReimport();
    }

    private static List<Renderer> FindWaters(Material waterMaterial)
    {
        var found = new List<Renderer>();
        var renderers = Object.FindObjectsByType<MeshRenderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var r in renderers)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == waterMaterial) { found.Add(r); break; }
            }
        }
        return found;
    }

    /// <summary>유닛·UI·연출을 뺀다. 🔴 맵 조각은 전부 Default(0) 이라 레이어로 **고를** 수는 없다
    /// (교훈 #33/#34). 여기서는 "빼는" 용도로만 쓴다.</summary>
    private static int BuildCullingMask()
    {
        int mask = ~0;
        foreach (string ln in new[] { "UI", "Player", "Enemy", "Water", "Soul", "Corpse",
                                      "Projectile", "Effect", "HazardArea", "CombatTarget" })
        {
            int l = LayerMask.NameToLayer(ln);
            if (l >= 0) mask &= ~(1 << l);
        }
        return mask;
    }

    /// <summary>
    /// Felzenszwalb–Huttenlocher 거리 변환. 행 → 열 2패스로 **정확한** 제곱 유클리드 거리.
    /// 🔴 블러 근사와 다르다 — 블러는 주변 점유율을 섞어서 같은 벽 거리라도 코너·좁은 틈에서
    ///    값이 달라진다. 그걸 d[m] 로 쓰면 안 된다.
    /// </summary>
    private static float[] EdtSquared(bool[] seeds, int w, int h)
    {
        const float INF = 1e20f;
        int m = Mathf.Max(w, h);
        var f = new float[m];
        var d = new float[m];
        var v = new int[m];
        var z = new float[m + 1];
        var grid = new float[w * h];

        for (int i = 0; i < grid.Length; i++) grid[i] = seeds[i] ? 0f : INF;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) f[x] = grid[y * w + x];
            Transform1D(f, d, v, z, w);
            for (int x = 0; x < w; x++) grid[y * w + x] = d[x];
        }
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) f[y] = grid[y * w + x];
            Transform1D(f, d, v, z, h);
            for (int y = 0; y < h; y++) grid[y * w + x] = d[y];
        }
        return grid;
    }

    private static void Transform1D(float[] f, float[] d, int[] v, float[] z, int n)
    {
        int k = 0;
        v[0] = 0;
        z[0] = -1e20f;
        z[1] = 1e20f;
        for (int q = 1; q < n; q++)
        {
            float s;
            while (true)
            {
                s = ((f[q] + (float)q * q) - (f[v[k]] + (float)v[k] * v[k])) / (2f * q - 2f * v[k]);
                if (k > 0 && s <= z[k]) k--;
                else break;
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = 1e20f;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            float dx = q - v[k];
            d[q] = dx * dx + f[v[k]];
        }
    }
}
