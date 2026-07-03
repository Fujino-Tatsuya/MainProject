#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 일회성 와이어링(에디터 전용): 조립된 ZoneLayout 프리팹 → 고정 ZoneSlot 스켈레톤 +
// ZoneLayoutCatalog 등록 + MapGenerator 참조 연결 + 임시 Zone_* 숨김. + 셔플 generate 메뉴.
// 슬롯 위치는 프레임 기준(2번째 이미지) — 현재 근사치, 이후 보정.
public static class ZoneWiring
{
    const string PrefabDir = "Assets/50.Art/MapGen/MapObj/prefab";
    const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";

    struct Slot { public string label; public ZoneSize size; public Vector3 pos; public float rotY; public bool q, s, b; }

    // 고정 스켈레톤 — 마스터 프레임(파란 탑다운) 기준 10존: 대형3(전투) + 중형4(전부 quest후보) + 소형3.
    // 좌표 = 이미지 정규화위치 → 월드(Wworld=210, Hworld=170, y반전) 환산. 스샷 보고 미세보정.
    // 소형 규칙(2026-07 팀장 확정): 우상단 1곳=고정 전투(S_typeA),
    // 나머지 2곳(좌상/좌하)=스폰 후보 겸 보스맵입구 후보 — 한쪽이 스폰이 되면 다른 쪽이 보스입구.
    static readonly Slot[] Slots =
    {
        new Slot{ label="S_TL",   size=ZoneSize.Small,  pos=new Vector3(-48f,0f, 65f), s=true, b=true }, // 상-좌(스폰/보스입구 후보)
        new Slot{ label="L_top",  size=ZoneSize.Large,  pos=new Vector3(  6f,0f, 63f) },         // 상-중(구조물)
        new Slot{ label="S_TR",   size=ZoneSize.Small,  pos=new Vector3(64.5f,0f, 48f) },        // 상-우(고정 소형 전투, 71→64.5: M_right 문(57.5) 앵커 확보)
        new Slot{ label="L_left", size=ZoneSize.Large,  pos=new Vector3(-59f,0f, 14f) },         // 좌(분수)
        new Slot{ label="M_ctr",  size=ZoneSize.Medium, pos=new Vector3(  0f,0f,  0f), q=true }, // 중앙(quest후보)
        new Slot{ label="M_right",size=ZoneSize.Medium, pos=new Vector3( 48f,0f,  5f), rotY=90f, q=true }, // 우-중(가로, 44→48: S_TR 다리 겹침 확보)
        new Slot{ label="M_BL",   size=ZoneSize.Medium, pos=new Vector3(-77.5f,0f,-36f), rotY=90f, q=true }, // 하-좌(가로, -71→-77.5: M_BC 서진에 따른 간격 유지)
        new Slot{ label="M_BC",   size=ZoneSize.Medium, pos=new Vector3(-41.5f,0f,-41f), q=true }, // 하-중좌(-35→-41.5: L_left 문(-48.5) 앵커 확보)
        new Slot{ label="S_BL",   size=ZoneSize.Small,  pos=new Vector3(  2f,0f,-65f), s=true, b=true }, // 하-좌측(스폰/보스입구 후보)
        new Slot{ label="L_BR",   size=ZoneSize.Large,  pos=new Vector3( 46f,0f,-44f) },         // 하-우(구조물)
    };

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

    // 연결 그래프 (신 SlotID, 13연결) — 구 MapCorridors.Pairs(v1 존ID) 이관.
    // 다리 방향(슬롯 Conn 플래그)과 다리 재생성 둘 다 이 표에서 파생.
    static readonly (int a, int b)[] SlotPairs =
    {
        (0, 3), (0, 1),          // S_TL ↔ L_left / L_top
        (1, 2), (1, 4),          // L_top ↔ S_TR / M_ctr
        (3, 4), (3, 7), (3, 6),  // L_left ↔ M_ctr / M_BC / M_BL
        (6, 7),                  // M_BL ↔ M_BC
        (4, 8), (4, 5),          // M_ctr ↔ S_BL / M_right
        (2, 5),                  // S_TR ↔ M_right
        (5, 9), (8, 9),          // M_right ↔ L_BR / S_BL ↔ L_BR
    };

