using System;
using System.Collections.Generic;
using UnityEngine;

// 거너 전용 수치 효과(할 일 127). 기여는 공용 ⑩ 스킬 수치 덮어쓰기(TraitModifier.SkillValue)와 같다 — 스킬은 ResolveSkillValue(GunnerTraitRules.*) 로 읽는다.
// 공용 TraitSkillValueEffect 의 entries 배열은 데이터 테이블 시트 행으로 나가지 않아서, 명세 필드 이름 그대로의 칸(시트 행 effects[i].필드)을 둔다.
// 필드 이름 = 카드 설명 토큰({chargeTimeReduction:%} 등) — 별칭 없이 풀린다. 비율 0.25 = 25%.

/// <summary>거너 수치 효과 공용 — 이름 붙은 스킬 수치 몇 개를 낸다.</summary>
[Serializable]
public abstract class GunnerSkillValueEffect : TraitEffect
{
    protected static TraitModifier Set(string key, float value) => TraitModifier.SkillValue(key, TraitValueOp.Set, value);
    protected static TraitModifier Multiply(string key, float value) => TraitModifier.SkillValue(key, TraitValueOp.Multiply, value);

    protected void CheckRatio(List<string> errors, string field, float value, bool belowOne)
    {
        CheckNonNegative(errors, field, value);
        if (belowOne && value >= 1f)
            errors.Add($"{GetType().Name}.{field} = {value} ≥ 1");
    }
}

/// <summary>고속 집속 — Q 최대 충전시간 × (1 − 감소율).</summary>
[Serializable]
public sealed class GunnerChargeTimeEffect : GunnerSkillValueEffect
{
    [Tooltip("Q 최대 충전시간 감소율(0.25 = 25% 짧게). 0 이상 1 미만.")]
    [SerializeField] private float chargeTimeReduction = 0.25f;

    public GunnerChargeTimeEffect() { }
    public GunnerChargeTimeEffect(float chargeTimeReduction) => this.chargeTimeReduction = chargeTimeReduction;

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(Multiply(GunnerTraitRules.QMaxChargeTime, 1f - chargeTimeReduction));

    public override void Validate(List<string> errors) => CheckRatio(errors, nameof(chargeTimeReduction), chargeTimeReduction, true);
}

/// <summary>한계 돌파 — 기본 공격 과열 증가량 × (1 + 증가율), 과열 단계 피해표 교체(기본 공격·Q/R). 동작은 스킬이 <see cref="TraitModeIds.GunnerOverdrive"/> 로 분기.</summary>
[Serializable]
public sealed class GunnerOverdriveEffect : GunnerSkillValueEffect
{
    [Tooltip("기본 공격 1발 과열 증가량 증가율(0.35 = 35% 더).")]
    [SerializeField] private float heatGainIncrease = 0.35f;
    [Tooltip("기본 공격 과열 단계 피해 배율 — 0단계.")]
    [SerializeField] private float basicStageScale0 = 1f;
    [SerializeField] private float basicStageScale1 = 1.2f;
    [SerializeField] private float basicStageScale2 = 1.4f;
    [SerializeField] private float basicStageScale3 = 1.6f;
    [Tooltip("Q·R 과열 단계 피해 배율 — 0단계(Q 보호막 단계 배율은 원래 표).")]
    [SerializeField] private float skillStageScale0 = 1f;
    [SerializeField] private float skillStageScale1 = 1.25f;
    [SerializeField] private float skillStageScale2 = 1.5f;
    [SerializeField] private float skillStageScale3 = 1.75f;

    private float[] BasicScales => new[] { basicStageScale0, basicStageScale1, basicStageScale2, basicStageScale3 };
    private float[] SkillScales => new[] { skillStageScale0, skillStageScale1, skillStageScale2, skillStageScale3 };

    public override void GetModifiers(List<TraitModifier> result)
    {
        result.Add(Multiply(GunnerTraitRules.BasicHeatPerShot, 1f + heatGainIncrease));
        float[] basic = BasicScales;
        float[] skill = SkillScales;
        for (int i = 0; i < basic.Length; i++)
        {
            result.Add(Set(GunnerTraitRules.BasicStageDamagePrefix + i, basic[i]));
            result.Add(Set(GunnerTraitRules.SkillStageDamagePrefix + i, skill[i]));
        }
    }

