/// <summary>
/// 어쌔신 특성 콘텐츠 등록(할 일 128 어쌔신 레인 전용 파일 — 공용 파일·다른 캐릭터 파일은 고치지 않는다, 할 일 125 5/5).
/// 여기서만 등록한다: 카드 설명 {dmg} 계산식(<c>r.Damage</c>) · 툴팁 별칭(<c>r.Value</c>) · 2차 상충 규칙(<c>r.Conflict</c>).
/// 다른 캐릭터 ID 를 등록하면 등록부 오류(EditMode <c>AssassinTraitTests</c> 가 잡는다).
/// </summary>
public static partial class TraitContent
{
    static partial void RegisterAssassin(TraitContentRegistry.Registrar r)
    {
        // 명세 §2 — 절제된 분노는 변신(R)을 없애 광란 강화(변신 E)의 효과가 사라진다. 고를 때 확인창(기반 레인이 미리 등록).
        r.Conflict("ASN_2_RESTRAINT", "ASN_1_TRANS_E_DAMAGE",
            "변신을 사용할 수 없어 ‘{traits}’의 효과를 받을 수 없습니다. 선택할까요?");

        // 돌파 가속 — 명세 문구는 감소율(0.15)이고 효과는 쿨 증감(−0.15)으로 보관한다.
        r.Value("ASN_1_Q_COOLDOWN", "cooldownReduction", CooldownReduction);

        // 절제된 분노 — 피해 효과 두 칸(일반 Q / 강타)이 같은 필드 이름이라 첫 칸 값, 쿨은 감소율 별칭(돌파 가속과 같은 식).
        r.Value("ASN_2_RESTRAINT", "damageIncrease", DamageIncrease);
        r.Value("ASN_2_RESTRAINT", "cooldownReduction", CooldownReduction);

        // 그림자 회귀 — 창·복귀 피해 비율은 스킬 수치 칸(⑩ entries[i].value), 쿨은 증가율(배율 − 1).
        r.Value("ASN_2_RETURN", "returnWindow", trait => SkillValue(trait, AssassinShadowReturnRules.WindowKey));
        r.Value("ASN_2_RETURN", "returnDamageRatio", trait => SkillValue(trait, AssassinShadowReturnRules.DamageRatioKey));
        r.Value("ASN_2_RETURN", "cooldownIncrease", CooldownIncrease);

        // 칼날의 영역 — 범위 효과 두 칸(강타 길이·폭 / 변신 E 반경)이 같은 필드 이름이라 첫 칸 값으로 푼다.
        r.Value("ASN_3_E_AREA", "areaIncrease", AreaIncrease);

        // 사냥의 박자 — 발동 간격은 효과 거름(filter.minInterval)에 있다.
        r.Value("ASN_3_BASIC_CDR", "procInterval", ProcInterval);

        // 추격의 궤적 — 현재 상태(일반/변신) 출발 Q 1회 기준 피해 × 비율(명세 §8). 복귀 궤적(×0.6)은 도움말 몫.
        r.Damage("ASN_4_Q_TRAIL", QTrailDamage);

        // 끝없는 칼날 — 현재 상태(일반/변신) 기본 공격 마지막 원래 타 × 비율.
        r.Damage("ASN_4_COMBO_EXTRA", ComboExtraDamage);
    }

    private static SkillTooltipDamage QTrailDamage(TraitDefinition trait, Player player)
    {
        PlayerSkillController skills = player != null ? player.GetComponent<PlayerSkillController>() : null;
        PlayerSkillBase q = skills != null ? skills.GetSkill(PlayerSkillSlot.Main) : null;
        if (q == null || q.Data == null)
            return default;

        var key = TraitAttackKey.ForSkill(PlayerSkillSlot.Main, skills.IsAlternateSkill(q));
        int dref = player.ResolveDamage(q.Data.AttackDamageMultiplier, q.Data.FlatDamageBonus, key);
        return AssassinDerivedTooltipRules.From(dref, q.Data.AttackDamageMultiplier, q.Data.FlatDamageBonus,
            AssassinDerivedTooltipRules.RatioOf(trait));
    }

