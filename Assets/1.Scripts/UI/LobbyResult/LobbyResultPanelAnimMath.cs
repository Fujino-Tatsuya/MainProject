using System;
using EuniTween;
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

    /// <summary>
    /// 카운트업 중 보일 시간(초). 진행도는 0~1 로 자른다 — 1 이면 정확히 최종값, 그 전엔 최종값을 넘지 않는다.
    /// 표기(mm:ss, 초 아래 버림)는 <see cref="SessionResult.FormatTime"/>.
    /// </summary>
    public static float CountUpSeconds(float finalSeconds, float progress)
    {
        float final = Mathf.Max(0f, finalSeconds);
        if (progress >= 1f)
            return final;
        if (progress <= 0f)
            return 0f;
        return Mathf.Min(final, final * progress);
    }

    /// <summary>왕관 첫 점프 시작 시각(열기부터 초) = 숫자 지연 + 숫자 시간 + 왕관 지연 — 카운트업이 끝난 직후. 음수 칸은 0 취급.</summary>
    public static float CrownFirstStart(float countUpDelay, float countUpDuration, float crownDelay) =>
        Mathf.Max(0f, countUpDelay) + Mathf.Max(0f, countUpDuration) + Mathf.Max(0f, crownDelay);

    /// <summary>
    /// 왕관 점프 주기(시작→다음 시작, 초). 반복 간격 0 이하 = 0(1회만). 간격이 점프 시간보다 짧으면 점프 시간(겹치지 않게).
    /// </summary>
    public static float CrownPeriod(float crownDuration, float repeatInterval) =>
        repeatInterval <= 0f ? 0f : Mathf.Max(repeatInterval, Mathf.Max(0f, crownDuration));

    /// <summary>
    /// 지금 왕관 점프 진행도 0~1 — 첫 시작 전 0, 점프 중 0→1, 다음 점프까지 1(제자리).
    /// 반복이 있으면 <see cref="CrownPeriod"/> 마다 다시 0 부터. 점프 시간 0 이하 = 늘 1(연출 없음).
    /// </summary>
    public static float CrownCycleProgress(float sinceOpen, float firstStart, float crownDuration, float repeatInterval)
    {
        float t = sinceOpen - Mathf.Max(0f, firstStart);
        if (t < 0f)
            return 0f;
        if (crownDuration <= 0f)
            return 1f;
        return Progress(Repeat(t, CrownPeriod(crownDuration, repeatInterval)), crownDuration);
    }

    /// <summary>
    /// 가장 최근 착지부터 지난 시간(초). 아직 한 번도 착지 안 했으면 -1. 반복이 있으면 착지마다 0 으로 돌아간다(반짝임 기준).
    /// </summary>
    public static float SinceLastCrownLand(float sinceOpen, float firstStart, float crownDuration, float repeatInterval)
    {
        float t = sinceOpen - Mathf.Max(0f, firstStart) - Mathf.Max(0f, crownDuration);
        if (t < 0f)
            return -1f;
        return Repeat(t, CrownPeriod(crownDuration, repeatInterval));
    }

    // t ≥ 0 을 주기로 접는다. 주기 0 이하 = 그대로.
    private static float Repeat(float t, float period) => period > 0f ? t - Mathf.Floor(t / period) * period : t;

    /// <summary>
    /// 왕관 점프 높이(UI 단위) — 0 → <paramref name="height"/> → 0. EuniTween <see cref="Easing.Punch"/>:
    /// 오르는 절반에 <paramref name="ease"/>(OutQuad 면 위로 갈수록 느려짐), 내려오는 절반은 그 거울(떨어질수록 빨라짐).
    /// 진행도 0·1 이면 정확히 0(제자리).
    /// </summary>
    public static float CrownJumpOffset(float progress, float height, Ease ease)
    {
        if (progress <= 0f || progress >= 1f)
            return 0f;
        return height * Easing.Punch(ease, progress);
    }

    /// <summary>
    /// 왕관 회전 각도(도) — 0 → <paramref name="degrees"/>. EuniTween <see cref="Easing.Evaluate"/> 로 이징.
    /// 진행도 1 이면 정확히 <paramref name="degrees"/>(360 = 제자리 모습).
    /// </summary>
    public static float CrownSpinAngle(float progress, float degrees, Ease ease)
    {
        if (progress <= 0f)
            return 0f;
        if (progress >= 1f)
            return degrees;
        return degrees * Easing.Evaluate(ease, progress);
    }

    /// <summary>
    /// 반짝임 하나(<paramref name="index"/>, 0..count-1)의 지금 진행도 0~1, 안 보일 때는 -1.
    /// - 착지 순간 한 번 몰아서: 반짝임 i 는 착지 + i × <paramref name="stagger"/> 에 시작해 <paramref name="duration"/> 동안.
    /// - 그 뒤 은은한 반복: 몰아치기가 끝나고 <paramref name="repeatInterval"/> 마다 하나씩(0, 1, 2, 0, …) 번갈아. 간격 0 이하 = 반복 없음.
    /// 간격이 지속보다 짧으면 반복 하나가 다음 차례에 잘린다(다음 반짝임으로 넘어감).
    /// </summary>
    public static float SparkleProgress(float sinceLand, int index, int count, float stagger, float duration, float repeatInterval)
    {
        if (duration <= 0f || count <= 0 || index < 0 || index >= count || sinceLand < 0f)
            return -1f;

        stagger = Mathf.Max(0f, stagger);
        float burstT = (sinceLand - index * stagger) / duration;
        if (burstT >= 0f && burstT < 1f)
            return burstT;

        if (repeatInterval <= 0f)
            return -1f;

        float burstEnd = (count - 1) * stagger + duration;
        float sinceRepeat = sinceLand - burstEnd - repeatInterval; // 몰아치기 뒤 한 간격 쉬고 첫 반복
        if (sinceRepeat < 0f)
            return -1f;

        int turn = Mathf.FloorToInt(sinceRepeat / repeatInterval);
        if (turn % count != index)
            return -1f;
        float t = (sinceRepeat - turn * repeatInterval) / duration;
        return t < 1f ? t : -1f;
    }

    /// <summary>반짝임 크기·투명도 0 → 1 → 0(EuniTween <see cref="Easing.Punch"/>). 진행도 0 미만(안 보임)·1 이상이면 0.</summary>
    public static float SparklePulse(float progress, Ease ease)
    {
        if (progress <= 0f || progress >= 1f)
            return 0f;
        return Easing.Punch(ease, progress);
    }
}
