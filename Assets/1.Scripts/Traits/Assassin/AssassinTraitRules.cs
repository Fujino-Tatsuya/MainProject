using UnityEngine;

/// <summary>
/// 칼날의 영역(ASN_3_E_AREA, 할 일 128 2/5) — 어쌔신 판정 치수에 범위 배율(⑦ <see cref="PlayerTraitModifiers.AreaMultiplier"/>)을 바르는 순수 규칙.
/// 면적이 아니라 치수 배율이다(명세 — 길이·폭·반경 각각 ×1.25). 자기 이동·중심 오프셋은 바꾸지 않는다. VFX 크기는 여기서 다루지 않는다(판정·예고만).
/// </summary>
public static class AssassinTraitAreaRules
{
    /// <summary>일반 E 강타(강화한 기본 공격) — 부채꼴 길이·폭.</summary>
    public static readonly TraitAttackKey EnhancedStrike = new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced);

    /// <summary>변신 E 원형 5타 — 원 반경.</summary>
    public static readonly TraitAttackKey CircleStrike = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate);

    /// <summary>
    /// 부채꼴 판정의 치수 배율. 길이 = 사거리 × <paramref name="lengthMultiplier"/>, 폭 = 끝 현 길이(<see cref="ConeWidth"/>) × <paramref name="widthMultiplier"/>.
    /// 두 배율이 같으면 각도는 그대로(닮은꼴 확대 — 사거리만 늘려도 폭이 같은 비율로 는다). 다르면 폭이 맞게 각도를 고친다(180° 이상은 폭 = 지름이라 그대로).
    /// </summary>
    public static void ScaleCone(float range, float angle, float lengthMultiplier, float widthMultiplier,
        out float scaledRange, out float scaledAngle)
    {
        lengthMultiplier = Mathf.Max(0f, lengthMultiplier);
        widthMultiplier = Mathf.Max(0f, widthMultiplier);
        scaledRange = range * lengthMultiplier;
        scaledAngle = angle;
        if (lengthMultiplier <= 0f || Mathf.Approximately(lengthMultiplier, widthMultiplier) || angle >= 180f)
            return;

        float halfSin = Mathf.Sin(angle * 0.5f * Mathf.Deg2Rad) * widthMultiplier / lengthMultiplier;
        scaledAngle = 2f * Mathf.Asin(Mathf.Clamp01(halfSin)) * Mathf.Rad2Deg;
    }

    /// <summary>스킬 판정용 — 그 공격 키의 길이·폭 배율로 <see cref="ScaleCone(float,float,float,float,out float,out float)"/>. 특성 출처가 없으면 그대로.</summary>
    public static void ScaleCone(PlayerTraitModifiers modifiers, TraitAttackKey key, float range, float angle,
        out float scaledRange, out float scaledAngle)
    {
        float length = modifiers != null ? modifiers.AreaMultiplier(key, TraitAreaAxis.Length) : 1f;
        float width = modifiers != null ? modifiers.AreaMultiplier(key, TraitAreaAxis.Width) : 1f;
        ScaleCone(range, angle, length, width, out scaledRange, out scaledAngle);
    }

    /// <summary>부채꼴 끝의 폭(현 길이) — 180° 미만 = 2r·sin(θ/2), 이상 = 지름 2r.</summary>
    public static float ConeWidth(float range, float angle) =>
        angle >= 180f ? 2f * range : 2f * range * Mathf.Sin(angle * 0.5f * Mathf.Deg2Rad);

    /// <summary>원 반경 × 배율(0 미만 배율은 0).</summary>
    public static float ScaleRadius(float radius, float multiplier) => radius * Mathf.Max(0f, multiplier);

    /// <summary>스킬 판정·예고용 — 그 공격 키의 반경 배율. 특성 출처가 없으면 그대로.</summary>
    public static float ScaleRadius(PlayerTraitModifiers modifiers, TraitAttackKey key, float radius) =>
        ScaleRadius(radius, modifiers != null ? modifiers.AreaMultiplier(key, TraitAreaAxis.Radius) : 1f);
}

/// <summary>
/// 어쌔신 기본 공격 동작과 특성 사건의 묶음 규칙(할 일 128 2/5 — 돌파의 여세·사냥의 박자).
/// 동작 1개 = 일반 연계 한 타 · 강타 1회 · 변신 4타 묶음 1개 — 동작마다 시전 ID 하나(<see cref="Player.NextServerCastId"/>)를 받아
/// 그 동작의 모든 판정을 같은 ID 로 알린다(사냥의 박자 '동작당 1회' = 효과 거름 oncePerCast).
/// </summary>
public static class AssassinBasicAttackTraitRules
{
    /// <summary>
    /// 돌파의 여세(⑨) 소모 자리 — 그 동작의 첫 원래 피해 판정(1부터 센 판정 번호 = 1)에서만 소모를 묻는다.
    /// 빗나가도 그 판정에서 소모되고, 변신 4타 묶음의 2~4타는 강화되지 않는다.
    /// </summary>
    public static bool ConsumesNextAttackBuff(int hitNumberInAction) => hitNumberInAction == 1;
}
