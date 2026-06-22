public enum ZoneGrade : byte
{
    A_UpToTier1,  // 1티어까지 생성 가능
    B_UpToTier2,  // 2티어까지만 생성 가능
    Quest         // 노드 없이 퀘스트만 진행되는 구역
}

public enum ZoneType : byte
{
    Combat,       // 현재 사용됨
    Exploration,  // 탐험
    Shop          // 상점
}

public enum ZoneRole : byte
{
    Combat,       // 전투 영역 (노드 배치)
    Quest,        // 퀘스트 전용 (노드 X)
    BossRoom,     // 보스방 (노드 X)
    PlayerSpawn   // 플레이어 스폰 (+상점, 노드 X)
}

public enum NodeTier : byte
{
    Tier1_Large,
    Tier2_Medium,
    Tier3_Small
}

public enum NodeContentType : byte
{
    CombatNode,   // 전투 노드 (몬스터 스폰)
    Obstacle,     // 장애물 (진입 불가)
    Recovery,     // 회복 오브젝트 (3티어전용)
    Teleport,     // 순간이동 오브젝트 (3티어전용)
    BossGate,     // 보스 입구
    QuestNode,    // 퀘스트 진행 (퀘스트 구역 전용)
    Buff          // 버프 오브젝트 (3티어전용) — 직렬화 값 보존 위해 끝에 추가
}

public enum MonsterBehavior : byte
{
    Idle,         // 제자리 대기
    Patrol        // 정찰
}

public enum ClearCondition : byte
{
    None,
    KillAll,      // 모든 몬스터 처치
    KeyUnlock     // 열쇠를 소모하여 클리어 (몬스터 처치 무시 가능)
}

public enum Difficulty : byte
{
    Easy,
    Normal,
    Hard
}

public enum ZoneSize : byte
{
    Large,   // 대형 — 1티어 노드 포함 전투 존
    Medium,  // 중형
    Small    // 소형
}
