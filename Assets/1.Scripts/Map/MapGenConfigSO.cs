using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapGenConfig", menuName = "VeyTrace/Map Generator Config")]
public class MapGenConfigSO : ScriptableObject
{
    [Header("=== 전역 설정 (Global) ===")]
    public int TotalSpawnPoints = 30;
    
    [Header("=== 1티어 노드 (대형) ===")]
    [Tooltip("A등급 구역 3곳에 각각 1개씩 할당됩니다.")]
    public int Tier1NodeCount = 3;

    [Header("=== 2티어 (중형) — Min/Max = 맵 전체 총량 ===")]
    [Tooltip("전투 구역마다 2티어 노드 1개는 무조건 보장됨. 그 외 슬롯 중 장애물로 만들 '맵 전체' 개수 범위.")]
    [Range(0, 40)] public int Tier2Obstacle_Min = 4;
    [Range(0, 40)] public int Tier2Obstacle_Max = 12;

    [Header("=== 3티어 (소형) — Min/Max = 맵 전체 총량 ===")]
    [Tooltip("맵 전체 3티어 슬롯 풀에서 종류별로 배정하고, 나머지는 장애물.")]
    [Range(0, 40)] public int Recovery_Min = 5;
    [Range(0, 40)] public int Recovery_Max = 10;
    [Range(0, 40)] public int Teleport_Min = 3;
    [Range(0, 40)] public int Teleport_Max = 7;
    [Range(0, 40)] public int Buff_Min = 3;
    [Range(0, 40)] public int Buff_Max = 7;

    [Header("=== 난이도별 몬스터 마릿수 범위 ===")]
    public Vector2Int MonstersPerNode_Easy = new Vector2Int(2, 4);
    public Vector2Int MonstersPerNode_Normal = new Vector2Int(4, 6);
    public Vector2Int MonstersPerNode_Hard = new Vector2Int(6, 9);

    [Header("=== 노드 간 배제 반경 (반경 내 겹침 방지) ===")]
    public float Tier1ExclusionRadius = 15f;
    public float Tier2ExclusionRadius = 10f;
    public float Tier3ExclusionRadius = 5f;

    [Header("=== 몬스터 그룹 풀 (Pool) ===")]
    public List<MonsterGroupData> MonsterGroups;
}
