using UnityEngine;

[System.Serializable]
public struct MonsterGroupData
{
    public int GroupID;                  // 몬스터 그룹 고유 ID
    public string GroupName;             // 몬스터 그룹 이름 (예: "Goblin Ambush")
    
    public NodeTier TargetTier;          // 이 그룹이 배정될 수 있는 노드 티어 (1, 2, 3)
    public Difficulty TargetDifficulty;  // 이 그룹의 적정 난이도 (Easy, Normal, Hard)

    public GameObject MonsterPrefab;     // 스폰할 몬스터 프리팹 (NetworkObject 포함)
    public MonsterBehavior DefaultBehavior; // 스폰 시 기본 행동 (대기, 정찰)
    
    [Tooltip("난이도에 따라 몇 마리를 스폰할지 결정하는 기준값")]
    public int BaseSpawnWeight;
}
