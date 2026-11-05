using EuniTween;
using UnityEngine;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215·220) 계산 — MonoBehaviour 밖 순수 함수(EditMode 테스트 대상).
/// 시간은 호출 측이 넘긴다(<see cref="LobbyResultPanelAnimator"/> = <c>Time.unscaledDeltaTime</c> 누적).
/// 곡선 평가는 호출 측이 한다 — 여기는 진행도·보간·숫자만.
/// </summary>
public static class LobbyResultPanelAnimMath
{
    /// <summary>
    /// 감속 곡선 가중치(할 일 220 후속) — 최종값의 정수 단계 수(<paramref name="steps"/>)가
    /// <paramref name="linearMaxSteps"/> 이하면 0(선형), <paramref name="curveFullSteps"/> 이상이면 1(곡선 그대로), 사이는 log10 으로 보간.
    /// 작은 값은 감속 꼬리 동안 정수가 안 바뀌어 멈춰 보이므로 선형 쪽으로, 큰 값은 꼬리에도 자리가 넉넉해 곡선 그대로.
    /// 기준이 뒤집히거나 같으면(곡선 ≤ 선형) 기준 하나로 자른다(그 이하 선형, 넘으면 곡선).
    /// </summary>
    public static float CountUpCurveWeight(double steps, float linearMaxSteps, float curveFullSteps)
    {
        double lo = Mathf.Max(1f, linearMaxSteps);
        double hi = Mathf.Max(1f, curveFullSteps);
        if (steps <= lo)
            return 0f;
        if (steps >= hi || hi <= lo)
            return 1f;
        return Mathf.Clamp01((float)((System.Math.Log10(steps) - System.Math.Log10(lo)) / (System.Math.Log10(hi) - System.Math.Log10(lo))));
    }

