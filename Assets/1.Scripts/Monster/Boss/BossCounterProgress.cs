using UnityEngine;

/// <summary>
/// 간파(카운터) 성공 한 번의 결과 — 다음 게이지, 제압 진입 여부, <b>전체 행동 불능 시간</b>.
///
/// 🔴 <see cref="Duration"/> 은 총 시간이다. 앞에 Hit 리액션 시간을 따로 더하지 않는다
///    (설계 §3.3 — SO 에 1.5 를 넣었으면 실제로도 1.5 여야 한다).
/// </summary>
public readonly struct BossCounterOutcome
{
    /// <summary>감소 뒤 간파 게이지(0~<see cref="BossCounterProgress.GaugeMax"/>). 제압이면 0.</summary>
    public float NextGauge { get; }
    /// <summary>게이지가 0 에 닿았다 → 제압(취약을 거치지 않는다 — 간파 문서 §7.2).</summary>
    public bool IsSuppress { get; }
    public float Duration { get; }

    public BossCounterOutcome(float nextGauge, bool isSuppress, float duration) =>
        (NextGauge, IsSuppress, Duration) = (nextGauge, isSuppress, duration);
}

/// <summary>
/// 간파 게이지 판정(팀 기획 `Re_C_간파_시스템.md` §10). 상태를 갖지 않는 순수 함수라 EditMode 로 전 경계를 고정한다.
/// 게이지 보관·상태 전이·제압 종료 시 100 복원은 호출측(<c>TwentyThreeBoss</c>)의 몫이다.
/// </summary>
/// <remarks>
/// ⚠️ 2026-09-28 교체: 예전엔 성공 **횟수**(0→maxGroggyCount)를 올려 임계에서 Break 로 갔다.
/// 이제 게이지 100 에서 성공마다 <c>step</c>(20)씩 깎고 0 이면 제압이다 — 기본값이면 횟수(5)가 같다.
/// 송전기 전멸 그로기는 **게이지와 무관**하다(팀장 09-28) — 이 함수를 거치지 않는다.
/// </remarks>
public static class BossCounterProgress
{
    public const float GaugeMax = 100f;

    /// <param name="currentGauge">지금 간파 게이지(0~100).</param>
    /// <param name="step">이번 감소량(간파 성공 20 · 벽 20 · 벤트 20 — SO).</param>
    /// <param name="groggyDuration">게이지가 남을 때의 그로기 시간(간파 성공 1.5초).</param>
    /// <param name="suppressDuration">게이지가 0 이 됐을 때의 제압 시간(5초).</param>
    public static BossCounterOutcome Resolve(float currentGauge, float step,
        float groggyDuration, float suppressDuration)
    {
        // 음수·초과 방어 — 게이지는 외부(네트워크 변수·저작값)에서 온다.
        float current = Mathf.Clamp(currentGauge, 0f, GaugeMax);
        float next = Mathf.Max(0f, current - Mathf.Max(0f, step));

        // 부동소수 오차로 0.0001 이 남아 제압이 한 번 밀리는 일을 막는다.
        bool isSuppress = next <= 0.001f;

        return new BossCounterOutcome(
            isSuppress ? 0f : next,
            isSuppress,
            // 0 이면 그 상태에서 못 빠져나오므로 최소값을 둔다. 저작 실수의 안전망이다.
            Mathf.Max(0.05f, isSuppress ? suppressDuration : groggyDuration));
    }
}
