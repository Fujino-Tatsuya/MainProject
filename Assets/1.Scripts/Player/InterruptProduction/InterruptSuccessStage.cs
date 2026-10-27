using System.Collections.Generic;

/// <summary>
/// 간파 연출 — 성공 단계(할 일 118). [서버] 성공 슬로우(+히트스톱 A1)를 다시 걸고, 시전자의 캐릭터 성공 전송로(relay)를 부른다.
/// 슬로우가 거부돼도(컷신 잠금·씬 전환) relay 는 부른다 — 지금 캐릭터 RPC 전송로와 같다(설계 Q1-a).
/// </summary>
public sealed class InterruptSuccessStage
{
    private readonly IInterruptProductionSettings _settings;
    private readonly ITimeScalePort _timeScale;

    public InterruptSuccessStage(IInterruptProductionSettings settings, ITimeScalePort timeScale)
    {
        _settings = settings;
        _timeScale = timeScale;
    }

    /// <summary>
    /// [서버] 마지막 것이 이기므로 시도 슬로우의 지금 배율에서 이어지고, 예측이 빗나가 시도 슬로우가 없었어도 건다.
    /// 연출 기준 위치 = 카운터 창을 소비한 첫 대상, 없으면 시전자.
    /// </summary>
    public uint Server(in InterruptProductionEvent e, IReadOnlyList<IInterruptSuccessRelay> relays)
    {
        uint triggerId = _timeScale.ServerStart(_settings.InterruptSuccessProfile, e.CasterClientId, SlowMotionPurpose.Success,
            e.EffectPosition, _settings.Get(InterruptProductionCue.HitStop));

        if (relays != null)
        {
            for (int i = 0; i < relays.Count; i++)
                relays[i]?.ServerRelaySuccess(e);
        }

        return triggerId;
    }
}
