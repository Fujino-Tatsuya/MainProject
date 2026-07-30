#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 슬롯 저작 검증/청소가 공유하는 "도달 가능 조합" 모델.
//
// 왜 별도 파일인가: 검증기와 청소 도구가 각자 후보를 계산하면 반드시 갈라진다. 한쪽이
// 도달 불가라고 지운 조합을 다른 쪽이 미저작이라고 보고하는 사고가 실제로 났다.
// 계산은 여기 한 곳만 두고, 두 도구는 이 결과만 읽는다.
//
// ⚠️ 저작 데이터의 정본은 **씬의 Stage1 인스턴스**다. Save Placements가 씬 인스턴스에 쓰고
// 씬을 dirty 처리하며 프리팹에 Apply하지 않기 때문이다(SavePlacements.cs). 프리팹 에셋만
// 읽으면 씬 오버라이드를 놓쳐 "미저작 9건" 같은 거짓 경보가 난다 — 이 모델은 씬을 읽는다.
public static class SlotAuthoringModel
{
    public const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";

    /// <summary>슬롯 하나가 실제로 받을 수 있는 프리팹 집합 + 가능한 역할.</summary>
    public class SlotPlan
    {
        public ZoneSlot Slot;
        public readonly HashSet<ZoneRole> PossibleRoles = new HashSet<ZoneRole>();
        public readonly List<GameObject> Reachable = new List<GameObject>();
    }