    private static SkillTooltipDamage ComboExtraDamage(TraitDefinition trait, Player player)
    {
        AssassinBasicAttack basic = player != null ? player.GetComponent<AssassinBasicAttack>() : null;
        AssassinBasicAttackData data = basic != null ? basic.Data : null;
        if (data == null)
            return default;

        AssassinState state = player.GetComponent<AssassinState>();
        bool transformed = state != null && state.IsTransformed;
        AssassinBasicAttackStepData last;
        bool found = transformed
            ? data.TryGetTransformedStep(out last)
            : data.TryGetNormalStep(data.NormalSteps != null ? data.NormalSteps.Length - 1 : -1, out last);
        if (!found || last == null)
            return default;

        var key = new TraitAttackKey(AttackType.Default, transformed ? TraitAttackForm.Alternate : TraitAttackForm.Normal);
        int dref = player.ResolveDamage(last.AttackDamageMultiplier, 0, key);
        return AssassinDerivedTooltipRules.From(dref, last.AttackDamageMultiplier, 0, AssassinDerivedTooltipRules.RatioOf(trait));
    }

    // 정의의 첫 쿨 효과 감소율(1 − 배율). 쿨 효과가 없으면 0.
    private static object CooldownReduction(TraitDefinition trait)
    {
        if (trait != null && trait.Effects != null)
        {
            foreach (TraitEffect effect in trait.Effects)
            {
                if (effect is TraitCooldownEffect cooldown)
                    return 1f - cooldown.Multiplier;
            }
        }

        return 0f;
    }

    // 정의의 첫 쿨 효과 증가율(배율 − 1). 쿨 효과가 없으면 0.
    private static object CooldownIncrease(TraitDefinition trait)
    {
        if (trait != null && trait.Effects != null)
        {
            foreach (TraitEffect effect in trait.Effects)
            {
                if (effect is TraitCooldownEffect cooldown)
                    return cooldown.Multiplier - 1f;
            }
        }

        return 0f;
    }

    // 정의의 스킬 수치 칸(⑩) 중 이름이 같은 첫 값. 없으면 0.
    private static object SkillValue(TraitDefinition trait, string key)
    {
        if (trait != null && trait.Effects != null)
        {
            var modifiers = new System.Collections.Generic.List<TraitModifier>();
            foreach (TraitEffect effect in trait.Effects)
            {
                if (!(effect is TraitSkillValueEffect))
                    continue;

                modifiers.Clear();
                effect.GetModifiers(modifiers);
                foreach (TraitModifier modifier in modifiers)
                {
                    if (string.Equals(modifier.Key, key, System.StringComparison.Ordinal))
                        return modifier.Value;
                }
            }
        }

        return 0f;
    }

    // 정의의 첫 피해 효과 증가율(배율 − 1). 피해 효과가 없으면 0.
    private static object DamageIncrease(TraitDefinition trait)
    {
        if (trait != null && trait.Effects != null)
        {
            foreach (TraitEffect effect in trait.Effects)
            {
                if (effect is TraitDamageEffect damage)
                    return damage.Multiplier - 1f;
            }
        }

        return 0f;
    }

    // 정의의 첫 범위 효과 증가율(배율 − 1). 범위 효과가 없으면 0.
    private static object AreaIncrease(TraitDefinition trait)
    {
        if (trait != null && trait.Effects != null)
        {
            var modifiers = new System.Collections.Generic.List<TraitModifier>();
            foreach (TraitEffect effect in trait.Effects)
            {
                if (!(effect is TraitAreaEffect))
                    continue;

                effect.GetModifiers(modifiers);
                if (modifiers.Count > 0)
                    return modifiers[0].Value - 1f;
            }
        }

        return 0f;
    }

    // 정의의 첫 쿨 차감 효과의 발동 간격(초). 없으면 0.
    private static object ProcInterval(TraitDefinition trait)
    {
        if (trait != null && trait.Effects != null)
        {
            foreach (TraitEffect effect in trait.Effects)
            {
                if (effect is TraitCooldownRefundEffect refund && refund.Filter != null)
                    return refund.Filter.MinInterval;
            }
        }

        return 0f;
    }
}
