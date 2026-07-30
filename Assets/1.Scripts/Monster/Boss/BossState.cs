// 코드 FSM 보스 상태. 순수 enum(절대 [BlackboardEnum] 아님 — 기존 BT용 Blackboard enum과 무관).
// MonsterState와 별개다: 보스는 Charging/Break 페이즈 골격이 추가된다.
public enum BossState
{
    Idle,       // 비교전 대기
    Chase,      // 타겟 추격/교전 접근
    Attack,     // 기본 공격 수행 중(windup→hit→duration)
    Charging,   // 페이즈 전환 강제 진입(쉴드/버프 + 브로드캐스트) 골격
    Groggy,     // 그로기(다운) 골격
    Break,      // 파츠 파괴/무력화 골격(연기된 메커닉 진입점)
    Dead        // 사망 → 디스폰
}
