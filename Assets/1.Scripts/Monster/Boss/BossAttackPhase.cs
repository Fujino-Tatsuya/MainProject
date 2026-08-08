// 다단계 공격의 내부 단계 — 3층 구조의 3층.
//
//   BossState(=MonsterState) : Idle / Walk(Chase) / Attack / Hit / Groggy / Dead
//   BossAttackId             : LeftHook … Dash
//   BossAttackPhase          : 이 파일
//
// 🔴 단계마다 상태를 늘리지 않는다. 전부 MonsterState.Attack **안에서** 진행한다 —
//    SpinnerBot 선례(준비→돌진→Dizzy 3단계를 Attack 안에서 자체 타이머로 처리).
//    잡기 체인이 Grab/Hold/Throw 상태를 따로 갖지 않는 이유가 이것이다(정본 §1).
//
// 🔴 값 추가는 끝에만. 직렬화되지는 않지만(런타임 전용) 로그·디버그 표시가 정수로 읽힌다.
public enum BossAttackPhase
{
    None = 0,   // 다단계 아님(단타 공격) 또는 체인 종료
    Windup,     // 준비 — 카운터 창이 열려 있는 구간
    Acquire,    // 판정 순간(잡기 성립/빗나감이 갈린다). 애니 이벤트 OnAttackHit 시점
    Hold,       // 붙잡고 있는 동안(주기 데미지)
    Throw,      // 던지기
    Recovery,   // 복귀 — 끝나면 DecideNextAfterAction

    // JumpAttack (끝에 추가)
    Leap,       // 수직 도약 + 체공. 착지점 확정 · 예고 장판 표시 · 메시 off
    Land,       // 순간이동 완료 · 메시 on · 착지 클립 재생 중(히트 이벤트 대기)

    // 페이즈 시퀀스 (끝에 추가)
    ChargeWait, // 송전기 — 중앙에서 대기. 실드 점증 + 전기 장판. 결과 대기.
    RageDash,   // 레이지 돌진 — 정해진 횟수만큼 반복한다.

    // S5 돌진 (끝에 추가)
    Dash,       // 평상시 돌진 — 첫 1명을 정면에 붙여 끌고 간다. 보행면 끝에서 정지.
}
