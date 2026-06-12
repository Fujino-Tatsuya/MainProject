using UnityEngine;
using System.Collections.Generic;

public class NodePlacer : MonoBehaviour
{
    private MapGenConfigSO _config;
    private MapPrefabCatalogSO _catalog;
    private System.Random _rng;   // MapGenerator와 동일한 단일 RNG 스트림 공유

    public void Initialize(MapGenConfigSO config, MapPrefabCatalogSO catalog, System.Random rng)
    {
        _config = config;
        _catalog = catalog;
        _rng = rng;
    }

    // 1티어: A등급 전투 영역마다 1개씩 (최대 Tier1NodeCount).
    public void PlaceTier1(List<List<SpawnPoint>> aZonePointLists)
    {
        int placed = 0;
        foreach (var points in aZonePointLists)
        {
            if (placed >= _config.Tier1NodeCount) break;
            SpawnPoint sp = PickUnassigned(points, NodeTier.Tier1_Large);
            if (sp == null) continue;
            Assign(sp, NodeTier.Tier1_Large, NodeContentType.CombatNode);
            placed++;
        }
    }

    // 2티어: 전투 영역마다 노드 1개 보장 + 나머지 슬롯에 '맵 전체' 장애물 총량(Min/Max) 배분.
    public void PlaceTier2(List<List<SpawnPoint>> combatZonePointLists)
    {
        var pool = new List<SpawnPoint>(); // 보장 노드 제외 나머지 후보(맵 전체 풀)
        foreach (var points in combatZonePointLists)
        {
            var cands = GetUnassigned(points, NodeTier.Tier2_Medium);
            if (cands.Count == 0) continue;

            Shuffle(cands);
            Assign(cands[0], NodeTier.Tier2_Medium, NodeContentType.CombatNode); // 존별 보장 노드
            for (int i = 1; i < cands.Count; i++) pool.Add(cands[i]);
        }

        Shuffle(pool);
        int obstacles = RollCount(_config.Tier2Obstacle_Min, _config.Tier2Obstacle_Max, pool.Count);
        for (int i = 0; i < pool.Count; i++)
            Assign(pool[i], NodeTier.Tier2_Medium, i < obstacles ? NodeContentType.Obstacle : NodeContentType.CombatNode);
    }

    // 3티어: '맵 전체' 슬롯 풀에서 회복/순간이동/버프 총량(Min/Max) 배분, 나머지는 장애물.
    public void PlaceTier3(List<List<SpawnPoint>> combatZonePointLists)
    {
        var pool = new List<SpawnPoint>();
        foreach (var points in combatZonePointLists)
            pool.AddRange(GetUnassigned(points, NodeTier.Tier3_Small));

        Shuffle(pool);
        int n = pool.Count;
        int recovery = RollCount(_config.Recovery_Min, _config.Recovery_Max, n);
        int teleport = RollCount(_config.Teleport_Min, _config.Teleport_Max, n - recovery);
        int buff = RollCount(_config.Buff_Min, _config.Buff_Max, n - recovery - teleport);

        int idx = 0;
        for (int i = 0; i < recovery && idx < n; i++, idx++) Assign(pool[idx], NodeTier.Tier3_Small, NodeContentType.Recovery);
        for (int i = 0; i < teleport && idx < n; i++, idx++) Assign(pool[idx], NodeTier.Tier3_Small, NodeContentType.Teleport);
        for (int i = 0; i < buff && idx < n; i++, idx++)     Assign(pool[idx], NodeTier.Tier3_Small, NodeContentType.Buff);
        for (; idx < n; idx++)                               Assign(pool[idx], NodeTier.Tier3_Small, NodeContentType.Obstacle);
    }

    // [min,max]에서 1개 뽑되 cap(가용 슬롯 수)으로 제한.
    private int RollCount(int min, int max, int cap)
    {
        if (cap <= 0) return 0;
        max = Mathf.Clamp(max, 0, cap);
        min = Mathf.Clamp(min, 0, max);
        return (max >= min) ? _rng.Next(min, max + 1) : 0;
    }

    private void Assign(SpawnPoint sp, NodeTier tier, NodeContentType content)
    {
        int variant = (_catalog != null) ? _catalog.PickVariantIndex(_rng, tier, content) : -1;
        sp.NodeData = new GeneratedNodeData
        {
            SpawnPointID = sp.PointID,
            Tier = tier,
            Content = content,
            PrefabId = variant
        };
        sp.IsAssigned = true;
    }

    private SpawnPoint PickUnassigned(List<SpawnPoint> points, NodeTier tier)
    {
        var list = GetUnassigned(points, tier);
        return list.Count == 0 ? null : list[_rng.Next(list.Count)];
    }

    private List<SpawnPoint> GetUnassigned(List<SpawnPoint> points, NodeTier tier)
    {
        var result = new List<SpawnPoint>();
        if (points == null) return result;

        foreach (var sp in points)
            if (sp != null && !sp.IsAssigned && sp.AllowedTier == tier) result.Add(sp);
        return result;
    }

    private void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = _rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}
