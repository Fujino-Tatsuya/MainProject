using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 존 프리팹 루트에 <see cref="MonsterSpawner"/> 를 배선한다.
///
/// 아트가 존 프리팹에 <see cref="MonsterSpawnPoint"/> 마커만 저작해 두었을 때(2026-08-18 `20d657a`),
/// "이 존의 기본 몬스터가 무엇인가"를 채워 주는 도구다. 지점의 <c>Monster Prefab Override</c> 가
/// 비어 있으면 스포너의 기본 몬스터가 쓰이므로, 스포너가 없으면 그 지점들은 아무것도 스폰하지 않는다.
///
/// 🔴 <b>멱등하다.</b> 이미 스포너가 있으면 기본 몬스터만 맞추고, 지점의 개별 지정은 건드리지 않는다.
/// 아트가 다시 돌려도 저작이 덮이지 않는다.
///
/// ⚠️ 스폰 지점 목록(<c>Spawn Points</c>)은 <b>일부러 비워 둔다</b> — 비어 있어야 자식 계층에서
/// 자동 수집한다(<see cref="MonsterSpawner.ResolveSpawnPoints"/>). 채워 넣으면 그 뒤에 아트가
/// 마커를 추가해도 반영되지 않는다.
///
/// ⚠️ 존 프리팹에 <c>NetworkObject</c> 는 붙이지 않는다 — 존은 비네트워크 규약이고,
/// 맵 생성 경로는 <c>MapContentSpawner.SpawnFromZoneSpawner</c> 가 서버에서 대신 읽어 스폰한다.
/// </summary>
public static class ZoneMonsterSpawnerAuthoring
{
    private const string ZoneFolder = "Assets/2.Prefabs/Map/Zoneprefab";
    private const string MonsterFolder = "Assets/2.Prefabs/Monster";

    // 팀장 확정(2026-08-18): 존 크기별 기본 몬스터.
    // 대형 존에 이미 저작된 중간보스 1지점(Gauntlet/Wall/Humanoid)은 지점 Override 라 그대로 남는다.
    private static readonly Dictionary<string, string> DefaultByZone = new Dictionary<string, string>
    {
        { "ZoneL_typeA",      "MortarBot" },
        { "ZoneL_typeB",      "MortarBot" },
        { "ZoneL_typeC",      "MortarBot" },
        { "ZoneM_typeA",      "PeekABot"  },
        { "ZoneM_typeB",      "PeekABot"  },
        { "ZoneS_typeA",      "ChompBot"  },
        { "Zone_typeQuest01", "ChompBot"  },
        { "Zone_typeQuest02", "ChompBot"  },
    };

    // 🔴 일부러 제외한다(팀장 확정). 스포너를 붙이지 않으면 그 존의 마커는 아무것도 스폰하지 않는다.
    //    ZoneS_typeStart 에는 마커 4개가 저작돼 있지만 시작 지점이라 전투를 붙이지 않는다.
    //    ZoneM_typeC / ZoneS_typeBossEnter 는 마커가 0개다.
    private static readonly string[] Excluded =
    {
        "ZoneS_typeStart", "ZoneS_typeBossEnter", "ZoneM_typeC",
    };

    [MenuItem("Tools/Map/Authoring/존 몬스터 스포너 배선 (적용)")]
    public static void Apply() => Run(dryRun: false);

    [MenuItem("Tools/Map/Authoring/존 몬스터 스포너 배선 — 검증 (읽기 전용)")]
    public static void Validate() => Run(dryRun: true);

    private static void Run(bool dryRun)
    {
        string tag = dryRun ? "검증" : "적용";
        int changed = 0, already = 0, failed = 0;
        var lines = new List<string>();

        foreach (var kv in DefaultByZone.OrderBy(k => k.Key))
        {
            string zonePath = $"{ZoneFolder}/{kv.Key}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(zonePath) == null)
            {
                lines.Add($"  ✗ {kv.Key} — 프리팹 없음 ({zonePath})");
                failed++;
                continue;
            }

            GameObject monster = FindMonster(kv.Value);
            if (monster == null)
            {
                lines.Add($"  ✗ {kv.Key} — 몬스터 프리팹 '{kv.Value}' 를 {MonsterFolder} 에서 못 찾음");
                failed++;
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(zonePath);
            try
            {
                int markers = root.GetComponentsInChildren<MonsterSpawnPoint>(true).Length;
                var spawner = root.GetComponentInChildren<MonsterSpawner>(true);
                bool addedNow = spawner == null;
                if (addedNow) spawner = root.AddComponent<MonsterSpawner>();

                var so = new SerializedObject(spawner);
                var prefabProp = so.FindProperty("defaultMonsterPrefab");
                bool needsWrite = addedNow || prefabProp.objectReferenceValue != monster;

                if (!needsWrite)
                {
                    lines.Add($"  = {kv.Key} — 이미 {kv.Value} (마커 {markers})");
                    already++;
                    continue;
                }

                if (dryRun)
                {
                    string what = addedNow ? "스포너 추가 + " : "";
                    lines.Add($"  ▶ {kv.Key} — {what}기본 몬스터 → {kv.Value} (마커 {markers})");
                    changed++;
                    continue;
                }

                prefabProp.objectReferenceValue = monster;
                // 자동 수집을 살리려면 목록은 비어 있어야 한다(클래스 주석 참조).
                so.FindProperty("spawnPoints").ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, zonePath);
                lines.Add($"  ✓ {kv.Key} — 기본 몬스터 {kv.Value} (마커 {markers})");
                changed++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        foreach (string skipped in Excluded)
            lines.Add($"  – {skipped} — 제외(팀장 확정)");

        if (!dryRun) AssetDatabase.SaveAssets();

        Debug.Log($"[ZoneSpawner/{tag}] 변경 {changed} / 이미맞음 {already} / 실패 {failed}\n"
                  + string.Join("\n", lines));
    }

    /// <summary>몬스터 프리팹을 이름으로 찾는다. 50.Art(SVN) 쪽 동명 애셋을 피하려고 폴더를 한정한다.</summary>
    private static GameObject FindMonster(string monsterName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{monsterName} t:Prefab", new[] { MonsterFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == monsterName)
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }
}
