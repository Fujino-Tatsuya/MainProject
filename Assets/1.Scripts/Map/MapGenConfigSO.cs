using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapGenConfig", menuName = "VeyTrace/Map Generator Config")]
public class MapGenConfigSO : ScriptableObject
{
    [Header("=== 스폰포인트 분산 배제 반경 (구 워크플로 — MapSpawnPointScatter용) ===")]
    public float Tier1ExclusionRadius = 15f;
    public float Tier2ExclusionRadius = 10f;
    public float Tier3ExclusionRadius = 5f;

    [Header("=== 몬스터 그룹 풀 (MonsterGroupID → 프리팹) ===")]
    public List<MonsterGroupData> MonsterGroups;
}
