using System.Collections.Generic;

/// <summary>
/// 간파 연출 — 성공 단계(할 일 118).
/// [서버] 성공 슬로우(+히트스톱 A1)를 다시 걸고, 시전자의 캐릭터 성공 전송로(relay)를 부른다.
/// 슬로우가 거부돼도(컷신 잠금·씬 전환) relay 는 부른다 — 지금 캐릭터 RPC 전송로와 같다(설계 Q1-a).
/// [각 피어] 슬로우 Started(Success) 를 받아 개인·공용 채널로 나눠 암순응(A2)·성공 이펙트 칸(A4)을 튼다.
/// </summary>
public sealed class InterruptSuccessStage
{
    private readonly IInterruptProductionSettings _settings;
    private readonly ITimeScalePort _timeScale;
    private readonly InterruptPersonalChannel _personal;
    private readonly InterruptSharedChannel _shared;

    public InterruptSuccessStage(IInterruptProductionSettings settings, ITimeScalePort timeScale,
        InterruptPersonalChannel personal = null, InterruptSharedChannel shared = null)
    {
        _settings = settings;
        _timeScale = timeScale;
        _personal = personal ?? new InterruptPersonalChannel(settings);
        _shared = shared ?? new InterruptSharedChannel(settings);
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

    /// <summary>
    /// [각 피어] 슬로우 시작을 받았다. 간파 성공(<see cref="SlowMotionPurpose.Success"/>)일 때만 튼다 — 시도 슬로우는 무반응.
    /// 암순응: 본인 화면 = 개인 채널(self), 다른 화면 = 공용 채널(others). 성공 이펙트 칸 = 공용(발동자 플레이어의 칸).
    /// </summary>
    public void LocalStarted(SlowMotionPurpose purpose, in InterruptProductionEvent e, bool viewerIsCaster,
        IInterruptDarkAdaptationOutput darkAdaptation, IReadOnlyList<IInterruptCueSink> successFxSinks)
    {
        if (purpose != SlowMotionPurpose.Success)
            return;

        if (viewerIsCaster)
            _personal.PlayDarkAdaptation(e, darkAdaptation);
        else
            _shared.PlayDarkAdaptation(e, darkAdaptation);

        _shared.PlayCue(InterruptProductionCue.SuccessFxSlot, e, successFxSinks);
    }

    /// <summary>[각 피어] 슬로우 강제 리셋(컷신·씬 전환·세션 종료) — 남아 있는 암순응을 즉시 끈다(칸과 무관하게 늘 끈다).</summary>
    public void LocalForcedReset(IInterruptDarkAdaptationOutput darkAdaptation) => darkAdaptation?.Stop();

    /// <summary>A3 — 개인 채널 오너 줌 배율.</summary>
    public float OwnerZoomFactor(float zoomFactor) => _personal.OwnerZoomFactor(zoomFactor);
}
