using UnityEngine;

/// <summary>
/// 간파 연출 — 시도 단계(할 일 118). [서버] 예측 적중이면 시도 슬로우를 건다(전 피어 공용 타임라인).
/// </summary>
public sealed class InterruptAttemptStage
{
    private readonly IInterruptProductionSettings _settings;
    private readonly ITimeScalePort _timeScale;

    public InterruptAttemptStage(IInterruptProductionSettings settings, ITimeScalePort timeScale)
    {
        _settings = settings;
        _timeScale = timeScale;
    }

    /// <summary>[서버] 시도 슬로우 발동 번호. 예측이 빗나가거나 시간 레이어가 거부하면 0.</summary>
    public uint Server(in InterruptProductionEvent e)
    {
        if (!e.PredictsHit)
            return 0;

        // 히트스톱 칸은 시도 슬로우에도 지금처럼 넘긴다(시도 프로필 hitStopDuration 기본 0 — 동작 동일).
        return _timeScale.ServerStart(_settings.InterruptProfile, e.CasterClientId, SlowMotionPurpose.Attempt, default(Vector3),
            _settings.Get(InterruptProductionCue.HitStop));
    }
}
