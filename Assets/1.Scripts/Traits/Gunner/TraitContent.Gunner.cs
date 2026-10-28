using UnityEngine;

/// <summary>
/// 거너 특성 콘텐츠 등록(할 일 127 거너 레인 전용 파일 — 공용 파일·다른 캐릭터 파일은 고치지 않는다, 할 일 125 5/5).
/// 여기서만 등록한다: 카드 설명 {dmg} 계산식(<c>r.Damage</c>) · 툴팁 별칭(<c>r.Value</c> — 예 집속 연사 <c>totalDamageRatio</c> = 발수 × 발당 배율) · 2차 상충 규칙(<c>r.Conflict</c>).
/// 다른 캐릭터 ID 를 등록하면 등록부 오류(EditMode <c>GunnerTraitTests</c> 가 잡는다).
/// 계산 도우미는 <see cref="GunnerTraitTooltip"/>(partial 클래스에 private 이름을 늘리지 않는다 — 다른 캐릭터 파일과 겹치지 않게).
/// </summary>
public static partial class TraitContent
{
    static partial void RegisterGunner(TraitContentRegistry.Registrar r)
    {
        // 나머지 토큰은 거너 효과 필드 이름 그대로 풀린다(GunnerTraitEffects.cs — chargeTimeReduction·heatGainIncrease 등).
        // 공용 효과 필드와 명세 토큰 이름이 다른 것만 별칭으로 둔다.
        r.Value("GUN_1_WIDTH", "widthIncrease", trait => GunnerTraitTooltip.QWidthMultiplier(trait) - 1f);
        // 명세 §8 — 표시용 검증값(발수 × 발당 배율). 독립 조정값이 아니다.
        r.Value("GUN_3_VOLLEY", "totalDamageRatio", trait => GunnerTraitTooltip.VolleyTotalRatio(trait));
        r.Value("GUN_4_EXTRA_BASIC", "requiredShots", trait => GunnerTraitTooltip.RequiredCount(trait, GunnerTraitRules.BasicExtraDerivedKey));

        // {dmg} — 이중 출력 = 현재 원래 기본 발 × 0.40(과열 단계별 최소~최대), 잔류 포격 = 현재 Q 원래 발 × 0.25(충전·저장 단계별 최소~최대).
        r.Damage("GUN_4_EXTRA_BASIC", GunnerTraitTooltip.ExtraBasicDamage);
        r.Damage("GUN_4_ECHO_Q", GunnerTraitTooltip.EchoQDamage);
    }
}

/// <summary>
/// 거너 특성 카드 툴팁 계산(할 일 127). 값은 정의 하나만으로 합성한 수치(<see cref="TraitModifierSet"/>)에서 읽는다 — 스킬이 판정에 쓰는 값과 같은 연산이다.
/// </summary>
public static class GunnerTraitTooltip
{
    /// <summary>정의 하나만 가진 수치 합성.</summary>
    public static TraitModifierSet Modifiers(TraitDefinition trait)
    {
        var set = new TraitModifierSet();
        if (trait != null)
            set.Rebuild(new[] { trait.IdHash }, _ => trait);
        return set;
    }

    /// <summary>⑩ 스킬 수치 — 기본값에 이 정의의 연산을 적용한 값.</summary>
    public static float SkillValue(TraitDefinition trait, string key, float baseValue) => Modifiers(trait).ResolveSkillValue(key, baseValue);

