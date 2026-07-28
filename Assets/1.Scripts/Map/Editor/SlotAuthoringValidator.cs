using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// 슬롯 저작 상태 검증 도구.
//
// v11은 (슬롯 × 프리팹) 조합마다 회전·위치가 저작돼 있어야 존이 통로에 붙는다. 저작이 빠진 조합은
// 런타임에 baseline 위치 + 0°로 떨어져 **문이 어긋나고 바닥이 벌어진 것처럼** 보인다. 어느 조합이
// 뽑히는지는 시드마다 달라서 Test Generate 한두 번으로는 드러나지 않는다.
//
// 이 도구는 카탈로그가 각 슬롯에 넣을 수 있는 프리팹을 모두 나열해 저작 여부를 표로 보여준다.
// 프리팹이 재생성돼 GUID가 바뀌면 기존 저작 항목은 참조를 잃고(null) 남는데, 그것도 함께 보고한다.
public static class SlotAuthoringValidator
{
    const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";
    const string StagePath = "Assets/2.Prefabs/Map/Stage1.prefab";

    [MenuItem("Tools/Map/Authoring/Validate Slot Authoring")]
    public static void Validate()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);
        if (catalog == null || catalog.Entries == null)
        {
            Debug.LogError($"[SlotAuthoring] 카탈로그 로드 실패: {CatalogPath}");
            return;
        }

        GameObject stage = PrefabUtility.LoadPrefabContents(StagePath);

        try
        {
            ZoneSlot[] slots = stage.GetComponentsInChildren<ZoneSlot>(true);
            if (slots.Length == 0)
            {
                Debug.LogError($"[SlotAuthoring] {StagePath}에 ZoneSlot이 없다.");
                return;
            }

            var report = new StringBuilder();
            int missingTotal = 0;
            int danglingTotal = 0;

            foreach (ZoneSlot slot in slots)
            {
                List<GameObject> candidates = CollectCandidates(catalog, slot);

                var missing = new List<string>();
                foreach (GameObject candidate in candidates)
                {
                    bool hasYaw = slot.TryGetYaw(candidate, out _);
                    bool hasPos = slot.TryGetPosition(candidate, out _);
                    if (hasYaw && hasPos) continue;

                    missing.Add($"{candidate.name}({(hasYaw ? "" : "회전")}{(!hasYaw && !hasPos ? "+" : "")}{(hasPos ? "" : "위치")} 없음)");
                }

                int dangling = 0;
                if (slot.Rotations != null)
                    foreach (ZoneSlot.RotationEntry entry in slot.Rotations)
                        if (entry.Prefab == null) dangling++;

                missingTotal += missing.Count;
                danglingTotal += dangling;

                report.AppendLine(
                    $"Slot {slot.SlotID} [{slot.Size}] 후보 {candidates.Count}개 / 미저작 {missing.Count}개" +
                    (dangling > 0 ? $" / 참조 잃은 항목 {dangling}개" : "") +
                    (missing.Count > 0 ? "\n    → " + string.Join(", ", missing) : ""));
            }

            string summary =
                $"[SlotAuthoring] 슬롯 {slots.Length}개 검증 — 미저작 조합 {missingTotal}개 / " +
                $"참조 잃은 저작 항목 {danglingTotal}개\n{report}";

            if (missingTotal > 0 || danglingTotal > 0)
            {
                Debug.LogWarning(
                    summary +
                    "\n미저작 조합은 해당 프리팹을 슬롯에 맞춘 뒤 Save Placements로 저장해야 한다. " +
                    "참조 잃은 항목은 프리팹이 재생성돼 GUID가 바뀐 잔재다(무해하지만 정리 권장).");
            }
            else
            {
                Debug.Log(summary + "\n모든 조합이 저작됨.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(stage);
        }
    }

    /// <summary>
    /// 이 슬롯에 실제로 들어올 수 있는 프리팹 전부.
    /// 역할 후보 플래그가 켜져 있으면 그 역할 디자인도 후보다(런타임 RNG가 역할을 배정한다).
    /// </summary>
    static List<GameObject> CollectCandidates(ZoneLayoutCatalogSO catalog, ZoneSlot slot)
    {
        var result = new List<GameObject>();

        if (slot.FixedPrefab != null)
        {
            result.Add(slot.FixedPrefab);
            return result; // 고정 슬롯은 셔플·역할과 무관하다
        }

        void AddUnique(GameObject prefab)
        {
            if (prefab != null && !result.Contains(prefab)) result.Add(prefab);
        }

        // 전투 셔플 풀 — 난이도 0 기준(생성기 기본값).
        foreach (GameObject prefab in catalog.GetCombatPool(slot.Size, 0)) AddUnique(prefab);

        if (slot.IsQuestCandidate)
        {
            if (slot.QuestPrefab != null) AddUnique(slot.QuestPrefab);
            foreach (GameObject prefab in catalog.GetRolePool(ZoneRole.Quest, slot.Size)) AddUnique(prefab);
        }

        if (slot.IsBossCandidate)
            AddUnique(catalog.GetRoleLayout(ZoneRole.BossRoom, slot.Size));

        if (slot.IsSpawnCandidate)
            AddUnique(catalog.GetRoleLayout(ZoneRole.PlayerSpawn, slot.Size));

        return result;
    }
}
