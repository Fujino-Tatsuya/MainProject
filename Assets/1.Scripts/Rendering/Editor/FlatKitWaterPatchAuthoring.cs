using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Flat Kit 물 — 파도가 보이는 격자 물 조각 저작 (PLAN-flatkit.md 9단계 · 2026-09-30).
//
// 왜: 기존 AbyssWater 는 내장 Quad(정점 4개)라 Flat Kit 파도(정점 y 변위)가 안 보인다. 330m 전체를 촘촘히
//     쪼개는 대신, 물이 실제로 보이는 곳(보스방 아래 → 이후 존 밑)에만 격자 조각을 깐다(팀장 09-30).
// - 파도는 월드 좌표 사인파(Water.shader 243~266) → 파장 = 2π/_WaveFrequency. Pool09 값(25.59 = 0.25m)은
//   수영장 기준이라 맵 스케일로 다시 잡는다. 정점 간격은 파장의 1/4 이하가 시작 기준(Codex 교차검증).
// - 격자와 기존 Quad 가 보이는 곳에서 겹치면 투명이 두 번 섞이고 경계가 보인다 → 조각이 덮는 곳의 Quad 는 끈다(지우지 않음).
// - 다시 실행하면 같은 이름의 조각을 지우고 새로 만든다.
public static class FlatKitWaterPatchAuthoring
{
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";
    const string WaterMaterialPath = "Assets/3.Materials/FlatKit/Water/FK_Water_Pool09.mat";
    const string MeshDir = "Assets/3.Materials/FlatKit/Water/Meshes";
    // 🔴 데모(Pool_Water.fbx) 조건을 그대로 맞춘다(09-30 실측: 27.57m 정사각 · UV 0~1 · 정점 2809 = 간격 0.53m).
    //    데모도 파장 0.25m(주파수 25.59) 파도를 0.53m 격자로 그린다 → 사인파가 아니라 **불규칙한 일렁임**으로 보인다.
    //    처음에 '파장 2.5m + 0.4m 격자'로 "바로잡았다"가 매끈한 파도가 돼 데모와 달라졌다(팀장: 어색함).
    // ⚠️ 뒤집음(09-30 인게임): 벤트 틈은 몇 cm 라 데모 파도(0.25m 를 0.53m 격자로 뭉갬)는 틈으로 보면 단색이다.
    //    좁은 틈으로도 흐름이 읽히게 **가늘고 대비 큰 마루(Crest) 선이 한 방향으로 지나가도록** 다시 잡는다.
    //    🔴 이 Flat Kit 버전은 파도 계산 전체가 `#if _WAVEMODE_GRID` 안이다(Water.shader 254~271) —
    //       Round/Pointy 로 바꾸면 파도 높이가 항상 0. Grid(수직인 두 진행파의 곱)를 유지한다.
    const float Spacing = 0.16f;        // 정점 간격(m) — 파장 0.63m 에 약 4점
    const float UvWorldSize = 27.57f;   // UV 1 = 27.57m — 거품·굴절(UV 기준) 무늬 크기를 데모와 같게
    // 09-30 2차: 흰 마루는 두 사인파의 곱 최고점 근처 '점'으로만 나와 좁은 틈엔 가끔 스친다 → 크게·촘촘히 + 표면 거품 줄무늬.
    const float WaveFrequency = 10f;    // 파장 2π/10 ≈ 0.63m (10-01 팀장: 더 촘촘히)
    const float WaveAmplitude = 0.05f;  // 마루 선은 높이가 아니라 위상(waveHeight)으로 칠해져 작아도 보인다 · 창살과 간섭 없음
    const float WaveSpeed = 1.2f;       // 진행 ≈ 2·속도/주파수 ≈ 0.5 m/s
    const float WaveNoise = 0.4f;       // 마루 선을 구불구불하게
    const float CrestSize = 0.45f;      // 작을수록 얇은 선(0.15 는 틈으로 거의 안 걸렸다 · 10-01 팀장: 더 크게)
    const float CrestSharpness = 0.7f;  // 클수록 경계가 또렷
    const float FoamSpeed = 1.5f;       // 표면 거품도 같이 흐르게
    const float FoamAmount = 0.4f;      // 표면 거품 면적(Pool09 0.08) — 흐르는 흰 줄무늬 (10-01 팀장: 더 많이)
    const float FoamSharpness = 0.9f;   // 거품 경계 또렷하게
    const float FoamStretchY = 3f;      // 흐름 방향으로 늘여 '줄무늬'로 (X 1 · Y 3)
    // Pool09 거품 색은 짙은 청록 a 0.25 — 흰 거품이 아니라 은은한 음영이라 양을 늘려도 흐름이 안 읽힌다.
    static readonly Color FoamColor = new Color(0.85f, 0.97f, 0.97f, 0.6f);

