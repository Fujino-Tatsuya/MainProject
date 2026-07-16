using System;

// 상태 효과 타입을 정의하는 열거형.
// 인스턴스 기반 시스템의 식별자 — 중첩 키(type, source)·직렬화·해제/면역·UI 조회에 쓰인다.
[Flags]
public enum StatusEffectType
{
    None = 0,

    // 차단류 (차단 매핑은 StatusEffectController의 테이블 참조)
    Airborne = 1 << 0,       // 공중에 뜸
    Stunned = 1 << 1,        // 기절
    Slowed = 1 << 2,         // 둔화
    Rooted = 1 << 3,         // 속박
    Silenced = 1 << 4,       // 침묵(스킬 봉인)
    Debilitated = 1 << 5,    // 약화(대쉬X, 둔화)
    SuperArmor = 1 << 6,     // 슈퍼아머(넉백/공격 취소 무시)

    // 스탯 modifier — 인스턴스의 magnitude(배율)가 곱으로 집계된다 (버프 > 1, 디버프 < 1)
    MoveSpeedModifier = 1 << 7,
    AttackDamageModifier = 1 << 8,
    AttackSpeedModifier = 1 << 9,
    DefenseModifier = 1 << 10,
    MaxHpModifier = 1 << 11,
}
