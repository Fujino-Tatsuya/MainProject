using UnityEngine;
using System.Collections.Generic;

// 한 슬롯에 대한 배치 결과 (결정적 — 같은 시드면 서버/클라 동일).
public struct ZonePlacement
{
    public ZoneSlot Slot;
    public ZoneRole Role;
    public GameObject LayoutPrefab;   // 선택된 ZoneLayout 프리팹 (없으면 null)
    public int ExtraYawSteps;         // 슬롯 회전 위에 더하는 90° 단위 회전(0~3) — 출입구↔다리 매칭
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
            Debug.LogWarning("[LayoutPlacer] ZoneLayoutCatalog 미연결 — 배치 생략.");
            return placements;
        }

        // 1) 역할 존 = 고정 디자인 / 전투 존 = 크기별로 모음
        var combatBySize = new Dictionary<ZoneSize, List<ZoneSlot>>();
        foreach (var slot in slots)
        {
            if (slot == null) continue;

            // 역할 전용 디자인이 있으면 고정 배치. 퀘스트는 전용 디자인이 없으면
            // 같은 크기 전투 풀에서 셔플로 뽑는다(위치+비주얼 모두 매판 달라짐).
            GameObject roleLayout = slot.AssignedRole == ZoneRole.Combat
                ? null : catalog.GetRoleLayout(slot.AssignedRole, slot.Size);

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
                    LayoutPrefab = roleLayout,
                    ExtraYawSteps = PickYaw(roleLayout, slot, rng)
                });
            }
        }

        // 2) 전투 존: 크기 고정 순서로 풀 셔플 → 1:1 배정
        foreach (var size in SizeOrder)
        {
            if (!combatBySize.TryGetValue(size, out var combatSlots)) continue;

            var pool = catalog.GetCombatPool(size, difficulty);
            if (pool.Count == 0)
            {
                Debug.LogWarning($"[LayoutPlacer] {size}/난이도{difficulty} 전투 풀이 비어 있음 — {combatSlots.Count}곳 미배치.");
                foreach (var s in combatSlots)
                    placements.Add(new ZonePlacement { Slot = s, Role = s.AssignedRole, LayoutPrefab = null });
                continue;
            }

            Shuffle(pool, rng);   // 위치만 달라지게 풀을 셔플
            if (pool.Count < combatSlots.Count)
                Debug.LogWarning($"[LayoutPlacer] {size}/난이도{difficulty} 디자인 {pool.Count}개 < 슬롯 {combatSlots.Count}개 — 일부 재사용됨.");

            for (int i = 0; i < combatSlots.Count; i++)
                placements.Add(new ZonePlacement
                {
                    Slot = combatSlots[i],
                    Role = combatSlots[i].AssignedRole, // 퀘스트 슬롯(전용 디자인 없음)도 풀 셔플로 오므로 Role 보존
                    LayoutPrefab = pool[i % pool.Count],
                    ExtraYawSteps = PickYaw(pool[i % pool.Count], combatSlots[i], rng)
                });
        }

        return placements;
    }

    // 회전 매칭: 존의 개방변(N/W, 벽 없음)이 슬롯의 다리 방향(월드)을 최대한 많이 향하는
    // 90° 단위 회전을 고른다(스코어링). 개방변이 못 덮는 연결은 벽의 문(door)으로 통과 —
    // 존 저작 규칙: N/W=완전 개방, S/E=벽+문(문 위치 정렬은 슬롯 좌표 보정에서).
    //  - 정사각(대/소): 0/90/180/270 전부 후보. 직사각(중): 풋프린트 축 유지를 위해 0/180만.
    //  - 최고 점수 후보가 여럿이면 rng로 하나(배치 다양성).
    private static int PickYaw(GameObject prefab, ZoneSlot slot, System.Random rng)
    {
        if (prefab == null || slot == null) return 0;
        var layout = prefab.GetComponent<ZoneLayout>();
        if (layout == null || layout.OpeningCount == 0) return 0; // 출입구 정보 없음 — 매칭 불가(감지 전 프리팹)

        int slotSteps = Mathf.RoundToInt(slot.transform.eulerAngles.y / 90f) & 3;
        int[] candidates = layout.Size == ZoneSize.Medium ? new[] { 0, 2 } : new[] { 0, 1, 2, 3 };

        int bestScore = -1;
        var best = new List<int>();
        foreach (int extra in candidates)
        {
            int total = (slotSteps + extra) & 3;
            int score = 0;
            for (int d = 0; d < 4; d++)
                if (slot.HasConn(d) && layout.HasOpening((d - total + 4) & 3))
                    score++;
            if (score > bestScore) { bestScore = score; best.Clear(); }
            if (score == bestScore) best.Add(extra);
        }
        return best[rng.Next(best.Count)];
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
