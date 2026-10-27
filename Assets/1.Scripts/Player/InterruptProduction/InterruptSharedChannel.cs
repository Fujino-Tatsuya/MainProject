using System.Collections.Generic;

/// <summary>
/// 간파 연출 공용 채널(할 일 118) — "모든 화면" 연출: 약한 암순응(A2 others, 본인 아닌 화면), 성공 이펙트 칸(A4) 등 cue.
/// 슬로우·히트스톱은 서버 타임라인이 이미 전 피어 공용이라 이 채널을 거치지 않는다. 값을 들지 않고 부를 때마다 설정을 읽는다.
/// </summary>
public sealed class InterruptSharedChannel
{
    private readonly IInterruptProductionSettings _settings;

    public InterruptSharedChannel(IInterruptProductionSettings settings)
    {
        _settings = settings;
    }

    /// <summary>A2 — 다른 사람 화면 암순응(others 강도). 칸이 꺼져 있으면 안 튼다.</summary>
    public void PlayDarkAdaptation(in InterruptProductionEvent e, IInterruptDarkAdaptationOutput darkAdaptation)
    {
        if (darkAdaptation == null || !_settings.Get(InterruptProductionCue.DarkAdaptation))
            return;

        darkAdaptation.Play(false, e.CasterClientId, e.TargetPosition);
    }

    /// <summary>칸이 켜져 있으면 sink 마다 cue 를 튼다. 꺼져 있으면 아무도 부르지 않는다.</summary>
    public void PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e, IReadOnlyList<IInterruptCueSink> sinks)
    {
        if (sinks == null || sinks.Count == 0 || !_settings.Get(cue))
            return;

        for (int i = 0; i < sinks.Count; i++)
            sinks[i]?.PlayCue(cue, e);
    }
}
