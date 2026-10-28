using System;
using UnityEngine;

/// <summary>오너 간파 연출 상태(할 일 132).</summary>
public enum InterruptOwnerPresentationState
{
    /// <summary>진행 중인 시도 없음.</summary>
    None,
    /// <summary>시도 — 성공 예측(강). 서버 슬로우 목적이 오면 서버 쪽을 따른다.</summary>
    AttemptStrong,
    /// <summary>시도 — 실패 예측(약).</summary>
    AttemptWeak,
    /// <summary>서버 확정 성공 — 암전 유지, 섬광·걷힘은 단계 5(지금은 일정 시간 뒤 걷는 임시 동작).</summary>
    Success,
    /// <summary>서버 확정 실패(또는 판정 전 종료·거부) — 암전·빛 모임을 걷는 중.</summary>
    Failed,
}

/// <summary>오너 연출 시간 칸 — 디렉터가 구현하고, 쓸 때마다 읽는다(캐싱 없음).</summary>
public interface IInterruptOwnerPresentationSettings
{
    /// <summary>암전 들어옴(초, 실시간).</summary>
    float OwnerDimInDuration { get; }
    /// <summary>암전·빛 모임 걷기(예측 빗나감·실패, 초, 실시간).</summary>
    float OwnerDimOutDuration { get; }
    /// <summary>성공 확정 뒤 암전을 걷기 시작할 때까지(초, 실시간) — 단계 5 섬광 전 임시.</summary>
    float OwnerSuccessHoldDuration { get; }
}

/// <summary>
/// 오너 간파 연출 상태 기계(할 일 132) — 순수 로직. 디렉터(<see cref="InterruptProductionDirector"/>)가 들고 사건을 넘긴다.
/// <list type="bullet">
/// <item>입력 순간 오너 예측(<see cref="Predict"/>): 강 = 오너 암전 + 빛 모임, 약 = 아무것도 안 켠다(줌·슬로우는 서버 슬로우가 처리), 대상 없음 = 시도로 치지 않는다.</item>
/// <item>내 시도 슬로우가 시작되면(<see cref="ServerStarted"/>) 목적(Attempt = 강 / AttemptWeak = 약)이 예측과 다르면 걷는다.</item>
/// <item>내 시도 슬로우 실패 복귀(<see cref="ServerFailed"/>)·판정 전 종료·거부(<see cref="LocalEnded"/>)면 걷는다.</item>
/// <item>내 성공 슬로우(<see cref="ServerStarted"/> Success)면 암전 유지로 성공 단계 — <see cref="SuccessEntered"/> 훅(단계 5).</item>
/// <item>빛 모임 끝 = 오너 애니 Hit 이벤트(<see cref="LocalHit"/>). 강제 리셋(<see cref="ForcedReset"/>)은 즉시 0.</item>
/// </list>
/// 시각(now)은 실시간(Time.unscaledTime) — 슬로우 중에도 걷기 0.2초가 그대로다. 팀원 화면은 이 기계를 타지 않는다.
/// </summary>
public sealed class InterruptOwnerPresentation
{
    private readonly IInterruptOwnerPresentationSettings _settings;
    private IInterruptOwnerDimOutput _dimOutput;
    private IInterruptLightGatherOutput _gatherOutput;

    // 암전 램프 — 시작값에서 목표값까지 길이(초) 동안 선형.
    private float _dimFrom;
    private float _dimTo;
    private float _rampStart;
    private float _rampDuration;
    private float _pushedDim;

    private bool _strongStanding;   // 이 시도가 강으로 남아 있다(예측 강 ∧ 서버 목적이 어긋나지 않음) — 실패 직후 같은 판정 성공이면 암전을 되살린다
    private float _successFadeAt = -1f;
    private PlayerInterruptSkillBase _skill;

    public InterruptOwnerPresentation(IInterruptOwnerPresentationSettings settings,
        IInterruptOwnerDimOutput dimOutput = null, IInterruptLightGatherOutput gatherOutput = null)
    {
        _settings = settings;
        _dimOutput = dimOutput;
        _gatherOutput = gatherOutput;
    }

