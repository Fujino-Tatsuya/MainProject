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

    // 패시브 충전 — 소모될 때까지 유지되는 표식(duration 0). 캐릭터 무관 공용 1개(한 플레이어에 패시브는 하나).
    // 차단·스탯 테이블에 넣지 않는다. 소모 규칙은 각 패시브가 정한다(예: FirstMeleePassive).
    PassiveCharge = 1 << 12,
}

// 상태 효과의 성격. 연출 일괄 해제처럼 "디버프만" 골라야 하는 곳이 쓴다.
public enum StatusEffectCategory
{
    Buff,
    Debuff,
}

public static class StatusEffectCategories
{
    // 스탯 modifier 는 같은 타입이 버프도 디버프도 된다(가속 1.2 / 감속 0.5) — magnitude 로 가른다.
    private const StatusEffectType StatModifiers =
        StatusEffectType.MoveSpeedModifier | StatusEffectType.AttackDamageModifier |
        StatusEffectType.AttackSpeedModifier | StatusEffectType.DefenseModifier | StatusEffectType.MaxHpModifier;

    private const StatusEffectType Buffs = StatusEffectType.SuperArmor | StatusEffectType.PassiveCharge;

    private const StatusEffectType Debuffs =
        StatusEffectType.Airborne | StatusEffectType.Stunned | StatusEffectType.Slowed |
        StatusEffectType.Rooted | StatusEffectType.Silenced | StatusEffectType.Debilitated;

    public static StatusEffectCategory Of(in StatusEffectInstance instance) => Of(instance.type, instance.magnitude);

    public static StatusEffectCategory Of(StatusEffectType type, float magnitude)
    {
        // 배율 1 은 사실상 중립이지만 Buff 로 둔다 — 일괄 해제(ClearDebuffsServer)가 지우지 않아야 하기 때문이다.
        if ((type & StatModifiers) != 0)
            return magnitude >= 1f ? StatusEffectCategory.Buff : StatusEffectCategory.Debuff;

        if ((type & Buffs) != 0)
            return StatusEffectCategory.Buff;

        if ((type & Debuffs) == 0)
            // 새 타입을 위 표에 안 넣었다 — 조용히 한쪽으로 떨어뜨리면 일괄 해제가 엉뚱한 걸 지운다.
            Edit.LogWarning($"[StatusEffect] {type} 의 Buff/Debuff 분류가 없다 — StatusEffectCategories 표에 추가할 것. Debuff 로 취급한다.");

        return StatusEffectCategory.Debuff;
    }
}