    [MenuItem("Tools/MapGen/Wire Slots + Catalog + Refs")]
    static void Wire()
    {
        var stage1 = GameObject.Find("Stage1");
        if (stage1 == null) { Debug.LogError("[Wire] Stage1 없음"); return; }

        var old = stage1.transform.Find("Slots");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var slotsRoot = new GameObject("Slots");
        slotsRoot.transform.SetParent(stage1.transform, false);

        int id = 0;
        var created = new List<ZoneSlot>();
        foreach (var s in Slots)
        {
            var go = new GameObject($"Slot_{id}_{s.label}");
            go.transform.SetParent(slotsRoot.transform, false);
            go.transform.position = s.pos;
            go.transform.rotation = Quaternion.Euler(0f, s.rotY, 0f);
            var slot = go.AddComponent<ZoneSlot>();
            slot.SlotID = id++;
            slot.Size = s.size;
            slot.Footprint = s.size switch
            {
                ZoneSize.Large  => new Vector2(40f, 40f),  // 10x10 타일
                ZoneSize.Medium => new Vector2(20f, 40f),  // 5x10 타일
                _               => new Vector2(20f, 20f),  // 5x5 타일
            };
            slot.IsQuestCandidate = s.q; slot.IsSpawnCandidate = s.s; slot.IsBossCandidate = s.b;
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

        int hidden = 0;
        var geom = stage1.transform.Find("MapGeometry");
        if (geom != null)
            foreach (Transform c in geom)
                if (c.name.StartsWith("Zone_")) { c.gameObject.SetActive(false); hidden++; }

        // 구 절차배치 모델 잔재 비활성(ZoneSlot이 대체) — 삭제는 검증 후 별도로.
        var volumes = stage1.transform.Find("ZoneVolumes");
        if (volumes != null && volumes.gameObject.activeSelf) { volumes.gameObject.SetActive(false); hidden++; }

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
    const float SegLen = 4f;          // 벽 세그먼트 길이(오브젝트 수 절약 — 바닥은 FillFloor가 1m 단위)

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
            // 바닥: 1×1×1 타일 그리드 (존 내부 타일 규격과 동일, top = 존 보행면)
            FillFloor(group, alongX, start, end, center, floorTop - 0.5f, CorridorWidth, floorMat);
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

    // 통로 바닥을 1×1×1 타일 그리드로 채운다 (존 내부 바닥 규격과 동일, 문 폭/높이 정렬).
    // y = 타일 중심 Y (타일 상면 = y + 0.5 = floorTop).
    static void FillFloor(Transform parent, bool alongX, float from, float to, float center, float y, float width, Material mat)
    {
        int wTiles = Mathf.Max(1, Mathf.RoundToInt(width));
        float halfW = wTiles * 0.5f;
        int lTiles = Mathf.Max(1, Mathf.RoundToInt(to - from));
        for (int li = 0; li < lTiles; li++)
        for (int wi = 0; wi < wTiles; wi++)
        {
            float along = from + li + 0.5f;
            float cross = center - halfW + wi + 0.5f;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = $"tile_{li}_{wi}";
            box.transform.SetParent(parent, false);
            box.transform.position = alongX ? new Vector3(along, y, cross) : new Vector3(cross, y, along);
            box.transform.localScale = Vector3.one;
            box.GetComponent<Renderer>().sharedMaterial = mat;
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

    // ---------------- 문 위치 리포트 (다리↔문 정렬용 표준화 검증) ----------------

    // 각 존 프리팹의 door 피스 중심을 존-로컬 좌표로 출력하고 변(N/E/S/W)을 분류.
    // 같은 크기 존끼리 문 로컬 좌표가 표준화돼 있으면 → 다리 중심을 문 위치에 고정 가능.
    [MenuItem("Tools/MapGen/Report Zone Door Positions")]
    static void ReportDoors()
    {
        var sb = new System.Text.StringBuilder("[Doors] 존-로컬 문 중심 (변 분류, 피벗=존 중앙):\n");
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<ZoneLayout>() == null) continue;
            if (!prefab.name.StartsWith("Zone")) continue;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Object.DestroyImmediate(inst); continue; }
            Bounds all = rends[0].bounds;
            foreach (var r in rends) all.Encapsulate(r.bounds);

            // door 상위 노드 단위로 묶어 중심 계산 (Dupli 조각 개별 출력 방지)
            var doorGroups = new Dictionary<Transform, Bounds>();
            foreach (var r in rends)
            {
                Transform doorRoot = null;
                for (var c = r.transform; c != null; c = c.parent)
                    if (c.name.ToLowerInvariant().StartsWith("door")) doorRoot = c;
                if (doorRoot == null) continue;
                if (doorGroups.TryGetValue(doorRoot, out var b)) { b.Encapsulate(r.bounds); doorGroups[doorRoot] = b; }
                else doorGroups[doorRoot] = r.bounds;
            }

            var items = new List<string>();
            foreach (var kv in doorGroups)
            {
                Vector3 c = kv.Value.center; // 인스턴스가 원점이므로 월드=존로컬
                float dN = all.max.z - c.z, dS = c.z - all.min.z, dE = all.max.x - c.x, dW = c.x - all.min.x;
                float min = Mathf.Min(dN, Mathf.Min(dS, Mathf.Min(dE, dW)));
                string edge = min == dN ? "N" : min == dS ? "S" : min == dE ? "E" : "W";
                // 변 진행축 좌표(존 중앙 기준)로 출력 — 표준화 비교용
                float along = (edge == "N" || edge == "S") ? c.x : c.z;
                items.Add($"{edge}@{along:F1}");
            }
            items.Sort();
            sb.AppendLine($"  {prefab.name} [{inst.GetComponent<ZoneLayout>().Size}]: {string.Join(", ", items)}");
            Object.DestroyImmediate(inst);
        }
        Debug.Log(sb.ToString());
    }

    // ---------------- 퀘스트 임시 존 (기본 5x10: 바닥 + 4변 벽, 변마다 6m 출입구) ----------------

    [MenuItem("Tools/MapGen/Build Temp Quest Zone Prefab")]
    static void BuildTempQuestZone()
    {
        var floorMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/MA_floor.mat");
        var wallMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/MA_Wall_basic.mat");
        if (floorMat == null || wallMat == null) { Debug.LogError("[QuestZone] MapObj/material 머티리얼 없음 (MA_floor.mat / MA_Wall_basic.mat)"); return; }

        const float hx = 10.5f, hz = 20.5f; // 중형 21x41 half
        // 실제 존 보행면 높이에 맞춤 (다리/벽 Y 정렬과 동일 기준)
        float floorTop = MeasureFloorTopY("ZoneM_typeA");
        var root = new GameObject("ZoneM_typeQuest");

        // 바닥 (top = 실측 보행면)
        var floor = root.transform;
        int cols = 6, rows = 12;
        float sx = hx * 2f / cols, sz = hz * 2f / rows;
        for (int cx = 0; cx < cols; cx++)
            for (int cz = 0; cz < rows; cz++)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = $"floor_{cx}_{cz}";
                box.transform.SetParent(floor, false);
                box.transform.position = new Vector3(-hx + sx * (cx + 0.5f), floorTop - 0.2f, -hz + sz * (cz + 0.5f));
                box.transform.localScale = new Vector3(sx, 0.4f, sz);
                box.GetComponent<Renderer>().sharedMaterial = floorMat;
            }

        // 표준 M 규격 준수: N/W 완전 개방, S/E 벽 + 표준 문 위치(문 폭 4m).
        //  E변 문 z=10.5/-9.5, S변 문 x=-0.5 (Report Zone Door Positions 실측값과 동일)
        // S/E 벽은 통짜(문 없음) — 실제 존과 동일 규칙(문은 장식일 뿐, 통로=개방변 N/W)
        WallLineWithGaps(root.transform, true, -hx, hx, -hz + WallThickness * 0.5f, null, 0f, wallMat, floorTop);  // S
        WallLineWithGaps(root.transform, false, -hz, hz, hx - WallThickness * 0.5f, null, 0f, wallMat, floorTop); // E

        var layout = root.AddComponent<ZoneLayout>();
        layout.Size = ZoneSize.Medium;
        layout.Role = ZoneRole.Quest;
        layout.Difficulty = 0;
        layout.ThemeName = "Factory(temp)";
        layout.OpenN = layout.OpenW = true;   // 표준 존과 동일: N/W 개방
        layout.OpenE = layout.OpenS = false;  // 벽+문(문은 다리 앵커가 맞춰줌)

        string path = $"{PrefabDir}/ZoneM_typeQuest.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[QuestZone] 임시 퀘스트 존 생성 → {path} (아트 정식본 오면 같은 이름 FBX 임포트로 교체)");
    }
}
#endif
