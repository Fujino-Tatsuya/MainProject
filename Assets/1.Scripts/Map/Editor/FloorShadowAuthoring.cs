using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 지면 바닥의 그림자 드리우기(Cast Shadows)를 끈다 — PLAN-cleanup-optimization S4 (팀장 10-06).
///
/// 왜: 바닥 그림자는 바닥 **아래**로 떨어져 보일 일이 없는데, 화면 안이라 컬링을 통과해 그림자 맵에
///     매번 그려진다(드로우·정점 비용만 쓰고 결과 0). 받는 그림자(Receive Shadows)는 그대로라 캐릭터·벽
///     그림자는 바닥에 계속 보인다.
/// 🔴 높은 곳의 바닥(통로·다리·윗층)은 아래층에 그림자가 보이므로 **건드리지 않는다.** 같은 바닥 프리팹이
///    지면·높은 곳에 함께 쓰여서 원본 프리팹이 아니라 **존 프리팹 안의 배치별**로 끈다(SVN 원본 무수정).
///
/// 판정: 바닥 = (Ground 레이어 또는 이름에 floor/ground/tile) + 얇은 판(높이 &lt; 0.6m) − 조명류.
///       지면 = 그 존에서 **가장 흔한 바닥 윗면 높이** + 0.3m 이하(움푹 꺼진 구역도 지면으로 본다).
/// 멱등 — 다시 돌려도 같은 결과. 새 존을 만들면 Zones 목록에 넣고 돌린다.
/// </summary>
static class FloorShadowAuthoring
{
    static readonly string[] Zones =
    {
        "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneL_typeA.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneL_typeB.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneL_typeC.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneM_typeA.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneM_typeB.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneS_typeA.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Zones/Zone_typeQuest01.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Zones/bossroom.prefab",
        "Assets/2.Prefabs/Environment/Layouts/Stages/StageTutorial.prefab",
    };

    const float ThinY = 0.6f;
    const float GroundTolerance = 0.3f;
    const string Tag = "[FloorShadow]";

    [MenuItem("Tools/Map/Authoring/지면 바닥 그림자 끄기 (보고만)")]
    static void Report() => Run(apply: false);

    [MenuItem("Tools/Map/Authoring/지면 바닥 그림자 끄기 (적용)")]
    static void Apply() => Run(apply: true);

    static void Run(bool apply)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        var sb = new StringBuilder();
        int totalOff = 0, totalKeep = 0, totalChanged = 0;

        foreach (string path in Zones)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                MeshRenderer[] all = root.GetComponentsInChildren<MeshRenderer>(true);
                List<MeshRenderer> floors = all.Where(r => IsFloor(r, groundLayer)).ToList();
                float baseY = ModeTop(floors);

                int off = 0, keep = 0, changed = 0;
                var keepNames = new Dictionary<string, int>();
                foreach (MeshRenderer r in floors)
                {
                    bool ground = r.bounds.max.y - baseY <= GroundTolerance;
                    if (!ground)
                    {
                        keep++;
                        string k = MeshName(r);
                        keepNames[k] = keepNames.TryGetValue(k, out int c) ? c + 1 : 1;
                        continue;
                    }
                    off++;
                    if (r.shadowCastingMode != ShadowCastingMode.Off)
                    {
                        changed++;
                        if (apply) r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }

                if (apply && changed > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, path);

                totalOff += off; totalKeep += keep; totalChanged += changed;
                sb.AppendLine($"{Path.GetFileNameWithoutExtension(path)}: 기준 바닥 y={baseY:F2} · 지면 바닥 {off}(이번에 끔 {changed}) · 높은 곳 유지 {keep}" +
                              (keep > 0 ? " [" + string.Join(", ", keepNames.Select(kv => $"{kv.Key}×{kv.Value}")) + "]" : ""));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        Debug.Log($"{Tag} {(apply ? "적용" : "보고")} — 지면 {totalOff}(변경 {totalChanged}) · 높은 곳 유지 {totalKeep}\n{sb}");
    }

    static bool IsFloor(MeshRenderer r, int groundLayer)
    {
        Bounds b = r.bounds;
        if (b.size.y >= ThinY || Mathf.Max(b.size.x, b.size.z) <= 0.5f) return false;
        if (NameHas(r, "lamp", "light")) return false;
        return r.gameObject.layer == groundLayer || NameHas(r, "floor", "ground", "tile");
    }

    // 바닥 윗면 높이의 최빈값(0.1m 단위) — 존의 "기본 바닥 높이".
    static float ModeTop(List<MeshRenderer> floors)
    {
        if (floors.Count == 0) return 0f;
        return floors.GroupBy(r => Mathf.Round(r.bounds.max.y * 10f) / 10f)
                     .OrderByDescending(g => g.Count()).First().Key;
    }

    static bool NameHas(MeshRenderer r, params string[] keys)
    {
        string n = r.name.ToLowerInvariant();
        string m = MeshName(r).ToLowerInvariant();
        return keys.Any(k => n.Contains(k) || m.Contains(k));
    }

    static string MeshName(MeshRenderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : r.name;
    }
}
