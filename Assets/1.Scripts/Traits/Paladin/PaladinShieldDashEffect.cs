using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 돌파의 방패(PAL_2_DASH) 수치 칸 — 모드 변경 특성이라 서버 연결은 없고, 스킬 수치(⑩ <see cref="TraitModifierKind.SkillValue"/>, Set)만 낸다.
/// 공용 <see cref="TraitSkillValueEffect"/> 와 같은 기여지만, 그쪽은 구조체 배열(entries)이라 데이터 테이블 시트에 나가지 않아
/// 명세 제안 필드 이름 그대로 평범한 칸으로 둔다 — 시트 Traits_Paladin 행 = <c>effects[0].dashDistance</c> 등, 카드 <c>{dashDistance}</c> 도 이 칸을 읽는다.
/// Q 는 <see cref="PlayerTraitModifiers.ResolveSkillValue"/>(<see cref="PaladinTraitValues.DashDistanceKey"/> 등, 0)으로 읽는다(<see cref="PaladinShieldDashSettings"/>).
/// </summary>
[Serializable]
public sealed class PaladinShieldDashEffect : TraitEffect
{
    [Tooltip("돌진 거리(m). 기본 Q 유효 최대 거리보다 짧아야 한다(아니면 콘텐츠 검증 오류).")]
    [Min(0f)]
    [SerializeField] private float dashDistance = 3f;
    [Tooltip("돌진 속도(m/s).")]
    [Min(0f)]
    [SerializeField] private float dashSpeed = 18f;
    [Tooltip("입력 뒤 제자리 준비(초).")]
    [Min(0f)]
    [SerializeField] private float prepareTime = 0.1f;
    [Tooltip("돌진 뒤 회수(초). 끝나면 Q 정상 종료.")]
    [Min(0f)]
    [SerializeField] private float recoveryTime = 0.12f;
    [Tooltip("교체 단발 피해 = 원래 Q 예정 피해 합계(DQfull) × 이 배율.")]
    [Min(0f)]
    [SerializeField] private float fullCastDamageRatio = 1.25f;
    [Tooltip("단발 넉백 거리(m).")]
    [Min(0f)]
    [SerializeField] private float pushDistance = 3f;
    [Tooltip("단발 넉백 시간(초).")]
    [Min(0f)]
    [SerializeField] private float pushDuration = 0.3f;

    public PaladinShieldDashEffect() { }

    public PaladinShieldDashEffect(float dashDistance, float dashSpeed, float prepareTime, float recoveryTime,
        float fullCastDamageRatio, float pushDistance, float pushDuration)
    {
        this.dashDistance = dashDistance;
        this.dashSpeed = dashSpeed;
        this.prepareTime = prepareTime;
        this.recoveryTime = recoveryTime;
        this.fullCastDamageRatio = fullCastDamageRatio;
        this.pushDistance = pushDistance;
        this.pushDuration = pushDuration;
    }

    public override void GetModifiers(List<TraitModifier> result)
    {
        result.Add(TraitModifier.SkillValue(PaladinTraitValues.DashDistanceKey, TraitValueOp.Set, dashDistance));
        result.Add(TraitModifier.SkillValue(PaladinTraitValues.DashSpeedKey, TraitValueOp.Set, dashSpeed));
        result.Add(TraitModifier.SkillValue(PaladinTraitValues.DashPrepareKey, TraitValueOp.Set, prepareTime));
        result.Add(TraitModifier.SkillValue(PaladinTraitValues.DashRecoveryKey, TraitValueOp.Set, recoveryTime));
        result.Add(TraitModifier.SkillValue(PaladinTraitValues.DashDamageRatioKey, TraitValueOp.Set, fullCastDamageRatio));
        result.Add(TraitModifier.SkillValue(PaladinTraitValues.DashPushDistanceKey, TraitValueOp.Set, pushDistance));
        result.Add(TraitModifier.SkillValue(PaladinTraitValues.DashPushDurationKey, TraitValueOp.Set, pushDuration));
    }

    public override void Validate(List<string> errors)
    {
        CheckPositive(errors, nameof(dashDistance), dashDistance);
        CheckPositive(errors, nameof(dashSpeed), dashSpeed);
        CheckNonNegative(errors, nameof(prepareTime), prepareTime);
        CheckNonNegative(errors, nameof(recoveryTime), recoveryTime);
        CheckPositive(errors, nameof(fullCastDamageRatio), fullCastDamageRatio);
        CheckPositive(errors, nameof(pushDistance), pushDistance);
        CheckPositive(errors, nameof(pushDuration), pushDuration);
    }
}
