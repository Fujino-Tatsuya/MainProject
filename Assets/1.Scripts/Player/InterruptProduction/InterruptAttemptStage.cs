using UnityEngine;

/// <summary>
/// 간파 연출 — 시도 단계(할 일 118). [서버] 간파 대상이 있으면 시도 슬로우를 건다(전 피어 공용 타임라인) —
/// 성공 예측 = 강(<see cref="IInterruptProductionSettings.InterruptProfile"/>), 실패 예측 = 약(<see cref="IInterruptProductionSettings.InterruptWeakProfile"/>, 할 일 132).
/// 대상 없음 예측이면 슬로우 없음(할 일 132 정정1).
/// [각 피어] 시전 시작(OnClientPlay)에 캐릭터 시도 cue(팔라딘 방패 발광 B1)를 공용 채널로 튼다.
/// 클립 이벤트 시도 연출(B2·C1)은 <see cref="InterruptProductionDirector.AllowsAnimEffect"/> 가 따로 묻는다.
/// </summary>
public sealed class InterruptAttemptStage
{
    private readonly IInterruptProductionSettings _settings;
    private readonly ITimeScalePort _timeScale;
    private readonly InterruptSharedChannel _shared;

    public InterruptAttemptStage(IInterruptProductionSettings settings, ITimeScalePort timeScale, InterruptSharedChannel shared = null)
    {
        _settings = settings;
        _timeScale = timeScale;
        _shared = shared ?? new InterruptSharedChannel(settings);
    }

    /// <summary>[서버] 시도 슬로우 발동 번호. 대상 없음 예측이거나 시간 레이어가 거부하면 0.</summary>
    public uint Server(in InterruptProductionEvent e)
    {
        if (!InterruptProductionRules.TrySelectAttemptPurpose(e.Forecast, out SlowMotionPurpose purpose))
            return 0;

        SlowMotionProfile profile = InterruptProductionRules.SelectAttemptProfile(purpose, _settings);

        // 히트스톱 칸은 시도 슬로우에도 지금처럼 넘긴다(시도 프로필 hitStopDuration 기본 0 — 동작 동일).
        // 대상 id = 예측이 고른 대상 — 각 피어 로컬 선착순 장부가 강 시도로 그 창을 닫는다(할 일 143-2).
        return _timeScale.ServerStart(profile, e.CasterClientId, purpose, default(Vector3),
            _settings.Get(InterruptProductionCue.HitStop), e.TargetObjectId);
    }

    /// <summary>
    /// [각 피어] 시도 — 그 스킬의 시도 cue 가 있고 칸이 켜져 있으면 <paramref name="sink"/>(보통 스킬 자신)에 튼다.
    /// 끄는 쪽(스킬 OnEnd)은 디렉터를 거치지 않고 늘 돈다.
    /// </summary>
    public void Local(in InterruptProductionEvent e, IInterruptCueSink sink)
    {
        if (!InterruptProductionRules.TryGetAttemptCue(e.Skill, out InterruptProductionCue cue))
            return;

        _shared.PlayCue(cue, e, sink);
    }
}
