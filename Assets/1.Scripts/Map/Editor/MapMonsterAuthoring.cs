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
    //
    // 인덱스 체계 (팀장 지정, 2026-07-30) — 구현된 몬스터 프리팹 8종을 0~7에 전부 배정한다.
    //   0~4 = 일반 몬스터 / 5~7 = 중간보스 (MonsterDataSO.isMidBoss = true 인 3종과 일치)
    //
    // ⚠️ 이전 체계(1=Chomp, 2=Humanoid, 3=Tesla, 4=Mortar, 5=Gauntlet)와 번호가 어긋난다.
    //    존 프리팹에 저작된 MonsterGroupID 를 그대로 두면 나오는 몹이 조용히 바뀐다 —
    //    메뉴 "Remap Zone MonsterGroupID (old -> new)" 로 함께 옮길 것.
    static readonly (int id, string name, string prefabPath)[] Groups =
    {
        (0, "Chomp Pack",     "Assets/2.Prefabs/Monster/ChompBot.prefab"),
        (1, "Mortar Squad",   "Assets/2.Prefabs/Monster/MortarBot.prefab"),
        (2, "PeekA Turret",   "Assets/2.Prefabs/Monster/PeekABot.prefab"),
        (3, "Tesla Turret",   "Assets/2.Prefabs/Monster/TeslaBot.prefab"),
        (4, "Humanoid Duo",   "Assets/2.Prefabs/Monster/HumanoidBot.prefab"),
        (5, "Spinner Elite",  "Assets/2.Prefabs/Monster/SpinnerBot.prefab"),
        (6, "Wall Elite",     "Assets/2.Prefabs/Monster/WallBot.prefab"),
        (7, "Gauntlet Elite", "Assets/2.Prefabs/Monster/GauntletBot.prefab"),
    };

    // 구 ID → 신 ID. "같은 몬스터를 유지"하는 매핑이다(번호만 바뀌고 몹은 그대로).
    //   1 Chomp→0 / 2 Humanoid→4 / 3 Tesla→3(동일) / 4 Mortar→1 / 5 Gauntlet→7
    // 구 0번은 프리팹이 비어 있어 아무것도 스폰하지 않던 껍데기이므로 매핑 대상이 아니다
    // (신 0번은 ChompBot 이다 — 구 0을 쓰던 존이 있으면 갑자기 몹이 생기니 경고한다).
    static readonly Dictionary<int, int> GroupIdRemap = new Dictionary<int, int>
    {
        { 1, 0 }, { 2, 4 }, { 3, 3 }, { 4, 1 }, { 5, 7 },
    };

    // ── 인덱스 재배정에 맞춰 존 저작값 이동 (2026-07-30) ──────────────────────
    //
    // Groups 를 새 번호 체계로 바꾸면 존 프리팹에 저작된 MonsterGroupID 가 다른 몬스터를 가리킨다
    // (예: ZoneL_typeC = 5 는 GauntletBot 이었는데 새 체계에서 5 = SpinnerBot).
    // 이 도구는 GroupIdRemap 을 따라 저작값을 옮겨 **같은 몬스터가 유지되게** 한다.
    //
    // 반드시 "Author MonsterGroups"(Config 갱신)와 함께, 그리고 한 번만 실행할 것 —
    // 두 번 돌리면 이미 옮긴 값을 또 옮긴다(멱등하지 않다). 그래서 실행 전 값을 로그로 남긴다.
    [MenuItem("Tools/Map/Authoring/Remap Zone MonsterGroupID (old -> new)")]
    public static void RemapZoneMonsterGroupIds()
    {
        if (!EditorUtility.DisplayDialog(
                "MonsterGroupID 재배정",
                "존 프리팹의 MonsterGroupID 를 새 인덱스 체계로 옮깁니다.\n" +
                "1→0, 2→4, 3→3, 4→1, 5→7\n\n" +
                "⚠️ 멱등하지 않습니다. 이미 옮긴 프로젝트에서 또 실행하면 값이 어긋납니다.\n" +
                "진행할까요?",
                "실행", "취소"))
            return;

        int changed = 0;
        var report = new System.Text.StringBuilder("[MapMonsterAuthoring] MonsterGroupID 재배정\n");

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/2.Prefabs/Map" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool dirty = false;
                foreach (ZoneLayout layout in root.GetComponentsInChildren<ZoneLayout>(true))
                {
                    int before = layout.MonsterGroupID;

                    if (before == 0)
                    {
                        // 구 0번은 프리팹이 비어 아무것도 안 나왔다. 새 0번은 ChompBot 이므로 의도치 않게 몹이 생긴다.
                        Debug.LogWarning(
                            $"[MapMonsterAuthoring] {System.IO.Path.GetFileName(path)} 가 구 0번을 쓰고 있다 — " +
                            "새 체계에서 0 = ChompBot 이라 몹이 새로 생긴다. 의도를 확인할 것.", root);
                        continue;
                    }

                    if (!GroupIdRemap.TryGetValue(before, out int after) || before == after)
                        continue;

                    layout.MonsterGroupID = after;

                    // 마커별 지정도 함께 옮긴다 — 이관을 먼저 돌린 프로젝트를 위해.
                    if (layout.MonsterSpawnEntries != null)
                    {
                        for (int i = 0; i < layout.MonsterSpawnEntries.Count; i++)
                        {
                            MonsterSpawnEntry entry = layout.MonsterSpawnEntries[i];
                            if (entry.MonsterGroupID >= 0 &&
                                GroupIdRemap.TryGetValue(entry.MonsterGroupID, out int entryAfter))
                            {
                                entry.MonsterGroupID = entryAfter;
                                layout.MonsterSpawnEntries[i] = entry;
                            }
                        }
                    }

                    report.AppendLine($"  {System.IO.Path.GetFileName(path)} / {layout.name} — {before} → {after}");
                    dirty = true;
                    changed++;
                }

                if (dirty)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        report.AppendLine($"  → {changed}건 재배정. MapScene 의 MonsterGroupID 오버라이드는 0건으로 확인됨(프리팹이 정본).");
        Debug.Log(report.ToString());
    }

    // ── 마커별 몬스터 지정으로 이관 (2026-07-30) ──────────────────────────────
    //
    // 기존 구조는 존 단위 MonsterGroupID 하나로 모든 마커에 같은 몬스터를 세웠다.
    // ZoneLayout.MonsterSpawnEntries 가 마커별 지정을 받으므로, 기존 마커를 그 목록으로 옮긴다.
    //
    // ⚠️ MonsterSpawnPoints 는 지우지 않는다 — 마커 27개를 한 번 유실한 이력이 있어
    //    구버전 경로를 남겨 두고, Entries 가 비면 자동으로 그쪽으로 폴백한다.
    // ⚠️ 존 저작의 정본은 프리팹뿐 아니라 MapScene 의 Stage1 인스턴스 오버라이드에도 있다.
    //    그래서 프리팹과 "열린 씬"을 모두 처리한다(다른 저작 도구와 같은 규칙).
    [MenuItem("Tools/Map/Authoring/Migrate Monster Spawn Points -> Entries")]
    public static void MigrateSpawnPointsToEntries()
    {
        int prefabChanged = 0;
        int sceneChanged = 0;
        var report = new System.Text.StringBuilder("[MapMonsterAuthoring] 마커별 몬스터 지정 이관\n");

        // 프리팹
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/2.Prefabs/Map" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = 0;
                foreach (ZoneLayout layout in root.GetComponentsInChildren<ZoneLayout>(true))
                    n += MigrateOne(layout, report, path);

                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabChanged += n;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 열린 씬 (Stage1 인스턴스 오버라이드가 정본인 경우)
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            int n = 0;
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (ZoneLayout layout in go.GetComponentsInChildren<ZoneLayout>(true))
                {
                    if (MigrateOne(layout, report, scene.name) > 0)
                    {
                        EditorUtility.SetDirty(layout);
                        n++;
                    }
                }

            if (n > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                sceneChanged += n;
            }
        }

        AssetDatabase.SaveAssets();
        report.AppendLine($"  → 프리팹 {prefabChanged}건, 씬 {sceneChanged}건 이관 " +
                          "(씬은 수동 저장 필요). MonsterSpawnPoints 는 폴백용으로 남겨 둠.");
        Debug.Log(report.ToString());
    }

    // 이미 Entries 가 있으면 손대지 않는다(수동 저작 존중). 이관한 항목 수를 돌려준다.
    static int MigrateOne(ZoneLayout layout, System.Text.StringBuilder report, string where)
    {
        if (layout == null) return 0;

        if (layout.MonsterSpawnEntries != null && layout.MonsterSpawnEntries.Count > 0)
        {
            report.AppendLine($"  건너뜀  {where} / {layout.name} — 이미 Entries {layout.MonsterSpawnEntries.Count}개");
            return 0;
        }

        if (layout.MonsterSpawnPoints == null || layout.MonsterSpawnPoints.Count == 0)
            return 0;

        layout.MonsterSpawnEntries = new List<MonsterSpawnEntry>();
        foreach (Transform marker in layout.MonsterSpawnPoints)
        {
            if (marker == null) continue;

            // 이관 시점에는 기존 동작을 그대로 재현한다 — 존 기본값을 각 마커에 복사해 두고,
            // 이후 인스펙터에서 마커별로 다른 ID로 바꿔 나가면 된다.
            layout.MonsterSpawnEntries.Add(new MonsterSpawnEntry
            {
                Marker = marker,
                MonsterGroupID = layout.MonsterGroupID,
            });
        }

        report.AppendLine(
            $"  이관    {where} / {layout.name} — 마커 {layout.MonsterSpawnEntries.Count}개, " +
            $"기본 그룹 {layout.MonsterGroupID}");
        return layout.MonsterSpawnEntries.Count;
    }

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
        // 배정 의도는 그대로 유지하고 번호만 새 체계로 옮겼다.
        if (role == ZoneRole.Quest)
            return new[] { 4, 3 }[indexInPool % 2];                          // Humanoid / Tesla
        if (role != ZoneRole.Combat)
            return -1; // BossRoom/PlayerSpawn — 몬스터 없음

        switch (size)
        {
            case ZoneSize.Large: return new[] { 0, 4, 7 }[indexInPool % 3];  // 대형 3번째=GauntletBot(중간보스)
            case ZoneSize.Medium: return new[] { 0, 3, 1 }[indexInPool % 3]; // 중형에 포탑/박격포 섞기
            default: return 0;                                               // 소형=ChompBot
        }
    }

    // 중간보스 그룹은 마커 수 = 스폰 수이므로 대형 존 기준 4마리가 되어 과했다.
    // 엘리트는 1마리만 세운다(팀장 피드백 2026-07-29).
    // 새 체계에서 중간보스는 5(Spinner)·6(Wall)·7(Gauntlet) 세 개다 — 하나만 특례로 두면 나머지가 4마리로 나온다.
    const int MidBossFirstGroupID = 5;
    const int MidBossLastGroupID = 7;
    const int EliteMarkerCount = 1;

    static bool IsMidBossGroup(int groupID) =>
        groupID >= MidBossFirstGroupID && groupID <= MidBossLastGroupID;

    static int MarkerCount(ZoneSize size, ZoneRole role, int monsterGroupID)
    {
        if (IsMidBossGroup(monsterGroupID)) return EliteMarkerCount;
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
