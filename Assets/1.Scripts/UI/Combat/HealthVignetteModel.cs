using UnityEngine;

/// <summary>
/// 체력 비네팅의 순수 계산. 체력 비율 → 강도(severity) → 알파·맥동 속도, 피격 플래시 색·알파, 최종 합성색.
/// 살아 있지 않으면 맥동·플래시는 끄고 검게 바뀌는 비네팅만 남긴다.
/// MonoBehaviour(<see cref="HealthVignetteHUD"/>)는 시간·상태만 넘기고 값은 여기서 받는다 — EditMode 테스트 대상.
/// </summary>
public static class HealthVignetteModel
{
    /// <summary>
    /// 시작 비율(threshold) 초과면 0, 그 아래로 내려갈수록 선형으로 올라 비율 0 에서 1.
    /// </summary>
    public static float Severity(float healthRatio, float threshold)
    {
        if (threshold <= 0f)
            return 0f;

        float ratio = Mathf.Clamp01(healthRatio);
        if (ratio > threshold)
            return 0f;

        return Mathf.Clamp01(1f - ratio / threshold);
    }

    /// <summary>체력 비율 → 0..1. 최대 체력이 0 이하면(스탯 미복제) 1 을 돌려 비네팅이 켜지지 않게 한다.</summary>
    public static float HealthRatio(int currentHealth, int maxHealth)
    {
        return maxHealth > 0 ? Mathf.Clamp01((float)currentHealth / maxHealth) : 1f;
    }

    /// <summary>맥동 속도(초당 주기 수). 강도 0 이면 맥동 없음, 강도가 클수록 최소→최대로 빨라진다.</summary>
    public static float PulseSpeed(float severity, float minSpeed, float maxSpeed)
    {
        return severity <= 0f ? 0f : Mathf.Lerp(minSpeed, maxSpeed, Mathf.Clamp01(severity));
    }

    /// <summary>
    /// 평상시 비네팅 알파. 기본값 = 강도 × 최대 알파, 여기에 강도에 비례한 진폭으로 sin 맥동을 더한다.
    /// <paramref name="pulsePhase"/> 는 누적 주기 수(속도 × 시간의 적분)라 속도가 바뀌어도 튀지 않는다.
    /// </summary>
    public static float VignetteAlpha(float severity, float maxAlpha, float pulseAmplitude, float pulsePhase)
    {
        if (severity <= 0f)
            return 0f;

        float s = Mathf.Clamp01(severity);
        float pulse = Mathf.Sin(pulsePhase * 2f * Mathf.PI) * pulseAmplitude * s;
        return Mathf.Clamp01(s * maxAlpha + pulse);
    }

    /// <summary>실제 적용할 맥동 진폭. 살아 있지 않으면(사망·Soul·관전) 0 — 비네팅 알파가 멈춘다.</summary>
    public static float ActivePulseAmplitude(bool alive, float pulseAmplitude)
    {
        return alive ? pulseAmplitude : 0f;
    }

    /// <summary>실제 적용할 플래시 알파. 살아 있지 않으면 0 — 진행 중이던 플래시도 끊는다.</summary>
    public static float ActiveFlashAlpha(bool alive, float flashAlpha)
    {
        return alive ? flashAlpha : 0f;
    }

    /// <summary>피격 플래시 색. 시작 비율 이상이면 안전색(흰색), 그 아래로 내려갈수록 위험색(빨강)으로 보간.</summary>
    public static Color FlashColor(float healthRatio, float threshold, Color safeColor, Color dangerColor)
    {
        return Color.Lerp(safeColor, dangerColor, Severity(healthRatio, threshold));
    }

    /// <summary>피격 직후 최대(peak)에서 시작해 duration 동안 선형으로 0 까지 빠진다.</summary>
    public static float FlashAlpha(float elapsed, float duration, float peakAlpha)
    {
        if (duration <= 0f || elapsed < 0f || elapsed >= duration)
            return 0f;

        return peakAlpha * (1f - elapsed / duration);
    }

    /// <summary>
    /// 평상시 비네팅과 플래시를 이미지 하나의 색으로 합친다. 알파는 둘 중 큰 쪽, RGB 는 플래시 비중만큼 플래시 색으로.
    /// 마지막에 <paramref name="darken"/>(0..1)만큼 RGB 에 검은색을 곱한다 — 사망·Soul·관전 표현.
    /// </summary>
    public static Color Compose(Color vignetteColor, float vignetteAlpha, Color flashColor, float flashAlpha, float darken)
    {
        float alpha = Mathf.Max(vignetteAlpha, flashAlpha);
        float flashWeight = alpha > 0f ? Mathf.Clamp01(flashAlpha / alpha) : 0f;

        Color rgb = Color.Lerp(vignetteColor, flashColor, flashWeight);
        float brightness = 1f - Mathf.Clamp01(darken);
        return new Color(rgb.r * brightness, rgb.g * brightness, rgb.b * brightness, Mathf.Clamp01(alpha));
    }
}