    public override void Validate(List<string> errors)
    {
        CheckNonNegative(errors, nameof(heatGainIncrease), heatGainIncrease);
        float[] basic = BasicScales;
        float[] skill = SkillScales;
        for (int i = 0; i < basic.Length; i++)
        {
            CheckNonNegative(errors, "basicStageScale" + i, basic[i]);
            CheckNonNegative(errors, "skillStageScale" + i, skill[i]);
        }
    }
}

/// <summary>축열 포격 — 과열을 다 소모했을 때 Q 피해 보너스(1 + 보너스 × 소모 비율). 동작은 <see cref="TraitModeIds.GunnerHeatShot"/> 분기.</summary>
[Serializable]
public sealed class GunnerHeatShotEffect : GunnerSkillValueEffect
{
    [Tooltip("최대 소모 시 Q 피해 보너스(0.6 = +60%).")]
    [SerializeField] private float maxSpentDamageBonus = 0.6f;

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(Set(GunnerTraitRules.QHeatSpentDamageBonus, maxSpentDamageBonus));

    public override void Validate(List<string> errors) => CheckNonNegative(errors, nameof(maxSpentDamageBonus), maxSpentDamageBonus);
}

/// <summary>정온 운용 — 기본 공격 피해 감소율. 과열 제거 동작은 <see cref="TraitModeIds.GunnerCoolMode"/> 분기.</summary>
[Serializable]
public sealed class GunnerCoolModeEffect : GunnerSkillValueEffect
{
    [Tooltip("기본 공격 피해 감소율(0.15 = 15% 감소). 0 이상 1 미만.")]
    [SerializeField] private float basicDamagePenalty = 0.15f;

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(Set(GunnerTraitRules.BasicCoolDamagePenalty, basicDamagePenalty));

    public override void Validate(List<string> errors) => CheckRatio(errors, nameof(basicDamagePenalty), basicDamagePenalty, true);
}

/// <summary>관통 탄도 — 두 번째 적부터 원래 피해 비율.</summary>
[Serializable]
public sealed class GunnerPierceEffect : GunnerSkillValueEffect
{
    [Tooltip("두 번째 적부터 피해 비율(0.65 = 65%). 반복 제곱 아님.")]
    [SerializeField] private float secondaryDamageRatio = 0.65f;

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(Set(GunnerTraitRules.BasicPierceSecondaryRatio, secondaryDamageRatio));

    public override void Validate(List<string> errors) => CheckPositive(errors, nameof(secondaryDamageRatio), secondaryDamageRatio);
}

/// <summary>집속 연사 — 발수·간격·한 발 비율. 총 비율(totalDamageRatio)은 표시용 파생 값(별칭). 동작은 <see cref="TraitModeIds.GunnerVolley"/> 분기.</summary>
[Serializable]
public sealed class GunnerVolleyEffect : GunnerSkillValueEffect
{
    [Tooltip("발수(1 이상).")]
    [SerializeField] private int shotCount = 5;
    [Tooltip("발사 간격(초).")]
    [SerializeField] private float shotInterval = 0.15f;
    [Tooltip("한 발 피해 비율(0.28 = 원래 Q 의 28%).")]
    [SerializeField] private float perShotDamageRatio = 0.28f;

    public override void GetModifiers(List<TraitModifier> result)
    {
        result.Add(Set(GunnerTraitRules.QVolleyShotCount, shotCount));
        result.Add(Set(GunnerTraitRules.QVolleyShotInterval, shotInterval));
        result.Add(Set(GunnerTraitRules.QVolleyPerShotRatio, perShotDamageRatio));
    }

    public override void Validate(List<string> errors)
    {
        if (shotCount < 1)
            errors.Add($"{nameof(GunnerVolleyEffect)}.shotCount = {shotCount} < 1");
        CheckPositive(errors, nameof(shotInterval), shotInterval);
        CheckPositive(errors, nameof(perShotDamageRatio), perShotDamageRatio);
    }
}

/// <summary>집중 포화 — 자신의 R 범위 안 적에게 기본 공격·Q 피해 보너스. 🔴 자동 피해 배율(①)이 아니다 — 스킬이 대상 위치를 보고 곱한다.</summary>
[Serializable]
public sealed class GunnerFocusFireEffect : GunnerSkillValueEffect
{
    [Tooltip("R 범위 안 적 피해 보너스(0.2 = +20%).")]
    [SerializeField] private float damageIncrease = 0.2f;

    public override void GetModifiers(List<TraitModifier> result) =>
        result.Add(Set(GunnerTraitRules.RFocusDamageBonus, damageIncrease));

    public override void Validate(List<string> errors) => CheckNonNegative(errors, nameof(damageIncrease), damageIncrease);
}
