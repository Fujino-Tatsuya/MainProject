using UnityEngine;

/// <summary>암순응 원형 중심 기준(할 일 87 C5).</summary>
public enum RevealCenterMode
{
    /// <summary>간파 성공 신호의 대상 위치(고정 월드 좌표).</summary>
    Target = 0,
    /// <summary>발동자 플레이어 오브젝트(매 프레임 추적).</summary>
    Caster = 1,
    /// <summary>대상과 시전자의 중간. 시전자를 못 찾으면 대상.</summary>
    Midpoint = 2,
}

/// <summary>
/// 간파 성공 암순응 곡선 — 순수 계산(MonoBehaviour·Unity 시간 없음). 할 일 87 C3.
///
/// <list type="bullet">
/// <item>곡선: 시작값 → 최대 강도(fadeIn) → 유지(hold) → 0(fadeOut). 시간은 호출자가 실시간으로 넘긴다.</item>
/// <item>진행 중에 새 성공이 오면 <b>지금 값</b>에서 다시 fadeIn 한다 — 화면이 튀지 않는다.</item>
/// <item>NaN·음수 입력은 0(꺼짐)으로 정리한다.</item>
/// </list>
/// </summary>
public sealed class InterruptDarkAdaptationModel
{
    private float _startValue;
    private float _peak;
    private float _elapsed;
    private float _fadeIn;
    private float _hold;
    private float _fadeOut;

    /// <summary>곡선이 진행 중인가(끝나면 false, 값 0).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>이번 프레임 강도 [0, 1].</summary>
    public float Value { get; private set; }

    /// <summary>
    /// 새 성공 — 현재 값에서 <paramref name="peak"/> 까지 다시 올라간다.
    /// 최대 강도가 0 이하이면 곡선을 끝낸다.
    /// </summary>
    public void Play(float peak, float fadeIn, float hold, float fadeOut)
    {
        _peak = SanitizeIntensity(peak);
        _fadeIn = SanitizeDuration(fadeIn);
        _hold = SanitizeDuration(hold);
        _fadeOut = SanitizeDuration(fadeOut);
        _startValue = IsPlaying ? Value : 0f;
        _elapsed = 0f;

        if (_peak <= 0f && _startValue <= 0f)
        {
            Stop();
            return;
        }

        IsPlaying = true;
        Value = Evaluate(0f, _startValue, _peak, _fadeIn, _hold, _fadeOut);
    }

    /// <summary>실시간 <paramref name="unscaledDeltaTime"/> 만큼 진행하고 현재 값을 돌려준다.</summary>
    public float Tick(float unscaledDeltaTime)
    {
        if (!IsPlaying)
            return 0f;

        _elapsed += SanitizeDuration(unscaledDeltaTime);
        Value = Evaluate(_elapsed, _startValue, _peak, _fadeIn, _hold, _fadeOut);

        if (_elapsed >= _fadeIn + _hold + _fadeOut)
            Stop();

        return Value;
    }

    /// <summary>즉시 0(강제 리셋·세션 종료·비활성).</summary>
    public void Stop()
    {
        IsPlaying = false;
        Value = 0f;
        _elapsed = 0f;
        _startValue = 0f;
    }

    /// <summary>
    /// 경과 실시간 → 곡선 값. fadeIn 구간은 <paramref name="start"/>→<paramref name="peak"/>,
    /// 유지 구간은 peak, fadeOut 구간은 peak→0, 끝나면 0.
    /// </summary>
    public static float Evaluate(float elapsed, float start, float peak, float fadeIn, float hold, float fadeOut)
    {
        start = SanitizeIntensity(start);
        peak = SanitizeIntensity(peak);
        fadeIn = SanitizeDuration(fadeIn);
        hold = SanitizeDuration(hold);
        fadeOut = SanitizeDuration(fadeOut);
        elapsed = SanitizeDuration(elapsed);

        if (elapsed < fadeIn)
            return Mathf.Lerp(start, peak, elapsed / fadeIn);

        elapsed -= fadeIn;
        if (elapsed < hold)
            return peak;

        elapsed -= hold;
        if (elapsed < fadeOut)
            return Mathf.Lerp(peak, 0f, elapsed / fadeOut);

        return 0f;
    }

    /// <summary>발동자가 나면 본인 강도, 아니면 타인 강도(정리된 값).</summary>
    public static float SelectIntensity(bool triggeredByMe, float selfIntensity, float othersIntensity) =>
        SanitizeIntensity(triggeredByMe ? selfIntensity : othersIntensity);

    /// <summary>
    /// 원형 중심 월드 위치(할 일 87 C5). Target = 신호의 대상 위치, Caster = 시전자(못 찾으면 원형 끔 → false),
    /// Midpoint = 둘의 중간(시전자 못 찾으면 대상). 알 수 없는 값은 Target 으로 본다.
    /// </summary>
    public static bool TrySelectRevealCenter(RevealCenterMode mode, Vector3 target, bool hasCaster, Vector3 caster,
                                             out Vector3 center)
    {
        switch (mode)
        {
            case RevealCenterMode.Caster:
                center = hasCaster ? caster : default;
                return hasCaster;
            case RevealCenterMode.Midpoint:
                center = hasCaster ? (target + caster) * 0.5f : target;
                return true;
            default:
                center = target;
                return true;
        }
    }

    /// <summary>원형 반경(화면 높이 비율). 0 이하·NaN 이면 0 = 원형 끔.</summary>
    public static float SanitizeRadius(float radius) =>
        float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f ? 0f : radius;

    /// <summary>강도 [0, 1]. NaN·음수 = 0.</summary>
    public static float SanitizeIntensity(float value) =>
        float.IsNaN(value) ? 0f : Mathf.Clamp01(value);

    /// <summary>시간·비율 등 0 이상 값. NaN·음수·무한대 = 0.</summary>
    public static float SanitizeDuration(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
}
