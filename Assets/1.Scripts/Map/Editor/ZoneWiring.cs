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
    static readonly Slot[] Slots =
    {
        new Slot{ label="Spawn",  size=ZoneSize.Small,  pos=new Vector3(-48f,0f, 65f), s=true }, // 상-좌(스폰 후보)
        new Slot{ label="L_top",  size=ZoneSize.Large,  pos=new Vector3(  6f,0f, 63f) },         // 상-중(구조물)
        new Slot{ label="S_TR",   size=ZoneSize.Small,  pos=new Vector3( 71f,0f, 48f) },         // 상-우(고정 소형)
        new Slot{ label="L_left", size=ZoneSize.Large,  pos=new Vector3(-59f,0f, 14f) },         // 좌(분수)
        new Slot{ label="M_ctr",  size=ZoneSize.Medium, pos=new Vector3(  0f,0f,  0f), q=true }, // 중앙(quest후보)
        new Slot{ label="M_right",size=ZoneSize.Medium, pos=new Vector3( 44f,0f,  5f), rotY=90f, q=true }, // 우-중(가로)
        new Slot{ label="M_BL",   size=ZoneSize.Medium, pos=new Vector3(-71f,0f,-36f), rotY=90f, q=true }, // 하-좌(가로)
        new Slot{ label="M_BC",   size=ZoneSize.Medium, pos=new Vector3(-27f,0f,-41f), q=true }, // 하-중좌
        new Slot{ label="S_BC",   size=ZoneSize.Small,  pos=new Vector3(  2f,0f,-65f) },         // 하-중
        new Slot{ label="L_BR",   size=ZoneSize.Large,  pos=new Vector3( 46f,0f,-44f) },         // 하-우(구조물)
    };

    // 카탈로그 9엔트리 — 블렌더 통짜 FBX 임포트본(Mesh_zone, 2026-07 교체).
    // 중형: 전용 Quest 디자인 없음 → 퀘스트 슬롯도 중형 전투 풀에서 셔플(LayoutPlacer 폴백).
    // 소형: 전투 풀 1종(S_typeA — 소형 전투 2슬롯에 재사용) + 보스방/스폰 역할 고정.
    // 보스방(zone_S_typeBoss)은 이 10존 레이아웃에 보스 슬롯이 없어 당장은 미사용(등록만).
    static readonly (string name, ZoneSize size, ZoneRole role)[] CatEntries =
    {
        ("zone_L_typeA", ZoneSize.Large, ZoneRole.Combat),
        ("zone_L_typeB", ZoneSize.Large, ZoneRole.Combat),
        ("zone_L_typeC", ZoneSize.Large, ZoneRole.Combat),
        ("zone_M_typeA", ZoneSize.Medium, ZoneRole.Combat),
        ("zone_M_typeB", ZoneSize.Medium, ZoneRole.Combat),
        ("zone_M_typeC", ZoneSize.Medium, ZoneRole.Combat),
        ("zone_S_typeA", ZoneSize.Small, ZoneRole.Combat),
        ("zone_S_typeBoss", ZoneSize.Small, ZoneRole.BossRoom),
        ("zone_S_typeStart", ZoneSize.Small, ZoneRole.PlayerSpawn),
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
}
#endif
