using UnityEngine;

/// <summary>
/// 최후의 심판(R) 마무리 낙뢰(칼이 떨어지는 FX_Lightening_Strike)를 <b>언제 쏘고 언제 걷는지</b>의 순수 규칙.
///
/// 칼은 발사 뒤 프리팹 delay + 낙하 시간이 지나야 바닥에 닿는다. 그래서 피해 시각(채널 완주)보다
/// <c>lead</c> 초 <b>앞서</b> 쏜다 — 피해 시각·채널 길이는 그대로다(할 일 52.1 후속).
/// 앞당긴 만큼 "칼은 떨어지기 시작했는데 채널이 끊기는" 구간이 생긴다 → 완주(Completed)가 아니면 걷는다.
///
/// 서버가 시전마다 <see cref="Reset"/> 하고 매 틱 <see cref="TryFire"/> 를 묻는다. 한 시전에 한 번만 true 다.
/// </summary>
public sealed class FinalStrikeVfxSchedule
{
    private bool fired;

    public bool Fired => fired;

    /// <summary>앞당김을 0 ~ 채널 길이로 자른다. 채널보다 길면 채널 시작 순간에 쏜다.</summary>
    public static float ClampLead(float lead, float channelDuration)
        => Mathf.Clamp(lead, 0f, Mathf.Max(0f, channelDuration));

    /// <summary>채널 시작 기준 발사 시각. lead 0 이면 완주 시각과 같다.</summary>
    public static float FireTime(float lead, float channelDuration)
        => Mathf.Max(0f, channelDuration) - ClampLead(lead, channelDuration);

    /// <summary>
    /// 칼이 이미 떨어지는 중에 채널이 끝났다 — 걷어야 하나. 완주만 끝까지 재생하고,
    /// 대상 소실·사망·취소·안전망 만료 등 나머지는 피해가 없으니 전부 걷는다.
    /// </summary>
    public static bool ShouldRecall(SkillEndReason reason) => reason != SkillEndReason.Completed;

    /// <summary>시전 시작. 지난 시전의 발사 기록을 지운다.</summary>
    public void Reset() => fired = false;

    /// <summary>발사 시각에 닿은 첫 호출만 true. 이후로는 같은 시전 동안 계속 false 다.</summary>
    public bool TryFire(float channelElapsed, float channelDuration, float lead)
    {
        if (fired || channelElapsed < FireTime(lead, channelDuration)) return false;

        fired = true;
        return true;
    }
}
