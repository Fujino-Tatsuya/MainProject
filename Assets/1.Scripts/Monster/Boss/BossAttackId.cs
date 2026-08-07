// 보스 공격 종류 — 3층 구조의 2층.
//
//   BossState(=MonsterState) : Idle / Walk(Chase) / Attack / Hit / Groggy / Dead
//   BossAttackId             : 이 파일
//   BossAttackPhase          : 다단계 공격 내부(Grab 체인 등) — 필요해지는 슬라이스에서 추가
//
// 🔴 공격마다 상태를 늘리지 않는다. MonsterState.Attack 하나 안에서 이 값으로 분기한다.
//    선례: GauntletBot 은 공격 7종을 전부 Attack 상태 안에서 처리하고 MonsterState 에 값을 추가하지 않았다.
//    (중간보스 3종 중 어느 것도 MonsterState 를 건드리지 않았다 — Docs/tech/boss-rebuild-standard.md §0)
//
// 🔴 값 추가는 **끝에만** 한다. BossDataSO 의 공격 테이블에 정수로 직렬화되므로
//    중간에 삽입하면 이미 저작된 공격 행의 종류가 한 칸씩 밀린다.
//    (MonsterArchetype 과 완전히 같은 함정.)
public enum BossAttackId
{
    LeftHook,   // 좌 훅(근접 단타)
    RightHook,  // 우 훅(근접 단타)
    Upper,      // 어퍼(근접 단타 + Airborne CC)
    Grab,       // 잡기 체인(창 → Hold → Throw). 카운터 창을 여는 공격.
    Jump,       // 점프 공격(거리 무관 / 최원거리 플레이어 타겟 / 장판 2단)
    Dash,       // 돌진(캐리-푸시). 카운터 창을 여는 공격.

    // ── 페이즈 시퀀스 전용 (끝에 추가) ────────────────────────────────
    // 🔴 이 둘은 공격 테이블에 **weight = 0** 으로 저작한다 — 가중치 룰렛이 절대 뽑지 않고,
    //    페이즈 시스템이 직접 트리거한다. 행으로 두는 이유는 슈퍼아머·데드락 타이머·애니 상태명·
    //    쿨다운 같은 기존 기계를 전부 재사용하기 위해서다(새 상태·새 base 훅 0).
    ChargeSequence, // 송전기(차징) — 중앙에서 대기, 실드 점증. 카운터 창 없음.
    RageDash,       // 레이지 돌진 3회 — 송전기 실패 벌칙. 카운터 창 없음.
}