    public InterruptOwnerPresentationState State { get; private set; }
    /// <summary>이번 시도의 오너 예측 — true = 강.</summary>
    public bool PredictedStrong { get; private set; }
    /// <summary>서버 슬로우 목적이 오너 예측과 달랐다.</summary>
    public bool Mispredicted { get; private set; }
    /// <summary>이번 시도의 서버 시도 슬로우 발동 번호. 0 = 아직 못 받음.</summary>
    public uint AttemptTriggerId { get; private set; }
    /// <summary>마지막으로 출력에 넘긴 암전 강도.</summary>
    public float DimIntensity => _pushedDim;
    public bool LightGatherActive { get; private set; }

    /// <summary>서버 확정 성공에 들어섰다(단계 5 섬광 훅). 인자 = 그 순간 암전이 켜져 있는가(강 시도).</summary>
    public event Action<bool> SuccessEntered;

    /// <summary>출력 교체(단계 3·4 가 실제 렌더·VFX 를 붙일 때). 지금 값을 새 출력에 한 번 넘긴다.</summary>
    public void SetOutputs(IInterruptOwnerDimOutput dimOutput, IInterruptLightGatherOutput gatherOutput)
    {
        if (LightGatherActive)
            _gatherOutput?.StopLightGather();
        _dimOutput?.SetOwnerDim(0f);

        _dimOutput = dimOutput;
        _gatherOutput = gatherOutput;
        _dimOutput?.SetOwnerDim(_pushedDim);
        if (LightGatherActive)
            _gatherOutput?.StartLightGather(_skill);
    }

    private static bool IsAttempt(InterruptOwnerPresentationState state) =>
        state == InterruptOwnerPresentationState.AttemptStrong || state == InterruptOwnerPresentationState.AttemptWeak;

    /// <summary>이 예측을 새 시도로 받아들이는가 — 대상 없음·결론 안 난 시도 중이면 false(<see cref="Predict"/> 와 같은 조건).</summary>
    public bool Accepts(BeaverLobby.Player.Dash.InterruptAttemptForecast forecast) =>
        forecast != BeaverLobby.Player.Dash.InterruptAttemptForecast.NoTarget && !IsAttempt(State);

    /// <summary>
    /// [오너] 간파 입력 순간 예측. 대상 없음이면 아무것도 켜지 않고 상태도 그대로(false, 할 일 132 정정1).
    /// 시도가 아직 결론나지 않았으면 중복 입력으로 보고 무시한다(false).
    /// </summary>
    public bool Predict(BeaverLobby.Player.Dash.InterruptAttemptForecast forecast, PlayerInterruptSkillBase skill, float now)
    {
        if (!Accepts(forecast))
            return false;

        bool predictsSuccess = forecast == BeaverLobby.Player.Dash.InterruptAttemptForecast.Strong;

        _skill = skill;
        PredictedStrong = predictsSuccess;
        Mispredicted = false;
        AttemptTriggerId = 0;
        _strongStanding = predictsSuccess;
        _successFadeAt = -1f;
        State = predictsSuccess ? InterruptOwnerPresentationState.AttemptStrong : InterruptOwnerPresentationState.AttemptWeak;

        if (predictsSuccess)
        {
            RampTo(1f, _settings.OwnerDimInDuration, now);
            SetGather(true);
        }
        else
        {
            RampTo(0f, _settings.OwnerDimOutDuration, now); // 앞 시도의 남은 암전이 있으면 걷는다
            SetGather(false);
        }

        Push(now);
        return true;
    }

    /// <summary>
    /// [각 피어 → 오너만 의미] 슬로우 시작. <paramref name="mine"/> = 발동자가 이 화면의 본인. 남의 슬로우는 무시한다(팀원 화면 무변화).
    /// </summary>
    public void ServerStarted(uint triggerId, SlowMotionPurpose purpose, bool mine, float now)
    {
        if (!mine)
            return;

        if (purpose == SlowMotionPurpose.Success)
        {
            EnterSuccess(now);
            return;
        }

        // 시도 슬로우 — 오너 예측이 없던(결론난) 시도는 고칠 것이 없다.
        if (!IsAttempt(State))
            return;

        AttemptTriggerId = triggerId;
        bool serverStrong = purpose == SlowMotionPurpose.Attempt;
        State = serverStrong ? InterruptOwnerPresentationState.AttemptStrong : InterruptOwnerPresentationState.AttemptWeak;
        if (serverStrong == PredictedStrong)
            return;

        // 예측 빗나감 — 강을 예측했는데 서버가 약이면 걷는다. 약 예측·서버 강은 켜지 않는다(늦게 켜지면 더 어색하다).
        Mispredicted = true;
        _strongStanding = false;
        FadeOut(now);
    }

