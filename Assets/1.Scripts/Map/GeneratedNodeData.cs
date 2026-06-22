using Unity.Netcode;

[System.Serializable]
public struct GeneratedNodeData : INetworkSerializable
{
    public int SpawnPointID;           // 스폰 포인트의 고유 ID
    public ZoneType AssignedZoneType;  // 배정된 구역의 타입 (전투, 탐험 등)
    public NodeTier Tier;              // 노드 티어 (1, 2, 3)
    public NodeContentType Content;    // 노드의 내용물 (전투, 장애물, 회복 등)
    public int PrefabId;               // 카탈로그 변형(variant) 인덱스 (-1 = 없음)
    public Difficulty NodeDifficulty;  // 이 노드에 배정된 난이도
    public ClearCondition ClearType;   // 노드 클리어 조건

    // 몬스터 관련 (CombatNode인 경우만 유효 — 현재는 데이터 골격만)
    public int MonsterGroupID;         // 배정된 몬스터 그룹의 ID
    public int MonsterCount;           // 스폰될 몬스터 마릿수

    // 네트워크 직렬화 (서버 스폰 시/필요 시 사용)
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SpawnPointID);
        serializer.SerializeValue(ref AssignedZoneType);
        serializer.SerializeValue(ref Tier);
        serializer.SerializeValue(ref Content);
        serializer.SerializeValue(ref PrefabId);
        serializer.SerializeValue(ref NodeDifficulty);
        serializer.SerializeValue(ref ClearType);
        serializer.SerializeValue(ref MonsterGroupID);
        serializer.SerializeValue(ref MonsterCount);
    }
}
