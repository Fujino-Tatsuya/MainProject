using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 존 프리팹 루트에 <see cref="ZoneMonsterSpawnSet"/> 을 배선한다.
///
/// 아트가 존 프리팹에 <see cref="MonsterSpawnPoint"/> 마커만 저작해 두었을 때(2026-08-18 `20d657a`),
/// "이 존의 기본 몬스터가 무엇인가"를 채워 주는 도구다. 지점의 <c>Monster Prefab Override</c> 가
/// 비어 있으면 존의 기본 몬스터가 쓰이므로, 이 컴포넌트가 없으면 그 지점들은 아무것도 스폰하지 않는다.
///
/// 🔴 <b>멱등하다.</b> 이미 있으면 기본 몬스터만 맞추고, 지점의 개별 지정은 건드리지 않는다.
/// 아트가 다시 돌려도 저작이 덮이지 않는다.
///
/// 🔴 <b>이관도 함께 한다</b>(2026-09-09). 구 <c>MonsterSpawner</c>(<c>NetworkBehaviour</c>)와
/// 그것 때문에 NGO 가 되붙인 <c>NetworkObject</c> 를 존 루트에서 <b>제거</b>하고 기본 몬스터를
/// 새 컴포넌트로 옮긴다. 왜 떼는지는 <see cref="ZoneMonsterSpawnSet"/> 주석 — 요약하면
/// 존은 비네트워크라 그 스포너의 <c>IsServer</c> 가 영원히 false 여서 처음부터 저작 데이터로만
/// 쓰였는데, 그 <c>NetworkBehaviour</c> 하나가 인스펙터를 열 때마다 <c>NetworkObject</c> 를
/// 끌고 왔다(끄는 토글이 <c>EditorPrefs</c> = 머신 단위라 팀에 안 먹는다).
///
/// ⚠️ 스폰 지점 목록(<c>Spawn Points</c>)은 <b>일부러 비워 둔다</b> — 비어 있어야 자식 계층에서
/// 자동 수집한다(<see cref="ZoneMonsterSpawnSet.ResolveSpawnPoints"/>). 채워 넣으면 그 뒤에 아트가
/// 마커를 추가해도 반영되지 않는다.
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

                // ── 이관: 구 MonsterSpawner + 그것이 끌고 온 NetworkObject 를 걷는다 ──
                var legacy = root.GetComponentInChildren<MonsterSpawner>(true);
                var netObj = root.GetComponent<Unity.Netcode.NetworkObject>();
                bool migrating = legacy != null || netObj != null;

                var spawner = root.GetComponentInChildren<ZoneMonsterSpawnSet>(true);
                bool addedNow = spawner == null;

                if (dryRun)
                {
                    var todo = new List<string>();
                    if (legacy != null) todo.Add("구 MonsterSpawner 제거");
                    if (netObj != null) todo.Add("NetworkObject 제거");
                    if (addedNow) todo.Add("ZoneMonsterSpawnSet 추가");
                    if (spawner != null && spawner.DefaultMonsterPrefab != monster) todo.Add($"기본 몬스터 → {kv.Value}");

                    if (todo.Count == 0)
                    {
                        lines.Add($"  = {kv.Key} — 이미 {kv.Value} (마커 {markers})");
                        already++;
                    }
                    else
                    {
                        lines.Add($"  ▶ {kv.Key} — {string.Join(" + ", todo)} (마커 {markers})");
                        changed++;
                    }
                    continue;
                }

                // ⚠️ 파괴 뒤에는 Unity 의 == 오버로드가 그 참조를 null 로 취급하므로
                //    로그용 사실을 미리 붙잡아 둔다(안 하면 "이관 안 함"으로 찍힌다).
                bool hadLegacy = legacy != null;
                bool hadNetObj = netObj != null;

                // 🔴 순서가 중요하다 — NetworkObject 를 먼저 지우면 NGO 가 남은
                //    NetworkBehaviour(구 스포너)를 보고 되붙일 수 있다. 스포너부터 지운다.
                if (hadLegacy) Object.DestroyImmediate(legacy, true);
                if (hadNetObj) Object.DestroyImmediate(netObj, true);

                if (addedNow) spawner = root.AddComponent<ZoneMonsterSpawnSet>();

                var so = new SerializedObject(spawner);
                var prefabProp = so.FindProperty("defaultMonsterPrefab");

                if (!migrating && !addedNow && prefabProp.objectReferenceValue == monster)
                {
                    lines.Add($"  = {kv.Key} — 이미 {kv.Value} (마커 {markers})");
                    already++;
                    continue;
                }

                prefabProp.objectReferenceValue = monster;
                // 자동 수집을 살리려면 목록은 비어 있어야 한다(클래스 주석 참조).
                so.FindProperty("spawnPoints").ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, zonePath);
                string migrated = migrating
                    ? $"[이관: {(hadLegacy ? "구 스포너 " : "")}{(hadNetObj ? "NetworkObject " : "")}제거] "
                    : "";
                lines.Add($"  ✓ {kv.Key} — {migrated}기본 몬스터 {kv.Value} (마커 {markers})");
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