    public static float QWidthMultiplier(TraitDefinition trait) => Modifiers(trait).AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Width);

    public static int VolleyShotCount(TraitDefinition trait) => Mathf.RoundToInt(SkillValue(trait, GunnerTraitRules.QVolleyShotCount, 1f));

    public static float VolleyPerShotRatio(TraitDefinition trait) => SkillValue(trait, GunnerTraitRules.QVolleyPerShotRatio, 1f);

    public static float VolleyTotalRatio(TraitDefinition trait) => VolleyShotCount(trait) * VolleyPerShotRatio(trait);

    public static TraitDerivedAttackEffect FindDerived(TraitDefinition trait, string derivedKey)
    {
        var effects = trait != null ? trait.Effects : null;
        for (int i = 0; effects != null && i < effects.Count; i++)
        {
            if (effects[i] is TraitDerivedAttackEffect derived && derived.DerivedKey == derivedKey)
                return derived;
        }

        return null;
    }

    /// <summary>파생 공격의 'N번마다'(없으면 0).</summary>
    public static int RequiredCount(TraitDefinition trait, string derivedKey) =>
        FindDerived(trait, derivedKey)?.CreateRuntime(0).Counter.Required ?? 0;

    /// <summary>이중 출력 {dmg} — 판정식(GunnerBasicAttack) round(Dref × 과열 단계 배율)의 원래 발 × 비율.</summary>
    public static SkillTooltipDamage ExtraBasicDamage(TraitDefinition trait, Player player)
    {
        TraitDerivedAttackEffect derived = FindDerived(trait, GunnerTraitRules.BasicExtraDerivedKey);
        GunnerBasicAttackData attack = player != null ? player.GetComponent<GunnerBasicAttack>()?.Data : null;
        GunnerHeatData heat = player != null ? player.GetComponent<GunnerHeat>()?.Data : null;
        if (derived == null || attack == null || heat == null)
            return default;

        return DerivedDamage(player, attack.AttackDamageMultiplier, attack.FlatDamageBonus, GunnerTraitRules.BasicAttackKey,
            heat.StageDamageMultiplier(0), heat.StageDamageMultiplier(int.MaxValue), derived.DamageRatio);
    }

    /// <summary>잔류 포격 {dmg} — 판정식(GunnerChargeLaserSkill.Fire) round(Dref × 충전 배율 × 저장 단계 배율)의 원래 발 × 비율. 최소 = 충전 0·0단계, 최대 = 최대 충전·최고 단계.</summary>
    public static SkillTooltipDamage EchoQDamage(TraitDefinition trait, Player player)
    {
        TraitDerivedAttackEffect derived = FindDerived(trait, GunnerTraitRules.QEchoDerivedKey);
        PlayerSkillController skills = player != null ? player.GetComponent<PlayerSkillController>() : null;
        var q = skills != null ? skills.GetSkill(PlayerSkillSlot.Main)?.Data as GunnerChargeLaserData : null;
        if (derived == null || q == null)
            return default;

        return DerivedDamage(player, q.AttackDamageMultiplier, q.FlatDamageBonus, GunnerTraitRules.QAttackKey,
            q.DamageMultiplierAt(0f) * q.StageDamageMultiplier(0), q.DamageMultiplierAt(1f) * q.StageDamageMultiplier(int.MaxValue),
            derived.DamageRatio);
    }

    /// <summary>원래 발 피해(Dref × 상태 배율, 최소~최대) × 파생 비율. 집중 포화·적 방어·백어택은 넣지 않는다(명세 §8).</summary>
    public static SkillTooltipDamage DerivedDamage(Player player, float coefficient, int flatBonus, TraitAttackKey key,
        float minimumScale, float maximumScale, float ratio)
    {
        float trait = player.TraitDamageMultiplier(key);
        int dref = PlayerStatFormula.Snapshot(Mathf.RoundToInt(player.FinalAttackDamage), coefficient, flatBonus, player.DealtDamagePercent, trait);
        return DerivedDamage(dref, coefficient * trait, Mathf.RoundToInt(flatBonus * trait), minimumScale, maximumScale, ratio);
    }

    /// <summary>순수 판(테스트) — 원래 발 = round(dref × 배율), 파생 = 원래 발 × 비율(<see cref="TraitDerivedAttackRules.Damage"/>).</summary>
    public static SkillTooltipDamage DerivedDamage(int dref, float coefficient, int flatBonus, float minimumScale, float maximumScale, float ratio)
    {
        int minimum = TraitDerivedAttackRules.Damage(Mathf.Max(0, Mathf.RoundToInt(dref * minimumScale)), ratio);
        int maximum = TraitDerivedAttackRules.Damage(Mathf.Max(0, Mathf.RoundToInt(dref * maximumScale)), ratio);
        return new SkillTooltipDamage(minimum, maximum,
            coefficient * minimumScale * ratio, coefficient * maximumScale * ratio,
            Mathf.RoundToInt(flatBonus * minimumScale * ratio), Mathf.RoundToInt(flatBonus * maximumScale * ratio));
    }
}
