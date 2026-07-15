#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 와이어링(에디터 전용): 씬 ZoneVolume → ZoneSlot 스켈레톤 생성 +
// ZoneLayoutCatalog 등록 + MapGenerator 참조 연결 + 임시 Zone_* 숨김. + 셔플 generate 메뉴.
// 배치 소스오브트루스 = ZoneVolume(씬). 볼륨을 옮기고 Wire 재실행하면 맵이 따라온다.
// 2026-07 리팩토링: 통로는 Stage1/Level_wall_hallway 손배치로 고정 — 절차생성/연결그래프/벽컷/개방변 매칭은 폐기.
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

    // 카탈로그 (prefab 폴더, 2026-07 정리 완료).
    // 대형: 3디자인 ↔ 3슬롯 시드 셔플(재사용 없음).
    // 중형: 퀘스트 슬롯(4후보 중 랜덤 1곳) = 전용 디자인 2종(Quest01/02) 중 랜덤 1개.
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
        // Quest: 전용 디자인 2종(Medium) — LayoutPlacer가 이 중 랜덤 1개로 배정.
        // 2026-07-14 아트 재임포트로 파일명 ZoneM_typeQuest0x → Zone_typeQuest0x 로 변경(GUID도 신규).
        ("Zone_typeQuest01", ZoneSize.Medium, ZoneRole.Quest),
        ("Zone_typeQuest02", ZoneSize.Medium, ZoneRole.Quest),
        ("ZoneS_typeA",        ZoneSize.Small, ZoneRole.Combat),
        ("ZoneS_typeBossEnter",ZoneSize.Small, ZoneRole.BossRoom),
        ("ZoneS_typeStart",    ZoneSize.Small, ZoneRole.PlayerSpawn),
    };

    [MenuItem("Tools/MapGen/Wire Slots + Catalog + Refs")]
    static void Wire()
    {
        var stage1 = GameObject.Find("Stage1");
        if (stage1 == null) { Debug.LogError("[Wire] Stage1 없음"); return; }

        // v11: 슬롯은 저작 데이터(위치·회전·FixedPrefab·QuestPrefab·플래그)의 소스오브트루스.
        // 이미 있으면 절대 재생성하지 않는다(재생성 = 저작 전부 소실). 없을 때만 볼륨에서 최초 생성.
        var slotsRoot = stage1.transform.Find("Slots");
        int slotCount;
        if (slotsRoot != null && slotsRoot.GetComponentsInChildren<ZoneSlot>(true).Length > 0)
        {
            slotCount = slotsRoot.GetComponentsInChildren<ZoneSlot>(true).Length;
            Debug.Log($"[Wire] 기존 Slots {slotCount}개 보존(저작 데이터 유지) — 슬롯 재생성 건너뜀, 카탈로그·참조만 갱신.");
        }
        else
        {
            if (slotsRoot != null) Object.DestroyImmediate(slotsRoot.gameObject);
            var newRoot = new GameObject("Slots");
            newRoot.transform.SetParent(stage1.transform, false);

            // 씬 ZoneVolume 수집 (비활성 포함) → ZoneID 순 정렬. (최초 셋업에서만 사용)
            var vols = Object.FindObjectsByType<ZoneVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(v => v.Zone != null).OrderBy(v => v.Zone.ZoneID).ToList();
            if (vols.Count == 0) { Debug.LogError("[Wire] ZoneVolume 없음 — Stage1의 ZoneVolumes 확인"); return; }
            for (int i = 0; i < vols.Count; i++)
                if (vols[i].Zone.ZoneID != i + 1)
                { Debug.LogError($"[Wire] ZoneID 불연속: {vols[i].name} = {vols[i].Zone.ZoneID} (기대 {i + 1}) — ZoneDef 지정 확인"); return; }

            int id = 0;
            foreach (var v in vols)
            {
                bool lx = v.Size.x >= 30f, lz = v.Size.z >= 30f;
                ZoneSize size = lx && lz ? ZoneSize.Large : (lx || lz ? ZoneSize.Medium : ZoneSize.Small);
                float rotY = Mathf.Round(v.transform.eulerAngles.y / 90f) * 90f;

                var go = new GameObject($"Slot_{id}_Z{v.Zone.ZoneID}_{size.ToString()[0]}");
                go.transform.SetParent(newRoot.transform, false);
                go.transform.position = v.transform.position;
                go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                var slot = go.AddComponent<ZoneSlot>();
                slot.SlotID = id++;
                slot.Size = size;
                slot.Footprint = size switch
                {
                    ZoneSize.Large  => new Vector2(40f, 40f),
                    ZoneSize.Medium => new Vector2(20f, 40f),
                    _               => new Vector2(20f, 20f),
                };
                slot.IsQuestCandidate = v.Zone.IsQuestZoneCandidate;
                slot.IsSpawnCandidate = v.Zone.IsPlayerSpawnCandidate;
                slot.IsBossCandidate = v.Zone.IsBossGateCandidate;
            }
            slotCount = id;
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
        Debug.Log($"[Wire] ✔ 슬롯 {slotCount} + 카탈로그 {cat.Entries.Count} + 참조 연결. 임시 Zone_* {hidden} 숨김. (LayoutPlacer={(lpOk ? "OK" : "없음")}, ContentSpawner={(csOk ? "OK" : "없음")})");
    }

    // 매번 다른 배치: 대형 3 순열 / 중형 3+퀘스트(4곳 중 1) / 스폰↔보스입구(좌상·좌하 랜덤)
    [MenuItem("Tools/MapGen/Test Generate (random seed)")]
    static void GenRandom() => RunGen(System.Environment.TickCount);

    // 고정 시드 — 재현/디버그용
    [MenuItem("Tools/MapGen/Test Generate (seed 12345, 재현용)")]
    static void Gen12345() => RunGen(12345);

    static void RunGen(int seed)
    {
        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null) { Debug.LogError("[Gen] MapGenerator 없음"); return; }
        mg.Generate(seed, 0);
    }
}
#endif
