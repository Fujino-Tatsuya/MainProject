using UnityEngine;
using System.Collections.Generic;

// 한 슬롯에 대한 배치 결과 (결정적 — 같은 시드면 서버/클라 동일).
public struct ZonePlacement
{
    public ZoneSlot Slot;
    public ZoneRole Role;
    public GameObject LayoutPrefab;   // 선택된 ZoneLayout 프리팹 (없으면 null). 회전은 슬롯이 프리팹별로 들고 있음(ZoneSlot.Rotations).
}

// 슬롯에 ZoneLayout 프리팹을 선택해 배치 결과를 만든다 (스폰은 MapContentSpawner).
//  - 역할 존(보스/스폰/퀘스트): 카탈로그의 고정 디자인.
//  - 전투 존: (Size, Difficulty) 풀을 시드 셔플 → 같은 크기 슬롯에 1:1 (위치만 랜덤).
//
// 결정성(NGO 디싱크 방지): 슬롯은 SlotID로 정렬된 입력을 기대하고, 크기 순회는 고정 순서,
// 무작위는 주입된 단일 RNG만 사용한다.
public class LayoutPlacer : MonoBehaviour
{
    // 크기 순회 고정 순서 (Dictionary 열거 순서 비결정성 회피)
    private static readonly ZoneSize[] SizeOrder = { ZoneSize.Large, ZoneSize.Medium, ZoneSize.Small };

    public List<ZonePlacement> SelectLayouts(List<ZoneSlot> slots, ZoneLayoutCatalogSO catalog,
                                             int difficulty, System.Random rng)
    {
        var placements = new List<ZonePlacement>();
        if (slots == null || slots.Count == 0) return placements;
        if (catalog == null)
        {
            Edit.LogWarning("[LayoutPlacer] ZoneLayoutCatalog 미연결 — 배치 생략.");
            return placements;
        }

        // 슬롯에 FixedPrefab이 지정돼 있으면 셔플·역할과 무관하게 그 프리팹으로 고정 배치.
        // 고정 프리팹은 전투 셔플 풀에서도 제외한다(다른 슬롯에 중복 등장 방지).
        var pinned = new HashSet<GameObject>();
        foreach (var s in slots)
            if (s != null && s.FixedPrefab != null) pinned.Add(s.FixedPrefab);

        // 1) 고정/역할 존 = 지정 디자인 / 전투 존 = 크기별로 모음
        var combatBySize = new Dictionary<ZoneSize, List<ZoneSlot>>();
        foreach (var slot in slots)
        {
            if (slot == null) continue;

            // 고정 프리팹 슬롯: 역할 무관 즉시 배치.
            if (slot.FixedPrefab != null)
            {
                placements.Add(new ZonePlacement { Slot = slot, Role = slot.AssignedRole, LayoutPrefab = slot.FixedPrefab });
                continue;
            }

            // 역할 전용 디자인이 있으면 고정 배치. 퀘스트는 슬롯에 QuestPrefab이 지정돼 있으면 그걸(고정 페어링),
            // 없으면 전용 디자인 풀 중 rng 랜덤 1개, 그것도 없으면 같은 크기 전투 풀에서 셔플로 뽑는다.
            GameObject roleLayout;
            if (slot.AssignedRole == ZoneRole.Combat)
            {
                roleLayout = null;
            }
            else if (slot.AssignedRole == ZoneRole.Quest)
            {
                if (slot.QuestPrefab != null)
                {
                    roleLayout = slot.QuestPrefab;
                }
                else
                {
                    var questPool = catalog.GetRolePool(slot.AssignedRole, slot.Size);
                    roleLayout = questPool.Count > 0 ? questPool[rng.Next(questPool.Count)] : null;
                }
            }
            else
            {
                roleLayout = catalog.GetRoleLayout(slot.AssignedRole, slot.Size);
            }

            if (slot.AssignedRole == ZoneRole.Combat ||
                (slot.AssignedRole == ZoneRole.Quest && roleLayout == null))
            {
                if (!combatBySize.TryGetValue(slot.Size, out var list))
                    combatBySize[slot.Size] = list = new List<ZoneSlot>();
                list.Add(slot);
            }
            else
            {
                placements.Add(new ZonePlacement
                {
                    Slot = slot,
                    Role = slot.AssignedRole,
                    LayoutPrefab = roleLayout
                });
            }
        }

        // 2) 전투 존: 크기 고정 순서로 풀 셔플 → 1:1 배정
        foreach (var size in SizeOrder)
        {
            if (!combatBySize.TryGetValue(size, out var combatSlots)) continue;

            var pool = catalog.GetCombatPool(size, difficulty);
            if (pinned.Count > 0) pool.RemoveAll(p => pinned.Contains(p)); // 고정 프리팹은 전투 셔플에서 제외
            if (pool.Count == 0)
            {
                Edit.LogWarning($"[LayoutPlacer] {size}/난이도{difficulty} 전투 풀이 비어 있음 — {combatSlots.Count}곳 미배치.");
                foreach (var s in combatSlots)
                    placements.Add(new ZonePlacement { Slot = s, Role = s.AssignedRole, LayoutPrefab = null });
                continue;
            }

            Shuffle(pool, rng);   // 위치만 달라지게 풀을 셔플
            if (pool.Count < combatSlots.Count)
                Edit.LogWarning($"[LayoutPlacer] {size}/난이도{difficulty} 디자인 {pool.Count}개 < 슬롯 {combatSlots.Count}개 — 일부 재사용됨.");

            for (int i = 0; i < combatSlots.Count; i++)
                placements.Add(new ZonePlacement
                {
                    Slot = combatSlots[i],
                    Role = combatSlots[i].AssignedRole, // 퀘스트 슬롯(전용 디자인 없음)도 풀 셔플로 오므로 Role 보존
                    LayoutPrefab = pool[i % pool.Count]
                });
        }

        return placements;
    }

    // Fisher–Yates (결정적: 주입된 rng만 사용)
    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
