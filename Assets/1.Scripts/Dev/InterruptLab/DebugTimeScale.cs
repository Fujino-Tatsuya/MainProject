using UnityEngine;

/// <summary>
/// 간파 제작용 디버그 배속(DebugInterruptLab 씬에만 둔다, 할 일 115 C5).
///
/// <see cref="Time.timeScale"/> 을 직접 쓰지 않는다 — <see cref="GlobalTimeScale.BaseScale"/>(기본 배속)만 정해
/// 최종 배율 = 디렉터 슬로우 × 디버그 배속이 되게 한다. 그래서 간파 시도·성공 슬로우·히트스톱이 이 배속 위에서 그대로 돈다.
/// 이 피어에만 적용된다(복제 안 함) — 솔로 호스트 디버그용.
///
/// 간파 성공(<see cref="InterruptProductionDirector.LocalSucceeded"/>) 뒤 <see cref="normalSpeedAfterSuccess"/>초(실제 시간) 동안은
/// 기본 배속을 1 로 둔다(C6) — 성공 연출을 평소 속도로 본다. 디렉터 슬로우·히트스톱은 그대로 곱해진다.
/// 슬라이더 값은 건드리지 않으므로 그 사이 바꾼 값이 끝날 때 쓰인다.
///
/// 비활성·파괴(씬 언로드 포함) 시 기본 배속을 1 로 되돌린다. 프리셋 버튼은 인스펙터(<c>DebugTimeScaleEditor</c>)에 있다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-101)] // GlobalTimeScale(-100) 이 이번 프레임 배율을 정하기 전에 값을 넣는다
public sealed class DebugTimeScale : MonoBehaviour
{
    public const float MinScale = 0.05f;
    public const float MaxScale = 2f;

    /// <summary>인스펙터 프리셋 버튼 값.</summary>
    public static readonly float[] Presets = { 0.1f, 0.25f, 0.5f, 1f };

    [Tooltip("디버그 배속. 디렉터 슬로우 배율에 곱해진다(1 = 평소와 같음). Play 중 바로 반영된다.")]
    [SerializeField, Range(MinScale, MaxScale)] float scale = 1f;

    [Tooltip("간파 성공 뒤 이 시간(초, 실제 시간) 동안 디버그 배속을 1 로 둔다. 0 = 끔. 그 사이 또 성공하면 그때부터 다시 잰다.")]
    [SerializeField, Min(0f)] float normalSpeedAfterSuccess = 3f;

    GlobalTimeScale _applied;
    double _normalSpeedUntil = double.NegativeInfinity;

    public float Scale
    {
        get => scale;
        set => scale = ClampScale(value);
    }

    /// <summary>성공 후 1배속이 지금 걸려 있는가(실제 시간 기준).</summary>
    public bool IsHoldingNormalSpeed => IsHolding(Time.unscaledTimeAsDouble, _normalSpeedUntil);

    /// <summary>성공 후 1배속 남은 실제 시간(초). 없으면 0.</summary>
    public float NormalSpeedRemaining => (float)System.Math.Max(0.0, _normalSpeedUntil - Time.unscaledTimeAsDouble);

    public static float ClampScale(float value) => Mathf.Clamp(value, MinScale, MaxScale);

    /// <summary>이번 프레임 기본 배속 — 성공 후 1배속 중이면 1, 아니면 슬라이더 값.</summary>
    public static float ResolveBaseScale(float sliderScale, bool holdingNormalSpeed) =>
        holdingNormalSpeed ? 1f : ClampScale(sliderScale);

    /// <summary>성공 시각 <paramref name="now"/> 에서 1배속 끝 시각. 0 이하면 걸지 않는다(지금 끝 시각 유지).</summary>
    public static double HoldUntil(double now, double currentUntil, float seconds) =>
        seconds > 0f ? now + seconds : currentUntil;

    public static bool IsHolding(double now, double until) => now < until;

    void OnEnable() => InterruptProductionDirector.LocalSucceeded += HandleInterruptSucceeded;

    void Update()
    {
        GlobalTimeScale target = GlobalTimeScale.Instance;
        if (target == null)
            return;

        if (_applied != target)
        {
            Release();
            _applied = target;
        }

        target.BaseScale = ResolveBaseScale(scale, IsHoldingNormalSpeed);
    }

    void OnDisable()
    {
        InterruptProductionDirector.LocalSucceeded -= HandleInterruptSucceeded;
        _normalSpeedUntil = double.NegativeInfinity;
        Release();
    }

    void HandleInterruptSucceeded(InterruptProductionEvent _)
    {
        _normalSpeedUntil = HoldUntil(Time.unscaledTimeAsDouble, _normalSpeedUntil, normalSpeedAfterSuccess);

        // 성공 슬로우는 이번 프레임부터 돈다 — 다음 Update 를 기다리지 않고 바로 1 로 둔다.
        if (_applied != null && IsHoldingNormalSpeed)
            _applied.BaseScale = 1f;
    }

    void Release()
    {
        if (_applied != null)
            _applied.BaseScale = 1f;
        _applied = null;
    }
}
