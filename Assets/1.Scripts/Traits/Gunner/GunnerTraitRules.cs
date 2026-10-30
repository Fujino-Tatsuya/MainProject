using UnityEngine;

/// <summary>
/// 거너 특성 수치 이름·질의(할 일 127). 스킬 코드(<c>Player/Gunner/*</c>)·정의 에셋(<c>9.ScriptableObject/Traits/Gunner/</c>)·카드 툴팁(<c>TraitContent.Gunner.cs</c>)이 같은 이름을 쓴다.
/// 스킬 수치 이름은 정의 에셋의 공용 스킬 수치 효과(<see cref="TraitSkillValueEffect"/>, 할 일 133 에서 옛 거너 전용 효과 7종을 합침)가 기여하고 스킬이 <see cref="PlayerTraitModifiers.ResolveSkillValue"/> 로 묻는다(⑩ 과 같은 기여).
/// </summary>
public static class GunnerTraitRules
{
    // ── ⑩ 스킬 수치 이름 ───────────────────────────────────────────────

    /// <summary>고속 집속 — Q 최대 충전시간(Multiply 0.75).</summary>
    public const string QMaxChargeTime = "GUN_Q_MAX_CHARGE_TIME";
    /// <summary>한계 돌파 — 기본 공격 1발 과열 증가량(Multiply 1.35).</summary>
    public const string BasicHeatPerShot = "GUN_BASIC_HEAT_PER_SHOT";
    /// <summary>한계 돌파 — 기본 공격 과열 단계 피해표 교체(Set, 접미사 0~3).</summary>
    public const string BasicStageDamagePrefix = "GUN_BASIC_STAGE_DAMAGE_";
    /// <summary>한계 돌파 — Q·R 과열 단계 피해표 교체(Set, 접미사 0~3).</summary>
    public const string SkillStageDamagePrefix = "GUN_SKILL_STAGE_DAMAGE_";
    /// <summary>축열 포격 — 과열을 모두 소모했을 때 Q 피해 보너스(Set 0.6).</summary>
    public const string QHeatSpentDamageBonus = "GUN_Q_HEAT_SPENT_DAMAGE_BONUS";
    /// <summary>정온 운용 — 기본 공격 피해 감소(Set 0.15).</summary>
    public const string BasicCoolDamagePenalty = "GUN_BASIC_COOL_DAMAGE_PENALTY";
    /// <summary>관통 탄도 — 두 번째 적부터 피해 비율(Set 0.65).</summary>
    public const string BasicPierceSecondaryRatio = "GUN_BASIC_PIERCE_SECONDARY_RATIO";
    /// <summary>집속 연사 — 발수(Set 5).</summary>
    public const string QVolleyShotCount = "GUN_Q_VOLLEY_SHOT_COUNT";
    /// <summary>집속 연사 — 발사 간격 초(Set 0.15).</summary>
    public const string QVolleyShotInterval = "GUN_Q_VOLLEY_SHOT_INTERVAL";
    /// <summary>집속 연사 — 한 발 피해 비율(Set 0.28).</summary>
    public const string QVolleyPerShotRatio = "GUN_Q_VOLLEY_PER_SHOT_RATIO";
    /// <summary>집중 포화 — 자신의 R 범위 안 적에게 기본 공격·Q 피해 보너스(Set 0.2).</summary>
    public const string RFocusDamageBonus = "GUN_R_FOCUS_DAMAGE_BONUS";

    // ── ⑧ 파생 공격 이름 ───────────────────────────────────────────────

    /// <summary>이중 출력 — 기본 공격 3발마다 추가 레이저.</summary>
    public const string BasicExtraDerivedKey = "GUN_BASIC_EXTRA";
    /// <summary>잔류 포격 — Q 끝난 뒤 추가 레이저.</summary>
    public const string QEchoDerivedKey = "GUN_Q_ECHO";

