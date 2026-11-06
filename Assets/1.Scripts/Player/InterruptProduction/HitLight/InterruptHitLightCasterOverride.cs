using System;
using UnityEngine;

/// <summary>
/// 시전자 캐릭터별 적중 라이트 덮어쓰기(할 일 238) — 디렉터 칸. 지금은 거너 하나(<c>gunnerHitLight</c>).
/// 일반·대쉬·제압 세 단계(V3 일반·<c>V3/Dash</c>·<c>V3/Suppress</c> SO·프리팹) 모두에 같은 값을 얹는다 — 단계 SO 를 복제하지 않는다.
/// 캐릭터 구분 = 시전자 플레이어의 간파 스킬 종류(모든 피어가 자기 스폰 목록으로 같은 답).
/// </summary>
[Serializable]
public sealed class InterruptHitLightCasterOverride
{
    [Tooltip("켜면 이 캐릭터가 간파 성공했을 때 아래 값을 얹는다")]
    public bool enabled = true;

    [Tooltip("재생하지 않을 월드 이펙트 파티클 이름(FX_InterruptHitLight* 의 자식 이름 — Core·Streak·Sparks·Rays). 기본 = 빛줄기 Rays + 불꽃 Sparks")]
    public string[] hiddenParticles = { InterruptHitLightFx.RaysName, InterruptHitLightFx.SparksName };

    [Tooltip("켜면 포인트 라이트 색을 아래 색으로(SO LightColor 대신). 세기·반경·곡선은 SO 그대로")]
    public bool overrideLightColor = true;

    [Tooltip("포인트 라이트 색 — 기본 = 거너 간파 이펙트 FX_Arrow_hail 의 LightFlash 시작 색(0.42, 0.588, 1)")]
    public Color lightColor = new Color(0.41960788f, 0.58787316f, 1f, 1f);

    /// <summary>요청에 실을 값. 꺼져 있거나 null 이면 덮어쓰기 없음.</summary>
    public static InterruptHitLightFxOverride ToFx(InterruptHitLightCasterOverride source) =>
        source != null && source.enabled
            ? new InterruptHitLightFxOverride(source.hiddenParticles, source.overrideLightColor, source.lightColor)
            : default;
}

/// <summary>적중 라이트 월드 이펙트 한 번에 얹는 값(할 일 238). 기본값 = 덮어쓰기 없음.</summary>
public readonly struct InterruptHitLightFxOverride
{
    public readonly string[] HiddenParticles;
    public readonly bool HasLightColor;
    public readonly Color LightColor;

    public InterruptHitLightFxOverride(string[] hiddenParticles, bool hasLightColor, Color lightColor)
    {
        HiddenParticles = hiddenParticles;
        HasLightColor = hasLightColor;
        LightColor = lightColor;
    }

    /// <summary>그 이름의 파티클을 숨기는가(대소문자 구분, 앞뒤 공백 무시).</summary>
    public bool Hides(string particleName)
    {
        if (HiddenParticles == null || string.IsNullOrEmpty(particleName))
            return false;

        for (int i = 0; i < HiddenParticles.Length; i++)
        {
            string hidden = HiddenParticles[i];
            if (!string.IsNullOrEmpty(hidden) && hidden.Trim() == particleName)
                return true;
        }

        return false;
    }

    /// <summary>쓸 라이트 색 — 덮어쓰기 없으면 SO 색.</summary>
    public Color LightColorOr(Color lookColor) => HasLightColor ? LightColor : lookColor;
}
