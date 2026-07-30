// 몬스터 코드 FSM 상태값.
// BlackboardEnum(Unity.Behavior)이 아니라 순수 enum이다 — BT와의 결합을 완전히 끊고
// 서버가 상태를 소유(NetworkVariable), 클라는 상태→Animator 매핑만 담당하기 위함.
public enum MonsterState
{
    Idle,    // 대기(타깃 없음, 스폰 지점 근처)
    Chase,   // 타깃 추격(NavMeshAgent 이동)
    Attack,  // 공격 모션 + 판정(트랜지언트: 끝나면 Idle/Chase로 복귀)
    Hit,     // 피격 경직(공격 취소, 짧은 스턴)
    Groggy,  // 그로기(그로기 공격 누적 임계 도달)
    Return,  // 리쉬 이탈 후 스폰 지점 복귀
    Dead,    // 사망(디스폰 대기)
    Knockback, // 지속넉백(서버틱 직접 밀기, agent off — 종료 시 Stunned 경직 후 재개)
}
