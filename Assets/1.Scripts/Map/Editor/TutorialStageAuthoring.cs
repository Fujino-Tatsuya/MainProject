using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

// 튜토리얼 스테이지 저작 도구 (PLAN-tutorial-stage.md · 2026-09-29 팀장 승인).
//
// 튜토리얼 벽(Level_wall_hallway_tutorial.prefab)은 SVN 벽 프리팹 인스턴스 253개로만 되어 있고 콜라이더가 0개다
// (원본 벽 FBX 도 addColliders 0). 그대로 쓰면 플레이어·몬스터가 벽을 통과하고 NavMesh 가 벽 밖으로 번진다.
// 원본(SVN)을 고치면 다른 존까지 바뀌므로 **튜토리얼 프리팹 안에서만** 추가 컴포넌트로 BoxCollider 를 붙인다.
//
// 🔴 구석(corner) 메시는 ㄱ 자라 경계 박스 하나로 덮으면 안쪽 모서리가 막힌다 → ㅡ·ㅣ 두 개(팀장 확정 T5).
//    어느 두 변이 팔인지는 정점 분포로 판정한다(변마다 두께 안쪽 정점 수 — 많은 쪽이 팔).
public static class TutorialStageAuthoring
{
    const string TutorialWallsPath = "Assets/2.Prefabs/Environment/Architecture/Walls/Corridors/Sections/Level_wall_hallway_tutorial.prefab";
    const string StagePath = "Assets/2.Prefabs/Environment/Layouts/Stages/StageTutorial.prefab";
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";
    const string Stage1PrefabPath = "Assets/2.Prefabs/Environment/Layouts/Stages/Stage1.prefab";
    const string ZonesDir = "Assets/2.Prefabs/Environment/Layouts/Zones/";
    const string BarrierMaterialPath = "Assets/3.Materials/MA_AoeTelegraph_Red.mat";

    // 이름으로 찾는 코드(MinimapController · MapOverviewUI · WallOcclusionDriver)가 기대하는 이름.
    const string StageRootName = "Stage1";
    const string CorridorName = "Level_wall_hallway";
    const string DisabledStageName = "Stage1_Procedural";

    // 아트 씬 all_mesh.unity 의 튜토리얼 배치(회전 0). 벽 인스턴스는 z −160.6.
    static readonly Vector3 WallsPosition = new Vector3(0f, 0f, -160.6f);

    // 스테이지 전체 Y 회전(90° 단위). 은희 투명화(상시 하단 / 구역 상단)가 카메라 쪽 벽을 전제로 저작돼
    // all_mesh 배치(0°) 그대로면 반대쪽 벽이 투명해진다 → 180°(팀장 09-29).
    // 벽 인스턴스 위치(WallsPosition)를 축으로 돌린다. 존 위치·회전은 월드 값으로 저장되므로
    // (MapContentSpawner — TryGetPosition·YawSteps) 슬롯 좌표와 YawSteps 도 같이 돌린다.
    const int StageYawSteps = 2;
    static Quaternion StageRotation => Quaternion.Euler(0f, StageYawSteps * 90f, 0f);
    static Vector3 RotateAroundWalls(Vector3 p) => WallsPosition + StageRotation * (p - WallsPosition);

    struct SlotSpec
    {
        public string Zone; public Vector3 Pos; public ZoneSize Size; public bool Spawn, Boss;
        public ZoneEntry[] Entries;   // null = Zone 하나를 슬롯 위치·회전 0(스테이지 회전만)으로
    }

    // 슬롯 Rotations 한 줄. Pos·Yaw 는 슬롯과 같이 **스테이지 회전 전** 값 — 빌드 때 StageYawSteps 를 더하고 위치를 돌린다.
    struct ZoneEntry
    {
        public string Zone; public int Yaw; public Vector3 Pos;
    }

