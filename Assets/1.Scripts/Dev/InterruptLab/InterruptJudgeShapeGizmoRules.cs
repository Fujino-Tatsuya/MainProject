#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>상시 미리보기 상태 — 지금 자리·조준 방향의 판정 형상에 오너 예측이 고를 대상이 있는가, 그 창이 열렸는가.</summary>
public enum InterruptJudgePreviewState
{
    NoTarget,
    TargetClosed,
    TargetOpen,
}

/// <summary>
/// 간파 판정 범위 기즈모(할 일 156)의 순수 규칙 — 색·잔상 나이·불투명도. 형상·포즈는 여기서 만들지 않는다
/// (<see cref="PlayerInterruptSkillBase.TryGetPlacedJudgeShape"/> = 시도 예측 오버랩과 같은 <see cref="HitboxAnchorShape.PlacedAt"/>).
/// </summary>
public static class InterruptJudgeShapeGizmoRules
{
    public const float DefaultAfterimageSeconds = 3f;
    public const float AfterimageMinAlpha = 0.25f;
    public const int MaxAfterimages = 12;

    // 시도 잔상 = 예측 결과(InterruptAttemptForecast) 색.
    public static readonly Color AttemptNoTargetColor = new Color(0.75f, 0.75f, 0.75f);
    public static readonly Color AttemptWeakColor = new Color(1f, 0.85f, 0.2f);
    public static readonly Color AttemptStrongColor = new Color(1f, 0.3f, 0.3f);

    // 상시 미리보기 = 예측 결과가 아니라 지금 대상·창 상태라 다른 색 계열.
    public static readonly Color PreviewNoTargetColor = new Color(1f, 1f, 1f, 0.45f);
    public static readonly Color PreviewClosedColor = new Color(1f, 0.6f, 0.1f, 0.8f);
    public static readonly Color PreviewOpenColor = new Color(0.2f, 0.9f, 1f, 0.9f);

    public static Color ForForecast(InterruptAttemptForecast forecast) => forecast switch
    {
        InterruptAttemptForecast.Strong => AttemptStrongColor,
        InterruptAttemptForecast.Weak => AttemptWeakColor,
        _ => AttemptNoTargetColor,
    };

    public static InterruptJudgePreviewState PreviewStateOf(bool hasTarget, bool open) =>
        !hasTarget ? InterruptJudgePreviewState.NoTarget
        : open ? InterruptJudgePreviewState.TargetOpen
        : InterruptJudgePreviewState.TargetClosed;

    public static Color ForPreview(InterruptJudgePreviewState state) => state switch
    {
        InterruptJudgePreviewState.TargetOpen => PreviewOpenColor,
        InterruptJudgePreviewState.TargetClosed => PreviewClosedColor,
        _ => PreviewNoTargetColor,
    };

    /// <summary>
    /// 잔상 나이 진행 — 실시간으로 늙되 시간이 멈춘 동안(timeScale 0 — 보정 질문 창 정지)은 늙지 않는다.
    /// 슬로우(0.05배 등) 중에도 실시간으로 늙어 몇 초 안에 사라진다.
    /// </summary>
    public static float AdvanceAge(float age, float timeScale, float unscaledDeltaTime) =>
        timeScale > 0f ? age + Mathf.Max(0f, unscaledDeltaTime) : age;

    public static bool IsExpired(float age, float duration) => age >= duration;

    /// <summary>잔상 불투명도 배율 — 처음 1 에서 수명 끝 <see cref="AfterimageMinAlpha"/> 까지 선형, 수명이 지나면 0.</summary>
    public static float AfterimageAlpha(float age, float duration)
    {
        if (age <= 0f)
            return 1f;
        if (duration <= 0f || age >= duration)
            return 0f;
        return Mathf.Lerp(1f, AfterimageMinAlpha, age / duration);
    }

    public static Color WithAlphaScale(Color color, float scale) => new Color(color.r, color.g, color.b, color.a * scale);

    public static string DescribeForecast(InterruptAttemptForecast forecast) => forecast switch
    {
        InterruptAttemptForecast.Strong => "강",
        InterruptAttemptForecast.Weak => "약",
        _ => "대상 없음",
    };

    public static string DescribePreview(InterruptJudgePreviewState state) => state switch
    {
        InterruptJudgePreviewState.TargetOpen => "대상 창 열림",
        InterruptJudgePreviewState.TargetClosed => "대상 창 닫힘",
        _ => "대상 없음",
    };
}
#endif