    /// <summary>[각 피어] 슬로우 실패 복귀. 이번 시도의 서버 시도 슬로우일 때만 걷는다.</summary>
    public void ServerFailed(uint triggerId, float now)
    {
        if (triggerId == 0 || triggerId != AttemptTriggerId || !IsAttempt(State))
            return;

        State = InterruptOwnerPresentationState.Failed;
        FadeOut(now);
    }

    /// <summary>
    /// [오너] 판정 전에 스킬이 끝났다·서버가 요청을 거부했다 — 아직 결론이 없으면 실패처럼 걷는다.
    /// (남의 슬로우가 내 시도 슬로우를 덮으면 내 실패 복귀가 오지 않는다 — 이 경로가 정리한다.)
    /// </summary>
    public void LocalEnded(float now)
    {
        if (!IsAttempt(State))
            return;

        State = InterruptOwnerPresentationState.Failed;
        FadeOut(now);
    }

    /// <summary>[오너] 애니 Hit 이벤트 — 빛 모임 끝.</summary>
    public void LocalHit()
    {
        SetGather(false);
    }

    /// <summary>[각 피어] 슬로우 강제 리셋(컷신·씬 전환·세션 종료) — 즉시 0, 상태 없음.</summary>
    public void ForcedReset()
    {
        SetGather(false);
        _dimFrom = _dimTo = 0f;
        _rampDuration = 0f;
        _successFadeAt = -1f;
        _strongStanding = false;
        AttemptTriggerId = 0;
        State = InterruptOwnerPresentationState.None;
        PushValue(0f);
    }

    /// <summary>매 프레임(실시간) — 램프 진행·성공 임시 걷기·걷기 끝나면 없음으로.</summary>
    public void Tick(float now)
    {
        if (State == InterruptOwnerPresentationState.Success && _successFadeAt >= 0f && now >= _successFadeAt)
        {
            _successFadeAt = -1f;
            RampTo(0f, _settings.OwnerDimOutDuration, now);
        }

        Push(now);

        bool concluded = State == InterruptOwnerPresentationState.Failed ||
                         (State == InterruptOwnerPresentationState.Success && _successFadeAt < 0f);
        if (concluded && _pushedDim <= 0f && DimAt(now) <= 0f)
        {
            State = InterruptOwnerPresentationState.None;
            _strongStanding = false;
        }
    }

    private void EnterSuccess(float now)
    {
        // 같은 판정에서 실패 복귀가 성공 바로 앞에 한 번 날 수 있다(예측 대기 밖 성공) — 강이 살아 있으면 암전을 되살린다.
        bool dimmed = _strongStanding;
        State = InterruptOwnerPresentationState.Success;
        SetGather(false);
        if (dimmed)
            RampTo(1f, _settings.OwnerDimInDuration, now);
        _successFadeAt = now + Mathf.Max(0f, _settings.OwnerSuccessHoldDuration);
        Push(now);
        SuccessEntered?.Invoke(dimmed);
    }

    private void FadeOut(float now)
    {
        SetGather(false);
        RampTo(0f, _settings.OwnerDimOutDuration, now);
        Push(now);
    }

    private void SetGather(bool active)
    {
        if (LightGatherActive == active)
            return;

        LightGatherActive = active;
        if (active)
            _gatherOutput?.StartLightGather(_skill);
        else
            _gatherOutput?.StopLightGather();
    }

    private void RampTo(float target, float duration, float now)
    {
        _dimFrom = DimAt(now);
        _dimTo = target;
        _rampStart = now;
        _rampDuration = Mathf.Max(0f, duration);
    }

    private float DimAt(float now)
    {
        if (_rampDuration <= 0f)
            return _dimTo;

        return Mathf.Lerp(_dimFrom, _dimTo, Mathf.Clamp01((now - _rampStart) / _rampDuration));
    }

    private void Push(float now) => PushValue(DimAt(now));

    private void PushValue(float value)
    {
        // 정확 비교 — 근사 비교면 램프 끝의 1·0 직전 값에서 멈춘다(바뀐 프레임에만 넘긴다).
        if (value == _pushedDim)
            return;

        _pushedDim = value;
        _dimOutput?.SetOwnerDim(value);
    }
}
