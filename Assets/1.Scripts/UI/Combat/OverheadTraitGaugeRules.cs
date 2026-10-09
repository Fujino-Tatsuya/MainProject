using UnityEngine;

public enum AssassinRageTone { Building, Ready, Transformed }
public enum GunnerHeatTone { Cool, Warm, Hot }
public enum FirstMeleePassiveTone { Charging, Charged }

/// <summary>
/// 머리 위 특성 게이지의 채움·색 단계 판정(character_assassin.md 구현 결정 '머리 위 특성 게이지'). MonoBehaviour 밖 순수 계산이라 EditMode 테스트로 고정한다.
/// 단계 → 실제 색은 각 공급자 컴포넌트의 직렬화 색이 정한다.
/// </summary>
public static class OverheadTraitGaugeRules
{
    /// <summary>value / max 를 0~1 로. max 가 0 이하면 0.</summary>
    public static float Normalize(float value, float max) => max > 0f ? Mathf.Clamp01(value / max) : 0f;

    /// <summary>암살자 — 변신 중 보라 / 최소 변신량 이상 초록 / 미만 흰색.</summary>
    public static AssassinRageTone AssassinTone(float rage, float minTransformRage, bool transformed)
    {
        if (transformed)
            return AssassinRageTone.Transformed;
        return rage >= minTransformRage ? AssassinRageTone.Ready : AssassinRageTone.Building;
    }

    /// <summary>거너 — 3단계·과열 빨강 / 1~2단계 주황 / 0단계 흰색.</summary>
    public static GunnerHeatTone GunnerTone(int stage, bool overheated)
    {
        if (overheated || stage >= 3)
            return GunnerHeatTone.Hot;
        return stage >= 1 ? GunnerHeatTone.Warm : GunnerHeatTone.Cool;
    }

    /// <summary>과열 깜빡임 — 주기 앞 절반에 깜빡임 색을 쓴다(GunnerHeatGauge 와 같은 규칙).</summary>
    public static bool IsBlinkOn(bool overheated, float time, float period)
        => overheated && period > 0f && Mathf.Repeat(time, period) < period * 0.5f;

    /// <summary>가붕이 — 충전(활성) 노랑 / 차는 중 흰색.</summary>
    public static FirstMeleePassiveTone PassiveTone(bool charged)
        => charged ? FirstMeleePassiveTone.Charged : FirstMeleePassiveTone.Charging;

    /// <summary>가붕이 패시브 쿨타임 진행률 — 충전 상태면 1, 아니면 1 − 남은 쿨/쿨타임.</summary>
    public static float FirstMeleePassiveFill(bool charged, float remainingCooldown, float cooldownTime)
    {
        if (charged || cooldownTime <= 0f)
            return 1f;
        return 1f - Normalize(remainingCooldown, cooldownTime);
    }
}
