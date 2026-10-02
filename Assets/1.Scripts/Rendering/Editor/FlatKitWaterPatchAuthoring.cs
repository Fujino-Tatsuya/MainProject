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
    // ⚠️ 뒤집음(10-01 9-b): 흰 마루 0.45·흰 거품 0.4 가 화면을 덮었다(팀장: 거품 압도적으로 줄여라) → 마루·거품은 데모 Pool09 값.
    //    데모의 밝은 얼룩은 거품이 아니라 바닥 경사가 만든 깊이 색 + 굴절이다 → 바닥(벽까지 거리 수심)이 그 역할을 맡는다.
    const float CrestSize = 0.102f;     // Pool09
    const float CrestSharpness = 0f;    // Pool09
    const float FoamSpeed = 0.5f;       // Pool09
    const float FoamAmount = 0.08f;     // Pool09
    const float FoamSharpness = 0.737f; // Pool09
    const float FoamStretchY = 1f;      // Pool09
    static readonly Color FoamColor = new Color(0.12348701f, 0.3490566f, 0.34481347f, 0.24705882f);   // Pool09 짙은 청록 a 0.25

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
    // 존 물 바닥(10-01 9-b): 수심 = **구덩이 벽까지의 거리** — 벽 쪽 얕음(밝은 청록 + 물가 거품) → 안쪽 깊음(검정에 가까운 청록).
    //    데모 풀장의 '가장자리 밝음 → 가운데 어두움'을 그대로 만드는 구조. 펄린(5~9m)은 가장자리 바로 안쪽이 절벽이라 중간 색이 없었다.
    //    그라데이션(Pool09) 실측: depth_fade 0.3 = 밝은 청록 · 0.6 = 중간 청록 · 1.0 = 거의 검정 (시선 방향 깊이 − 0.61) / 9.28.
    const float BedShallowDepth = 0.5f; // 벽 바로 앞 수심(m)
    const float BedDeepDepth = 7f;      // 벽에서 BedRampDistance 이상 떨어진 곳 수심(m)
    const float BedRampDistance = 3f;   // 얕음 → 깊음 경사 폭(m). 이보다 좁은 도랑은 끝까지 깊어지지 않는다
    const int BedStep = 3;              // 물 격자 3칸(≈0.48m)마다 바닥 정점
    // 바닥 테두리 = 수면 바로 아래. 수면 위(+0.62)로 세우면 테두리 경사가 벽 앞 수면 위로 튀어나온다(물이 벽 면에서 멈추므로).
    const float BedBorderBelowSurface = 0.05f;

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

    // [진단] 존 프리팹을 위에서 직교로 찍어 PNG 로(물 켠 것 · 끈 것) — 물 마스크가 실제 구덩이와 맞는지 눈으로 대조하려고.
    //    결과: 프로젝트 루트 Temp/ZoneShots/<존>_water.png · <존>_raw.png (1px = 0.05m)
    [MenuItem("Tools/Rendering/Flat Kit/Water/0d. Snapshot Zones (top-down)")]
    public static void SnapshotZones()
    {
        string outDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Temp", "ZoneShots");
        System.IO.Directory.CreateDirectory(outDir);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string zoneName = System.IO.Path.GetFileNameWithoutExtension(path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);

                var lightGo = new GameObject("L");
                SceneManager_Move(lightGo, scene);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(60f, 30f, 0f);

                var camGo = new GameObject("C");
                SceneManager_Move(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(b.size.x, b.size.z) * 0.5f + 1f;
                cam.transform.SetPositionAndRotation(new Vector3(b.center.x, b.max.y + 20f, b.center.z), Quaternion.Euler(90f, 0f, 0f));
                cam.nearClipPlane = 0.1f; cam.farClipPlane = b.size.y + 60f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f, 0f, 0.5f);
                int px = Mathf.Clamp(Mathf.CeilToInt(cam.orthographicSize * 2f / 0.05f), 256, 1024);

                Transform water = go.transform.Find(ZoneRootName);
                Transform waves = water != null ? water.Find(ZoneWaterName) : null;
                Transform bedT = water != null ? water.Find(ZoneBedName) : null;
                foreach (string mode in new[] { "water", "raw", "bedonly", "wavesonly" })
                {
                    bool withWater = mode != "raw";
                    if (water != null) water.gameObject.SetActive(withWater);
                    if (waves != null) waves.gameObject.SetActive(mode != "bedonly");
                    if (bedT != null) bedT.gameObject.SetActive(mode != "wavesonly");
                    var rt = RenderTexture.GetTemporary(px, px, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt;
                    cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, px, px), 0, 0); tex.Apply();
                    RenderTexture.active = prev; cam.targetTexture = null;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"{zoneName}_{mode}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        Debug.Log($"[WaterProbe] 존 스냅샷 → {outDir}");
    }

    // [진단] 존 중심을 게임 카메라 각도(오프셋 7,17.5,-7 · 화각 26 = MainCamera.prefab)로 찍는다 — 물 가장자리 확인용.
    //    결과: Temp/ZoneShots/<존>_cam0~4.png (0 = 물 중심, 1~4 = 네 방향)
    [MenuItem("Tools/Rendering/Flat Kit/Water/0e. Snapshot Zones (game camera)")]
    public static void SnapshotZonesGameCamera()
    {
        string outDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Temp", "ZoneShots");
        System.IO.Directory.CreateDirectory(outDir);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform water = go.transform.Find(ZoneRootName);
                if (water == null) continue;
                var lightGo = new GameObject("L"); SceneManager_Move(lightGo, scene);
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(60f, 30f, 0f);
                var camGo = new GameObject("C"); SceneManager_Move(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.fieldOfView = 26f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
                // 열린 면 존은 0·90·180·270° 로 돌려 찍는다(슬롯마다 회전 배치 — ZoneWater 가 회전별 메시를 고르는지 확인).
                string zname = System.IO.Path.GetFileNameWithoutExtension(path);
                int yawCount = EdgePitZones.Contains(zname) ? 4 : 1;
                for (int yaw = 0; yaw < yawCount; yaw++)
                {
                go.transform.rotation = Quaternion.Euler(0f, 90f * yaw, 0f);
                water.gameObject.SetActive(false); water.gameObject.SetActive(true);   // ZoneWater.OnEnable → 회전별 메시
                string tag = yawCount > 1 ? $"_y{yaw * 90}" : "";
                // 중심 + 네 방향(존 크기의 1/4) — 물 가장자리·벽·파이프가 걸리게
                Bounds wb = new Bounds(go.transform.position, Vector3.zero);
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) wb.Encapsulate(r.bounds);
                var offs = new[] { Vector2.zero, new Vector2(-0.25f, 0.25f), new Vector2(0.25f, 0.25f), new Vector2(-0.25f, -0.25f), new Vector2(0.25f, -0.25f) };
                for (int k = 0; k < offs.Length; k++)
                {
                    Vector3 target = new Vector3(wb.center.x + offs[k].x * wb.size.x, 0f, wb.center.z + offs[k].y * wb.size.z);
                    Vector3 offset = new Vector3(7f, 17.5f, -7f);
                    cam.transform.SetPositionAndRotation(target + offset, Quaternion.LookRotation(-offset.normalized, Vector3.up));
                    var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt; cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
                    RenderTexture.active = prev; cam.targetTexture = null;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"{zname}{tag}_cam{k}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
                }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        Debug.Log($"[WaterProbe] 게임 카메라 스냅샷 → {outDir}");
    }

    // 선택한 오브젝트 밑에 물 잘라내기 상자를 만든다(존 프리팹 편집 중에 — 선택이 Water 밑이면 존 루트로 올린다).
    [MenuItem("GameObject/Flat Kit Water/Trim Box (물 잘라내기 상자)", false, 10)]
    static void CreateTrimBox(MenuCommand cmd)
    {
        Transform parent = (cmd.context as GameObject)?.transform ?? Selection.activeTransform;
        while (parent != null && parent.GetComponentInParent<ZoneWater>() != null) parent = parent.parent;   // Water 는 재생성 때 지워진다
        var go = new GameObject("WaterTrimBox");
        Undo.RegisterCreatedObjectUndo(go, "Create Water Trim Box");
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(4f, 1f, 4f);
        go.AddComponent<WaterTrimBox>();
        Selection.activeGameObject = go;
    }

    // 존 프리팹·맵 씬(보스방 물)이 참조하지 않는 생성 메시(WaterHoles_* · WaterBed_* · WaterGrid_*)를 지운다.
    [MenuItem("Tools/Rendering/Flat Kit/Water/5. Delete Unused Water Meshes")]
    public static void DeleteUnusedWaterMeshes()
    {
        var roots = AssetDatabase.FindAssets("t:Prefab", new[] { ZonesDir }).Select(AssetDatabase.GUIDToAssetPath).Append(MapScenePath).ToArray();
        var used = new System.Collections.Generic.HashSet<string>(AssetDatabase.GetDependencies(roots, true));
        var removed = new System.Collections.Generic.List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            string n = System.IO.Path.GetFileName(p);
            if (!(n.StartsWith("WaterHoles_") || n.StartsWith("WaterBed_") || n.StartsWith("WaterGrid_")) || used.Contains(p)) continue;
            if (AssetDatabase.DeleteAsset(p)) removed.Add(n);
        }
        Debug.Log($"[WaterPatch] 안 쓰는 물 메시 {removed.Count}개 삭제: {string.Join(", ", removed)}");
    }

    // [진단] M_A 벽 단면(수면 높이)의 축 정렬 선 분포 — 존 바깥 벽 선이 어디인지.
    [MenuItem("Tools/Rendering/Flat Kit/Water/0g. Probe Wall Lines (M_A)")]
    public static void ProbeWallLines()
    {
        GameObject root = PrefabUtility.LoadPrefabContents($"{ZonesDir}/ZoneM_typeA.prefab");
        try
        {
            Matrix4x4 toLocal = root.transform.worldToLocalMatrix;
            Transform wt = root.transform.Find(ZoneRootName);
            float y = (wt != null && wt.TryGetComponent(out ZoneWater zw) ? zw.WaterHeight : DefaultZoneWaterHeight) + 0.05f;
            var segs = WallSectionSegments(root, toLocal, y);
            var sb = new System.Text.StringBuilder($"[WaterProbe] M_A 벽 단면 y={y:0.00} 선분 {segs.Count}\n");
            sb.Append("  z 방향 선(x 고정): " + string.Join(" · ", segs.Where(s => Mathf.Abs(s.Item1.x - s.Item2.x) < 0.05f)
                .GroupBy(s => Mathf.Round(s.Item1.x * 20f) / 20f).OrderBy(g => g.Key)
                .Select(g => $"x {g.Key:0.00}: z {g.Min(s => Mathf.Min(s.Item1.y, s.Item2.y)):0.0}~{g.Max(s => Mathf.Max(s.Item1.y, s.Item2.y)):0.0}")) + "\n");
            sb.Append("  x 방향 선(z 고정): " + string.Join(" · ", segs.Where(s => Mathf.Abs(s.Item1.y - s.Item2.y) < 0.05f)
                .GroupBy(s => Mathf.Round(s.Item1.y * 20f) / 20f).OrderBy(g => g.Key)
                .Select(g => $"z {g.Key:0.00}: x {g.Min(s => Mathf.Min(s.Item1.x, s.Item2.x)):0.0}~{g.Max(s => Mathf.Max(s.Item1.x, s.Item2.x)):0.0}")) + "\n");
            sb.Append("  x > 8.5 인 렌더러: " + string.Join(" · ", root.GetComponentsInChildren<MeshRenderer>(true)
                .Select(r => (r, b: LocalAabb(r, toLocal))).Where(t => t.b.max.x > 8.5f && t.b.max.y > y - 0.05f && (wt == null || !t.r.transform.IsChildOf(wt)))
                .GroupBy(t => System.Text.RegularExpressions.Regex.Replace(t.r.gameObject.name, @"[\s_]*\(?\d+\)?$", ""))
                .Select(g => $"{g.Key} ×{g.Count()} x {g.Min(t => t.b.min.x):0.0}~{g.Max(t => t.b.max.x):0.0} y {g.Min(t => t.b.min.y):0.0}~{g.Max(t => t.b.max.y):0.0} z {g.Min(t => t.b.min.z):0.0}~{g.Max(t => t.b.max.z):0.0}")) + "\n");
            if (wt != null)
            {
                MeshFilter mf = wt.Find(ZoneWaterName)?.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    var vs = mf.sharedMesh.vertices.Select(v => wt.localPosition + v).ToList();
                    sb.Append($"  물 정점 범위(루트 기준) x {vs.Min(v => v.x):0.00}~{vs.Max(v => v.x):0.00} · z {vs.Min(v => v.z):0.00}~{vs.Max(v => v.z):0.00}\n");
                }
            }
            Debug.Log(sb.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // [진단] 주 바닥 높이(±0.6)가 아닌데 수면보다 위에 있는 floor* 렌더러 — '막힌 칸' 규칙을 정하려고.
    [MenuItem("Tools/Rendering/Flat Kit/Water/0f. Probe Off-Level Floors")]
    public static void ProbeOffLevelFloors()
    {
        var sb = new System.Text.StringBuilder("[WaterProbe] 주 바닥 높이가 아닌 floor* (수면 −4.43 위)\n");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Matrix4x4 toLocal = root.transform.worldToLocalMatrix;
                var rows = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.gameObject.name.StartsWith("floor", System.StringComparison.OrdinalIgnoreCase))
                    .Select(r => (r, b: LocalAabb(r, toLocal)))
                    .Where(t => Mathf.Abs(t.b.max.y) >= 0.6f && t.b.max.y > DefaultZoneWaterHeight + 0.1f)
                    .GroupBy(t => System.Text.RegularExpressions.Regex.Replace(t.r.gameObject.name, @"[\s_]*\(?\d+\)?$", "") + $" y {t.b.min.y:0.0}~{t.b.max.y:0.0}")
                    .Select(g => $"{g.Key} ×{g.Count()}").ToList();
                if (rows.Count > 0) sb.Append($" {System.IO.Path.GetFileNameWithoutExtension(path)}: {string.Join(" · ", rows)}\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Debug.Log(sb.ToString());
    }

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

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
    // 구덩이가 있어도 물을 두지 않는 존(팀장 10-01 — 시작 존은 벤트 처리만 · L_C 는 안개 구역, '따로 추가 안 해도 됨').
    static readonly string[] NoWaterZones = { "ZoneS_typeStart", "ZoneL_typeC", "ZoneL_typeB" };   // L_B: 10-01 팀장 '제일 큰 존 물 없애기'
    // 존 가장자리로 이어진(= '바깥' 판정) 구덩이에도 물을 채우는 존 — 중간 크기 존 두 개의 구석 구덩이(팀장 10-01).
    static readonly string[] EdgePitZones = { "ZoneM_typeA", "ZoneM_typeB" };
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
                // 물 조각(WaterPart_N)별로 팀장이 직접 조절한 위치·크기도 지킨다 — 이름이 같으면 그대로 되돌려 놓는다.
                var keptParts = new System.Collections.Generic.Dictionary<string, (Vector3 pos, Vector3 scale)>();
                if (prevWater != null)
                    foreach (Transform p in prevWater)
                        if (p.name.StartsWith(PartPrefix)) keptParts[p.name] = (p.localPosition, p.localScale);
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
                //    → 칸마다 위에 주 바닥 판이 있으면 막힘, 없으면 물. 바닥 타일은 4m 축 정렬이라 범위가 정확하다.
                //    ⚠️ 뒤집음(10-01): 창살(벤트) 밑도 물이었으나 팀장이 **벤트 밑은 구멍으로만 흐르게 보이는 처리를 따로** 하기로 해
                //    창살도 막힌 칸으로 친다 → 물은 열린 구덩이에만. (벤트 밑 물은 디더 벽 앞에서 점무늬로 보이기도 했다.)
                //    막힌 칸은 렌더러 로컬 경계의 8모서리를 루트 기준으로 옮겨 잡는다(월드 AABB 크기를 그대로 쓰면 회전 조각에서 틀린다 — Codex 10-01).
                //    주 바닥 높이(±0.6) 판 + **더 높은 단(3m 블록)의 바닥**도 막힘 — 솟은 단 안쪽(M_A)에 안 보이는 물이 깔렸다.
                //    🔴 다리·이동 발판·크레인은 그 밑으로 물이 흘러야 한다 — 막으면 L_B 해자 다리 밑이 계단 모양으로 검게 뚫렸다(10-01).
                bool Spans(Renderer r) => new[] { "bridge", "_MV", "crane", "lamp" }.Any(k => r.gameObject.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0);
                var solid = floors
                    .Select(r => (r, b: LocalAabb(r, toLocal)))
                    .Where(t => Mathf.Abs(t.b.max.y - top) < 0.6f || (t.b.max.y >= top + 0.6f && !Spans(t.r)))
                    .Select(t => t.b).ToList();
                // 구덩이 벽(바닥 아래로 내려가는 wall_*)이 차지하는 칸도 막힌 곳 — 물이 벽 두께만큼 벽 뒤로 들어가지 않게(정확히 벽 안쪽 면까지).
                solid.AddRange(root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.gameObject.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.bounds.min.y < top - 0.6f)
                    .Select(r => LocalAabb(r, toLocal)));
                bool IsOpen(float x, float z) => !solid.Any(b => x > b.min.x && x < b.max.x && z > b.min.z && z < b.max.z);
                // 물이 넘어가면 안 되는 선 = **수면 바로 위 높이에서 자른 벽 메시의 실제 단면**(벽 디더 구멍으로 뒤의 물이 비치므로).
                // 경계 상자(AABB)는 기둥·돌출부까지 포함해 실제 벽돌 면보다 두꺼워, 물이 면 앞에서 끊기고 그 사이로 어두운 바닥이
                // 띠로 보였다(L_B). 수면 위에서 끝나는 벽은 단면이 없으니 물이 그 밑으로 들어간다(BuildWaterMask ⑤).
                var faceSegs = WallSectionSegments(root, toLocal, waterY + 0.05f);
                var wallBoxes = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.gameObject.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.bounds.min.y < top - 0.6f)
                    .Select(r => LocalAabb(r, toLocal)).ToList();
                bool InWall(float x, float z) => wallBoxes.Any(b => x > b.min.x && x < b.max.x && z > b.min.z && z < b.max.z);
                float w = Mathf.Max(1f, lb.size.x), d = Mathf.Max(1f, lb.size.z);
                // 🔴 존 경계와 이어진 빈 칸 = 맵 바깥 허공 → 물 없음(팀장 10-01 '구덩이·벤트만'). 바깥까지 깔면 4m 타일 계단 경계가 탑다운에서 그대로 보였다.
                Vector3 c0 = lb.center;
                bool[,] mask = BuildWaterMask(w, d, Spacing, (lx, lz) => IsOpen(c0.x + lx, c0.z + lz), (lx, lz) => InWall(c0.x + lx, c0.z + lz),
                    EdgePitZones.Contains(zoneName),
                    faceSegs.Select(s => (s.Item1 - new Vector2(c0.x, c0.z), s.Item2 - new Vector2(c0.x, c0.z))).ToList(),
                    out float[,] wallDist, out int outsideCells, out int waterCells);
                // 위에서 안 보이는 물 덩어리 제거 — 연결된 물 칸이 **전부** 수면 위 렌더러(AABB)에 덮여 있으면 지운다.
                // (솟은 단 안·덮인 도랑 밑 등 — M_A·M_B 에 화면에 안 나오는 물이 2,000칸대로 깔렸다. 열린 구덩이는 위가 비어 남는다.)
                if (mask != null)
                {
                    // 이전 Water 자식은 위에서 이미 지웠다. 입자·데칼은 경계가 커서 전부 덮은 것으로 오판할 수 있어 MeshRenderer 만.
                    var covers = root.GetComponentsInChildren<MeshRenderer>(true)
                        .Select(r => LocalAabb(r, toLocal))
                        .Where(b => b.max.y > waterY + 0.1f)
                        .Select(b => new Rect(b.min.x - c0.x, b.min.z - c0.z, b.size.x, b.size.z)).ToList();
                    mask = DropHiddenWater(mask, w, d, covers, ref waterCells);
                }
                // 존 바깥 벽 선 밖의 물 제거 — 물 범위 기준인 바닥 판 경계가 바깥 벽보다 ≈0.45m 더 나가 있어, ㄱ자 존의 빈 모서리(M_A)
                // 물이 벽 선 밖으로 띠처럼 삐져나와 보였다(팀장 10-01 Play). 카메라 회전이 고정이라 시점과 무관하게 벽 선에서 자르면 된다.
                // 경계 = 수면 높이에서 자른 벽 단면 전체의 최소·최대 XZ(= 바깥 벽의 바깥 면).
                int envClipped = 0;
                Rect? wallEnvelope = null;   // 물 루트(c0) 기준 — 물 밑 바닥도 이 선에서 자른다(회전 0° 기준)
                Rect[] yawEnvelopes = null;  // 회전 0·90·180·270° 별 카메라 쪽 변 한계(열린 면 존만)
                // 열린 면(벽 없는 존 경계)이 있는 존에만 — 사방이 벽인 구덩이(S_A)는 카메라 쪽 가장자리를 벽·바닥이 가려 필요 없고,
                // 맨 바깥 벽이 곧 구덩이 벽이라 적용하면 구덩이 물이 시차만큼 깎였다(10-01 S_A 973칸).
                if (ClipOpenEdges && mask != null && faceSegs.Count > 0 && EdgePitZones.Contains(zoneName))
                {
                    // 🔴 기준 = 존 바깥 벽의 **안쪽 면**(팀장 10-01 — 벽이 투명해져도 물이 벽 선 밖으로 안 보이게). 바깥 벽은 수면까지
                    //    안 내려오는 경우가 많아(M_A) 수면 높이 단면 대신 **바닥 바로 아래(top − 0.3)** 단면도 함께 읽는다.
                    var rimSegs = WallSectionSegments(root, toLocal, top - 0.3f);
                    rimSegs.AddRange(faceSegs);
                    float ex0 = InnerWallLine(rimSegs, true, false), ex1 = InnerWallLine(rimSegs, true, true);
                    float ez0 = InnerWallLine(rimSegs, false, false), ez1 = InnerWallLine(rimSegs, false, true);
                    // 🔴 시차 보정 — 카메라는 회전 고정(오프셋 CameraOffset)이라, 바닥(top)보다 (top − 수면) 아래에 있는 물 가장자리는
                    //    화면에서 카메라 쪽으로 depth × (오프셋 XZ / 오프셋 Y) 만큼 밀려 보인다(4.43m → 1.77m). 카메라를 향한 열린 면은
                    //    그만큼 안쪽으로 당겨야 화면에서 물 가장자리가 바닥 모서리 선과 겹친다 — 안 그러면 벽 바깥에 물·검은 띠가 붙어 보였다(M_A, 10-01).
                    //    반대쪽 면은 시차가 안쪽으로 생겨 그대로 둔다.
                    //    ⚠️ 뒤집음(10-01 팀장 '너무 줄였다 — 벽 쪽에서 물이 갑자기 끊긴다'): 깊이를 바닥(top) 기준으로 잡으면 1.77m 를
                    //    당겨 과했다. 화면에서 물 가장자리가 맞춰야 할 선은 **그 변 바깥 벽의 아랫단** — 깊이 = 벽 아랫단 − 수면.
                    var sideWalls = root.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(r => r.gameObject.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.bounds.max.y > waterY)   // 쌓인 벽 조각 전부
                        .Select(r => LocalAabb(r, toLocal)).ToList();
                    float SideDepth(System.Func<Bounds, bool> onSide)
                    {
                        // 수면 위에서 끝나는 벽(= 밑으로 물이 비칠 수 있는 벽)의 아랫단 = 쌓인 조각 중 가장 깊은 아랫단(벽 더미의 바닥).
                        // 수면까지 내려오는 벽(구덩이 벽)은 물을 가리므로 제외. 해당 변에 벽이 전혀 없으면 바닥 모서리(top)가 기준.
                        var ws = sideWalls.Where(onSide).ToList();
                        var hanging = ws.Where(b => b.min.y > waterY + 0.05f).ToList();
                        float bottom = hanging.Count > 0 ? hanging.Min(b => b.min.y) : (ws.Count > 0 ? waterY : top);
                        return Mathf.Max(0f, bottom - waterY);
                    }
                    // ⚠️ 뒤집음(팀장 10-01 '처음처럼 꽉 채우고 맵 밖 비주얼만 없애라'): 맵 밖으로 비치는 건 **카메라를 향한 변**뿐이다.
                    //    먼 쪽 변은 자르지 않는다(처음 범위 그대로). (무한대는 Rect 에서 −∞+∞ = NaN 이 되므로 큰 값으로.)
                    // 🔴 같은 존 프리팹이 슬롯마다 **0·90·180·270° 로 회전 배치**된다(Stage1·StageTutorial의 ZoneSlot.Rotations — 튜토리얼 M_A 180°).
                    //    프리팹 기준 +x·−z 를 카메라 쪽으로 보고 잘랐더니 180° 배치에선 그 변이 먼 쪽이 돼 덜 차고, 안 자른 변이 카메라 쪽이 돼
                    //    띠가 보였다(팀장 10-01 '두 문제만 합쳤다'). → **회전 4가지마다** 존 로컬 카메라 방향으로 카메라 쪽 변을 따로 계산해
                    //    메시를 4벌 만들고, ZoneWater 가 런타임에 존의 실제 회전으로 고른다(MapContentSpawner: Euler(0, 90·YawSteps, 0)).
                    float exIn1 = ex1, exIn0 = ex0, ezIn1 = ez1, ezIn0 = ez0;
                    float dXp = SideDepth(b => b.max.x >= exIn1 - 0.1f && b.min.x >= exIn1 - 1.6f);
                    float dXn = SideDepth(b => b.min.x <= exIn0 + 0.1f && b.max.x <= exIn0 + 1.6f);
                    float dZp = SideDepth(b => b.max.z >= ezIn1 - 0.1f && b.min.z >= ezIn1 - 1.6f);
                    float dZn = SideDepth(b => b.min.z <= ezIn0 + 0.1f && b.max.z <= ezIn0 + 1.6f);
                    yawEnvelopes = new Rect[4];
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 lo = Quaternion.Euler(0f, -90f * k, 0f) * CameraOffset;   // 존 로컬에서 본 카메라 오프셋
                        float kx = OpenEdgeParallaxScale * lo.x / lo.y, kz = OpenEdgeParallaxScale * lo.z / lo.y;
                        float x0 = -1e5f, x1 = 1e5f, z0 = -1e5f, z1 = 1e5f;
                        if (kx > 0.01f) x1 = exIn1 - kx * dXp; else if (kx < -0.01f) x0 = exIn0 - kx * dXn;
                        if (kz > 0.01f) z1 = ezIn1 - kz * dZp; else if (kz < -0.01f) z0 = ezIn0 - kz * dZn;
                        yawEnvelopes[k] = Rect.MinMaxRect(x0 - c0.x, z0 - c0.z, x1 - c0.x, z1 - c0.z);
                    }
                    wallEnvelope = yawEnvelopes[0];
                }
                // 팀장이 존 프리팹에 놓은 잘라내기 상자(WaterTrimBox) 안의 물 칸을 지운다 — 열린 모서리·벽 투명화 때 비치는 곳 다듬기.
                var trims = root.GetComponentsInChildren<WaterTrimBox>(true);
                int trimmed = 0;
                if (mask != null && trims.Length > 0)
                {
                    int tsx = mask.GetLength(0), tsz = mask.GetLength(1);
                    for (int z = 0; z < tsz; z++)
                        for (int x = 0; x < tsx; x++)
                        {
                            if (!mask[x, z]) continue;
                            Vector3 wp = root.transform.TransformPoint(new Vector3(c0.x - w * 0.5f + (x + 0.5f) / tsx * w, waterY, c0.z - d * 0.5f + (z + 0.5f) / tsz * d));
                            if (trims.Any(t => t.Contains(wp))) { mask[x, z] = false; trimmed++; }
                        }
                    waterCells -= trimmed;
                    if (waterCells <= 0) mask = null;
                }

                // 존 루트 └ Water(ZoneWater: 수면 높이) ├ WaterWaves └ WaterBed
                var water = new GameObject(ZoneRootName);
                water.transform.SetParent(root.transform, false);
                water.transform.localPosition = new Vector3(lb.center.x, waterY, lb.center.z);
                water.AddComponent<ZoneWater>().WaterHeight = waterY;
                Material wallSrc = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
                Material wallWet = GetOrCreateWetWallMaterial(wallSrc);
                SwapMaterial(root, wallWet, wallSrc, null);   // 재실행 대비 — 먼저 전부 원본으로 되돌린다
                if (NoWaterZones.Contains(zoneName)) { log.Append($"  {zoneName,-22} 물 빼기로 한 존(팀장) — 물 없음\n"); Object.DestroyImmediate(water); PrefabUtility.SaveAsPrefabAsset(root, path); continue; }
                if (mask == null) { log.Append($"  {zoneName,-22} 둘러싸인 구덩이 없음(바깥 {outsideCells}칸 제외) — 물 없음\n"); Object.DestroyImmediate(water); PrefabUtility.SaveAsPrefabAsset(root, path); continue; }
                Mesh grid, bed;
                if (yawEnvelopes != null)
                {
                    // 회전별 4벌 — 0° 는 기존 이름(GUID 유지), 나머지는 _r1~_r3. 기본(프리팹에 보이는 것) = 0°.
                    var grids = new Mesh[4]; var beds = new Mesh[4];
                    for (int k = 0; k < 4; k++)
                    {
                        bool[,] mk = ClipToRect(mask, w, d, yawEnvelopes[k], out int clippedK);
                        if (k == 0) envClipped = clippedK;
                        string suffix = k == 0 ? "" : $"_r{k}";
                        grids[k] = CreateMaskedGridMesh(w, d, $"{zoneName}{suffix}", mk);
                        beds[k] = grids[k] != null ? CreateMaskedBedMesh(w, d, $"{zoneName}{suffix}", mk, wallDist, yawEnvelopes[k]) : null;
                    }
                    grid = grids[0]; bed = beds[0];
                    water.GetComponent<ZoneWater>().SetYawMeshes(grids, beds);
                }
                else
                {
                    grid = null; bed = null;
                }
                int partCount = 0;
                if (yawEnvelopes != null)
                {
                    AddChild(water, ZoneWaterName, Vector3.zero, grid, mat);
                    AddChild(water, ZoneBedName, Vector3.zero, bed, bedMat);
                }
                else
                {
                    // 🔴 떨어진 물 덩어리마다 오브젝트 하나(WaterPart_N └ WaterWaves · WaterBed) — 팀장 10-01 '존 안 물이 꼭 하나여야 하나?
                    //    왼쪽·오른쪽을 따로 조절하면 된다'. 한 덩어리를 자르면 다른 덩어리도 잘리던 문제(M_A 모서리 둘)를 없앤다.
                    //    조각 원점 = 그 덩어리 범위 중심 → Transform 크기(Scale X/Z)를 줄이면 그 중심 쪽으로 줄어든다. 물·거품은 월드 UV 라 무늬가 늘지 않는다.
                    //    이름은 중심 좌표(z, x) 순서로 매겨 재생성해도 같은 덩어리가 같은 이름을 받는다.
                    var parts = SplitComponents(mask, w, d);
                    for (int pi = 0; pi < parts.Count; pi++)
                    {
                        (bool[,] pm, Vector2 ctr) = parts[pi];
                        string partName = $"{PartPrefix}{pi}";
                        Mesh pg = CreateMaskedGridMesh(w, d, $"{zoneName}_p{pi}", pm, ctr);
                        if (pg == null) continue;
                        Mesh pb = CreateMaskedBedMesh(w, d, $"{zoneName}_p{pi}", pm, wallDist, null, ctr);
                        var part = new GameObject(partName);
                        part.transform.SetParent(water.transform, false);
                        part.transform.localPosition = new Vector3(ctr.x, 0f, ctr.y);
                        if (keptParts.TryGetValue(partName, out var kept)) { part.transform.localPosition = kept.pos; part.transform.localScale = kept.scale; }
                        AddChild(part, ZoneWaterName, Vector3.zero, pg, mat);
                        AddChild(part, ZoneBedName, Vector3.zero, pb, bedMat);
                        if (grid == null) { grid = pg; bed = pb; }
                        partCount++;
                    }
                }

                // 물과 맞닿는 벽·파이프 = 바닥 아래로 내려가고(min.y < 바닥−0.6) XZ 가 물 칸(±WetMargin)에 걸치는 렌더러 → 수면용 머티리얼.
                int cellsX = mask.GetLength(0), cellsZ = mask.GetLength(1);
                bool TouchesWater(Renderer r)
                {
                    Bounds rb = LocalAabb(r, toLocal);
                    if (rb.min.y > top - 0.6f) return false;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((rb.min.x - WetMargin - c0.x + w * 0.5f) / w * cellsX));
                    int x1 = Mathf.Min(cellsX - 1, Mathf.FloorToInt((rb.max.x + WetMargin - c0.x + w * 0.5f) / w * cellsX));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((rb.min.z - WetMargin - c0.z + d * 0.5f) / d * cellsZ));
                    int z1 = Mathf.Min(cellsZ - 1, Mathf.FloorToInt((rb.max.z + WetMargin - c0.z + d * 0.5f) / d * cellsZ));
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                            if (mask[x, z]) return true;
                    return false;
                }
                int wetSlots = SwapMaterial(root, wallSrc, wallWet, TouchesWater);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.Append($"  {zoneName,-22} {w:0.#}×{d:0.#}m · 바닥 윗면 {top:0.##} → 수면 {waterY:0.##}{(keptHeight.HasValue ? " (조절값 유지)" : "")} · 물 {waterCells}칸 · 바깥 제외 {outsideCells}칸 · 물 조각 {partCount}개 · 디더 끈 슬롯 {wetSlots}{(envClipped > 0 ? $" · 벽 선 밖 {envClipped}칸 제거" : "")}{(wallEnvelope.HasValue ? $" · 카메라 쪽 변 한계(벽 안쪽 면−시차) x {Lim(wallEnvelope.Value.xMin + c0.x)}~{Lim(wallEnvelope.Value.xMax + c0.x)} z {Lim(wallEnvelope.Value.yMin + c0.z)}~{Lim(wallEnvelope.Value.yMax + c0.z)}" : "")}{(trims.Length > 0 ? $" · 잘라내기 상자 {trims.Length}개로 {trimmed}칸 제거" : "")}\n");
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

    // ── 물과 맞닿는 벽·파이프의 디더 끄기 (10-01 팀장: '이 디더 무조건 잡아야 함' · Codex 교차검증) ──
    // 맵 공용 Generic_01_A(SVN)는 바닥 아래로 갈수록 화면공간 디더로 사라진다(_WallOccZoneBaseY −13 · FadeHeight 13 → 수면 −4.43 에서
    // 불투명도 0.66). 밝은 물 앞에서는 그 구멍이 점무늬 잡음이 된다. 물 없는 구덩이(안개)는 이 '심연' 연출을 유지한다.
    // → 원본을 부모로 둔 **머티리얼 변형**(git)에서 _WallOccZoneFadeHeight 만 0(= 높이 마스크 항상 1, WallTransparencyDither.hlsl)으로
    //   덮어쓰고, 물 칸에 걸치는 렌더러 슬롯만 존 프리팹에서 교체한다. 프리팹에 구워져 런타임 처리·NGO 순서와 무관.
    //   MPB 는 SRP Batcher 에서 빠지고 WallTransparencyGroup 이 같은 속성을 인스턴스에 써서 경쟁하므로 쓰지 않는다(Codex).
    //   수면 아래 벽은 물(불투명 출력)과 물 밑 바닥이 가리므로 디더를 아예 꺼도 된다 — 수면 높이를 바꿔도 다시 계산할 것 없음.
    const string WallMaterialPath = "Assets/50.Art/Environment/Materials/UsedInMap/Shared/Atlases/Generic_01_A.mat";
    const string WetWallMaterialPath = "Assets/3.Materials/FlatKit/Water/Generic_01_A_Wet.mat";
    const float WetMargin = 0.5f;   // 물 칸에서 이만큼(m) 안에 걸치면 '맞닿음' — 벽 두께·단면 오차 흡수

    static Material GetOrCreateWetWallMaterial(Material src)
    {
        var wet = AssetDatabase.LoadAssetAtPath<Material>(WetWallMaterialPath);
        if (wet == null)
        {
            wet = new Material(src) { name = "Generic_01_A_Wet" };
            AssetDatabase.CreateAsset(wet, WetWallMaterialPath);
        }
        if (wet.parent != src) wet.parent = src;   // 변형 — 원본의 다른 값 변경은 따라온다
        wet.SetFloat("_WallOccZoneFadeHeight", 0f);
        EditorUtility.SetDirty(wet);
        return wet;
    }

    // root 아래(물 오브젝트 제외) 렌더러 슬롯 중 from 을 to 로. pick 이 null 이면 전부. 바꾼 슬롯 수.
    static int SwapMaterial(GameObject root, Material from, Material to, System.Func<Renderer, bool> pick)
    {
        int n = 0;
        Transform waterRoot = root.transform.Find(ZoneRootName);
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (waterRoot != null && r.transform.IsChildOf(waterRoot)) continue;
            Material[] mats = r.sharedMaterials;
            bool hit = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == from) { hit = true; break; }
            if (!hit || (pick != null && !pick(r))) continue;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == from) { mats[i] = to; n++; }
            r.sharedMaterials = mats;
        }
        return n;
    }

    // 렌더러 로컬 경계 8모서리 → 루트 기준 AABB.
    static Bounds LocalAabb(Renderer r, Matrix4x4 toLocal)
    {
        Bounds b = r.localBounds;
        Matrix4x4 m = toLocal * r.localToWorldMatrix;
        var o = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            o.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
        return o;
    }

    // 게임 카메라의 대상 기준 오프셋(MainCamera.prefab 7, 17.5, −7 — CameraTargetSwitcher 가 이 방향으로 회전 고정). 시차 보정에 쓴다.
    static readonly Vector3 CameraOffset = new Vector3(7f, 17.5f, -7f);
    // ⚠️ 뒤집음(팀장 10-01 '자른 부분 다시 채워라'): 열린 면 자르기(벽 안쪽 면·시차·회전별 메시)를 끈다 — 물은 존 경계까지 꽉.
    //    코드는 남겨 둔다(true 로 되살림). 꺼져 있으면 회전별 메시도 만들지 않는다.
    const bool ClipOpenEdges = false;
    // 열린 면 시차 보정 비율(0 = 안 당김 · 1 = 계산값 전부). 🔴 M_A 오른쪽 변은 존 프리팹 안에 바깥 벽이 없어(바깥 벽돌 벽은 프리팹 밖)
    // 바닥 높이 기준 계산값 1.77m 를 전부 당기면 '너무 줄여 벽 쪽에서 물이 끊긴다'(팀장 10-01) → 절반부터. 화면 보고 이 값만 조절.
    //    ⚠️ 뒤집음(10-01): '너무 줄였다'의 실제 원인은 존 회전 배치(180° 튜토리얼 M_A)였다 — 회전별 메시로 풀었으니 다시 1.
    const float OpenEdgeParallaxScale = 1f;
    const float UnderhangDistance = 1f; // 벽 상자 안에서 물을 벽 면까지 넓히는 최대 거리(m) — ⑤
    const int SlitCloseCells = 2;  // ≈0.32m — 바닥 타일 경계 사이 틈(한두 칸)을 막아, 틈으로 구덩이가 바깥과 이어지지 않게
    // ⚠️ 뒤집음(10-01): 물 가장자리를 벽 속으로 0.32m 밀어 넣었더니(EdgeTuck) 벽 뒤에 물이 생겼다. 구덩이 벽 머티리얼
    //    (Generic_01_A, SVN)은 바닥 아래로 갈수록 디더로 사라지게 돼 있어(_WallOccZoneBaseY −13 · FadeHeight 13 → 수면 −4.43 에서
    //    불투명도 0.66) 벽 구멍으로 뒤의 밝은 물이 비쳐 가장자리가 지직거렸다(팀장 Play). → 물은 **벽 안쪽 면에서 멈춘다**(막힌 칸 제외).

    // 물 칸 = **둘러싸인** 뚫린 칸. 격자 테두리에서 뚫린 칸을 따라 닿는 곳은 맵 바깥으로 보고 뺀다.
    // ① 막힌 칸을 SlitCloseCells 만큼 넓혀 좁은 틈을 닫고 ② 테두리에서 채우기 ③ 안 닿은 칸 = 구덩이
    // ④ ①에서 깎인 만큼 다시 넓히고 원래 막힌 칸은 뺀다(물 = 벽 안쪽 면까지). wallDist = 각 칸에서 구덩이 벽(원래 막힌 칸)까지 거리(m).
    // 물 칸이 하나도 없으면 null.
    const string PartPrefix = "WaterPart_";

    // 물 칸을 4방향 연결 덩어리로 나눈다. 덩어리별 (그 덩어리만 남긴 마스크, 범위 중심(물 루트 기준 XZ)), 중심 (z, x) 순.
    static System.Collections.Generic.List<(bool[,], Vector2)> SplitComponents(bool[,] mask, float w, float d)
    {
        int sx = mask.GetLength(0), sz = mask.GetLength(1);
        var seen = new bool[sx, sz];
        var result = new System.Collections.Generic.List<(bool[,], Vector2)>();
        var stack = new System.Collections.Generic.Stack<Vector2Int>();
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (!mask[x, z] || seen[x, z]) continue;
                var pm = new bool[sx, sz];
                int x0 = x, x1 = x, z0 = z, z1 = z;
                stack.Push(new Vector2Int(x, z)); seen[x, z] = true;
                while (stack.Count > 0)
                {
                    Vector2Int p = stack.Pop(); pm[p.x, p.y] = true;
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.y); z1 = Mathf.Max(z1, p.y);
                    foreach (Vector2Int dv in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                    {
                        int nx = p.x + dv.x, nz = p.y + dv.y;
                        if (nx < 0 || nz < 0 || nx >= sx || nz >= sz || seen[nx, nz] || !mask[nx, nz]) continue;
                        seen[nx, nz] = true; stack.Push(new Vector2Int(nx, nz));
                    }
                }
                var ctr = new Vector2(-w * 0.5f + (x0 + x1 + 1) * 0.5f / sx * w, -d * 0.5f + (z0 + z1 + 1) * 0.5f / sz * d);
                result.Add((pm, ctr));
            }
        return result.OrderBy(r => Mathf.Round(r.Item2.y)).ThenBy(r => Mathf.Round(r.Item2.x)).ToList();
    }

    // 물 루트 기준 사각형 밖의 물 칸을 뺀 사본. 원본은 그대로.
    static bool[,] ClipToRect(bool[,] mask, float w, float d, Rect r, out int clipped)
    {
        int sx = mask.GetLength(0), sz = mask.GetLength(1);
        var o = (bool[,])mask.Clone();
        clipped = 0;
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (!o[x, z]) continue;
                float px = -w * 0.5f + (x + 0.5f) / sx * w, pz = -d * 0.5f + (z + 0.5f) / sz * d;
                if (!r.Contains(new Vector2(px, pz))) { o[x, z] = false; clipped++; }
            }
        return o;
    }

    static string Lim(float v) => Mathf.Abs(v) > 1e4f ? "끝" : v.ToString("0.00");   // 로그용 — 자르지 않는 변

    // 존 한 변(axisX: x 고정 선 = 좌우 변 / max: + 쪽)의 바깥 벽 **안쪽 면** 좌표. 그 변과 평행한 단면 선 중 가장 바깥 = 바깥 면,
    // 그보다 1.5m 이내 안쪽의 다음 선 = 안쪽 면(벽 두께). 다음 선이 없으면 바깥 면.
    static float InnerWallLine(System.Collections.Generic.List<(Vector2, Vector2)> segs, bool axisX, bool max)
    {
        var lines = segs.Where(s => axisX ? Mathf.Abs(s.Item1.x - s.Item2.x) < 0.05f : Mathf.Abs(s.Item1.y - s.Item2.y) < 0.05f)
            .Select(s => axisX ? s.Item1.x : s.Item1.y).ToList();
        if (lines.Count == 0)
            return max ? segs.Max(s => axisX ? Mathf.Max(s.Item1.x, s.Item2.x) : Mathf.Max(s.Item1.y, s.Item2.y))
                       : segs.Min(s => axisX ? Mathf.Min(s.Item1.x, s.Item2.x) : Mathf.Min(s.Item1.y, s.Item2.y));
        float outer = max ? lines.Max() : lines.Min();
        var inner = lines.Where(v => max ? v < outer - 0.05f && v > outer - 1.5f : v > outer + 0.05f && v < outer + 1.5f).ToList();
        return inner.Count == 0 ? outer : (max ? inner.Max() : inner.Min());
    }

    // 이름에 wall 이 든 메시를 높이 y(루트 기준)에서 자른 단면 선분들(루트 기준 XZ).
    static System.Collections.Generic.List<(Vector2, Vector2)> WallSectionSegments(GameObject root, Matrix4x4 toLocal, float y)
    {
        var segs = new System.Collections.Generic.List<(Vector2, Vector2)>();
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.gameObject.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (mf.transform.parent != null && mf.transform.parent.name == ZoneRootName) continue;
            Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
            Vector3[] v = mf.sharedMesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            int[] t = mf.sharedMesh.triangles;
            var pts = new System.Collections.Generic.List<Vector2>(3);
            for (int k = 0; k < t.Length; k += 3)
            {
                pts.Clear();
                for (int e = 0; e < 3; e++)
                {
                    Vector3 a = v[t[k + e]], b = v[t[k + (e + 1) % 3]];
                    if ((a.y - y) * (b.y - y) > 0f || Mathf.Approximately(a.y, b.y)) continue;
                    float f = (y - a.y) / (b.y - a.y);
                    pts.Add(new Vector2(Mathf.Lerp(a.x, b.x, f), Mathf.Lerp(a.z, b.z, f)));
                }
                if (pts.Count >= 2) segs.Add((pts[0], pts[1]));
            }
        }
        return segs;
    }

    static bool[,] BuildWaterMask(float w, float d, float spacing, System.Func<float, float, bool> open, System.Func<float, float, bool> inWall,
        bool includeOutside, System.Collections.Generic.List<(Vector2, Vector2)> faceSegs,
        out float[,] wallDist, out int outsideCells, out int waterCells)
    {
        int sx = Mathf.Max(1, Mathf.CeilToInt(w / spacing)), sz = Mathf.Max(1, Mathf.CeilToInt(d / spacing));
        var blocked = new bool[sx, sz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                blocked[x, z] = !open(-w * 0.5f + (x + 0.5f) / sx * w, -d * 0.5f + (z + 0.5f) / sz * d);
        bool[,] closed = Dilate(blocked, SlitCloseCells);

        var outside = new bool[sx, sz];
        var queue = new System.Collections.Generic.Queue<Vector2Int>();
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                if ((x == 0 || z == 0 || x == sx - 1 || z == sz - 1) && !closed[x, z]) { outside[x, z] = true; queue.Enqueue(new Vector2Int(x, z)); }
        var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            foreach (Vector2Int dv in dirs)
            {
                int nx = p.x + dv.x, nz = p.y + dv.y;
                if (nx < 0 || nz < 0 || nx >= sx || nz >= sz || outside[nx, nz] || closed[nx, nz]) continue;
                outside[nx, nz] = true; queue.Enqueue(new Vector2Int(nx, nz));
            }
        }

        var interior = new bool[sx, sz];
        bool any = false;
        outsideCells = 0;
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (outside[x, z]) outsideCells++;
                interior[x, z] = !closed[x, z] && (includeOutside || !outside[x, z]);
                any |= interior[x, z];
            }
        wallDist = null; waterCells = 0;
        if (!any) return null;
        // 폭 ≈1m 미만의 가는 띠 제거(열기 = 깎았다가 다시 넓힘) — 존 가장자리를 따라 바닥 판 사이 틈이 물 띠로 남았다(M_A 좌우, 10-01).
        //   정사각 깎기·넓히기라 4m 단위 구덩이 모양은 그대로 돌아온다.
        {
            const int ThinCells = 3;
            bool[,] inv = new bool[sx, sz];
            for (int z = 0; z < sz; z++) for (int x = 0; x < sx; x++) inv[x, z] = !interior[x, z];
            bool[,] eroded = Dilate(inv, ThinCells);
            for (int z = 0; z < sz; z++) for (int x = 0; x < sx; x++) eroded[x, z] = !eroded[x, z];
            bool[,] opened = Dilate(eroded, ThinCells);
            any = false;
            for (int z = 0; z < sz; z++) for (int x = 0; x < sx; x++) { interior[x, z] &= opened[x, z]; any |= interior[x, z]; }
            if (!any) return null;
        }

        // 구덩이 원래 모양(닫기로 깎인 가장자리 복원, 막힌 칸은 제외) — 벽까지 거리의 기준
        bool[,] pit = Dilate(interior, SlitCloseCells);
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                pit[x, z] &= !blocked[x, z];
        // ⑤ **벽 상자 안에서만** 벽의 실제 단면(수면 높이)까지 넓힌다 — 벽 상자(AABB)는 기둥·돌출부 때문에 벽돌 면보다 두꺼워,
        //    물이 상자 끝에서 멈추면 벽 면과 물 사이로 어두운 바닥이 띠로 보였다(L_B). 단면이 없는(수면 위에서 끝나는) 벽은 두께만큼만.
        //    ⚠️ 뒤집음(10-01): 처음엔 바닥 판 밑으로 3m 까지 넓혔다 → 디더 벽 구멍 너머로 물이 청록 점으로 비쳤다(벽 구멍은 원래 검은 심연 연출).
        bool[,] water = (bool[,])pit.Clone();
        {
            // 벽 단면이 지나는 칸 = 넘지 못하는 칸. 선분을 칸 반 크기 간격으로 찍는다.
            var face = new bool[sx, sz];
            float cw = w / sx, cd = d / sz;
            foreach ((Vector2 a, Vector2 b) in faceSegs)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (Mathf.Min(cw, cd) * 0.5f)));
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)n);
                    // 🔴 벽은 4m 격자 = 칸 경계 위에 정확히 놓인다 → 한쪽 칸에만 찍히면 벽 쪽 첫 칸이 비어 물이 새어 나갔다(S_A).
                    //    경계 양쪽(±0.3칸)을 모두 찍는다.
                    for (int oz = -1; oz <= 1; oz += 2)
                        for (int ox = -1; ox <= 1; ox += 2)
                        {
                            int fx = Mathf.FloorToInt((p.x + ox * 0.3f * cw + w * 0.5f) / cw), fz = Mathf.FloorToInt((p.y + oz * 0.3f * cd + d * 0.5f) / cd);
                            if (fx >= 0 && fz >= 0 && fx < sx && fz < sz) face[fx, fz] = true;
                        }
                }
            }
            // 구덩이 칸이 단면에 걸쳤으면 그 칸까지는 물(벽 면 바로 앞) — 넓히기만 막는다.
            int steps = Mathf.CeilToInt(UnderhangDistance / Mathf.Min(w / sx, d / sz));
            var frontier = new System.Collections.Generic.List<Vector2Int>();
            for (int z = 0; z < sz; z++)
                for (int x = 0; x < sx; x++)
                    if (water[x, z]) frontier.Add(new Vector2Int(x, z));
            for (int s = 0; s < steps && frontier.Count > 0; s++)
            {
                var next = new System.Collections.Generic.List<Vector2Int>();
                foreach (Vector2Int p in frontier)
                    foreach (Vector2Int dv in dirs)
                    {
                        int nx = p.x + dv.x, nz = p.y + dv.y;
                        if (nx < 0 || nz < 0 || nx >= sx || nz >= sz || water[nx, nz] || !blocked[nx, nz]) continue;
                        if (!inWall(-w * 0.5f + (nx + 0.5f) / sx * w, -d * 0.5f + (nz + 0.5f) / sz * d)) continue;   // 벽 상자 안에서만
                        water[nx, nz] = true;
                        if (!face[nx, nz]) next.Add(new Vector2Int(nx, nz));   // 단면 칸은 물로 채우되(면까지) 그 너머로는 안 간다
                    }
                frontier = next;
            }
        }
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                if (water[x, z]) waterCells++;

        // 2패스 챔퍼 거리(칸 단위, 대각 √2) → m
        float cell = Mathf.Min(w / sx, d / sz);
        var dist = new float[sx, sz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                dist[x, z] = pit[x, z] ? float.MaxValue : 0f;
        const float D1 = 1f, D2 = 1.41421356f;
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                float v = dist[x, z];
                if (x > 0) v = Mathf.Min(v, dist[x - 1, z] + D1);
                if (z > 0) v = Mathf.Min(v, dist[x, z - 1] + D1);
                if (x > 0 && z > 0) v = Mathf.Min(v, dist[x - 1, z - 1] + D2);
                if (x < sx - 1 && z > 0) v = Mathf.Min(v, dist[x + 1, z - 1] + D2);
                dist[x, z] = v;
            }
        for (int z = sz - 1; z >= 0; z--)
            for (int x = sx - 1; x >= 0; x--)
            {
                float v = dist[x, z];
                if (x < sx - 1) v = Mathf.Min(v, dist[x + 1, z] + D1);
                if (z < sz - 1) v = Mathf.Min(v, dist[x, z + 1] + D1);
                if (x < sx - 1 && z < sz - 1) v = Mathf.Min(v, dist[x + 1, z + 1] + D2);
                if (x > 0 && z < sz - 1) v = Mathf.Min(v, dist[x - 1, z + 1] + D2);
                dist[x, z] = v;
            }
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                dist[x, z] *= cell;
        wallDist = dist;
        return water;
    }

    // 연결된(4방향) 물 덩어리 중 덮이지 않은 칸이 하나도 없는 것을 지운다. covers = 물 루트(c0) 기준 XZ 사각형. 남은 칸이 없으면 null.
    static bool[,] DropHiddenWater(bool[,] water, float w, float d, System.Collections.Generic.List<Rect> covers, ref int waterCells)
    {
        int sx = water.GetLength(0), sz = water.GetLength(1);
        float cw = w / sx, cd = d / sz;
        var covered = new bool[sx, sz];
        foreach (Rect r in covers)
        {
            int x0 = Mathf.Max(0, Mathf.CeilToInt((r.xMin + w * 0.5f) / cw - 0.5f)), x1 = Mathf.Min(sx - 1, Mathf.FloorToInt((r.xMax + w * 0.5f) / cw - 0.5f));
            int z0 = Mathf.Max(0, Mathf.CeilToInt((r.yMin + d * 0.5f) / cd - 0.5f)), z1 = Mathf.Min(sz - 1, Mathf.FloorToInt((r.yMax + d * 0.5f) / cd - 0.5f));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    covered[x, z] = true;   // 칸 중심이 사각형 안
        }
        var seen = new bool[sx, sz];
        var comp = new System.Collections.Generic.List<Vector2Int>();
        var stack = new System.Collections.Generic.Stack<Vector2Int>();
        var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        waterCells = 0;
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (!water[x, z] || seen[x, z]) continue;
                comp.Clear(); bool visible = false;
                stack.Push(new Vector2Int(x, z)); seen[x, z] = true;
                while (stack.Count > 0)
                {
                    Vector2Int p = stack.Pop(); comp.Add(p);
                    visible |= !covered[p.x, p.y];
                    foreach (Vector2Int dv in dirs)
                    {
                        int nx = p.x + dv.x, nz = p.y + dv.y;
                        if (nx < 0 || nz < 0 || nx >= sx || nz >= sz || seen[nx, nz] || !water[nx, nz]) continue;
                        seen[nx, nz] = true; stack.Push(new Vector2Int(nx, nz));
                    }
                }
                if (visible) waterCells += comp.Count;
                else foreach (Vector2Int p in comp) water[p.x, p.y] = false;
            }
        return waterCells > 0 ? water : null;
    }

    // 정사각 이웃(체비셰프 r칸)으로 true 를 넓힌다.
    static bool[,] Dilate(bool[,] src, int r)
    {
        int sx = src.GetLength(0), sz = src.GetLength(1);
        var o = new bool[sx, sz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (!src[x, z]) continue;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int px = x + dx, pz = z + dz;
                        if (px >= 0 && pz >= 0 && px < sx && pz < sz) o[px, pz] = true;
                    }
            }
        return o;
    }

    // 존 물 바닥 — 물 칸 밑에만, BedStep 칸마다 정점. 수심 = 벽까지 거리로 얕음 → 깊음(BedShallowDepth → BedDeepDepth).
    // 바닥 블록을 물보다 한 블록 넓게 깔고, 맨 바깥 정점은 수면 바로 아래(BedBorderBelowSurface)로 올린다 — 물 가장자리 밑이 비지 않게.
    static Mesh CreateMaskedBedMesh(float w, float d, string zoneName, bool[,] water, float[,] wallDist, Rect? envelope = null, Vector2 offset = default)
    {
        int sx = water.GetLength(0), sz = water.GetLength(1);
        int bx = Mathf.CeilToInt(sx / (float)BedStep), bz = Mathf.CeilToInt(sz / (float)BedStep);
        var block = new bool[bx, bz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
                if (water[x, z]) block[x / BedStep, z / BedStep] = true;
        bool[,] wetBlock = (bool[,])block.Clone();   // 물이 실제로 있는 블록은 항상 남긴다(가장자리 물 밑이 비면 검게 뚫린다)
        block = Dilate(block, 1);
        // 여유 블록이 존 바깥 벽 선 밖으로 나가면 물 가장자리 밖에 어두운 띠로 매달려 보였다(M_A 모서리) — 블록 중심이 벽 선 밖이면 뺀다.
        if (envelope.HasValue)
            for (int z = 0; z < bz; z++)
                for (int x = 0; x < bx; x++)
                {
                    float cx = -w * 0.5f + (Mathf.Min((x + 0.5f) * BedStep, sx) / sx) * w, cz = -d * 0.5f + (Mathf.Min((z + 0.5f) * BedStep, sz) / sz) * d;
                    if (!wetBlock[x, z] && !envelope.Value.Contains(new Vector2(cx, cz))) block[x, z] = false;
                }

        int nx = bx + 1;
        var remap = new int[nx * (bz + 1)];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        bool Block(int x, int z) => x >= 0 && z >= 0 && x < bx && z < bz && block[x, z];
        int V(int x, int z)
        {
            int i = z * nx + x;
            if (remap[i] >= 0) return remap[i];
            int cx = Mathf.Min(x * BedStep, sx), cz = Mathf.Min(z * BedStep, sz);
            float px = -w * 0.5f + cx / (float)sx * w, pz = -d * 0.5f + cz / (float)sz * d;
            bool clampedToOpenEdge = false;
            if (envelope.HasValue)   // 블록 격자(0.48m)가 벽 선과 안 맞아 삐져나오는 바닥 조각 — 정점을 벽 선 안으로 당긴다
            {
                // (시도·폐기 10-01: 열린 면에서 바닥을 2.8m 안쪽에서 끝내 가장자리 밑 어두운 면을 없애려 했으나, 가장자리 근처 물 픽셀의
                //  시선이 바닥에 안 닿아 물 안에 검은 얼룩이 생겼다 — 물 색이 깊이로 정해지므로 물 밑 바닥은 물 전체 밑에 있어야 한다.)
                float cpx = Mathf.Clamp(px, envelope.Value.xMin, envelope.Value.xMax);
                float cpz = Mathf.Clamp(pz, envelope.Value.yMin, envelope.Value.yMax);
                clampedToOpenEdge = cpx != px || cpz != pz;
                px = cpx; pz = cpz;
            }
            // 이 정점을 둘러싼 4블록이 모두 바닥이면 안쪽, 하나라도 비면 테두리
            bool border = !(Block(x - 1, z - 1) && Block(x, z - 1) && Block(x - 1, z) && Block(x, z));
            // 🔴 열린 면(카메라 쪽 자른 변) 근처 테두리는 세우지 않는다 — 그 안쪽 바닥이 깊어(최대 7m) 테두리를 수면 밑까지 세우면
            //    물 가장자리 밑에 높이 수 m 짜리 검은 벽면이 허공에 매달려 보였다(M_A 0°·180°, 10-01). 세우지 않으면 가장자리 밖은 빈 공간.
            if (envelope.HasValue && border)
            {
                float blk = BedStep * Mathf.Max(w / sx, d / sz) * 1.01f;
                Rect e = envelope.Value;
                if (clampedToOpenEdge || px >= e.xMax - blk || px <= e.xMin + blk || pz >= e.yMax - blk || pz <= e.yMin + blk) border = false;
            }
            float dist = wallDist[Mathf.Clamp(cx, 0, sx - 1), Mathf.Clamp(cz, 0, sz - 1)];
            float depth = Mathf.Lerp(BedShallowDepth, BedDeepDepth, Mathf.SmoothStep(0f, 1f, dist / BedRampDistance));
            remap[i] = verts.Count;
            verts.Add(new Vector3(px - offset.x, border ? -BedBorderBelowSurface : -depth, pz - offset.y));
            return remap[i];
        }
        for (int z = 0; z < bz; z++)
            for (int x = 0; x < bx; x++)
            {
                if (!block[x, z]) continue;
                int a = V(x, z), b = V(x, z + 1), c = V(x + 1, z), e = V(x + 1, z + 1);
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(c); tris.Add(b); tris.Add(e);
            }

        string path = $"{MeshDir}/WaterBed_{zoneName}.asset";
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/3.Materials/FlatKit/Water", "Meshes");
        var mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
        if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return SaveMesh(mesh, path);
    }

    // 물 칸에만 면을 만든 격자 물(존 전용 — 존마다 모양이 달라 존 이름으로 저장). 쓰지 않는 정점은 빼서 압축한다.
    // offset = 이 메시를 붙일 오브젝트의 위치(물 루트 기준 XZ) — 정점에서 빼서 오브젝트 원점 기준으로 만든다(물 조각별 오브젝트).
    static Mesh CreateMaskedGridMesh(float w, float d, string zoneName, bool[,] water, Vector2 offset = default)
    {
        int sx = water.GetLength(0), sz = water.GetLength(1);
        int nx = sx + 1;
        var remap = new int[nx * (sz + 1)];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        int V(int x, int z)
        {
            int i = z * nx + x;
            if (remap[i] < 0) { remap[i] = verts.Count; verts.Add(new Vector3(-w * 0.5f + x / (float)sx * w - offset.x, 0f, -d * 0.5f + z / (float)sz * d - offset.y)); }
            return remap[i];
        }
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (!water[x, z]) continue;
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
        mesh.RecalculateBounds();
        Bounds mb = mesh.bounds; mb.Expand(new Vector3(0f, 2f, 0f)); mesh.bounds = mb;   // 파도 정점 변위(y) 여유
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
        if (existing != null)
        {
            // 🔴 CopySerialized 로 덮어쓰면 직렬화 데이터만 바뀌고 **GPU 정점 버퍼는 이전 메시 그대로** 남는다(10-01 실측) —
            //    새 인덱스 + 낡은 정점이 섞여, 물이 없어야 할 구덩이(M_A 왼쪽 아래)에 이전 모양 조각이 검게 그려졌다.
            //    → 메시 API 로 다시 채운다(API 경로는 GPU 버퍼를 갱신한다).
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            if (mesh.normals.Length == mesh.vertexCount) existing.SetNormals(mesh.normals);
            if (mesh.tangents.Length == mesh.vertexCount) existing.SetTangents(mesh.tangents);
            var uv = new System.Collections.Generic.List<Vector2>(); mesh.GetUVs(0, uv);
            if (uv.Count == mesh.vertexCount) existing.SetUVs(0, uv);
            existing.SetTriangles(mesh.triangles, 0);
            existing.bounds = mesh.bounds;
            existing.name = mesh.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
