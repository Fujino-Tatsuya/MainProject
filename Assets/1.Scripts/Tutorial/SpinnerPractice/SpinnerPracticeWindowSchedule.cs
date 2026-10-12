/// <summary>
/// 연습 모드의 간파 창 간격(순수 C#). 원래 스피너봇의 확률 40%·쿨 6초 대신 쓴다.
///
/// 🔴 간격은 <b>창이 열린 시각 → 다음 창이 열릴 수 있는 시각</b>이다(열림 기준). 첫 창은 바로 열 수 있다.
///    스핀 시작 = 창 열림이라 스피너봇이 스핀을 고르는 순간 <see cref="TryBegin"/> 이 시각을 찍는다.
/// </summary>
public sealed class SpinnerPracticeWindowSchedule
{
    float _nextAllowedAt = float.NegativeInfinity;

    /// <param name="interval">창 사이 간격(초). 음수는 0 으로 본다.</param>
    public SpinnerPracticeWindowSchedule(float interval)
    {
        Interval = interval > 0f ? interval : 0f;
    }

    public float Interval { get; }

    /// <summary>다음 창을 열 수 있는 시각. 아직 한 번도 안 열었으면 음의 무한대.</summary>
    public float NextAllowedAt => _nextAllowedAt;

    public bool IsReady(float now) => now >= _nextAllowedAt;

    /// <summary>지금 창을 열어도 되면 true 를 돌려주고 다음 허용 시각을 <c>now + Interval</c> 로 민다.</summary>
    public bool TryBegin(float now)
    {
        if (!IsReady(now)) return false;

        _nextAllowedAt = now + Interval;
        return true;
    }

    /// <summary>다음 창을 적어도 <paramref name="time"/> 까지 미룬다(이미 더 늦으면 그대로) — 연계 복귀 직후 우클릭이 준비될 틈(v8 §3.5).</summary>
    public void DelayUntil(float time)
    {
        if (time > _nextAllowedAt) _nextAllowedAt = time;
    }

    /// <summary>처음 상태로 — 다음 창을 바로 열 수 있다(연습 모드 재진입).</summary>
    public void Reset() => _nextAllowedAt = float.NegativeInfinity;
}
