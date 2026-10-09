using System;
using UnityEngine;

/// <summary>
/// 슬로우 모션 한 번의 모양 — 목표 배율과 단계별 길이(실시간 초).
///
/// 곡선은 고정이다: 진입 = linear, 복귀·실패 복귀 = ease-out(quad).
/// AnimationCurve 를 쓰지 않는 이유는 <see cref="SlowMotionTimeline.LostTimeUntil"/> 이 피어마다 같은 값을
/// 닫힌 식으로 내야 하기 때문이다(GameNow 보정 — 샘플링 적분은 피어·프레임마다 어긋난다).
///
/// 저작값은 그대로 두고, 읽을 때 <c>Clamped*</c> 로 정리한다 — 타임라인은 시작 시점에 정리된 값을 복사해 쓴다.
/// </summary>
[Serializable]
public sealed class SlowMotionProfile
{
    /// <summary>배율 하한. 0 은 일시정지와 구분이 안 되고, 복귀 곡선이 0 에서 출발하면 의미가 없어 막는다.</summary>
    public const float MinScale = 0.01f;

    // 범위 속성은 인스펙터·데이터 테이블 검증용. 코드 경로는 아래 Clamped* 가 따로 지킨다.
    [Tooltip("유지 단계의 시간 배율. [0.01, 1] — 1 이면 느려지지 않는다")]
    [Range(MinScale, 1f)]
    public float scale = 0.3f;

    [Tooltip("진입 단계 길이(실시간 초). 현재 배율 → scale, linear")]
    [Min(0f)]
    public float enterDuration = 0.05f;

    [Tooltip("유지 단계 길이(실시간 초). scale 고정")]
    [Min(0f)]
    public float holdDuration = 0.35f;

    [Tooltip("복귀 단계 길이(실시간 초). scale → 1, ease-out")]
    [Min(0f)]
    public float exitDuration = 0.25f;

    [Tooltip("실패 복귀 길이(실시간 초). 빗나갔을 때 현재 배율 → 1, ease-out")]
    [Min(0f)]
    public float failExitDuration = 0.1f;

    public float ClampedScale => float.IsNaN(scale) ? 1f : Mathf.Clamp(scale, MinScale, 1f);
    public float ClampedEnterDuration => ClampDuration(enterDuration);
    public float ClampedHoldDuration => ClampDuration(holdDuration);
    public float ClampedExitDuration => ClampDuration(exitDuration);
    public float ClampedFailExitDuration => ClampDuration(failExitDuration);

    // 음수·NaN 은 0 = "그 단계 없음". 무한대는 끝나지 않는 슬로우라 0 으로 본다 — 전역 시간이 영영 묶이는 쪽이 더 위험하다.
    static float ClampDuration(float seconds) =>
        float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f ? 0f : seconds;
}
