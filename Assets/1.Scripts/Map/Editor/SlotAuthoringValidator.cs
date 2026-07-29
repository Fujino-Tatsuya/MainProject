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
// ⚠️ 2026-07-29 수정 — 이 도구는 예전에 Stage1.prefab **에셋**을 읽었다. 그런데 저작을 쓰는
// Save Placements는 **씬의 Stage1 인스턴스**에 쓰고 프리팹에 Apply하지 않는다. 그래서 재저작을
// 다 마친 뒤에도 "미저작 9건 / 참조 잃은 항목 9건"을 계속 보고했고, 그걸 믿고 이미 끝난 재저작을
// 다시 하려 드는 사고가 났다. 지금은 열린 씬을 읽는다(= 런타임이 실제로 쓰는 값).
//
// 후보 계산도 넓게 잡지 않는다. 역할 후보가 역할 수와 딱 맞아 절대 Combat이 안 되는 슬롯,
// QuestPrefab이 지정돼 카탈로그 Quest 풀을 안 보는 슬롯, FixedPrefab으로 셔플에서 빠진 프리팹은
// 애초에 도달하지 않는 조합이므로 미저작으로 세지 않는다. (계산은 SlotAuthoringModel)
public static class SlotAuthoringValidator
{
    [MenuItem("Tools/Map/Authoring/Validate Slot Authoring")]
    public static void Validate()
    {
        ZoneLayoutCatalogSO catalog = SlotAuthoringModel.LoadCatalog();
        if (catalog == null) return;

        List<ZoneSlot> slots = SlotAuthoringModel.GatherSceneSlots();
        if (slots.Count == 0)
        {
            Debug.LogError(
                "[SlotAuthoring] 열린 씬에 ZoneSlot이 없습니다. 저작의 정본은 씬의 Stage1 인스턴스이므로 " +
                "4.MapScene을 열고 다시 실행하세요.");
            return;
        }

        List<SlotAuthoringModel.SlotPlan> plans = SlotAuthoringModel.BuildPlans(slots, catalog);

        var report = new StringBuilder();
        int missingTotal = 0;
        int deadTotal = 0;
        int strayTotal = 0;

        foreach (SlotAuthoringModel.SlotPlan plan in plans)
        {
            ZoneSlot slot = plan.Slot;

            var missing = new List<string>();
            foreach (GameObject candidate in plan.Reachable)
            {
                bool hasYaw = slot.TryGetYaw(candidate, out _);
                bool hasPos = slot.TryGetPosition(candidate, out _);
                if (hasYaw && hasPos) continue;

                missing.Add($"{candidate.name}({(hasYaw ? "" : "회전")}{(!hasYaw && !hasPos ? "+" : "")}{(hasPos ? "" : "위치")} 없음)");
            }

            int dead = SlotAuthoringModel.CountDeadEntries(slot);

            // 도달 불가 조합에 남아 있는 저작 = 무해하지만 리포트를 흐리고 인스펙터를 부풀린다.
            var stray = new List<string>();
            if (slot.Rotations != null)
                foreach (ZoneSlot.RotationEntry entry in slot.Rotations)
                    if (entry.Prefab != null && !plan.Reachable.Contains(entry.Prefab))
                        stray.Add(entry.Prefab.name);

            missingTotal += missing.Count;
            deadTotal += dead;
            strayTotal += stray.Count;

            string roles = string.Join("/", plan.PossibleRoles);
            report.AppendLine(
                $"Slot {slot.SlotID} [{slot.Size}] 가능역할 {roles} / 도달가능 {plan.Reachable.Count}개 / 미저작 {missing.Count}개" +
                (dead > 0 ? $" / 참조 잃은 항목 {dead}개" : "") +
                (stray.Count > 0 ? $" / 불필요 저작 {stray.Count}개" : "") +
                (missing.Count > 0 ? "\n    → 미저작: " + string.Join(", ", missing) : "") +
                (stray.Count > 0 ? "\n    → 불필요: " + string.Join(", ", stray) : ""));
        }

        string summary =
            $"[SlotAuthoring] 씬 '{slots[0].gameObject.scene.name}' 슬롯 {slots.Count}개 검증 — " +
            $"미저작 {missingTotal}개 / 참조 잃은 항목 {deadTotal}개 / 불필요 저작 {strayTotal}개\n{report}";

        if (missingTotal > 0)
            Debug.LogWarning(summary + "\n미저작 조합은 해당 프리팹을 슬롯에 맞춘 뒤 Save Placements로 저장해야 한다.");
        else if (deadTotal > 0 || strayTotal > 0)
            Debug.LogWarning(summary +
                "\n도달 가능한 조합은 모두 저작됨. 남은 항목은 'Tools/Map/Authoring/Cleanup Slot Authoring'으로 정리한다.");
        else
            Debug.Log(summary + "\n모든 조합이 저작됨. 잔재 없음.");
    }
}
