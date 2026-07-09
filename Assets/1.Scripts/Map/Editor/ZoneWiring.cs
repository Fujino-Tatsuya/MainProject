#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 와이어링(에디터 전용): 씬 ZoneVolume → ZoneSlot 스켈레톤 생성 +
// ZoneLayoutCatalog 등록 + MapGenerator 참조 연결 + 임시 Zone_* 숨김. + 셔플 generate 메뉴.
// 배치 소스오브트루스 = ZoneVolume(씬). 볼륨을 옮기고 Wire → Build GeoV2 재실행하면 맵이 따라온다.
public static class ZoneWiring
{
    const string PrefabDir = "Assets/50.Art/MapGen/MapObj/Zoneprefab"; // 2026-07-03 아트가 prefab→Zoneprefab로 폴더명 변경(GUID 유지)
    const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";

    // 배치 소스오브트루스 = 씬(Stage1)의 ZoneVolume 10개 (2026-07 리팩토링).
    //  - ZoneVolume.transform.position → 슬롯 중심 (Y는 0으로 클램프)
    //  - ZoneVolume.Size 가로세로비 → 크기/회전 (양축≥30m=대형 / 한축만≥30m=중형, X가 길면 90° / 그 외=소형)
    //  - ZoneDefinitionSO 플래그 → 퀘스트/스폰/보스입구 후보
    //  - SlotID = ZoneID - 1 (1~10 필수, 결정적 순서)
    // 디자이너가 씬에서 볼륨을 옮기면 Wire 재실행만으로 배치가 따라온다.
    // 존 간 간격 = 디자이너가 볼륨으로 잡은 그대로 — 다리는 존 사이 빈 간격만큼 생성되고,
    // 존 회전(PickYaw)이 출입구를 다리 방향에 자동 매칭한다.

    // 카탈로그 (prefab 폴더, 2026-07 정리 완료).
    // 대형: 3디자인 ↔ 3슬롯 시드 셔플(재사용 없음).
    // 중형: 퀘스트 슬롯(4후보 중 랜덤 1곳) = 전용 디자인 없음 → Combat 풀(A/B/C)에서 셔플.
    // 소형: typeA=우상단 고정 전투, typeBossEnter=보스맵 입구, typeStart=스폰
    //       (좌상/좌하 후보 2곳에 스폰/보스입구가 매판 랜덤 배정).
    static readonly (string name, ZoneSize size, ZoneRole role)[] CatEntries =
    {
        ("ZoneL_typeA", ZoneSize.Large,  ZoneRole.Combat),
        ("ZoneL_typeB", ZoneSize.Large,  ZoneRole.Combat),
        ("ZoneL_typeC", ZoneSize.Large,  ZoneRole.Combat),
        ("ZoneM_typeA", ZoneSize.Medium, ZoneRole.Combat),
        ("ZoneM_typeB", ZoneSize.Medium, ZoneRole.Combat),
        ("ZoneM_typeC", ZoneSize.Medium, ZoneRole.Combat),
        // Quest: 전용 디자인 없음 — LayoutPlacer가 Combat 풀에서 셔플로 배정
        ("ZoneS_typeA",        ZoneSize.Small, ZoneRole.Combat),
        ("ZoneS_typeBossEnter",ZoneSize.Small, ZoneRole.BossRoom),
        ("ZoneS_typeStart",    ZoneSize.Small, ZoneRole.PlayerSpawn),
    };

    // 연결 그래프 (ZoneID 쌍, 13연결) — 다리 방향(슬롯 Conn 플래그)과 다리 재생성 둘 다 이 표에서 파생.
    static readonly (int a, int b)[] ZonePairs =
    {
        (6, 1), (6, 2),          // 소형좌상 ↔ 대형좌 / 대형상중
        (2, 7), (2, 4),          // 대형상중 ↔ 소형우상 / 중형중앙
        (1, 4), (1, 5), (1, 9),  // 대형좌 ↔ 중형중앙 / 중형하중좌 / 중형하좌
        (9, 5),                  // 중형하좌 ↔ 중형하중좌
        (4, 8), (4, 10),         // 중형중앙 ↔ 소형하좌 / 중형우중
        (7, 10),                 // 소형우상 ↔ 중형우중
        (10, 3), (8, 3),         // 중형우중 ↔ 대형하우 / 소형하좌 ↔ 대형하우
    };

