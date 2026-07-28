using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 일회성 저작 도구 — MapScene 몬스터 통합 (PLAN 2026-07-21 §2·§3).
//
//  §2 Author ZoneLayouts: ZoneLayoutCatalog의 Entries를 소스로, 각 존 프리팹에
//     ZoneLayout을 부착하고 Size/Role/Difficulty를 카탈로그와 일치시킨다.
//     Combat/Quest 존에는 렌더 바운즈 기반 스폰 마커(자식 Transform)를 자동 생성
//     (L=4/M=3/S=2/Quest=2, 이미 마커가 있으면 유지 — 수동 조정 존중).
//     Nodes는 비워 둔다 → MapContentSpawner가 존 단위 MonsterSpawnPoints 폴백 사용.
//
//  §3 Author MonsterGroups: MapGenConfig.MonsterGroups에 몹 그룹 풀 등록
//     (YAML 수기 편집 대신 AssetDatabase 경유 — guid/fileID 실수 원천 차단).
public static class MapMonsterAuthoring
{
    const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";
    const string ConfigPath = "Assets/50.Art/MapGen/MapObj/MapZoonSettingObj/MapGenConfig.asset";
    const string MarkerRootName = "MonsterSpawnPoints";

    // 그룹 풀 정의 (GroupID → 프리팹 경로). MapGenConfig에 등록 + 존 배정에 사용.
    static readonly (int id, string name, string prefabPath)[] Groups =
    {
        (1, "Chomp Pack",    "Assets/2.Prefabs/Monster/ChompBot.prefab"),
        (2, "Humanoid Duo",  "Assets/2.Prefabs/Monster/HumanoidBot.prefab"),
        (3, "Tesla Turret",  "Assets/2.Prefabs/Monster/TeslaBot.prefab"),
        (4, "Mortar Squad",  "Assets/2.Prefabs/Monster/MortarBot.prefab"),
        (5, "Gauntlet Elite","Assets/2.Prefabs/Monster/GauntletBot.prefab"),
    };

