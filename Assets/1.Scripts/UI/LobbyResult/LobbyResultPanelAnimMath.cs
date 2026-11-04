using System;
using UnityEngine;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215) 계산 — MonoBehaviour 밖 순수 함수(EditMode 테스트 대상).
/// 시간은 호출 측이 넘긴다(<see cref="LobbyResultPanelAnimator"/> = <c>Time.unscaledDeltaTime</c> 누적).
/// 곡선 평가는 호출 측이 한다 — 여기는 진행도·보간·숫자만.
/// </summary>
public static class LobbyResultPanelAnimMath
{
    /// <summary>경과 시간 → 진행도 0~1. 지속시간 0 이하면 바로 1(즉시 끝).</summary>
    public static float Progress(float elapsed, float duration)
    {
        if (duration <= 0f)
            return 1f;
        return Mathf.Clamp01(elapsed / duration);
    }

    /// <summary>
    /// 카운트업 진행도 — 열린 뒤 <paramref name="delay"/> 동안 0, 그 뒤 <paramref name="duration"/> 에 걸쳐 0→1.
    /// 지속시간 0 이하면 지연이 지나는 순간 1.
    /// </summary>
    public static float CountUpProgress(float sinceOpen, float delay, float duration)
    {
        float t = sinceOpen - Mathf.Max(0f, delay);
        if (t < 0f)
            return 0f;
        return Progress(t, duration);
    }

    /// <summary>창 보임 정도(0 = 숨김, 1 = 다 보임) → 투명도. 곡선이 넘쳐도 0~1 로 자른다.</summary>
    public static float Alpha(float visibility) => Mathf.Clamp01(visibility);

    /// <summary>창 보임 정도 → 크기 배율. 0 이면 <paramref name="hiddenScale"/>, 1 이면 1. 곡선이 넘치면(Back 류) 그대로 넘친다.</summary>
    public static float Scale(float visibility, float hiddenScale) => Mathf.LerpUnclamped(hiddenScale, 1f, visibility);

    /// <summary>
    /// 카운트업 중 보일 숫자. 진행도는 0~1 로 자른다(곡선이 넘쳐도 최종값을 넘지 않는다). 1 이면 정확히 최종값.
    /// 큰 값의 float 오차를 피하려고 double 로 곱한다.
    /// </summary>
    public static int CountUpValue(int finalValue, float progress)
    {
        if (progress >= 1f)
            return finalValue;
        if (progress <= 0f)
            return 0;
        return (int)Math.Round(finalValue * (double)progress, MidpointRounding.AwayFromZero);
    }
}