    // SlotID(=ZoneID-1) 기준 쌍 — GeoV2/Conn 파생용
    static (int a, int b)[] SlotPairs
    {
        get
        {
            var arr = new (int a, int b)[ZonePairs.Length];
            for (int i = 0; i < ZonePairs.Length; i++) arr[i] = (ZonePairs[i].a - 1, ZonePairs[i].b - 1);
            return arr;
        }
    }

    [MenuItem("Tools/MapGen/Wire Slots + Catalog + Refs")]
    static void Wire()
    {
        var stage1 = GameObject.Find("Stage1");
        if (stage1 == null) { Debug.LogError("[Wire] Stage1 없음"); return; }

        var old = stage1.transform.Find("Slots");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var slotsRoot = new GameObject("Slots");
        slotsRoot.transform.SetParent(stage1.transform, false);

        // 씬 ZoneVolume 수집 (비활성 포함) → ZoneID 순 정렬
        var vols = Object.FindObjectsByType<ZoneVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(v => v.Zone != null).OrderBy(v => v.Zone.ZoneID).ToList();
        if (vols.Count == 0) { Debug.LogError("[Wire] ZoneVolume 없음 — Stage1의 ZoneVolumes 확인"); return; }
        for (int i = 0; i < vols.Count; i++)
            if (vols[i].Zone.ZoneID != i + 1)
            { Debug.LogError($"[Wire] ZoneID 불연속: {vols[i].name} = {vols[i].Zone.ZoneID} (기대 {i + 1}) — ZoneDef 지정 확인"); return; }

        int id = 0;
        var created = new List<ZoneSlot>();
        foreach (var v in vols)
        {
            bool lx = v.Size.x >= 30f, lz = v.Size.z >= 30f;
            ZoneSize size = lx && lz ? ZoneSize.Large : (lx || lz ? ZoneSize.Medium : ZoneSize.Small);
            float rotY = size == ZoneSize.Medium && lx ? 90f : 0f; // 가로로 긴 중형 = 90°

            var go = new GameObject($"Slot_{id}_Z{v.Zone.ZoneID}_{size.ToString()[0]}");
            go.transform.SetParent(slotsRoot.transform, false);
            go.transform.position = new Vector3(v.transform.position.x, 0f, v.transform.position.z);
            go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            var slot = go.AddComponent<ZoneSlot>();
            slot.SlotID = id++;
            slot.Size = size;
            slot.Footprint = size switch
            {
                ZoneSize.Large  => new Vector2(40f, 40f),  // 10x10 타일
                ZoneSize.Medium => new Vector2(20f, 40f),  // 5x10 타일
                _               => new Vector2(20f, 20f),  // 5x5 타일
            };
            slot.IsQuestCandidate = v.Zone.IsQuestZoneCandidate;
            slot.IsSpawnCandidate = v.Zone.IsPlayerSpawnCandidate;
            slot.IsBossCandidate = v.Zone.IsBossGateCandidate;
            created.Add(slot);
        }

        // 연결 그래프 → 슬롯별 다리 개수(월드 지배축 방향별)
        foreach (var p in SlotPairs)
        {
            var a = created[p.a]; var b = created[p.b];
            Vector3 d = b.transform.position - a.transform.position;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.z))
            {
                if (d.x > 0) { a.ConnE++; b.ConnW++; }
                else         { a.ConnW++; b.ConnE++; }
            }
            else
            {
                if (d.z > 0) { a.ConnN++; b.ConnS++; }
                else         { a.ConnS++; b.ConnN++; }
            }
        }

        var cat = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);
        if (cat == null)
        {
            // 폴더 정리 등으로 삭제됐을 때 자동 재생성
            string dir = System.IO.Path.GetDirectoryName(CatalogPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'),
                                           System.IO.Path.GetFileName(dir));
            cat = ScriptableObject.CreateInstance<ZoneLayoutCatalogSO>();
            AssetDatabase.CreateAsset(cat, CatalogPath);
            Debug.Log($"[Wire] 카탈로그 신규 생성 → {CatalogPath}");
        }
        cat.Entries.Clear();
        foreach (var e in CatEntries)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{e.name}.prefab");
            if (pf == null) { Debug.LogWarning($"[Wire] 프리팹 없음 {e.name}"); continue; }
            cat.Entries.Add(new ZoneLayoutCatalogSO.Entry { Prefab = pf, Size = e.size, Role = e.role, Difficulty = 0 });
        }
        EditorUtility.SetDirty(cat);

        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg != null)
        {
            mg.ZoneLayoutCatalog = cat;
            if (mg.LayoutPlacer == null) mg.LayoutPlacer = Object.FindFirstObjectByType<LayoutPlacer>();
            if (mg.ContentSpawner == null) mg.ContentSpawner = Object.FindFirstObjectByType<MapContentSpawner>();
            mg.AutoGenerateOnStart = false;
            EditorUtility.SetDirty(mg);
        }

        // 미니맵 컨트롤러 — 없으면 생성, 참조 연결 (런타임에 MapGenerator.OnGenerated 구독)
        var mm = Object.FindFirstObjectByType<MinimapController>();
        if (mm == null) mm = new GameObject("Minimap").AddComponent<MinimapController>();
        mm.Generator = mg;
        // 탐사 상태 서버 공유 — 씬 NetworkObject + Sync 컴포넌트
        if (mm.GetComponent<Unity.Netcode.NetworkObject>() == null)
            mm.gameObject.AddComponent<Unity.Netcode.NetworkObject>();
        if (mm.GetComponent<MinimapNetworkSync>() == null)
            mm.gameObject.AddComponent<MinimapNetworkSync>();
        EditorUtility.SetDirty(mm);

        int hidden = 0;
        var geom = stage1.transform.Find("MapGeometry");
        if (geom != null)
            foreach (Transform c in geom)
                if (c.name.StartsWith("Zone_")) { c.gameObject.SetActive(false); hidden++; }

        // ZoneVolumes = 배치 소스오브트루스 — 반드시 활성 유지(과거 Wire가 비활성화했던 것 복구)
        var volumes = stage1.transform.Find("ZoneVolumes");
        if (volumes != null && !volumes.gameObject.activeSelf) volumes.gameObject.SetActive(true);

        AssetDatabase.SaveAssets();
        bool lpOk = mg != null && mg.LayoutPlacer != null;
        bool csOk = mg != null && mg.ContentSpawner != null;
        Debug.Log($"[Wire] ✔ 슬롯 {id} + 카탈로그 {cat.Entries.Count} + 참조 연결. 임시 Zone_* {hidden} 숨김. (LayoutPlacer={(lpOk ? "OK" : "없음")}, ContentSpawner={(csOk ? "OK" : "없음")})");
    }

    // 매번 다른 배치: 대형 3 순열 / 중형 3+퀘스트(4곳 중 1) / 스폰↔보스입구(좌상·좌하 랜덤)
    [MenuItem("Tools/MapGen/Test Generate (random seed)")]
    static void GenRandom() => RunGen(System.Environment.TickCount);

    // 고정 시드 — 재현/디버그용
    [MenuItem("Tools/MapGen/Test Generate (seed 12345, 재현용)")]
    static void Gen12345() => RunGen(12345);

    [MenuItem("Tools/MapGen/Test Generate (seed 99, 재현용)")]
    static void Gen99() => RunGen(99);

    static void RunGen(int seed)
    {
        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null) { Debug.LogError("[Gen] MapGenerator 없음"); return; }
        mg.Generate(seed, 0);
    }

    // ---------------- 다리 v2 (MapObj/material 머티리얼, 슬롯 기준 절차 재생성) ----------------

    const string MatDir = "Assets/50.Art/MapGen/MapObj/material";
    const float CorridorWidth = 4f;   // 통로 폭 = 문 폭과 동일 (1×1×1 타일 4칸)
    const float WallHeight = 3f;
    const float WallThickness = 0.5f;
    const float SegLen = 4f;          // 벽 세그먼트 길이 = 존 벽 모듈 규격(4m) — 바닥도 FillFloor가 4m 정타일

    // 크기별 로컬 half-extents (회전 전)
    static Vector2 BaseHalf(ZoneSize size) => size switch
    {
        ZoneSize.Large  => new Vector2(20.5f, 20.5f),
        ZoneSize.Medium => new Vector2(10.5f, 20.5f),
        _               => new Vector2(10.5f, 10.5f),
    };

    // 슬롯의 월드 half-extents (회전 반영: 90°면 x/z 스왑)
    static Vector2 HalfExtents(ZoneSlot s)
    {
        Vector2 half = BaseHalf(s.Size);
        int steps = Mathf.RoundToInt(s.transform.eulerAngles.y / 90f) & 3;
        return (steps & 1) == 1 ? new Vector2(half.y, half.x) : half;
    }

    // 슬롯의 최종 회전(스텝) — LayoutPlacer.PickYaw와 동일 규칙을 "표준 존(N/W 개방)" 가정으로 복제.
    // 다리 "개수" 가중 최대 커버(동점=최소 회전, 결정적) — 어떤 디자인이 와도 슬롯별 벽 방향 고정.
    static int FinalStepsFor(ZoneSlot s)
    {
        int slotSteps = Mathf.RoundToInt(s.transform.eulerAngles.y / 90f) & 3;
        int[] candidates = s.Size == ZoneSize.Medium ? new[] { 0, 2 } : new[] { 0, 1, 2, 3 };
        int bestScore = -1, best = 0;
        foreach (int extra in candidates)
        {
            int total = (slotSteps + extra) & 3;
            int score = 0;
            for (int d = 0; d < 4; d++)
            {
                int l = (d - total + 4) & 3;
                if (l == 0 || l == 3) score += s.ConnCount(d); // N/W 개방변이 커버하는 다리 수
            }
            if (score > bestScore) { bestScore = score; best = extra; }
        }
        return (slotSteps + best) & 3;
    }

    const string GeoPrefabPath = "Assets/50.Art/MapGen/MapObj/MapGeometryV2.prefab";

    // 존 바닥 상면 Y 실측 — 바닥 머티리얼 렌더러 top의 최빈값.
    // zone_floor_* (구 규칙) 또는 MA_floor* (신 FBX 규칙) 양쪽 인식.
    // 다리/둘레벽을 존 보행면 높이에 정렬(단차 방지). 실패 시 0.
    static float MeasureFloorTopY(string prefabName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{prefabName}.prefab");
        if (prefab == null) return 0f;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var counts = new Dictionary<int, int>(); // 0.05m 단위 히스토그램
        foreach (var r in inst.GetComponentsInChildren<Renderer>())
        {
            var m = r.sharedMaterial;
            if (m == null) continue;
            string mn = m.name.ToLowerInvariant();
            if (!mn.StartsWith("zone_floor") && !mn.StartsWith("ma_floor")) continue;
            int key = Mathf.RoundToInt(r.bounds.max.y * 20f);
            counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        }
        Object.DestroyImmediate(inst);
        int bestKey = 0, bestCount = -1;
        foreach (var kv in counts)
            if (kv.Value > bestCount) { bestCount = kv.Value; bestKey = kv.Key; }
        if (bestCount <= 0)
            Debug.LogWarning($"[GeoV2] MeasureFloorTopY({prefabName}): 바닥 머티리얼(zone_floor_* 또는 MA_floor*) 없음 → 0 반환. 머티리얼 이름 확인 필요.");
        return bestCount > 0 ? bestKey / 20f : 0f;
    }

    // 정적 지오메트리 v2 통합 빌드 (2026-07 팀장 지시):
    //  ① 구 MapGeometry(v1 바닥/벽/다리) 삭제
    //  ② 슬롯 위치·결정 회전 기준으로 다리(문 앵커) + 존 개방변(N/W) 둘레벽(다리 입구만 트기) 생성
    //  ③ Tex_zone 머티리얼 통일 → MapGeometryV2.prefab 저장(씬 인스턴스 연결 유지)
    [MenuItem("Tools/MapGen/Build Static Geometry V2 (Corridors+Walls → Prefab)")]
    static void BuildStaticGeometryV2()
    {
        var stage1 = GameObject.Find("Stage1");
        if (stage1 == null) { Debug.LogError("[GeoV2] Stage1 없음"); return; }

        var slots = new List<ZoneSlot>(Object.FindObjectsByType<ZoneSlot>(FindObjectsSortMode.None));
        slots.Sort((a, b) => a.SlotID.CompareTo(b.SlotID));
        if (slots.Count == 0) { Debug.LogError("[GeoV2] ZoneSlot 없음 — 먼저 Wire 실행"); return; }

        var floorMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/MA_floor.mat");
        var wallMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/MA_Wall_basic.mat");
        if (floorMat == null || wallMat == null) { Debug.LogError("[GeoV2] MapObj/material 머티리얼 없음 (MA_floor.mat / MA_Wall_basic.mat)"); return; }

        // 존 보행면 높이 실측 — 다리/벽을 존 바닥 상면에 정렬(단차 방지)
        float floorTop = MeasureFloorTopY("ZoneS_typeA");
        Debug.Log($"[GeoV2] 존 바닥 상면 Y 실측: {floorTop:F2}m — 다리/벽 높이 기준");

        // ① 구 산출물 제거: v1 MapGeometry(프리팹 인스턴스면 언팩 후), 구 CorridorsV2, 기존 MapGeometryV2
        foreach (string oldName in new[] { "MapGeometry", "CorridorsV2", "MapGeometryV2" })
        {
            var old = stage1.transform.Find(oldName);
            if (old == null) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(old.gameObject))
            {
                var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(old.gameObject);
                if (outermost != null && outermost != stage1)
                    PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            }
            Object.DestroyImmediate(old.gameObject);
        }

        var geoRoot = new GameObject("MapGeometryV2");
        geoRoot.transform.SetParent(stage1.transform, false);
        var root = new GameObject("Corridors").transform;
        root.SetParent(geoRoot.transform, false);

        // 다리 입구 수집: (SlotID, 월드방향) → 입구 중심들 (개방변 벽에 구멍 낼 위치)
        var mouths = new Dictionary<(int slot, int dir), List<float>>();
        void AddMouth(int slot, int dir, float c)
        {
            if (!mouths.TryGetValue((slot, dir), out var list)) mouths[(slot, dir)] = list = new List<float>();
            list.Add(c);
        }

        // 벽 컷 지점 초기화 (다리 빌드에서 다시 채움)
        foreach (var s in slots) { s.WallCuts.Clear(); EditorUtility.SetDirty(s); }

        int built = 0, cuts = 0;
        foreach (var p in SlotPairs)
        {
            var a = slots[p.a]; var b = slots[p.b];
            Vector3 pa = a.transform.position, pb = b.transform.position;
            Vector2 ha = HalfExtents(a), hb = HalfExtents(b);
            Vector3 d = pb - pa;
            bool alongX = Mathf.Abs(d.x) >= Mathf.Abs(d.z);

            float start, end, ovLo, ovHi;
            int dirA, dirB; // a→b / b→a 월드 방향(0=N 1=E 2=S 3=W)
            if (alongX)
            {
                var (lo, hiS, loHalf, hiHalf) = d.x > 0 ? (pa, pb, ha, hb) : (pb, pa, hb, ha);
                start = lo.x + loHalf.x; end = hiS.x - hiHalf.x;
                ovLo = Mathf.Max(lo.z - loHalf.y, hiS.z - hiHalf.y);
                ovHi = Mathf.Min(lo.z + loHalf.y, hiS.z + hiHalf.y);
                dirA = d.x > 0 ? 1 : 3; dirB = d.x > 0 ? 3 : 1;
            }
            else
            {
                var (lo, hiS, loHalf, hiHalf) = d.z > 0 ? (pa, pb, ha, hb) : (pb, pa, hb, ha);
                start = lo.z + loHalf.y; end = hiS.z - hiHalf.y;
                ovLo = Mathf.Max(lo.x - loHalf.x, hiS.x - hiHalf.x);
                ovHi = Mathf.Min(lo.x + loHalf.x, hiS.x + hiHalf.x);
                dirA = d.z > 0 ? 0 : 2; dirB = d.z > 0 ? 2 : 0;
            }
            if (ovHi - ovLo < CorridorWidth) { Debug.LogWarning($"[CorridorV2] {a.name}↔{b.name} 측면 겹침 {ovHi - ovLo:F1}m < 폭 {CorridorWidth} — 스킵(슬롯 좌표 보정 필요)"); continue; }
            if (end - start < 0.5f) { Debug.LogWarning($"[CorridorV2] {a.name}↔{b.name} 간격 {end - start:F1}m — 존 겹침/근접, 스킵(슬롯 좌표 보정 필요)"); continue; }

            float center = (ovLo + ovHi) * 0.5f;

            // 벽 변으로 붙는 다리는 그 자리 벽 조각을 스폰 시 삭제(WallCuts 기록) —
            // 회전 최적화로 컷을 최소화하고, 불가피한 곳만 뚫는다(팀장 확정: 벽 밀기 방식).
            int lA = (dirA - FinalStepsFor(a) + 4) & 3;
            int lB = (dirB - FinalStepsFor(b) + 4) & 3;
            if (lA == 1 || lA == 2)
            {
                Vector3 mouthA = alongX ? new Vector3(dirA == 1 ? pa.x + ha.x : pa.x - ha.x, 0f, center)
                                        : new Vector3(center, 0f, dirA == 0 ? pa.z + ha.y : pa.z - ha.y);
                a.WallCuts.Add(new Vector4(mouthA.x, 0f, mouthA.z, dirA));
                cuts++;
            }
            if (lB == 1 || lB == 2)
            {
                Vector3 mouthB = alongX ? new Vector3(dirB == 1 ? pb.x + hb.x : pb.x - hb.x, 0f, center)
                                        : new Vector3(center, 0f, dirB == 0 ? pb.z + hb.y : pb.z - hb.y);
                b.WallCuts.Add(new Vector4(mouthB.x, 0f, mouthB.z, dirB));
                cuts++;
            }

            var group = new GameObject($"Cor_{a.SlotID}_{b.SlotID}").transform;
            group.SetParent(root, false);
            // 바닥: 존과 동일한 4m 규격 아트 타일 (top = 존 보행면)
            FillFloor(group, alongX, start, end, center, floorTop, floorMat);
            float wallOff = CorridorWidth * 0.5f + WallThickness * 0.5f;
            FillLine(group, alongX, start, end, center + wallOff, floorTop + WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), wallMat, "wall");
            FillLine(group, alongX, start, end, center - wallOff, floorTop + WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), wallMat, "wall");
            AddMouth(a.SlotID, dirA, center);
            AddMouth(b.SlotID, dirB, center);
            built++;
        }

        // ② 존 둘레 처리 — "갈 수 있는 경우의 수" 보장:
        //    개방변(N/W 로컬) = 다리 입구만 트고 벽으로 채움.
        //    벽 변(E/S 로컬) = 존 자체 벽+문 — 다리가 안 붙은 문은 바깥 벽 패치로 차단(떨어지는 길 방지).
        var wallsRoot = new GameObject("ZoneEdgeWalls").transform;
        wallsRoot.SetParent(geoRoot.transform, false);
        int wallEdges = 0;
        foreach (var s in slots)
        {
            int total = FinalStepsFor(s);
            Vector2 half = HalfExtents(s);
            Vector3 pos = s.transform.position;
            var edgeParent = new GameObject($"Walls_{s.SlotID}_{s.name}").transform;
            edgeParent.SetParent(wallsRoot, false);

            for (int d = 0; d < 4; d++)
            {
                int l = (d - total + 4) & 3;
                bool alongX = d == 0 || d == 2; // N/S 변은 X축 진행
                mouths.TryGetValue((s.SlotID, d), out var gaps);

                if (l == 1 || l == 2) continue; // 벽 변 — 존 자체 벽이 이미 막음(문은 장식일 뿐 구멍 아님)

                // 개방변: 다리 입구만 트고 채움
                float lo = alongX ? pos.x - half.x : pos.z - half.y;
                float hi = alongX ? pos.x + half.x : pos.z + half.y;
                float cross = d switch
                {
                    0 => pos.z + half.y - WallThickness * 0.5f,
                    1 => pos.x + half.x - WallThickness * 0.5f,
                    2 => pos.z - half.y + WallThickness * 0.5f,
                    _ => pos.x - half.x + WallThickness * 0.5f,
                };
                WallLineWithGaps(edgeParent, alongX, lo, hi, cross, gaps, CorridorWidth * 0.5f, wallMat, floorTop);
                wallEdges++;
            }
        }

        // ③ 프리팹 저장 (씬 인스턴스 연결 유지 — 재빌드 시 덮어쓰기)
        PrefabUtility.SaveAsPrefabAssetAndConnect(geoRoot, GeoPrefabPath, InteractionMode.AutomatedAction);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage1.scene);
        Debug.Log($"[GeoV2] 다리 {built}/{SlotPairs.Length} + 개방변 벽 {wallEdges}변 + 벽 컷 지점 {cuts}개 (보행면 y={floorTop:F2}) → {GeoPrefabPath}");
    }

    // 한 직선 구간을 벽으로 채우되 gaps(중심±gapHalf)만 트기. (다리 입구/문) 벽 바닥 = baseY.
    static void WallLineWithGaps(Transform parent, bool alongX, float lo, float hi, float cross, List<float> gaps, float gapHalf, Material mat, float baseY = 0f)
    {
        var sorted = gaps != null ? new List<float>(gaps) : new List<float>();
        sorted.Sort();
        float cursor = lo;
        foreach (float g in sorted)
        {
            if (g - gapHalf > cursor)
                FillLine(parent, alongX, cursor, g - gapHalf, cross, baseY + WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), mat, "wall");
            cursor = Mathf.Max(cursor, g + gapHalf);
        }
        if (hi > cursor)
            FillLine(parent, alongX, cursor, hi, cross, baseY + WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), mat, "wall");
    }

    // 통로 바닥 = 존 바닥과 동일한 4m 규격 아트 타일 (2026-07 팀장 지시: 다리 타일 크기/비율 = 존 큐브 규격).
    // 양끝은 통타일로 존 경계에 밀착시키고, 4m로 나눠떨어지지 않는 나머지는 "중앙 1장만" 트림 —
    // 비율 왜곡이 다리 중앙에 숨고 존 접합부는 항상 정타일이라 연결이 자연스럽다.
    const string FloorTileFbx = "Assets/50.Art/MapGen/MapObj/mesh/floor/Env_floor_basic_typeA.fbx";
    const float TileSize = 4f;

    static void FillFloor(Transform parent, bool alongX, float from, float to, float center, float floorTop, Material mat)
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(FloorTileFbx);
        if (template == null) { Debug.LogError($"[GeoV2] 바닥 타일 FBX 없음 {FloorTileFbx}"); return; }

        // 템플릿 바운즈 실측(피벗 보정용) — 피벗이 코너/센터 어디든 바운즈 기준으로 배치
        var probe = Object.Instantiate(template);
        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var rends = probe.GetComponentsInChildren<Renderer>();
        Bounds tb = rends[0].bounds;
        foreach (var r in rends) tb.Encapsulate(r.bounds);
        Vector3 pivotToCenter = tb.center; // 피벗(0,0,0) → 바운즈 중심
        float topFromPivot = tb.max.y;
        Object.DestroyImmediate(probe);

        float len = to - from;
        int whole = Mathf.FloorToInt(len / TileSize + 0.01f);
        float rem = len - whole * TileSize;
        if (rem < 0.05f) rem = 0f; else whole = Mathf.Min(whole, Mathf.FloorToInt(len / TileSize));

        // 구간 목록: 시작쪽 통타일 절반 → (중앙 트림 1장) → 끝쪽 통타일 절반
        var spans = new List<(float a, float b)>();
        int headTiles = rem > 0f ? (whole + 1) / 2 : whole;
        float cursor = from;
        for (int i = 0; i < headTiles; i++) { spans.Add((cursor, cursor + TileSize)); cursor += TileSize; }
        if (rem > 0f) { spans.Add((cursor, cursor + rem)); cursor += rem; }
        for (int i = headTiles; i < whole; i++) { spans.Add((cursor, cursor + TileSize)); cursor += TileSize; }

        int idx = 0;
        foreach (var (a, b) in spans)
        {
            float s = b - a;
            float factor = s / TileSize;
            var tile = Object.Instantiate(template);
            tile.name = $"tile_{idx++}";
            tile.transform.SetParent(parent, false);
            tile.transform.rotation = Quaternion.identity;
            tile.transform.localScale = alongX ? new Vector3(factor, 1f, 1f) : new Vector3(1f, 1f, factor);

            // 목표 바운즈 중심(월드): 진행축 = 구간 중앙, 교차축 = 통로 중심, 상면 = floorTop
            Vector3 off = pivotToCenter;
            if (alongX) off.x *= factor; else off.z *= factor;
            float along = (a + b) * 0.5f;
            Vector3 target = alongX ? new Vector3(along, 0f, center) : new Vector3(center, 0f, along);
            tile.transform.position = new Vector3(target.x - off.x, floorTop - topFromPivot, target.z - off.z);

            foreach (var r in tile.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            foreach (var mf in tile.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.GetComponent<MeshCollider>() == null)
                    mf.gameObject.AddComponent<MeshCollider>();
        }
    }

    // 진행축(alongX) 구간 [from,to]를 SegLen 단위 큐브로 정확히 채움. size=(가로폭, 높이).
    static void FillLine(Transform parent, bool alongX, float from, float to, float cross, float y, Vector2 size, Material mat, string label)
    {
        float len = to - from;
        int segs = Mathf.Max(1, Mathf.RoundToInt(len / SegLen));
        float step = len / segs;
        for (int i = 0; i < segs; i++)
        {
            float c = from + step * (i + 0.5f);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = $"{label}_{i}";
            box.transform.SetParent(parent, false);
            box.transform.position = alongX ? new Vector3(c, y, cross) : new Vector3(cross, y, c);
            box.transform.localScale = alongX ? new Vector3(step, size.y, size.x) : new Vector3(size.x, size.y, step);
            box.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

}
#endif
