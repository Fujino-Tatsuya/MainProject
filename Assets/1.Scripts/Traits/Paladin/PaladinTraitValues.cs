using System;
using System.Collections.Generic;

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
    /// <summary>돌파의 방패 — 모드 변경(효과 클래스 없이 Q 가 <see cref="PlayerTraitModifiers.HasTrait"/> 로 묻는다).</summary>
    public const string Dash = TraitModeIds.PaladinShieldDash;
    public const string QStun = "PAL_3_Q_STUN";
    public const string EShield = "PAL_3_E_SHIELD";
    public const string PassiveCdr = "PAL_3_PASSIVE_CDR";
    public const string EPush = "PAL_4_E_PUSH";
    public const string QBlast = "PAL_4_Q_BLAST";
    public const string HealShield = "PAL_4_HEAL_SHIELD";

    /// <summary>수호의 파동 밀치기 반경(m) — 스킬 수치 효과(⑩) 이름. E 가 <see cref="PlayerTraitModifiers.ResolveSkillValue"/>(이름, 0)으로 읽는다(0 = 특성 없음).</summary>
    public const string EPushRadiusKey = "PAL_E_PUSH_RADIUS";

    /// <summary>진격의 충격 파생 공격 이름(⑧ derivedKey). Q 가 <see cref="PlayerTraitEffects.GetDerivedAttacks"/> 결과에서 이 이름만 고른다.</summary>
    public const string QBlastKey = "PAL_Q_BLAST";

    // 돌파의 방패 모드 전용 스킬 수치(⑩) 이름 — 정의 에셋 PAL_2_DASH 의 PaladinShieldDashEffect 가 Set 으로 낸다(시트 Traits_Paladin 에 칸 이름으로 나간다).
    // Q 가 ResolveSkillValue(이름, 0)으로 읽는다(0 = 특성 없음·데이터 누락 → 원래 Q).
    public const string DashDistanceKey = "PAL_Q_DASH_DISTANCE";
    public const string DashSpeedKey = "PAL_Q_DASH_SPEED";
    public const string DashPrepareKey = "PAL_Q_DASH_PREPARE";
    public const string DashRecoveryKey = "PAL_Q_DASH_RECOVERY";
    public const string DashDamageRatioKey = "PAL_Q_DASH_DAMAGE_RATIO";
    public const string DashPushDistanceKey = "PAL_Q_DASH_PUSH_DISTANCE";
    public const string DashPushDurationKey = "PAL_Q_DASH_PUSH_DURATION";

    /// <summary>
    /// 돌파의 방패 카드 <c>{dmg}</c>(명세 §8) = 교체 Q 1회 = DQfull × fullCastDamageRatio(<see cref="PaladinTraitRules.QPacketDamage(int,float,float,float)"/>).
    /// 틱 Dref 는 판정과 같은 식(진격 강화 포함). 플레이어·Q 가 없으면 default(화면 "-").
    /// </summary>
    public static SkillTooltipDamage DashDamage(TraitDefinition trait, Player player)
    {
        if (trait == null)
            return default;

        return QPacketTooltip(player, PaladinShieldDashSettings.FromTrait(trait).DamageRatio, 1f);
    }

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

    /// <summary>수호의 파동 카드 <c>{radius}</c> = 스킬 수치 효과 <see cref="EPushRadiusKey"/>(밀치기 반경 m). 밀치기 거리·시간은 효과 필드 이름 그대로.</summary>
    public static float EPushRadius(TraitDefinition trait)
    {
        var modifiers = new List<TraitModifier>();
        trait?.GetModifiers(modifiers);
        foreach (TraitModifier modifier in modifiers)
        {
            if (modifier.Kind == TraitModifierKind.SkillValue && modifier.Key == EPushRadiusKey)
                return modifier.Value;
        }

        throw new InvalidOperationException($"팔라딘 특성 '{trait?.Id}' 별칭 — 스킬 수치 '{EPushRadiusKey}' 가 없다");
    }

    /// <summary>불굴의 가호 카드 <c>{healToShieldRatio:%}</c> = 보호막 효과의 사건 양(실제 회복량) 비율(0.75).</summary>
    public static float HealToShieldRatio(TraitDefinition trait) => EffectField(trait, "amountRatio");

    /// <summary>불굴의 가호 카드 <c>{shieldCapMaxHpRatio:%}</c> = 보호막 생성 상한(대상 최대 체력 비율 0.1).</summary>
    public static float ShieldCapMaxHpRatio(TraitDefinition trait) => EffectField(trait, "capMaxHpRatio");

    /// <summary>진격의 충격 파생 배율(명세 extraDamageRatio 0.3 = 효과 damageRatio).</summary>
    public static float QBlastRatio(TraitDefinition trait) => EffectField(trait, "damageRatio");

    /// <summary>
    /// 진격의 충격 카드 <c>{dmg}</c>(명세 §8) = 현재 Q 묶음(DQpacket, <see cref="PaladinTraitRules.QPacketDamage(int,float,float,float)"/>) × 파생 배율.
    /// 돌파의 방패를 골랐으면 묶음 = 교체 단발 피해(판정과 같은 분기). 틱 Dref 는 판정과 같은 식(<see cref="Player.ResolveDamage(float,int,TraitAttackKey)"/> — 진격 강화 포함).
    /// 플레이어·Q 가 없으면 default(화면 "-").
    /// </summary>
    public static SkillTooltipDamage QBlastDamage(TraitDefinition trait, Player player)
    {
        if (player == null || trait == null)
            return default;

        float dashRatio = 0f;
        if (player.TryGetComponent(out PlayerTraitModifiers modifiers) && modifiers.HasTrait(Dash))
            dashRatio = PaladinShieldDashSettings.From(modifiers).DamageRatio;

        return QPacketTooltip(player, dashRatio, QBlastRatio(trait));
    }

    /// <summary>현재 Q 묶음(DQpacket — <paramref name="dashRatio"/> &gt; 0 이면 교체 단발) × <paramref name="ratio"/> 의 카드 표시 피해.</summary>
    private static SkillTooltipDamage QPacketTooltip(Player player, float dashRatio, float ratio)
    {
        if (player == null)
            return default;

        PlayerSkillController skills = player.GetComponent<PlayerSkillController>();
        PlayerSkillBase q = skills != null ? skills.GetSkill(PlayerSkillSlot.Main) : null;
        PlayerSkillData data = q != null ? q.Data : null;
        if (data == null)
            return default;

        TraitAttackKey key = TraitAttackKey.ForSkill(PlayerSkillSlot.Main, skills.IsAlternateSkill(q));
        int tickDref = player.ResolveDamage(data.AttackDamageMultiplier, data.FlatDamageBonus, key);
        int damage = TraitDerivedAttackRules.Damage(PaladinTraitRules.QPacketDamage(tickDref, data.MaxActiveDuration, data.TickInterval, dashRatio), ratio);
        int ticks = PaladinTraitRules.PlannedHoldTicks(data.MaxActiveDuration, data.TickInterval);
        float packetRatio = dashRatio > 0f ? dashRatio : 1f;
        float coefficient = data.AttackDamageMultiplier * ticks * packetRatio * ratio;
        int flat = TraitHealRules.RoundHalfUp((double)data.FlatDamageBonus * ticks * packetRatio * ratio);
        return new SkillTooltipDamage(damage, damage, coefficient, coefficient, flat, flat);
    }

    private static float EffectField(TraitDefinition trait, string field)
    {
        var source = new TraitTooltipValueSource(trait, null);
        if (!source.TryGetTooltipValue(field, out object value, out string error))
            throw new InvalidOperationException($"팔라딘 특성 '{trait?.Id}' 별칭 — {error}");

        return Convert.ToSingle(value);
    }
}
