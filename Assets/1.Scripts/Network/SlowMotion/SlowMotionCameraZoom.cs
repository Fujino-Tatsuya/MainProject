using UnityEngine;

/// <summary>
/// 오너 슬로우 줌인 규칙(PLAN-interrupt-slowmo D10) — <see cref="CameraTargetSwitcher"/> 가 플레이어 vcam 의
/// FollowOffset 에 곱할 배율을 정한다.
///
/// <list type="bullet">
/// <item>줌 깊이는 슬로우 깊이를 그대로 따른다: depth = clamp01((1 − 현재 배율) / (1 − 프로필 배율)).
///       진입·유지·복귀·실패 복귀를 따로 다루지 않아도 곡선이 같이 따라온다.</item>
/// <item>내가 일으킨 슬로우일 때만 깊이를 쓴다. 남의 슬로우가 덮어쓰면 목표 0 → 원거리로 줌아웃.</item>
/// <item>낙하 뷰·관전·세션 미가동이면 줌 없음.</item>
/// <item>발동자 전환 순간 튀지 않게 실시간으로 짧게 감쇠한다(<see cref="StepDepth"/>).</item>
/// </list>
/// </summary>
public static class SlowMotionCameraZoom
{
    /// <summary>오프셋 배율 하한. 0 이면 카메라가 플레이어에 박힌다.</summary>
    public const float MinZoomFactor = 0.1f;

    /// <summary>목표 깊이까지 0 → 1 을 가는 데 걸리는 실시간 초.</summary>
    public const float DepthBlendSeconds = 0.12f;

    /// <summary>
    /// 이번 프레임 목표 줌 깊이 [0, 1].
    /// </summary>
    /// <param name="slowActive">전역 슬로우가 진행 중인가.</param>
    /// <param name="triggeredByMe">진행 중인 슬로우의 발동자가 이 피어의 LocalClientId 인가.</param>
    /// <param name="zoomAllowed">낙하 뷰·관전이 아니고 세션이 가동 중인가.</param>
    /// <param name="currentScale">이번 프레임 전역 배율.</param>
    /// <param name="profileScale">슬로우 프로필의 유지 배율(정리된 값).</param>
    public static float TargetDepth(bool slowActive, bool triggeredByMe, bool zoomAllowed,
                                    float currentScale, float profileScale)
    {
        if (!slowActive || !triggeredByMe || !zoomAllowed)
            return 0f;

        // 느려지지 않는 프로필(배율 1)은 깊이를 정의할 수 없다 — 줌 없음.
        float span = 1f - profileScale;
        if (float.IsNaN(span) || span <= 1e-4f || float.IsNaN(currentScale))
            return 0f;

        return Mathf.Clamp01((1f - currentScale) / span);
    }

    /// <summary>깊이 → FollowOffset 배율. depth 0 = 1, depth 1 = <paramref name="zoomFactor"/>.</summary>
    public static float OffsetMultiplier(float depth, float zoomFactor)
    {
        if (float.IsNaN(depth))
            return 1f;

        return Mathf.Lerp(1f, ClampZoomFactor(zoomFactor), depth);
    }

    /// <summary>줌은 당기기만 한다 — (0, 1] 밖은 정리한다. NaN 은 줌 없음.</summary>
    public static float ClampZoomFactor(float zoomFactor) =>
        float.IsNaN(zoomFactor) ? 1f : Mathf.Clamp(zoomFactor, MinZoomFactor, 1f);

    /// <summary>현재 깊이를 목표로 실시간 <paramref name="unscaledDeltaTime"/> 만큼 옮긴다(최대 속도 1 / <see cref="DepthBlendSeconds"/>).</summary>
    public static float StepDepth(float current, float target, float unscaledDeltaTime)
    {
        if (float.IsNaN(current))
            current = 0f;

        return Mathf.MoveTowards(current, target, Mathf.Max(0f, unscaledDeltaTime) / DepthBlendSeconds);
    }
}
