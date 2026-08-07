// 몬스터 아키타입. MonsterBase가 이 값으로 이동/교전 로직을 분기한다.
// - Melee: 근접 추격형(오버랩 히트)
// - RangedTurret: 고정 포탑형(이동 없음, 사거리 안이면 조준·사격)
// - RangedMobile: 원거리 이동형(카이팅 — minStandoff 유지하며 투사체 사격)
// - Boss: 다중 공격형(거리로 후보를 좁히고 슬롯별 쿨다운으로 고른다. 고를 게 없으면 접근)
//
// 🔴 값 추가는 **끝에만** 한다. MonsterDataSO 에 정수로 직렬화돼 있어
//    중간에 삽입하면 기존 몬스터 8종의 아키타입이 한 칸씩 밀린다.
public enum MonsterArchetype
{
    Melee,
    RangedTurret,
    RangedMobile,
    Boss,
}