    static readonly SlotSpec[] Slots =
    {
        new SlotSpec { Zone = "ZoneS_typeStart",     Pos = new Vector3(-29.8257f, 0f, -120.7997f),  Size = ZoneSize.Small,  Spawn = true },
        new SlotSpec { Zone = "ZoneS_typeA",         Pos = new Vector3(-0.1775f, 0f, -120.7997f), Size = ZoneSize.Small },
        // 존3 = 스피너봇 존(튜토리얼 시나리오 §1.3). 회전 270°·위치는 4.MapScene Stage1 에서 손으로 맞춘 값.
        // 🔴 M_typeA 줄은 쓰이지 않는다(TryGetYaw/TryGetPosition 은 FixedPrefab 줄만 본다). 그래도 지우지 않는 이유:
        //    씬 Stage1 오버라이드가 Rotations 를 **인덱스**(data[0]=M_A, data[1]=M_C)로 덮고 있어, 줄 수·순서가 다르면
        //    M_C 줄이 엉뚱한 회전·위치로 덮인다.
        new SlotSpec { Zone = "ZoneM_typeC",         Pos = new Vector3(-0.1765f, 0f, -160.6469f), Size = ZoneSize.Medium,
            Entries = new[]
            {
                new ZoneEntry { Zone = "ZoneM_typeA", Yaw = 0, Pos = new Vector3(-0.1765f, 0f, -145.7f) },
                new ZoneEntry { Zone = "ZoneM_typeC", Yaw = 1, Pos = new Vector3(-0.1765f, 0f, -160.2509f) },
            } },
        new SlotSpec { Zone = "ZoneL_typeB",         Pos = new Vector3(-10.1745f, 0f, -210.2951f), Size = ZoneSize.Large },
        new SlotSpec { Zone = "ZoneS_typeBossEnter", Pos = new Vector3(29.4707f, 0f, -220.0961f), Size = ZoneSize.Small,  Boss = true },
    };

    // 벽 투명화(WallTransparencyGroup) 구역 페이드 — 튜토리얼 벽 그룹 9개 전부 같은 값(4.MapScene Stage1 에서 맞춘 값).
    // 원본 Level_wall_hallway_tutorial 은 아트 씬(all_mesh)도 쓰고 그룹마다 기본값이 달라 그대로 두고, 이 스테이지에서만 덮는다.
    static readonly (string Field, float Value)[] WallFade =
    {
        ("fadeInDuration", 0.3f), ("fadeOutDuration", 0.3f),
        ("fadeHeightInside", -2f), ("baseYOffsetInside", 2f),
        ("fadeHeightOutside", -15f), ("baseYOffsetOutside", 15f),
    };

    static Vector2 FootprintOf(ZoneSize s) => s switch
    {
        ZoneSize.Large => new Vector2(40f, 40f),
        ZoneSize.Medium => new Vector2(20f, 40f),
        _ => new Vector2(20f, 20f),
    };

