using UnityEngine;

/// <summary>
/// 간파 제작용 디버그 배속(DebugInterruptLab 씬에만 둔다, 할 일 115 C5).
///
/// <see cref="Time.timeScale"/> 을 직접 쓰지 않는다 — <see cref="GlobalTimeScale.BaseScale"/>(기본 배속)만 정해
/// 최종 배율 = 디렉터 슬로우 × 디버그 배속이 되게 한다. 그래서 간파 시도·성공 슬로우·히트스톱이 이 배속 위에서 그대로 돈다.
/// 이 피어에만 적용된다(복제 안 함) — 솔로 호스트 디버그용.
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

    GlobalTimeScale _applied;

    public float Scale
    {
        get => scale;
        set => scale = ClampScale(value);
    }

    public static float ClampScale(float value) => Mathf.Clamp(value, MinScale, MaxScale);

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

        target.BaseScale = ClampScale(scale);
    }

    void OnDisable() => Release();

    void Release()
    {
        if (_applied != null)
            _applied.BaseScale = 1f;
        _applied = null;
    }
}
