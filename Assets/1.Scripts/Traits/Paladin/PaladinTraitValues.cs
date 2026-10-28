using System;

/// <summary>
/// 팔라딘 특성 ID·카드 별칭 값(할 일 126 팔라딘 레인). 별칭은 <see cref="TraitContent"/>(팔라딘 파일)이 등록하고, 값은 정의의 효과 필드에서 읽는다 —
/// 표와 실제 효과를 따로 고치지 않게(명세 §8). 효과 필드는 공용 값 소스(<see cref="TraitTooltipValueSource"/>, 등록부 없이 = 별칭 재귀 없음)로 찾는다.
/// </summary>
public static class PaladinTraitValues
{
    public const string QDamage = "PAL_1_Q_DAMAGE";
    public const string ECooldown = "PAL_1_E_COOLDOWN";
    public const string PassiveHeal = "PAL_1_PASSIVE_HEAL";
    public const string ERefund = "PAL_2_E_REFUND";
    public const string ShareHeal = "PAL_2_SHARE_HEAL";
    public const string QStun = "PAL_3_Q_STUN";
    public const string EShield = "PAL_3_E_SHIELD";
    public const string PassiveCdr = "PAL_3_PASSIVE_CDR";

    /// <summary>수호 재정비 카드 <c>{cooldownReduction:%}</c> = −cooldownChange(0.15 = 15% 감소).</summary>
    public static float CooldownReduction(TraitDefinition trait) => -EffectField(trait, "cooldownChange");

    /// <summary>나누는 불굴 카드 <c>{allyRange}</c> = 초과 회복 전달 범위(m).</summary>
    public static float AllyRange(TraitDefinition trait) => EffectField(trait, "overflowShareRange");

    /// <summary>제압의 진격 카드 <c>{stunDuration}</c> = 밀치기 효과의 뇌진탕 길이(초).</summary>
    public static float StunDuration(TraitDefinition trait) => EffectField(trait, "concussionDuration");

    /// <summary>수호의 잔광 카드 <c>{shieldMaxHpRatio:%}</c> = 보호막 효과의 최대 체력 비율(0.1 = 10%).</summary>
    public static float ShieldMaxHpRatio(TraitDefinition trait) => EffectField(trait, "amountRatio");

    /// <summary>수호의 잔광 카드 <c>{shieldDuration}</c> = 보호막 지속(초).</summary>
    public static float ShieldDuration(TraitDefinition trait) => EffectField(trait, "duration");

    private static float EffectField(TraitDefinition trait, string field)
    {
        var source = new TraitTooltipValueSource(trait, null);
        if (!source.TryGetTooltipValue(field, out object value, out string error))
            throw new InvalidOperationException($"팔라딘 특성 '{trait?.Id}' 별칭 — {error}");

        return Convert.ToSingle(value);
    }
}