    /// <summary>
    /// 값 하나의 진행도 = 선형과 곡선 진행도를 <paramref name="curveWeight"/> 로 섞은 것. 선형 진행도 1 이면 정확히 1
    /// — 모든 값이 같은 순간(카운트업 끝)에 최종값에 닿는다. 둘 다 0~1 로 잘라 섞어 넘침 없음, 가중치가 고정이라 되돌아가지 않음.
    /// </summary>
    public static float BlendedCountUpProgress(float linear, float curved, float curveWeight)
    {
        if (linear >= 1f)
            return 1f;
        if (linear <= 0f)
            return 0f;
        return Mathf.Lerp(Mathf.Clamp01(linear), Mathf.Clamp01(curved), Mathf.Clamp01(curveWeight));
    }

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
    /// 소수는 버린다(0 쪽으로, 할 일 220) — 최종값은 진행도 1(= 카운트업 끝, 패널 펀치 순간)에만 나온다.
    /// 반올림이면 작은 값(처치 12 등)이 끝나기 한참 전에 최종값에 닿아 감속 꼬리 동안 멈춰 보인다.
    /// 큰 값의 float 오차를 피하려고 double 로 곱한다.
    /// </summary>
    public static int CountUpValue(int finalValue, float progress)
    {
        if (progress >= 1f)
            return finalValue;
        if (progress <= 0f)
            return 0;
        return (int)(finalValue * (double)progress);
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

    /// <summary>카운트업이 끝나는 시각(열기부터 초) = 숫자 지연 + 숫자 시간. 패널 펀치가 이 순간 시작한다(할 일 220). 음수 칸은 0 취급.</summary>
    public static float CountUpEnd(float countUpDelay, float countUpDuration) =>
        Mathf.Max(0f, countUpDelay) + Mathf.Max(0f, countUpDuration);

    /// <summary>
    /// 카운트업 끝 패널 펀치 크기 배율(할 일 220) — 1 → <paramref name="peakScale"/> → 1. EuniTween <see cref="Easing.Punch"/>:
    /// 커지는 절반에 <paramref name="ease"/>, 줄어드는 절반은 그 거울. 펀치 전·후(경과 0 이하·지속 이상)와 지속 0 이하는 정확히 1.
    /// </summary>
    public static float PanelPunchScale(float sinceCountUpEnd, float duration, float peakScale, Ease ease)
    {
        if (duration <= 0f || sinceCountUpEnd <= 0f || sinceCountUpEnd >= duration)
            return 1f;
        return 1f + (peakScale - 1f) * Easing.Punch(ease, sinceCountUpEnd / duration);
    }

    /// <summary>
    /// 펀치 번쩍임 세기 0~1(할 일 220) — 지속의 <paramref name="riseFraction"/> 동안 0→1 로 빠르게 켜지고,
    /// 남은 시간 동안 1→0 으로 사라진다(<paramref name="ease"/> = 사라지는 쪽 이징, OutCubic 이면 금방 빠지고 꼬리가 길다).
    /// 시작 전·끝난 뒤·지속 0 이하 = 0. 켜지는 비율 0 이하 = 첫 순간부터 최대.
    /// </summary>
    public static float FlashAlpha(float sinceStart, float duration, float riseFraction, Ease ease)
    {
        if (duration <= 0f || sinceStart < 0f || sinceStart >= duration)
            return 0f;
        float t = sinceStart / duration;
        float rise = Mathf.Clamp(riseFraction, 0f, 0.99f);
        if (t < rise)
            return t / rise;
        return 1f - Easing.Evaluate(ease, (t - rise) / (1f - rise));
    }

    /// <summary>번쩍임 퍼짐 0~1(할 일 220) — EuniTween <see cref="Easing.Evaluate"/>. 시작 전 0, 끝·지속 0 이하 = 1.</summary>
    public static float FlashSpread(float sinceStart, float duration, Ease ease)
    {
        if (sinceStart <= 0f)
            return 0f;
        if (duration <= 0f || sinceStart >= duration)
            return 1f;
        return Easing.Evaluate(ease, sinceStart / duration);
    }

    /// <summary>
    /// 왕관 첫 점프 시작 시각(열기부터 초) = 카운트업 끝 + 패널 펀치 시간 + 왕관 지연 — 펀치가 끝난 직후(할 일 220).
    /// 펀치가 꺼져 있으면 <paramref name="punchDuration"/> = 0 을 넘긴다. 음수 칸은 0 취급.
    /// </summary>
    public static float CrownFirstStart(float countUpDelay, float countUpDuration, float punchDuration, float crownDelay) =>
        CountUpEnd(countUpDelay, countUpDuration) + Mathf.Max(0f, punchDuration) + Mathf.Max(0f, crownDelay);

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

/// <summary>
/// 카운트업 한 프레임(할 일 220 후속) — 선형 진행도·곡선 진행도·값 크기 기준을 함께 넘겨, 받는 쪽(행 숫자·소요 시간)이
/// 값마다 감속 세기를 고른다(<see cref="LobbyResultPanelAnimMath.CountUpCurveWeight"/>). 모두 선형 진행도 1 에서 동시에 최종값.
/// </summary>
public readonly struct LobbyResultCountUpFrame
{
    public readonly float Linear;
    public readonly float Curved;
    public readonly float LinearMaxSteps;
    public readonly float CurveFullSteps;

    public LobbyResultCountUpFrame(float linear, float curved, float linearMaxSteps, float curveFullSteps)
    {
        Linear = linear;
        Curved = curved;
        LinearMaxSteps = linearMaxSteps;
        CurveFullSteps = curveFullSteps;
    }

    /// <summary>최종값 그대로(5.ResultScene·카운트업 없음).</summary>
    public static LobbyResultCountUpFrame Final => new LobbyResultCountUpFrame(1f, 1f, 0f, 0f);

    /// <summary>정수 단계 수가 <paramref name="steps"/> 인 값의 진행도.</summary>
    public float ProgressFor(double steps) =>
        LobbyResultPanelAnimMath.BlendedCountUpProgress(Linear, Curved,
            LobbyResultPanelAnimMath.CountUpCurveWeight(steps, LinearMaxSteps, CurveFullSteps));

    /// <summary>정수 칸 — 단계 수 = 최종값.</summary>
    public int Value(int finalValue) =>
        LobbyResultPanelAnimMath.CountUpValue(finalValue, ProgressFor(System.Math.Abs((double)finalValue)));

    /// <summary>소요 시간(초) — 단계 수 = 표시되는 초 수(mm:ss 는 초 아래 버림).</summary>
    public float Seconds(float finalSeconds) =>
        LobbyResultPanelAnimMath.CountUpSeconds(finalSeconds, ProgressFor(System.Math.Floor(Mathf.Max(0f, finalSeconds))));
}