    // 보스방: 물을 벤트 창살 바로 밑(바닥 판 두께 안)에 둔다 — 판이 가리고 **벤트 틈으로만** 보이며,
    // 판 옆면이 수면을 관통해 벤트 가장자리에 데모 같은 거품 띠가 선다. (y=-19 는 19m 시차로 벤트 옆으로 밀려 보였다.)
    // 09-30 실측: 바닥 판 y 0~0.5 · 트렌치 덮개(창살) y 0.44~0.51.
    const float BelowFloorTop = 0.62f;  // 바닥 윗면(0.5)에서 이만큼 아래 = 수면 −0.12 (10-01 팀장 Play 확인값 — 0.2 는 물이 벽 위로 비쳤다)
    const float BelowGrateBottom = 0.56f; // 창살 아랫면(0.44)에서 이만큼 아래 = 수면 −0.12 — 존은 창살 기준(없으면 BelowFloorTop)

    const float BossRoomScale = 1.0f;   // 보스방 바닥(30×30)과 같은 크기 — 벤트로 비치는 모습 확인용(팀장 09-30, 1.5배 안은 어색해서 되돌림)

    // ── 물 바닥(경사) ─────────────────────────────────────────
    // 🔴 Flat Kit 물 색 = (불투명 씬 깊이 − 수면 깊이) 로 그라데이션을 읽는다(Water.shader DepthFade).
    //    물 밑에 아무것도 없으면 깊이가 무한 → 그라데이션 끝(Pool09 = 거의 검정 21,21,22)이 된다(09-30 첫 시도).
    //    데모 수영장처럼 **가장자리는 얕게(밝은 청록 + 물가 거품) → 바깥은 깊게(어둡게)** 바닥을 깐다.
    //    거품은 수심 약 0.6~1m 띠에서 생긴다(_FadeDistance 0.61 · _FoamDepth 0.04 · _WaterDepth 9.28).
    const string BedMaterialPath = "Assets/3.Materials/Environment/UsedInMap/Water/MA_WaterBed.mat";   // 불투명 Unlit
    // 데모의 '청록 위 어두운 얼룩' = 바닥 깊이가 들쭉날쭉해서 생긴다. 사각 링(가장자리 얕음→가운데 깊음)은
    // 안에서 밖으로 흐르는 것처럼 보여 어색했다(09-30). → 펄린 노이즈로 불규칙한 깊이.
    const float BedMinDepth = 5f;       // 얕은 곳 수심(m) — 10-01 팀장: 짙은 청록에 어둡게 (0.8·1.5 는 밝은 청록)
    const float BedMaxDepth = 9f;       // 깊은 곳 수심(m) — 그라데이션 끝(거의 검정) 쪽 얼룩
    const float BedNoiseScale = 0.12f;  // 얼룩 크기(1/m) — 0.12 ≈ 8m 주기
    const float BedSpacing = 0.75f;