    /// <summary>거너 Q 원래 공격의 키(범위·피해 배율 질의). 거너 Q 는 대체 스킬이 없다.</summary>
    public static TraitAttackKey QAttackKey => TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false);

    /// <summary>거너 기본 공격의 키.</summary>
    public static TraitAttackKey BasicAttackKey => new TraitAttackKey(AttackType.Default);

    /// <summary>
    /// 고속 집속 — 특성을 반영한 Q 최대 충전시간(데이터 값 × 0.75). 데이터와 같은 하한(0.01초)을 둔다.
    /// 피해·사거리 보간 끝점과 최대 도달 후 자동 발사 유지시간은 바꾸지 않는다(명세 GUN_1_CHARGE).
    /// </summary>
    public static float QMaxChargeTimeFor(PlayerTraitModifiers modifiers, float dataMaxChargeTime) =>
        ClampChargeTime(modifiers != null ? modifiers.ResolveSkillValue(QMaxChargeTime, dataMaxChargeTime) : dataMaxChargeTime);

    public static float ClampChargeTime(float seconds) => Mathf.Max(0.01f, float.IsNaN(seconds) ? 0.01f : seconds);

    /// <summary>
    /// 특성 충전시간으로 센 경과를 데이터 충전 곡선의 시계로 옮긴다 — <see cref="GunnerChargeLaserData.ChargeFactor"/> 는 데이터 최대 충전시간 기준이라
    /// 경과 × (데이터 최대 ÷ 특성 최대)를 넘기면 특성 최대 시간에 계수 1 에 닿는다(곡선 모양·끝점 그대로).
    /// </summary>
    public static float DataClockElapsed(float elapsed, float dataMaxChargeTime, float effectiveMaxChargeTime) =>
        elapsed * (ClampChargeTime(dataMaxChargeTime) / ClampChargeTime(effectiveMaxChargeTime));

    /// <summary>최대 충전 뒤 자동 발사 시각(시전 시작 기준 초) = 특성 최대 충전시간 + 데이터 유지시간(유지시간은 특성이 바꾸지 않는다).</summary>
    public static float AutoFireDelay(float effectiveMaxChargeTime, float autoFireHoldTime) =>
        ClampChargeTime(effectiveMaxChargeTime) + Mathf.Max(0f, autoFireHoldTime);

    /// <summary>관통 탄도 — 두 번째 적부터 피해 비율. 특성이 없으면 음수(관통 없음, 첫 대상 1체 — 원래 동작).</summary>
    public static float PierceSecondaryRatioFor(PlayerTraitModifiers modifiers) =>
        modifiers != null ? modifiers.ResolveSkillValue(BasicPierceSecondaryRatio, -1f) : -1f;

    /// <summary>집중 포화 — 자신의 R 범위 안 대상 피해 보너스. 특성이 없으면 0.</summary>
    public static float FocusFireBonusFor(PlayerTraitModifiers modifiers) =>
        modifiers != null ? modifiers.ResolveSkillValue(RFocusDamageBonus, 0f) : 0f;

    /// <summary>그 원래 공격 키에 붙은 파생 공격 중 이름이 맞는 첫 번째(없으면 null). 서버 전용 목록.</summary>
    public static TraitDerivedAttack FindDerived(PlayerTraitEffects effects, TraitAttackKey key, string derivedKey,
        System.Collections.Generic.List<TraitDerivedAttack> buffer)
    {
        if (effects == null)
            return null;

        effects.GetDerivedAttacks(key, buffer);
        for (int i = 0; i < buffer.Count; i++)
        {
            if (buffer[i].Key == derivedKey)
                return buffer[i];
        }

        return null;
    }

    /// <summary>광폭 레이저 — 특성을 반영한 Q 판정 폭(데이터 폭 × 1.3). 사거리·높이는 바꾸지 않는다. VFX 폭은 이 값을 쓰지 않는다.</summary>
    public static float QBeamWidthFor(PlayerTraitModifiers modifiers, float dataBeamWidth) =>
        modifiers != null ? dataBeamWidth * modifiers.AreaMultiplier(QAttackKey, TraitAreaAxis.Width) : dataBeamWidth;
}