    public static ZoneLayoutCatalogSO LoadCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(CatalogPath);
        if (catalog == null || catalog.Entries == null)
            Debug.LogError($"[SlotAuthoring] 카탈로그 로드 실패: {CatalogPath}");
        return catalog;
    }

    /// <summary>
    /// 열린 씬의 ZoneSlot을 MapGenerator와 같은 순서로 수집한다(SlotID → x → z).
    /// 순서를 맞추는 이유: 리포트의 슬롯 순번이 런타임 로그와 어긋나면 대조가 안 된다.
    /// </summary>
    public static List<ZoneSlot> GatherSceneSlots()
    {
        var slots = Object.FindObjectsByType<ZoneSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(s => s != null)
            .ToList();

        slots.Sort((a, b) =>
        {
            int c = a.SlotID.CompareTo(b.SlotID);
            if (c != 0) return c;
            Vector3 pa = a.transform.position, pb = b.transform.position;
            c = Mathf.RoundToInt(pa.x * 100f).CompareTo(Mathf.RoundToInt(pb.x * 100f));
            if (c != 0) return c;
            return Mathf.RoundToInt(pa.z * 100f).CompareTo(Mathf.RoundToInt(pb.z * 100f));
        });

        return slots;
    }

    /// <summary>
    /// 슬롯별 도달 가능 조합 계산. 난이도는 생성기 기본값 0.
    ///
    /// 핵심: 후보를 "플래그가 켜져 있으니 가능"으로 넓게 잡으면 안 된다. 역할 후보가 역할 수와
    /// 딱 맞으면(예: Small 슬롯 2곳이 둘 다 Boss·Spawn 후보) 그 슬롯은 **절대 Combat이 안 된다**.
    /// 그래서 MapGenerator.AssignSlotRoles의 모든 가능한 추첨 결과를 전수 시뮬레이션해
    /// 슬롯별 '가능한 역할' 집합을 먼저 확정한다.
    /// </summary>
    public static List<SlotPlan> BuildPlans(List<ZoneSlot> slots, ZoneLayoutCatalogSO catalog, int difficulty = 0)
    {
        var plans = slots.ToDictionary(s => s, s => new SlotPlan { Slot = s });

        SimulateRoleAssignments(slots, plans);

        // LayoutPlacer는 어느 슬롯이든 FixedPrefab으로 지정된 프리팹을 전투 셔플 풀에서 제외한다
        // (다른 슬롯 중복 방지). 따라서 고정 프리팹은 다른 슬롯의 도달 가능 집합에 들어가지 않는다.
        var pinned = new HashSet<GameObject>(slots.Where(s => s.FixedPrefab != null).Select(s => s.FixedPrefab));

        foreach (ZoneSlot slot in slots)
        {
            SlotPlan plan = plans[slot];

            // 고정 슬롯은 셔플·역할과 무관하게 항상 그 프리팹 하나다.
            if (slot.FixedPrefab != null)
            {
                plan.Reachable.Add(slot.FixedPrefab);
                continue;
            }

            foreach (ZoneRole role in plan.PossibleRoles)
                foreach (GameObject prefab in LayoutsForRole(catalog, slot, role, difficulty, pinned))
                    if (prefab != null && !plan.Reachable.Contains(prefab))
                        plan.Reachable.Add(prefab);
        }

        return slots.Select(s => plans[s]).ToList();
    }

    /// <summary>LayoutPlacer.SelectLayouts가 그 역할에서 고를 수 있는 프리팹 전부.</summary>
    static IEnumerable<GameObject> LayoutsForRole(ZoneLayoutCatalogSO catalog, ZoneSlot slot,
                                                 ZoneRole role, int difficulty, HashSet<GameObject> pinned)
    {
        List<GameObject> CombatPool()
        {
            List<GameObject> pool = catalog.GetCombatPool(slot.Size, difficulty);
            pool.RemoveAll(p => pinned.Contains(p));
            return pool;
        }

        if (role == ZoneRole.Combat)
            return CombatPool();

        if (role == ZoneRole.Quest)
        {
            // 슬롯에 QuestPrefab이 지정돼 있으면 카탈로그 Quest 풀은 아예 조회되지 않는다.
            if (slot.QuestPrefab != null) return new[] { slot.QuestPrefab };
            List<GameObject> questPool = catalog.GetRolePool(ZoneRole.Quest, slot.Size);
            return questPool.Count > 0 ? questPool : CombatPool();
        }

        // Boss/Spawn: 그 크기 역할 디자인이 없으면 LayoutPlacer가 전투 풀로 폴백한다.
        GameObject roleLayout = catalog.GetRoleLayout(role, slot.Size);
        return roleLayout != null ? new[] { roleLayout } : (IEnumerable<GameObject>)CombatPool();
    }

    /// <summary>
    /// AssignSlotRoles(Quest → BossRoom → PlayerSpawn, 각 1곳, 중복 배정 없음)의
    /// 모든 추첨 결과를 전수 열거해 슬롯별 가능한 역할을 모은다. 후보 수가 작아 비용은 무시 가능하다.
    /// </summary>
    static void SimulateRoleAssignments(List<ZoneSlot> slots, Dictionary<ZoneSlot, SlotPlan> plans)
    {
        List<ZoneSlot> quest = slots.Where(s => s.IsQuestCandidate).ToList();
        List<ZoneSlot> boss = slots.Where(s => s.IsBossCandidate).ToList();
        List<ZoneSlot> spawn = slots.Where(s => s.IsSpawnCandidate).ToList();

        // 후보가 없으면 그 역할은 아무 슬롯에도 배정되지 않는다(AssignRole이 조기 반환).
        foreach (ZoneSlot q in Choices(quest, null, null))
        foreach (ZoneSlot b in Choices(boss, q, null))
        foreach (ZoneSlot s in Choices(spawn, q, b))
        {
            foreach (ZoneSlot slot in slots)
            {
                ZoneRole role = slot == q ? ZoneRole.Quest
                              : slot == b ? ZoneRole.BossRoom
                              : slot == s ? ZoneRole.PlayerSpawn
                              : ZoneRole.Combat;
                plans[slot].PossibleRoles.Add(role);
            }
        }
    }

    // 이미 배정된 슬롯을 뺀 후보 목록. 비면 "아무도 안 뽑힘"을 나타내는 null 한 번을 낸다.
    static IEnumerable<ZoneSlot> Choices(List<ZoneSlot> pool, ZoneSlot taken1, ZoneSlot taken2)
    {
        var available = pool.Where(s => s != taken1 && s != taken2).ToList();
        if (available.Count == 0) { yield return null; yield break; }
        foreach (ZoneSlot s in available) yield return s;
    }

    /// <summary>회전+위치가 모두 저작된 프리팹인지.</summary>
    public static bool IsAuthored(ZoneSlot slot, GameObject prefab)
        => slot.TryGetYaw(prefab, out _) && slot.TryGetPosition(prefab, out _);

    /// <summary>참조를 잃은(프리팹이 삭제돼 null이 된) 저작 항목 수.</summary>
    public static int CountDeadEntries(ZoneSlot slot)
        => slot.Rotations == null ? 0 : slot.Rotations.Count(e => e.Prefab == null);
}
#endif