    // [진단] 보스방 벤트(트렌치 덮개·도랑)의 실제 높이와 그 밑이 막혔는지 — 물을 어디에 둘지 정하려고.
    [MenuItem("Tools/Rendering/Flat Kit/Water/0. Probe Boss Room Vents")]
    public static void ProbeBossRoomVents()
    {
        // 이미 열려 있으면 닫지 않는다(팀장이 편집 중인 씬을 도구가 닫아 버리면 안 된다).
        bool wasLoaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(MapScenePath).isLoaded;
        var scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject boss = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "bossroom");
            if (boss == null) { Debug.LogError("[WaterProbe] bossroom 없음"); return; }
            var sb = new System.Text.StringBuilder("[WaterProbe] 보스방 벤트\n");
            foreach (Renderer r in boss.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n.IndexOf("trench", System.StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("floor_stone", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Bounds b = r.bounds;
                sb.Append($"  {n,-34} y {b.min.y:0.00}~{b.max.y:0.00}  xz ({b.center.x:0.0},{b.center.z:0.0}) size ({b.size.x:0.0},{b.size.z:0.0})\n");
            }
            // 덮개 중심에서 아래로 레이 — 첫 번째·두 번째로 맞는 것(덮개 창살 틈을 지나 무엇이 있는지).
            Physics.SyncTransforms();
            foreach (Renderer r in boss.GetComponentsInChildren<Renderer>(true).Where(x => x.gameObject.name.StartsWith("floor_metal_trenchcover")).Take(3))
            {
                Vector3 c = r.bounds.center;
                for (float dx = -0.6f; dx <= 0.6f; dx += 0.3f)
                {
                    var hits = Physics.RaycastAll(new Vector3(c.x + dx, 10f, c.z), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(3);
                    sb.Append($"  ray ({c.x + dx:0.0},{c.z:0.0}): " + string.Join(" | ", hits.Select(h => $"{h.collider.name}@{h.point.y:0.00}")) + "\n");
                }
            }
            // 데모 수영장 물 메시 크기 — 거품·굴절이 UV 기준이라 우리 조각 UV 스케일의 기준이 된다.
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath("Assets/FlatKit/Demos/[Water] Pool/Pool Scene - Models/Pool_Water.fbx"))
                if (o is Mesh dm)
                {
                    var uvs = new System.Collections.Generic.List<Vector2>(); dm.GetUVs(0, uvs);
                    Vector2 uMin = uvs.Count > 0 ? new Vector2(uvs.Min(u => u.x), uvs.Min(u => u.y)) : Vector2.zero;
                    Vector2 uMax = uvs.Count > 0 ? new Vector2(uvs.Max(u => u.x), uvs.Max(u => u.y)) : Vector2.zero;
                    sb.Append($"  demo mesh {dm.name}: bounds {dm.bounds.size} verts {dm.vertexCount} uv {uMin}~{uMax}\n");
                }
            var demoGo = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FlatKit/Demos/[Water] Pool/Pool Scene - Models/Pool_Water.fbx");
            if (demoGo != null)
                foreach (MeshFilter mf in demoGo.GetComponentsInChildren<MeshFilter>(true))
                    sb.Append($"  demo renderer {mf.name}: lossyScale {mf.transform.lossyScale} worldBounds≈{Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale)}\n");
            Debug.Log(sb.ToString());
        }
        finally
        {
            if (!wasLoaded && UnityEngine.SceneManagement.SceneManager.loadedSceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // [진단] 존마다 바닥 아래로 내려가는 지오메트리(구덩이 벽·바닥)의 높이 분포 — 존별 수면 높이를 정하려고.
    [MenuItem("Tools/Rendering/Flat Kit/Water/0b. Probe Zone Pits")]
    public static void ProbeZonePits()
    {
        var sb = new System.Text.StringBuilder("[WaterProbe] 존 구덩이\n");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/2.Prefabs/Environment/Layouts/Zones" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var rs = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => !r.transform.IsChildOf(root.transform.Find("Water") ?? root.transform) || root.transform.Find("Water") == null)
                    .Where(r => r.name != "WaterWaves" && r.name != "WaterBed").ToList();
                var below = rs.Where(r => r.bounds.min.y < -0.6f).ToList();
                float minY = rs.Count > 0 ? rs.Min(r => r.bounds.min.y) : 0f;
                // 바닥 아래 지오메트리 중 '넓은 수평면'(구덩이 바닥 후보): 높이 얇고 면적 큰 것
                var floorsBelow = below.Where(r => r.bounds.size.y < 0.8f && r.bounds.size.x * r.bounds.size.z > 4f)
                    .GroupBy(r => Mathf.Round(r.bounds.max.y * 10f) / 10f).OrderByDescending(g => g.Sum(r => r.bounds.size.x * r.bounds.size.z)).Take(3)
                    .Select(g => $"{g.Key:0.0}({g.Count()}개)");
                Transform w = root.transform.Find("Water");
                string cur = w != null && w.TryGetComponent(out ZoneWater zw) ? zw.WaterHeight.ToString("0.##") : "-";
                sb.Append($"  {System.IO.Path.GetFileNameWithoutExtension(path),-22} 최저 {minY:0.00} · 바닥 아래 렌더러 {below.Count} · 아래 수평면 [{string.Join(", ", floorsBelow)}] · 현재 수면 {cur}\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Debug.Log(sb.ToString());
    }

    // [진단] ZoneS_typeA 의 벽 렌더러 위치 — 벽 안쪽 면 기준을 잡으려고.
    [MenuItem("Tools/Rendering/Flat Kit/Water/0c. Probe Zone Walls (S_A)")]
    public static void ProbeZoneWalls()
    {
        string path = "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneS_typeA.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var sb = new System.Text.StringBuilder("[WaterProbe] ZoneS_typeA 렌더러(바닥 아래로 내려가는 것 · 이름별)\n");
            foreach (var g in root.GetComponentsInChildren<MeshRenderer>(true)
                         .Where(r => r.bounds.min.y < -1f && r.name != "WaterWaves" && r.name != "WaterBed")
                         .GroupBy(r => System.Text.RegularExpressions.Regex.Replace(r.name, @"\s*\(\d+\)$", "")))
            {
                var bs = g.Select(r => r.bounds).ToList();
                sb.Append($"  {g.Key,-40} ×{bs.Count} · x {bs.Min(b => b.min.x):0.0}~{bs.Max(b => b.max.x):0.0} · z {bs.Min(b => b.min.z):0.0}~{bs.Max(b => b.max.z):0.0} · y {bs.Min(b => b.min.y):0.0}~{bs.Max(b => b.max.y):0.0} · 한 개 크기 ({bs[0].size.x:0.0},{bs[0].size.y:0.0},{bs[0].size.z:0.0})\n");
            }
            Debug.Log(sb.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Rendering/Flat Kit/Water/1. Boss Room Patch")]
    public static void BuildBossRoomPatch()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[WaterPatch] Play 중에는 하지 않는다."); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        var bedMat = AssetDatabase.LoadAssetAtPath<Material>(BedMaterialPath);
        if (mat == null || bedMat == null) { Debug.LogError("[WaterPatch] 물/바닥 머티리얼 없음"); return; }
        // 머티리얼 값(색·파도·거품)은 덮어쓰지 않는다 — 인스펙터에서 조절한 값이 유지돼야 한다(팀장 10-01).
        // 초기값으로 되돌리려면 '3. Reset Water Look (preset)'.
        EnsureWorldUvShader(mat);

        // 이미 열려 있으면 닫지 않는다(팀장이 편집 중인 씬을 도구가 닫아 버리면 안 된다).
        bool wasLoaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(MapScenePath).isLoaded;
        var scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject boss = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "bossroom");
            BoxCollider floor = boss != null ? boss.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault(c => c.name == "BossFloorCollider") : null;
            if (floor == null) { Debug.LogError("[WaterPatch] MapScene 에 bossroom/BossFloorCollider 가 없다"); return; }

            Bounds fb = floor.bounds;   // 월드 AABB(방이 90° 단위로 돌아 있어 정사각형 그대로)
            float size = Mathf.Max(fb.size.x, fb.size.z) * BossRoomScale;

            // 수면 높이: 이미 조절해 둔 값이 있으면 그대로(ZoneWater), 없으면 규칙값. 예전 구조(WaterWaves_BossRoom)면 그 높이를 옮긴다.
            GameObject oldWater = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "WaterWaves_BossRoom");
            GameObject existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == BossWaterName);
            float waterY = existing != null && existing.TryGetComponent(out ZoneWater zw) ? zw.WaterHeight
                         : oldWater != null ? oldWater.transform.position.y
                         : fb.max.y - BelowFloorTop;
            foreach (GameObject old in scene.GetRootGameObjects().Where(g => g.name == "WaterWaves_BossRoom" || g.name == "WaterBed_BossRoom" || g.name == BossWaterName))
                Object.DestroyImmediate(old);

            Vector3 center = new Vector3(fb.center.x, waterY, fb.center.z);
            var water = new GameObject(BossWaterName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(water, scene);
            water.transform.position = center;
            water.AddComponent<ZoneWater>().WaterHeight = waterY;   // 루트 오브젝트라 로컬 = 월드
            Mesh mesh = GetOrCreateGridMesh(size, size, Spacing);
            AddChild(water, ZoneWaterName, Vector3.zero, mesh, mat);
            AddChild(water, ZoneBedName, Vector3.zero, CreateBedMesh(size, size, BedSpacing), bedMat);

            // 이 조각이 덮는 곳의 기존 Quad(보스방 쪽 AbyssWater)를 끈다 — 투명 두 겹 방지.
            int disabled = 0;
            foreach (MeshRenderer q in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if (q.gameObject.name != "AbyssWater" || q.sharedMaterial != mat) continue;
                Bounds qb = q.bounds;
                if (qb.Contains(new Vector3(center.x, qb.center.y, center.z)) && Mathf.Abs(qb.center.x - center.x) < 150f)
                {
                    q.gameObject.SetActive(false);
                    disabled++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[WaterPatch] 보스방 물 조각 — 중심 {center} · 수면 {waterY:0.##} · {size:0.#}m 정사각 · 간격 {Spacing}m · 정점 {mesh.vertexCount} · 바닥 수심 {BedMinDepth}~{BedMaxDepth}m · 기존 Quad 끔 {disabled}");
        }
        finally
        {
            if (!wasLoaded && UnityEngine.SceneManagement.SceneManager.loadedSceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }

    const string BossWaterName = "Water_BossRoom";

    // 물 색·파도·거품 **초기값**(10-01 팀장 확정 시점). 보스방·존 조각이 같은 머티리얼(FK_Water_Pool09)을 쓴다.
    // 조각 메뉴는 더 이상 이 값을 덮어쓰지 않는다 — 평소엔 머티리얼 인스펙터에서 조절하고, 되돌릴 때만 이 메뉴.
    [MenuItem("Tools/Rendering/Flat Kit/Water/3. Reset Water Look (preset)")]
    static void ResetWaterLook()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (mat == null) { Debug.LogError($"[WaterPatch] {WaterMaterialPath} 없음"); return; }
        ApplyWaterLook(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("[WaterPatch] 물 머티리얼을 초기값으로 되돌림");
    }

    static void ApplyWaterLook(Material mat)
    {
        mat.SetFloat("_WaveFrequency", WaveFrequency);
        mat.SetFloat("_WaveAmplitude", WaveAmplitude);
        mat.SetFloat("_WaveSpeed", WaveSpeed);
        mat.SetFloat("_WaveNoise", WaveNoise);
        mat.SetFloat("_WaveMode", 2f);   // Grid — 위 주석 참고
        foreach (string k in new[] { "_WAVEMODE_NONE", "_WAVEMODE_ROUND", "_WAVEMODE_POINTY" }) mat.DisableKeyword(k);
        mat.EnableKeyword("_WAVEMODE_GRID");
        mat.SetFloat("_CrestSize", CrestSize);
        mat.SetFloat("_CrestSharpness", CrestSharpness);
        mat.SetFloat("_FoamSpeed", FoamSpeed);
        mat.SetFloat("_FoamAmount", FoamAmount);
        mat.SetFloat("_FoamSharpness", FoamSharpness);
        mat.SetFloat("_FoamStretchX", 1f);
        mat.SetFloat("_FoamStretchY", FoamStretchY);
        mat.SetColor("_FoamColor", FoamColor);
        EditorUtility.SetDirty(mat);
    }

    const string ZonesDir = "Assets/2.Prefabs/Environment/Layouts/Zones";
    const string ZoneRootName = "Water";          // ZoneWater(수면 높이) — 존별 높이는 여기서 조절
    // ⚠️ 뒤집음(10-01): 2m 안쪽으로 줄였더니 구덩이 안에 물 가장자리가 드러나 그 너머(−19 쿼드)와 겹쳐 보였다.
    //    벽 끝에 물이 비친 진짜 원인은 수면이 높아서(−0.62) — 내리면 아래층 벽(−10.5~−11 까지)이 가린다. → 0.
    const float ZoneInset = 0f;                   // 존 물 크기 = 바닥 범위에서 변마다 이만큼 안쪽(m)
    // 존 기본 수면 — 팀장이 ZoneS_typeA 에서 Play 로 맞춘 값(10-01). 존 구덩이는 아래층 벽이 −10.5~−11 까지 내려간다.
    const float DefaultZoneWaterHeight = -4.43f;
    const string WorldUvWaterShader = "Project/FlatKit Water (World UV)";
    const string ZoneWaterName = "WaterWaves";
    const string ZoneBedName = "WaterBed";

    // 존 프리팹마다 바닥 범위 사각형 크기의 물 조각 + 물 바닥을 자식으로 넣는다(팀장 10-01 — 존 밑에 사각형으로 전부).
    // 존은 런타임에 슬롯에 생성되니 프리팹에 넣어 두면 튜토리얼·랜덤 맵 어디든 따라온다. 다시 실행하면 교체.
    [MenuItem("Tools/Rendering/Flat Kit/Water/2. Zone Patches (all zone prefabs)")]
    public static void BuildZonePatches()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[WaterPatch] Play 중에는 하지 않는다."); return; }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        var bedMat = AssetDatabase.LoadAssetAtPath<Material>(BedMaterialPath);
        if (mat == null || bedMat == null) { Debug.LogError("[WaterPatch] 물/바닥 머티리얼 없음"); return; }
        // 머티리얼 값은 덮어쓰지 않는다(인스펙터 조절 유지) — 초기값은 '3. Reset Water Look (preset)'.
        EnsureWorldUvShader(mat);

        var log = new System.Text.StringBuilder("[WaterPatch] 존 물 조각\n");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string zoneName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (zoneName == "bossroom") continue;   // 보스방은 1번 메뉴(씬 배치)

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // 존별로 조절해 둔 수면 높이는 지킨다(팀장 10-01 — 존마다 구멍 깊이가 달라 인스펙터로 맞춘다).
                Transform prevWater = root.transform.Find(ZoneRootName);
                float? keptHeight = prevWater != null && prevWater.TryGetComponent(out ZoneWater prevZw) ? prevZw.WaterHeight : (float?)null;
                foreach (Transform c in root.transform.Cast<Transform>().Where(t => t.name == ZoneRootName || t.name == ZoneWaterName || t.name == ZoneBedName).ToList())
                    Object.DestroyImmediate(c.gameObject);

                // 바닥 범위 = 이름이 floor 로 시작하는 MeshRenderer(바닥 판·트렌치·덮개)의 합. 루트 기준 좌표로.
                var floors = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r is MeshRenderer && r.gameObject.name.StartsWith("floor", System.StringComparison.OrdinalIgnoreCase)).ToList();
                if (floors.Count == 0) { log.Append($"  {zoneName}: 바닥 렌더러 없음 — 건너뜀\n"); continue; }
                Matrix4x4 toLocal = root.transform.worldToLocalMatrix;
                Bounds lb = new Bounds(toLocal.MultiplyPoint3x4(floors[0].bounds.center), Vector3.zero);
                foreach (Renderer r in floors)
                {
                    Bounds wb = r.bounds;
                    for (int i = 0; i < 8; i++)
                        lb.Encapsulate(toLocal.MultiplyPoint3x4(new Vector3(
                            (i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z)));
                }
                // 바닥 윗면 = **면적이 가장 넓은 높이**(주 바닥)의 판 윗면. 창살 덮개(0.51)가 섞이지 않게 floor_stone* 우선.
                // 🔴 최댓값을 쓰면 안 된다 — L_A·L_B·M_A 는 3m 높이 단이 있어 수면이 2.4m 로 잡혀 1층 바닥 위로 물이 튀어나왔다(10-01).
                var slabs = floors.Where(r => r.gameObject.name.StartsWith("floor_stone", System.StringComparison.OrdinalIgnoreCase)).ToList();
                float top = (slabs.Count > 0 ? slabs : floors)
                    .GroupBy(r => Mathf.Round(toLocal.MultiplyPoint3x4(r.bounds.max).y * 10f) / 10f)
                    .OrderByDescending(g => g.Sum(r => r.bounds.size.x * r.bounds.size.z))
                    .First().Key;
                // 창살(벤트 덮개)이 있으면 **창살 기준**이 가장 정확하다 — 보스방: 창살 아랫면 0.44 → 수면 −0.12(0.56 아래, 팀장 확정).
                // 존마다 판 높이(0 / 0.5)가 섞여 있어 판 기준만으로는 창살과의 거리가 달라진다.
                var grates = floors.Where(r => r.gameObject.name.StartsWith("floor_metal_trenchcover", System.StringComparison.OrdinalIgnoreCase)).ToList();
                float waterY = grates.Count > 0
                    ? grates.GroupBy(r => Mathf.Round(toLocal.MultiplyPoint3x4(r.bounds.min).y * 100f) / 100f).OrderByDescending(g => g.Count()).First().Key - BelowGrateBottom
                    : top - BelowFloorTop;
                // 존은 창살 규칙(−0.6 근처)이 너무 높았다(벽 끝에 비침) → 기본값 −4.43. 조절해 둔 값이 있으면 그것.
                waterY = keptHeight ?? DefaultZoneWaterHeight;
                // 🔴 물 범위 = **뚫린 곳(구덩이·벤트)만**(팀장 10-01 '벽 안쪽 기준'). 존 전체 사각형이면 구덩이 벽 뒤(바닥 밑)에도
                //    물이 있어, 벽 투명화(디더) 때 벽에 물이 흐르는 것처럼 보였다. 존 가장자리에는 벽이 없다(S_A: 구덩이 벽만 ±6.8).
                //    → 칸마다 위에 주 바닥 판(창살 제외)이 있으면 막힘, 없거나 창살이면 물. 바닥 타일은 4m 축 정렬이라 범위가 정확하다.
                var solid = floors.Where(r => !r.gameObject.name.StartsWith("floor_metal_trenchcover", System.StringComparison.OrdinalIgnoreCase))
                    .Select(r => { Bounds wb = r.bounds; return new Bounds(toLocal.MultiplyPoint3x4(wb.center), wb.size); })
                    .Where(b => Mathf.Abs(b.max.y - top) < 0.6f).ToList();
                // 구덩이 벽(바닥 아래로 내려가는 wall_*)이 차지하는 칸도 막힌 곳 — 물이 벽 두께만큼 벽 뒤로 들어가지 않게(정확히 벽 안쪽 면까지).
                solid.AddRange(root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.gameObject.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.bounds.min.y < top - 0.6f)
                    .Select(r => { Bounds wb = r.bounds; return new Bounds(toLocal.MultiplyPoint3x4(wb.center), wb.size); }));
                bool IsOpen(float x, float z) => !solid.Any(b => x > b.min.x && x < b.max.x && z > b.min.z && z < b.max.z);
                float w = Mathf.Max(1f, lb.size.x), d = Mathf.Max(1f, lb.size.z);

                // 존 루트 └ Water(ZoneWater: 수면 높이) ├ WaterWaves └ WaterBed
                var water = new GameObject(ZoneRootName);
                water.transform.SetParent(root.transform, false);
                water.transform.localPosition = new Vector3(lb.center.x, waterY, lb.center.z);
                water.AddComponent<ZoneWater>().WaterHeight = waterY;
                Vector3 c0 = lb.center;
                Mesh grid = CreateMaskedGridMesh(w, d, Spacing, $"{zoneName}", (lx, lz) => IsOpen(c0.x + lx, c0.z + lz));
                if (grid == null) { log.Append($"  {zoneName,-22} 뚫린 곳 없음 — 물 없음\n"); Object.DestroyImmediate(water); PrefabUtility.SaveAsPrefabAsset(root, path); continue; }
                AddChild(water, ZoneWaterName, Vector3.zero, grid, mat);
                AddChild(water, ZoneBedName, Vector3.zero, CreateBedMesh(w, d, BedSpacing), bedMat);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.Append($"  {zoneName,-22} {w:0.#}×{d:0.#}m · 중심 ({lb.center.x:0.#},{lb.center.z:0.#}) · 바닥 윗면 {top:0.##} · 창살 {grates.Count} → 수면 {waterY:0.##}{(keptHeight.HasValue ? " (조절값 유지)" : "")} · 정점 {grid.vertexCount}\n");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    // 존 바닥 범위(lb, 루트 로컬)의 네 변마다, 그 변 근처(EdgeBand 이내)에 변을 따라 길게 놓인 벽 렌더러의
    // **안쪽 면** 위치를 모아 중앙값을 쓴다. 벽이 없는 변(입구 등)은 바닥 범위 그대로. 반환 Rect 는 (x, z) 평면.
    const float EdgeBand = 2.5f;
    static Rect InnerWallRect(GameObject root, Matrix4x4 toLocal, Bounds lb)
    {
        var walls = root.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.gameObject.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(r =>
            {
                Bounds wb = r.bounds;
                Vector3 a = toLocal.MultiplyPoint3x4(wb.min), b = toLocal.MultiplyPoint3x4(wb.max);
                return new Bounds((a + b) * 0.5f, new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z)));
            }).ToList();

        float Median(System.Collections.Generic.List<float> v, float fallback)
        {
            if (v.Count == 0) return fallback;
            v.Sort();
            return v[v.Count / 2];
        }

        // +X 변: x 최대 근처, z 방향으로 긴 벽 → 안쪽 면 = 벽의 min.x
        float xMax = Median(walls.Where(b => b.max.x >= lb.max.x - EdgeBand && b.size.z > b.size.x).Select(b => b.min.x).ToList(), lb.max.x);
        float xMin = Median(walls.Where(b => b.min.x <= lb.min.x + EdgeBand && b.size.z > b.size.x).Select(b => b.max.x).ToList(), lb.min.x);
        float zMax = Median(walls.Where(b => b.max.z >= lb.max.z - EdgeBand && b.size.x > b.size.z).Select(b => b.min.z).ToList(), lb.max.z);
        float zMin = Median(walls.Where(b => b.min.z <= lb.min.z + EdgeBand && b.size.x > b.size.z).Select(b => b.max.z).ToList(), lb.min.z);
        return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
    }

    // 물 머티리얼을 월드 UV 복제 셰이더로(같은 이름 프로퍼티·키워드는 유지된다). 이미 바뀌었으면 아무것도 안 한다.
    static void EnsureWorldUvShader(Material mat)
    {
        Shader s = Shader.Find(WorldUvWaterShader);
        if (s == null) { Debug.LogError($"[WaterPatch] {WorldUvWaterShader} 없음"); return; }
        if (mat.shader == s) return;
        mat.shader = s;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }

    // 모든 존 수면을 기본값(DefaultZoneWaterHeight)으로 맞추고, 맵 큰 쿼드(Abyss/AbyssWater)도 같은 높이로 —
    // 이웃 물(존 사이 틈으로 보이는 쿼드)과 높이가 달라 '위아래 물이 다른 상태'였다(10-01).
    // 존별로 조절한 값을 **덮어쓰는** 메뉴라 2번 메뉴와 분리했다.
    [MenuItem("Tools/Rendering/Flat Kit/Water/4. Set All Zones to Default Height (overwrites)")]
    public static void SetAllZonesDefaultHeight()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[WaterPatch] Play 중에는 하지 않는다."); return; }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (mat != null) EnsureWorldUvShader(mat);
        int zones = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform w = root.transform.Find(ZoneRootName);
                if (w == null || !w.TryGetComponent(out ZoneWater zw)) continue;
                zw.WaterHeight = DefaultZoneWaterHeight;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                zones++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        bool wasLoaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(MapScenePath).isLoaded;
        var scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
        int quads = 0;
        try
        {
            // 맵 큰 쿼드 = Abyss 밑 AbyssWater(330m). 보스방 쪽 쿼드(루트 AbyssWater)는 이미 꺼 둠.
            foreach (Transform t in scene.GetRootGameObjects().Where(g => g.name == "Abyss").SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
            {
                if (t.name != "AbyssWater") continue;
                Vector3 p = t.position; p.y = DefaultZoneWaterHeight - 0.05f; t.position = p;   // 존 조각 바로 밑(겹치는 곳은 조각이 덮는다)
                quads++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (!wasLoaded && UnityEngine.SceneManagement.SceneManager.loadedSceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
        Debug.Log($"[WaterPatch] 존 {zones}개 수면 = {DefaultZoneWaterHeight} · 맵 큰 쿼드 {quads}개 = {DefaultZoneWaterHeight - 0.05f}");
    }

    static void AddChild(GameObject root, string name, Vector3 localPos, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // 물 바닥. 수심 = 펄린 노이즈로 BedMinDepth~BedMaxDepth (불규칙 얼룩). 가장자리는 테두리(바닥 윗면까지).
    // 매번 새로 만든다(값 튜닝 → 재실행). 같은 경로에 덮어쓴다.
    static Mesh CreateBedMesh(float w, float d, float spacing)
    {
        int sx = Mathf.Max(1, Mathf.CeilToInt(w / spacing)), sz = Mathf.Max(1, Mathf.CeilToInt(d / spacing));
        string path = $"{MeshDir}/WaterBed_{w:0.#}x{d:0.#}m.asset";
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/3.Materials/FlatKit/Water", "Meshes");
        int nx = sx + 1, nz = sz + 1;
        var v = new Vector3[nx * nz];
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                float px = -w * 0.5f + x / (float)sx * w, pz = -d * 0.5f + z / (float)sz * d;
                float n = Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(px * BedNoiseScale + 37.1f, pz * BedNoiseScale + 11.3f));
                float depth = Mathf.Lerp(BedMinDepth, BedMaxDepth, n);
                // 🔴 테두리 — 가장자리 한 줄을 바닥 윗면까지 세운다(불투명). 바닥 판에 옆면이 없어 벽이 투명해지면
                //    (벽 투명화 디더) 판 밑의 물이 옆으로 비쳐 '벽에 물결이 그려진 것처럼' 보였다(09-30 인게임).
                bool border = x == 0 || z == 0 || x == sx || z == sz;
                v[z * nx + x] = new Vector3(px, border ? BelowFloorTop : -depth, pz);
            }
        var mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
        if (v.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = v; mesh.triangles = GridTriangles(sx, sz);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return SaveMesh(mesh, path);
    }

    // w×d 평면, XZ, 위를 향함. UV 는 UvWorldSize(데모 27.57m)당 1 — Flat Kit 거품·굴절이 UV 기준이라 크기가 달라도 무늬 크기가 같다.
    // 탄젠트 +X(물 셰이더가 파도 법선 계산에 쓴다).
    static Mesh GetOrCreateGridMesh(float w, float d, float spacing)
    {
        int sx = Mathf.Max(1, Mathf.CeilToInt(w / spacing)), sz = Mathf.Max(1, Mathf.CeilToInt(d / spacing));
        string path = $"{MeshDir}/WaterGrid_{w:0.#}x{d:0.#}m_{sx}x{sz}.asset";
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/3.Materials/FlatKit/Water", "Meshes");
        int nx = sx + 1, nz = sz + 1;
        var v = new Vector3[nx * nz]; var uv = new Vector2[nx * nz]; var nm = new Vector3[nx * nz]; var tg = new Vector4[nx * nz];
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                float px = -w * 0.5f + x / (float)sx * w, pz = -d * 0.5f + z / (float)sz * d;
                v[i] = new Vector3(px, 0f, pz);
                uv[i] = new Vector2((px + w * 0.5f) / UvWorldSize, (pz + d * 0.5f) / UvWorldSize);
                nm[i] = Vector3.up;
                tg[i] = new Vector4(1f, 0f, 0f, 1f);
            }
        var mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
        if (v.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = v; mesh.uv = uv; mesh.normals = nm; mesh.tangents = tg; mesh.triangles = GridTriangles(sx, sz);
        // 파도 진폭만큼 위아래로 여유 — 안 주면 가장자리에서 컬링으로 깜빡인다.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(w, 2f, d));
        return SaveMesh(mesh, path);
    }

    // 뚫린 칸에만 면을 만든 격자 물(존 전용 — 존마다 모양이 달라 존 이름으로 저장). 쓰지 않는 정점은 빼서 압축한다.
    // open(lx, lz): 물 루트 기준 로컬 좌표의 칸 중심이 뚫렸는가. 뚫린 칸이 하나도 없으면 null.
    static Mesh CreateMaskedGridMesh(float w, float d, float spacing, string zoneName, System.Func<float, float, bool> open)
    {
        int sx = Mathf.Max(1, Mathf.CeilToInt(w / spacing)), sz = Mathf.Max(1, Mathf.CeilToInt(d / spacing));
        int nx = sx + 1;
        var remap = new int[nx * (sz + 1)];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        int V(int x, int z)
        {
            int i = z * nx + x;
            if (remap[i] < 0) { remap[i] = verts.Count; verts.Add(new Vector3(-w * 0.5f + x / (float)sx * w, 0f, -d * 0.5f + z / (float)sz * d)); }
            return remap[i];
        }
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                float cx = -w * 0.5f + (x + 0.5f) / sx * w, cz = -d * 0.5f + (z + 0.5f) / sz * d;
                if (!open(cx, cz)) continue;
                int a = V(x, z), b = V(x, z + 1), c = V(x + 1, z), e = V(x + 1, z + 1);
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(c); tris.Add(b); tris.Add(e);
            }
        if (tris.Count == 0) return null;

        string path = $"{MeshDir}/WaterHoles_{zoneName}.asset";
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/3.Materials/FlatKit/Water", "Meshes");
        var mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
        if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(Enumerable.Repeat(Vector3.up, verts.Count).ToList());
        mesh.SetTangents(Enumerable.Repeat(new Vector4(1f, 0f, 0f, 1f), verts.Count).ToList());
        mesh.SetUVs(0, verts.Select(p => new Vector2(p.x / UvWorldSize, p.z / UvWorldSize)).ToList());   // 셰이더는 월드 UV — 참고용
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(w, 2f, d));
        return SaveMesh(mesh, path);
    }

    static int[] GridTriangles(int sx, int sz)
    {
        int nx = sx + 1;
        var tri = new int[sx * sz * 6];
        int t = 0;
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                int i = z * nx + x;
                tri[t++] = i; tri[t++] = i + nx; tri[t++] = i + 1;
                tri[t++] = i + 1; tri[t++] = i + nx; tri[t++] = i + nx + 1;
            }
        return tri;
    }

    static Mesh SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
