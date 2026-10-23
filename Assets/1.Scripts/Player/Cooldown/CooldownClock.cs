using System;

/// <summary>
/// 쿨 시계(순수 계산) — 원천 시간이 흐른 만큼 배율을 곱해 쌓는다: <c>Now += Δ원천 × 배율</c>.
/// MonoBehaviour·Unity 시간을 모른다. 원천 시각은 호출자(<see cref="PlayerCooldownClock"/>)가 넣는다.
///
/// - 첫 표본은 기준점만 잡는다(Δ = 0).
/// - 원천이 뒤로 가면(시계 재시작·씬 전환) 그 표본을 새 기준점으로 삼고 쌓지 않는다.
/// - 배율 0·음수·NaN·무한 = 그 구간 멈춤(쌓지 않음). 기준점은 계속 따라가 다음 구간에 몰아 쌓지 않는다.
/// 할 일 91 D8 · PLAN-91 §3.3.
/// </summary>
public sealed class CooldownClock
{
    private double now;
    private double lastSource;
    private bool hasSource;

    /// <summary>쌓인 쿨 시각(초). 원점은 시계를 만든 순간 0.</summary>
    public double Now => now;

    /// <summary>원천 시각 <paramref name="source"/> 까지 흐른 만큼 <paramref name="rate"/> 를 곱해 쌓는다.</summary>
    public void AdvanceTo(double source, float rate)
    {
        if (double.IsNaN(source) || double.IsInfinity(source))
            return;

        if (!hasSource || source < lastSource)
        {
            Rebase(source);
            return;
        }

        Advance(source - lastSource, rate);
        lastSource = source;
    }

    /// <summary>원천이 바뀌었을 때(예: 대시 원천이 NetworkClock ↔ Time 으로 전환) 쌓지 않고 기준점만 옮긴다.</summary>
    public void Rebase(double source)
    {
        lastSource = source;
        hasSource = true;
    }

    /// <summary>Δt 를 직접 쌓는다. 음수·NaN Δt 와 0 이하·비정상 배율은 무시한다.</summary>
    public void Advance(double dt, float rate)
    {
        if (!(dt > 0.0) || double.IsInfinity(dt))
            return;
        if (!(rate > 0f) || float.IsInfinity(rate))
            return;

        now += dt * rate;
    }
}
