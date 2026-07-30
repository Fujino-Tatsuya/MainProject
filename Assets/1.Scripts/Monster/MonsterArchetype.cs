// 몬스터 아키타입. MonsterBase가 이 값으로 이동/교전 로직을 분기한다.
// - Melee: 근접 추격형(오버랩 히트)
// - RangedTurret: 고정 포탑형(이동 없음, 사거리 안이면 조준·사격)
// - RangedMobile: 원거리 이동형(카이팅 — minStandoff 유지하며 투사체 사격)
public enum MonsterArchetype
{
    Melee,
    RangedTurret,
    RangedMobile,
}