    // ─── ① 벽 콜라이더 ────────────────────────────────────────────────────────
    [MenuItem("Tools/Map/Authoring/Tutorial/1. Add Wall Colliders")]
    public static void AddWallColliders()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(TutorialWallsPath);
        try
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && !IsFloor(f.transform) && !IsOcclusionZone(f.transform))
                .ToList();

            // 재실행 대비 — 이전에 이 도구가 붙인(= 추가 컴포넌트 오버라이드인) BoxCollider 를 걷어낸다.
            int removed = 0;
            foreach (MeshFilter f in filters)
                foreach (BoxCollider b in f.GetComponents<BoxCollider>())
                    if (PrefabUtility.IsAddedComponentOverride(b)) { Object.DestroyImmediate(b); removed++; }

            float thickness = EstimateWallThickness(filters);
            int straight = 0, corner = 0, cornerNamedBox = 0;
            var cornerLog = new List<string>();
            foreach (MeshFilter f in filters)
            {
                f.gameObject.layer = 0;   // 본맵 벽과 같은 Default — 플레이어 장애물 마스크 ∩ NavMesh 베이크(Default+Ground)
                Bounds mb = f.sharedMesh.bounds;
                if (IsCorner(f.transform) && IsLShaped(mb, thickness))
                {
                    AddCornerColliders(f, mb, thickness, cornerLog);
                    corner++;
                }
                else
                {
                    BoxCollider b = f.gameObject.AddComponent<BoxCollider>();
                    b.center = mb.center;
                    b.size = mb.size;
                    straight++;
                    if (IsCorner(f.transform))
                    {
                        cornerNamedBox++;
                        cornerLog.Add($"[TutorialStage] 이름만 구석 {f.transform.parent?.name}/{f.name} — 메시 크기 {mb.size.x:0.##}×{mb.size.z:0.##} → 박스 1개");
                    }
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, TutorialWallsPath);
            Debug.Log($"[TutorialStage] 벽 콜라이더 — 일반 {straight}(그중 이름만 corner {cornerNamedBox}) · 구석 {corner}(각 2개) · 이전 것 제거 {removed} · 추정 벽 두께 {thickness:0.###}m");
            foreach (string line in cornerLog.Take(200)) Debug.Log(line);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 🔴 벽 이름에 "Ground floor"·"basement floor" 가 들어 있다 — "floor 포함"으로 거르면 벽이 전부 빠진다.
    //    바닥 조각은 이름이 floor_ 로 **시작**한다(floor_hallway 1 — 이미 MeshCollider 보유).
    static bool IsFloor(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name.StartsWith("floor_", System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // 🔴 은희 투명화 존(Occlusion_zone_*)은 기본 큐브 MeshFilter + 트리거 BoxCollider 다. 벽으로 잡으면
    //    실체 BoxCollider 가 붙고 레이어가 0 으로 바뀌어 존 자리가 보이지 않는 벽이 된다.
    static bool IsOcclusionZone(Transform t) => t.GetComponent<WallTransparencyZone>() != null;

    static bool IsCorner(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name.IndexOf("corner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // 🔴 이름에 corner 가 들어가도 ㄱ 자가 아닐 수 있다 — SVN r339 `walll_brick_cornerCOM_*` 은
    //    구석 조각 2개 + 기둥(piloti) 1개 묶음이라 자식 전부가 이름으로는 corner 다.
    //    가로·세로 둘 다 벽 두께보다 충분히 길 때만 ㄱ 자로 본다(기둥·곧은 조각은 박스 하나).
    static bool IsLShaped(Bounds mb, float thickness) => Mathf.Min(mb.size.x, mb.size.z) > thickness * 1.5f;

    // 곧은 벽 메시의 얇은 쪽 치수 중앙값 = 벽 두께.
    static float EstimateWallThickness(List<MeshFilter> filters)
    {
        var t = filters.Where(f => !IsCorner(f.transform))
            .Select(f => Mathf.Min(f.sharedMesh.bounds.size.x, f.sharedMesh.bounds.size.z))
            .Where(v => v > 0.01f).OrderBy(v => v).ToList();
        return t.Count > 0 ? t[t.Count / 2] : 0.5f;
    }

    static void AddCornerColliders(MeshFilter f, Bounds mb, float thickness, List<string> log)
    {
        Vector3[] v = f.sharedMesh.vertices;
        float th = Mathf.Min(thickness, Mathf.Min(mb.size.x, mb.size.z) * 0.5f);
        int xMin = v.Count(p => p.x <= mb.min.x + th), xMax = v.Count(p => p.x >= mb.max.x - th);
        int zMin = v.Count(p => p.z <= mb.min.z + th), zMax = v.Count(p => p.z >= mb.max.z - th);
        bool useXMin = xMin >= xMax, useZMin = zMin >= zMax;

        // ㅣ 팔(z 방향으로 뻗음): 고른 x 변에 붙은 두께 th 의 판
        float xEdge = useXMin ? mb.min.x + th * 0.5f : mb.max.x - th * 0.5f;
        BoxCollider a = f.gameObject.AddComponent<BoxCollider>();
        a.center = new Vector3(xEdge, mb.center.y, mb.center.z);
        a.size = new Vector3(th, mb.size.y, mb.size.z);

        // ㅡ 팔(x 방향으로 뻗음): 고른 z 변
        float zEdge = useZMin ? mb.min.z + th * 0.5f : mb.max.z - th * 0.5f;
        BoxCollider b = f.gameObject.AddComponent<BoxCollider>();
        b.center = new Vector3(mb.center.x, mb.center.y, zEdge);
        b.size = new Vector3(mb.size.x, mb.size.y, th);

        log.Add($"[TutorialStage] 구석 {f.transform.parent?.name}/{f.name} — 변 정점 x−{xMin} x+{xMax} z−{zMin} z+{zMax} → ㅣ {(useXMin ? "x−" : "x+")} · ㅡ {(useZMin ? "z−" : "z+")} · 두께 {th:0.##}");
    }

    // ─── ② 튜토리얼 스테이지 프리팹 ────────────────────────────────────────────
    [MenuItem("Tools/Map/Authoring/Tutorial/2. Build StageTutorial Prefab")]
    public static void BuildStagePrefab()
    {
        GameObject walls = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialWallsPath);
        if (walls == null) { Debug.LogError($"[TutorialStage] {TutorialWallsPath} 없음"); return; }

        // 🔴 재실행 — 기존 프리팹의 벽 인스턴스 오버라이드(바닥 Cast Shadows 끔 b2582266 · 투명화 영역 손질 2ddf2838 등
        //    이 도구가 만들지 않는 손 수정)를 이어받는다. 안 그러면 다시 만들 때마다 조용히 사라진다.
        //    fileID 는 SaveAsPrefabAsset 이 **이름으로** 맞춰 이어 준다 — 슬롯 이름(Slot_{i}_{Zone})이 바뀌면 그 슬롯의
        //    fileID 가 새로 생겨 씬 Stage1 의 오버라이드가 끊긴다.
        GameObject oldStage = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
        Transform oldWalls = oldStage != null ? oldStage.transform.Find(CorridorName) : null;
        PropertyModification[] wallOverrides = oldWalls != null && PrefabUtility.IsAnyPrefabInstanceRoot(oldWalls.gameObject)
            ? PrefabUtility.GetPropertyModifications(oldWalls.gameObject) : null;

        var root = new GameObject("StageTutorial");
        try
        {
            // 튜토리얼 판별 마커 — 진행 상태(TutorialDirector)·타이머·패드 등 튜토리얼 전용 규칙이 이것만 본다.
            var marker = root.AddComponent<TutorialStageMarker>();
            // 배리어(S3) 머티리얼 — 기존 반투명 머티리얼 재사용(새 아트 없음)
            var markerSo = new SerializedObject(marker);
            markerSo.FindProperty("barrierMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(BarrierMaterialPath);
            markerSo.ApplyModifiedPropertiesWithoutUndo();

            var wallsInst = (GameObject)PrefabUtility.InstantiatePrefab(walls, root.transform);
            if (wallOverrides != null) PrefabUtility.SetPropertyModifications(wallsInst, wallOverrides);
            wallsInst.name = CorridorName;
            wallsInst.transform.localPosition = WallsPosition;
            wallsInst.transform.localRotation = StageRotation;
            ApplyWallFade(wallsInst);

            var slotsRoot = new GameObject("Slots");
            slotsRoot.transform.SetParent(root.transform, false);

            for (int i = 0; i < Slots.Length; i++)
            {
                SlotSpec s = Slots[i];
                GameObject zone = AssetDatabase.LoadAssetAtPath<GameObject>(ZonesDir + s.Zone + ".prefab");
                if (zone == null) { Debug.LogError($"[TutorialStage] 존 프리팹 없음: {s.Zone}"); return; }

                var go = new GameObject($"Slot_{i}_{s.Zone}");
                go.transform.SetParent(slotsRoot.transform, false);
                Vector3 pos = RotateAroundWalls(s.Pos);
                go.transform.localPosition = pos;
                var slot = go.AddComponent<ZoneSlot>();
                slot.SlotID = i;
                slot.Size = s.Size;
                slot.Footprint = FootprintOf(s.Size);
                slot.IsSpawnCandidate = s.Spawn;
                slot.IsBossCandidate = s.Boss;
                slot.IsQuestCandidate = false;     // T4 — 퀘스트 없음
                slot.FixedPrefab = zone;
                slot.Rotations = new List<ZoneSlot.RotationEntry>();
                foreach (ZoneEntry e in s.Entries ?? new[] { new ZoneEntry { Zone = s.Zone, Pos = s.Pos } })
                {
                    GameObject entryZone = AssetDatabase.LoadAssetAtPath<GameObject>(ZonesDir + e.Zone + ".prefab");
                    if (entryZone == null) { Debug.LogError($"[TutorialStage] 존 프리팹 없음: {e.Zone}"); return; }
                    slot.Rotations.Add(new ZoneSlot.RotationEntry
                    {
                        Prefab = entryZone, YawSteps = (StageYawSteps + e.Yaw) % 4, HasPosition = true, Position = RotateAroundWalls(e.Pos),
                    });
                }
            }

            TutorialZone1Authoring.AddTo(root);   // S7 존1 평타 허수아비 자리(런타임 스폰)

            PrefabUtility.SaveAsPrefabAsset(root, StagePath, out bool ok);
            Debug.Log($"[TutorialStage] {StagePath} 저장 {(ok ? "완료" : "실패")} — 회전 {StageYawSteps * 90}° · 슬롯 {Slots.Length}개(Start=스폰 후보 · BossEnter=보스 후보 · 퀘스트 없음)");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void ApplyWallFade(GameObject wallsInst)
    {
        foreach (WallTransparencyGroup g in wallsInst.GetComponentsInChildren<WallTransparencyGroup>(true))
        {
            var so = new SerializedObject(g);
            foreach (var (field, value) in WallFade) so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ─── ③ MapScene 교체 ──────────────────────────────────────────────────────
    [MenuItem("Tools/Map/Authoring/Tutorial/3. Swap MapScene Stage1 → Tutorial")]
    public static void SwapMapScene()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[TutorialStage] Play 중에는 하지 않는다."); return; }
        GameObject stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
        GameObject stage1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Stage1PrefabPath);
        if (stagePrefab == null) { Debug.LogError("[TutorialStage] StageTutorial.prefab 없음 — 2번 메뉴 먼저."); return; }

        var scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
        bool wasLoadedAlone = true;
        try
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (src == stagePrefab) { Object.DestroyImmediate(go); continue; }   // 재실행 — 기존 튜토리얼 제거
                if (src == stage1Prefab)
                {
                    go.name = DisabledStageName;
                    go.SetActive(false);   // GameObject.Find 와 MapGenerator.GatherSlots(활성만) 둘 다에서 빠진다
                }
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(stagePrefab, scene);
            inst.name = StageRootName;
            inst.transform.position = Vector3.zero;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TutorialStage] {MapScenePath} — {DisabledStageName} 비활성 · 튜토리얼을 '{StageRootName}/{CorridorName}' 로 배치 후 저장");
        }
        finally
        {
            if (wasLoadedAlone && UnityEngine.SceneManagement.SceneManager.loadedSceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ─── ④ 복도 바닥 틈 검사 (교체된 MapScene 을 연 상태에서) ────────────────────
    // 편집 모드에는 존이 없다(런타임 생성) — 존 자리는 비어 보이는 게 정상. 그래서 개수 대신 **격자 지도**를 찍는다:
    //   '#' 벽(히트 높이 > 1.5m) · '.' 바닥 · 'Z' 존 자리(슬롯 Footprint 안, 런타임에 채워짐) · ' ' 빈 곳(복도 안이면 추락 구멍)
    [MenuItem("Tools/Map/Authoring/Tutorial/4. Probe Floor Gaps")]
    public static void ProbeFloorGaps()
    {
        var scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject stage = scene.GetRootGameObjects().FirstOrDefault(g => g.name == StageRootName && g.activeSelf);
            Transform walls = stage != null ? stage.transform.Find(CorridorName) : null;
            if (walls == null) { Debug.LogError("[TutorialStage] MapScene 에 활성 Stage1/Level_wall_hallway 가 없다 — 3번 메뉴 먼저."); return; }

            Bounds b = new Bounds(walls.position, Vector3.zero);
            foreach (Renderer r in walls.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            ZoneSlot[] slots = stage.GetComponentsInChildren<ZoneSlot>();

            int mask = LayerMask.GetMask("Default", "Ground");
            var sb = new System.Text.StringBuilder();
            int holes = 0;
            for (float z = b.max.z - 1f; z > b.min.z; z -= 2f)   // 위(북) → 아래
            {
                for (float x = b.min.x + 1f; x < b.max.x; x += 2f)
                {
                    Vector3 top = new Vector3(x, b.max.y + 5f, z);
                    char c;
                    if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, b.size.y + 40f, mask, QueryTriggerInteraction.Ignore))
                        c = hit.point.y > 1.5f ? '#' : '.';
                    else if (slots.Any(s => Mathf.Abs(x - s.transform.position.x) <= s.Footprint.x * 0.5f &&
                                            Mathf.Abs(z - s.transform.position.z) <= s.Footprint.y * 0.5f))
                        c = 'Z';
                    else { c = ' '; holes++; }
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            Debug.Log($"[TutorialStage] 바닥 지도 x {b.min.x:0}~{b.max.x:0} · z {b.max.z:0}(위)~{b.min.z:0}(아래) · 2m 격자 · 빈칸 {holes}\n" +
                      "('#' 벽 · '.' 바닥 · 'Z' 존 자리 · ' ' 빈 곳 — 벽으로 둘러싸인 복도 안의 빈칸이 추락 구멍)\n" + sb);
        }
        finally
        {
            if (UnityEngine.SceneManagement.SceneManager.loadedSceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
