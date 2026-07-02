#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 일회성 와이어링(에디터 전용): 조립된 ZoneLayout 프리팹 → 고정 ZoneSlot 스켈레톤 +
// ZoneLayoutCatalog 등록 + MapGenerator 참조 연결 + 임시 Zone_* 숨김. + 셔플 generate 메뉴.
// 슬롯 위치는 프레임 기준(2번째 이미지) — 현재 근사치, 이후 보정.
public static class ZoneWiring
{
    const string PrefabDir = "Assets/50.Art/MapGen/MapObj/ZoneLayout/Prefabs";
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
        new Slot{ label="S_TR",   size=ZoneSize.Small,  pos=new Vector3( 71f,0f, 48f) },         // 상-우(고정 소형 전투)
        new Slot{ label="L_left", size=ZoneSize.Large,  pos=new Vector3(-59f,0f, 14f) },         // 좌(분수)
        new Slot{ label="M_ctr",  size=ZoneSize.Medium, pos=new Vector3(  0f,0f,  0f), q=true }, // 중앙(quest후보)
        new Slot{ label="M_right",size=ZoneSize.Medium, pos=new Vector3( 48f,0f,  5f), rotY=90f, q=true }, // 우-중(가로, 44→48: S_TR 다리 겹침 확보)
        new Slot{ label="M_BL",   size=ZoneSize.Medium, pos=new Vector3(-71f,0f,-36f), rotY=90f, q=true }, // 하-좌(가로)
        new Slot{ label="M_BC",   size=ZoneSize.Medium, pos=new Vector3(-35f,0f,-41f), q=true }, // 하-중좌(-27→-35: L_left 다리 겹침 확보)
        new Slot{ label="S_BL",   size=ZoneSize.Small,  pos=new Vector3(  2f,0f,-65f), s=true, b=true }, // 하-좌측(스폰/보스입구 후보)
        new Slot{ label="L_BR",   size=ZoneSize.Large,  pos=new Vector3( 46f,0f,-44f) },         // 하-우(구조물)
    };

    // 카탈로그 — 블렌더 통짜 FBX 임포트본(Mesh_zone, 2026-07 교체).
    // 대형: 3디자인 ↔ 3슬롯 시드 셔플(재사용 없음).
    // 중형: 퀘스트 슬롯(4후보 중 랜덤 1곳)=zone_M_typeQuest 전용 디자인(FBX 대기 — 수령 전엔
    //       LayoutPlacer 폴백으로 전투 풀에서 셔플), 나머지 3슬롯=M_typeA/B/C 셔플.
    // 소형: S_typeA=우상단 고정 전투(풀 1종↔1슬롯), typeBoss=보스맵 입구, typeStart=스폰
    //       (좌상/좌하 후보 2곳에 스폰/보스입구가 매판 랜덤 배정).
    static readonly (string name, ZoneSize size, ZoneRole role)[] CatEntries =
    {
        ("zone_L_typeA", ZoneSize.Large, ZoneRole.Combat),
        ("zone_L_typeB", ZoneSize.Large, ZoneRole.Combat),
        ("zone_L_typeC", ZoneSize.Large, ZoneRole.Combat),
        ("zone_M_typeA", ZoneSize.Medium, ZoneRole.Combat),
        ("zone_M_typeB", ZoneSize.Medium, ZoneRole.Combat),
        ("zone_M_typeC", ZoneSize.Medium, ZoneRole.Combat),
        ("zone_M_typeQuest", ZoneSize.Medium, ZoneRole.Quest), // FBX 미수령 — 있으면 자동 등록
        ("zone_S_typeA", ZoneSize.Small, ZoneRole.Combat),
        ("zone_S_typeBoss", ZoneSize.Small, ZoneRole.BossRoom),
        ("zone_S_typeStart", ZoneSize.Small, ZoneRole.PlayerSpawn),
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

        // 연결 그래프 → 슬롯별 다리 방향(월드 지배축) 플래그
        foreach (var p in SlotPairs)
        {
            var a = created[p.a]; var b = created[p.b];
            Vector3 d = b.transform.position - a.transform.position;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.z))
            {
                if (d.x > 0) { a.ConnE = true; b.ConnW = true; }
                else         { a.ConnW = true; b.ConnE = true; }
            }
            else
            {
                if (d.z > 0) { a.ConnN = true; b.ConnS = true; }
                else         { a.ConnS = true; b.ConnN = true; }
            }
        }

        var cat = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);
        if (cat == null) { Debug.LogError($"[Wire] 카탈로그 없음 {CatalogPath}"); return; }
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

    [MenuItem("Tools/MapGen/Test Generate (shuffle, seed 12345)")]
    static void Gen12345() => RunGen(12345);

    [MenuItem("Tools/MapGen/Test Generate (shuffle, seed 99)")]
    static void Gen99() => RunGen(99);

    static void RunGen(int seed)
    {
        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null) { Debug.LogError("[Gen] MapGenerator 없음"); return; }
        mg.Generate(seed, 0);
    }

    // ---------------- 다리 v2 (Tex_zone 머티리얼, 슬롯 기준 절차 재생성) ----------------

    const string TexDir = "Assets/50.Art/texture/Tex_zone";
    const float CorridorWidth = 6f;   // 통로 폭 (v1 MapCorridors.Width 승계)
    const float WallHeight = 3f;
    const float WallThickness = 0.5f;
    const float SegLen = 4f;          // 프리미티브 세그먼트 길이(UV 반복 밀도)

    // 슬롯의 월드 half-extents (회전 반영: 90°면 x/z 스왑)
    static Vector2 HalfExtents(ZoneSlot s)
    {
        Vector2 half = s.Size switch
        {
            ZoneSize.Large  => new Vector2(20.5f, 20.5f),
            ZoneSize.Medium => new Vector2(10.5f, 20.5f),
            _               => new Vector2(10.5f, 10.5f),
        };
        int steps = Mathf.RoundToInt(s.transform.eulerAngles.y / 90f) & 3;
        return (steps & 1) == 1 ? new Vector2(half.y, half.x) : half;
    }

    [MenuItem("Tools/MapGen/Build Corridors V2 (Tex_zone)")]
    static void BuildCorridorsV2()
    {
        var stage1 = GameObject.Find("Stage1");
        if (stage1 == null) { Debug.LogError("[CorridorV2] Stage1 없음"); return; }

        var slots = new List<ZoneSlot>(Object.FindObjectsByType<ZoneSlot>(FindObjectsSortMode.None));
        slots.Sort((a, b) => a.SlotID.CompareTo(b.SlotID));
        if (slots.Count == 0) { Debug.LogError("[CorridorV2] ZoneSlot 없음 — 먼저 Wire 실행"); return; }

        var floorMat = AssetDatabase.LoadAssetAtPath<Material>($"{TexDir}/zone_floor_basic.mat");
        var wallMat = AssetDatabase.LoadAssetAtPath<Material>($"{TexDir}/zone_wall_basic.mat");
        if (floorMat == null || wallMat == null) { Debug.LogError("[CorridorV2] Tex_zone 머티리얼 없음 — 먼저 Import All 실행"); return; }

        var oldV2 = stage1.transform.Find("CorridorsV2");
        if (oldV2 != null) Object.DestroyImmediate(oldV2.gameObject);
        var root = new GameObject("CorridorsV2").transform;
        root.SetParent(stage1.transform, false);

        // 구 다리는 비활성 (텍스처/위치 안 맞음 — 삭제는 검증 후)
        var oldCorr = stage1.transform.Find("MapGeometry/Corridors");
        if (oldCorr != null) oldCorr.gameObject.SetActive(false);

        int built = 0;
        foreach (var p in SlotPairs)
        {
            var a = slots[p.a]; var b = slots[p.b];
            Vector3 pa = a.transform.position, pb = b.transform.position;
            Vector2 ha = HalfExtents(a), hb = HalfExtents(b);
            Vector3 d = pb - pa;
            bool alongX = Mathf.Abs(d.x) >= Mathf.Abs(d.z);

            float start, end, center;
            if (alongX)
            {
                var (lo, hiS, loHalf, hiHalf) = d.x > 0 ? (pa, pb, ha, hb) : (pb, pa, hb, ha);
                start = lo.x + loHalf.x; end = hiS.x - hiHalf.x;
                float ovLo = Mathf.Max(lo.z - loHalf.y, hiS.z - hiHalf.y);
                float ovHi = Mathf.Min(lo.z + loHalf.y, hiS.z + hiHalf.y);
                if (ovHi - ovLo < CorridorWidth) { Debug.LogWarning($"[CorridorV2] {a.name}↔{b.name} 측면 겹침 {ovHi - ovLo:F1}m < 폭 {CorridorWidth} — 스킵(슬롯 좌표 보정 필요)"); continue; }
                center = (ovLo + ovHi) * 0.5f;
            }
            else
            {
                var (lo, hiS, loHalf, hiHalf) = d.z > 0 ? (pa, pb, ha, hb) : (pb, pa, hb, ha);
                start = lo.z + loHalf.y; end = hiS.z - hiHalf.y;
                float ovLo = Mathf.Max(lo.x - loHalf.x, hiS.x - hiHalf.x);
                float ovHi = Mathf.Min(lo.x + loHalf.x, hiS.x + hiHalf.x);
                if (ovHi - ovLo < CorridorWidth) { Debug.LogWarning($"[CorridorV2] {a.name}↔{b.name} 측면 겹침 {ovHi - ovLo:F1}m < 폭 {CorridorWidth} — 스킵(슬롯 좌표 보정 필요)"); continue; }
                center = (ovLo + ovHi) * 0.5f;
            }

            if (end - start < 0.5f) { Debug.LogWarning($"[CorridorV2] {a.name}↔{b.name} 간격 {end - start:F1}m — 존 겹침/근접, 스킵(슬롯 좌표 보정 필요)"); continue; }

            var group = new GameObject($"Cor_{a.SlotID}_{b.SlotID}").transform;
            group.SetParent(root, false);
            // 바닥 스트립 (top y=0) + 측벽 2줄
            FillLine(group, alongX, start, end, center, -0.2f, new Vector2(CorridorWidth, 0.4f), floorMat, "floor");
            float wallOff = CorridorWidth * 0.5f + WallThickness * 0.5f;
            FillLine(group, alongX, start, end, center + wallOff, WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), wallMat, "wall");
            FillLine(group, alongX, start, end, center - wallOff, WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), wallMat, "wall");
            built++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage1.scene);
        Debug.Log($"[CorridorV2] 다리 {built}/{SlotPairs.Length} 생성 (Tex_zone 머티리얼, 구 Corridors 비활성). 스킵된 쌍은 슬롯 좌표 보정 후 재실행.");
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

    // ---------------- 퀘스트 임시 존 (기본 5x10: 바닥 + 4변 벽, 변마다 6m 출입구) ----------------

    [MenuItem("Tools/MapGen/Build Temp Quest Zone Prefab")]
    static void BuildTempQuestZone()
    {
        var floorMat = AssetDatabase.LoadAssetAtPath<Material>($"{TexDir}/zone_floor_basic.mat");
        var wallMat = AssetDatabase.LoadAssetAtPath<Material>($"{TexDir}/zone_wall_basic.mat");
        if (floorMat == null || wallMat == null) { Debug.LogError("[QuestZone] Tex_zone 머티리얼 없음 — 먼저 Import All 실행"); return; }

        const float hx = 10.5f, hz = 20.5f; // 중형 21x41 half
        var root = new GameObject("zone_M_typeQuest");

        // 바닥 (top y=0)
        var floor = root.transform;
        int cols = 6, rows = 12;
        float sx = hx * 2f / cols, sz = hz * 2f / rows;
        for (int cx = 0; cx < cols; cx++)
            for (int cz = 0; cz < rows; cz++)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = $"floor_{cx}_{cz}";
                box.transform.SetParent(floor, false);
                box.transform.position = new Vector3(-hx + sx * (cx + 0.5f), -0.2f, -hz + sz * (cz + 0.5f));
                box.transform.localScale = new Vector3(sx, 0.4f, sz);
                box.GetComponent<Renderer>().sharedMaterial = floorMat;
            }

        // 4변 벽 — 변 중앙 6m 출입구
        void WallRuns(bool alongX, float cross, float lo, float hi)
        {
            float mid = (lo + hi) * 0.5f;
            FillLine(root.transform, alongX, lo, mid - CorridorWidth * 0.5f, cross, WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), wallMat, "wall");
            FillLine(root.transform, alongX, mid + CorridorWidth * 0.5f, hi, cross, WallHeight * 0.5f, new Vector2(WallThickness, WallHeight), wallMat, "wall");
        }
        WallRuns(true, hz - WallThickness * 0.5f, -hx, hx);   // N
        WallRuns(true, -hz + WallThickness * 0.5f, -hx, hx);  // S
        WallRuns(false, hx - WallThickness * 0.5f, -hz, hz);  // E
        WallRuns(false, -hx + WallThickness * 0.5f, -hz, hz); // W

        var layout = root.AddComponent<ZoneLayout>();
        layout.Size = ZoneSize.Medium;
        layout.Role = ZoneRole.Quest;
        layout.Difficulty = 0;
        layout.ThemeName = "Factory(temp)";
        layout.OpenN = layout.OpenE = layout.OpenS = layout.OpenW = true;

        string path = $"{PrefabDir}/zone_M_typeQuest.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[QuestZone] 임시 퀘스트 존 생성 → {path} (아트 정식본 오면 같은 이름 FBX 임포트로 교체)");
    }
}
#endif