    [MenuItem("Tools/Map/Authoring/Author ZoneLayouts (from Catalog)")]
    public static void AuthorZoneLayouts()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);
        if (catalog == null || catalog.Entries == null)
        {
            Debug.LogError($"[MapMonsterAuthoring] 카탈로그 로드 실패: {CatalogPath}");
            return;
        }

        // (Size,Role) 풀 내 순번 — 그룹 배정 결정성용.
        var poolIndex = new Dictionary<(ZoneSize, ZoneRole), int>();
        int changed = 0;

        foreach (var entry in catalog.Entries)
        {
            if (entry.Prefab == null) continue;
            string path = AssetDatabase.GetAssetPath(entry.Prefab);
            if (string.IsNullOrEmpty(path)) continue;

            var key = (entry.Size, entry.Role);
            poolIndex.TryGetValue(key, out int idx);
            poolIndex[key] = idx + 1;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ZoneLayout layout = root.GetComponent<ZoneLayout>();
                if (layout == null) layout = root.AddComponent<ZoneLayout>();

                layout.Size = entry.Size;
                layout.Role = entry.Role;
                layout.Difficulty = entry.Difficulty;
                layout.MonsterGroupID = PickGroupID(entry.Size, entry.Role, idx);

                int markerTarget = MarkerCount(entry.Size, entry.Role, layout.MonsterGroupID);
                // 기존 마커가 있으면 유지(수동 저작 존중), 없을 때만 자동 생성.
                layout.MonsterSpawnPoints.RemoveAll(t => t == null);
                if (markerTarget > 0 && layout.MonsterSpawnPoints.Count == 0)
                    CreateMarkers(root, layout, markerTarget);
                else if (layout.MonsterSpawnPoints.Count > markerTarget)
                    TrimMarkers(layout, markerTarget);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                changed++;
                Debug.Log($"[MapMonsterAuthoring] {System.IO.Path.GetFileName(path)} — Size:{entry.Size} Role:{entry.Role} Group:{layout.MonsterGroupID} 마커:{layout.MonsterSpawnPoints.Count}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"[MapMonsterAuthoring] ZoneLayout 저작 완료 — 프리팹 {changed}개.");
    }

    [MenuItem("Tools/Map/Authoring/Author MonsterGroups (MapGenConfig)")]
    public static void AuthorMonsterGroups()
    {
        var config = AssetDatabase.LoadAssetAtPath<MapGenConfigSO>(ConfigPath);
        if (config == null)
        {
            Debug.LogError($"[MapMonsterAuthoring] Config 로드 실패: {ConfigPath}");
            return;
        }

        config.MonsterGroups ??= new List<MonsterGroupData>();

        int added = 0, updated = 0;
        foreach (var (id, name, prefabPath) in Groups)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MapMonsterAuthoring] 몬스터 프리팹 없음: {prefabPath}");
                continue;
            }

            var data = new MonsterGroupData
            {
                GroupID = id,
                GroupName = name,
                TargetTier = NodeTier.Tier2_Medium,
                TargetDifficulty = Difficulty.Normal,
                MonsterPrefab = prefab,
                DefaultBehavior = MonsterBehavior.Idle,
                BaseSpawnWeight = 1,
            };

            int existing = config.MonsterGroups.FindIndex(g => g.GroupID == id);
            if (existing >= 0) { config.MonsterGroups[existing] = data; updated++; }
            else { config.MonsterGroups.Add(data); added++; }
        }

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapMonsterAuthoring] MonsterGroups 등록 완료 — 추가 {added} / 갱신 {updated} (총 {config.MonsterGroups.Count}).");
    }

    // (Size,Role) 풀 내 순번으로 그룹 배정 — 결정적. 포탑/원거리/중간보스를 섞는다.
    static int PickGroupID(ZoneSize size, ZoneRole role, int indexInPool)
    {
        if (role == ZoneRole.Quest)
            return new[] { 2, 3 }[indexInPool % 2];
        if (role != ZoneRole.Combat)
            return -1; // BossRoom/PlayerSpawn — 몬스터 없음

        switch (size)
        {
            case ZoneSize.Large: return new[] { 1, 2, 5 }[indexInPool % 3];  // 대형 3번째=GauntletBot(중간보스)
            case ZoneSize.Medium: return new[] { 1, 3, 4 }[indexInPool % 3]; // 중형에 포탑/박격포 섞기
            default: return 1;                                               // 소형=ChompBot
        }
    }

    // 중간보스(GauntletBot) 그룹은 마커 수 = 스폰 수이므로 대형 존 기준 4마리가 되어 과했다.
    // 엘리트는 1마리만 세운다(팀장 피드백 2026-07-29).
    const int EliteGroupID = 5;
    const int EliteMarkerCount = 1;

    static int MarkerCount(ZoneSize size, ZoneRole role, int monsterGroupID)
    {
        if (monsterGroupID == EliteGroupID) return EliteMarkerCount;
        if (role == ZoneRole.Quest) return 2;
        if (role != ZoneRole.Combat) return 0;
        switch (size)
        {
            case ZoneSize.Large: return 4;
            case ZoneSize.Medium: return 3;
            default: return 2;
        }
    }

    // 목표보다 많은 마커는 뒤에서부터 제거한다(수동 조정한 앞쪽 위치를 보존).
    static void TrimMarkers(ZoneLayout layout, int target)
    {
        for (int i = layout.MonsterSpawnPoints.Count - 1; i >= target; i--)
        {
            Transform marker = layout.MonsterSpawnPoints[i];
            layout.MonsterSpawnPoints.RemoveAt(i);
            if (marker != null)
                Object.DestroyImmediate(marker.gameObject);
        }
    }

    // 렌더 바운즈 중심 링 위에 마커 생성(존 로컬). 위치는 러프 — 테스트 후 프리팹에서 수동 조정.
    static void CreateMarkers(GameObject root, ZoneLayout layout, int count)
    {
        Bounds bounds = ComputeRendererBounds(root);

        Transform markerRoot = root.transform.Find(MarkerRootName);
        if (markerRoot == null)
        {
            markerRoot = new GameObject(MarkerRootName).transform;
            markerRoot.SetParent(root.transform, false);
        }

        // 링 반경: 바운즈 짧은 변의 30% (벽에 붙지 않게), 최소 1.5m.
        float radius = Mathf.Max(1.5f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.3f);
        Vector3 centerLocal = root.transform.InverseTransformPoint(bounds.center);

        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i * Mathf.Deg2Rad;
            Vector3 local = centerLocal + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            local.y = 0.1f; // 바닥 살짝 위 — 스폰 후 NavMeshAgent가 메시에 스냅

            var marker = new GameObject($"Spawn_{i}").transform;
            marker.SetParent(markerRoot, false);
            marker.localPosition = local;
            layout.MonsterSpawnPoints.Add(marker);
        }
    }

    static Bounds ComputeRendererBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one * 4f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
