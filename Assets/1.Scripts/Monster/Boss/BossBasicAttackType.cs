// 코드 FSM 보스 기본 공격 타입. 순수 enum(절대 [BlackboardEnum] 아님).
// None=0 은 "유효 공격 없음"(거리창/가중치 실패)을 의미 — GetRandomAttack이 0을 반환하면 공격 스킵.
// 잡기/폭탄/송전탑/차징전체/Dash/Jump 등은 이후 슬라이스에서 확장(여기엔 근접 2종만).
public enum BossBasicAttackType
{
    None = 0,   // 유효 공격 없음
    Slam = 1,   // 내려찍기(근접)
    Sweep = 2   // 휩쓸기(근접)
}
