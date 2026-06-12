using System;

// 상태 효과 타입을 정의하는 열거형
[Flags]
public enum StatusEffectType
{
    None = 0,
    Airborne = 1 << 0,       // 공중에 뜸
    Stunned = 1 << 1,        // 기절
    Slowed = 1 << 2,         // 둔화
    Rooted = 1 << 3,         // 속박
    Silenced = 1 << 4,       // 침묵(스킬 봉인)
    Debilitated = 1 << 5,    // 약화(대쉬X, 둔화)
}
