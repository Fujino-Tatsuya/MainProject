using UnityEngine;
using System.Collections.Generic;

// 존 레이아웃 프리팹 카탈로그.
//  - 전투 풀: (Size × Difficulty) 별 전투 존 디자인 목록 → 슬롯에 셔플 배치.
//  - 역할 고정: (Role, Size) 별 단일 디자인(보스/스폰/퀘스트).
[CreateAssetMenu(fileName = "ZoneLayoutCatalog", menuName = "VeyTrace/Zone Layout Catalog")]
public class ZoneLayoutCatalogSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("ZoneLayout 컴포넌트를 루트에 가진 프리팹.")]
        public GameObject Prefab;
        public ZoneSize Size;
        [Tooltip("Combat = 전투 풀(셔플 대상). 그 외 = 역할 고정 디자인.")]
        public ZoneRole Role;
        [Tooltip("전투 풀 난이도 밴드. 역할 고정은 무시.")]
        public int Difficulty;
    }

    public List<Entry> Entries = new List<Entry>();

    // 전투 디자인 풀: (Size, Difficulty) 매칭.
    public List<GameObject> GetCombatPool(ZoneSize size, int difficulty)
    {
        var pool = new List<GameObject>();
        if (Entries == null) return pool;
        foreach (var e in Entries)
            if (e.Prefab != null && e.Role == ZoneRole.Combat && e.Size == size && e.Difficulty == difficulty)
                pool.Add(e.Prefab);
        return pool;
    }

    // 역할 고정 디자인: (Role, Size) 우선, 없으면 Size 무관 fallback.
    public GameObject GetRoleLayout(ZoneRole role, ZoneSize size)
    {
        if (Entries == null) return null;
        foreach (var e in Entries)
            if (e.Prefab != null && e.Role == role && e.Size == size)
                return e.Prefab;
        foreach (var e in Entries)
            if (e.Prefab != null && e.Role == role)
                return e.Prefab;
        return null;
    }
}
