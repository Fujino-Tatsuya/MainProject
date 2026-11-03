using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>카메라 대상 중심 진행 상태(할 일 196 단계 2).</summary>
public enum InterruptCameraCenterState : byte
{
    /// <summary>옮기지 않음 — 이동량 0.</summary>
    Idle = 0,
    /// <summary>대상 쪽으로 옮겨 가는 중·유지(대상 추적).</summary>
    Centering = 1,
    /// <summary>평소 중심으로 돌아가는 중.</summary>
    Returning = 2,
}

/// <summary>
/// 카메라 대상 중심(할 일 196 단계 2) — 오너 화면 카메라 이동량(월드, 수평)을 정하는 상태 기계. 순수 로직(EditMode 테스트 대상) —
/// 시각·위치·슬로우 상태는 호출측(디렉터)이 넘긴다. 판단은 <see cref="InterruptCameraCenterRules"/> 를 쓴다.
/// <list type="bullet">
/// <item>시도 cue(<see cref="Begin"/>) — 강·약 + 대상 있음이면 이동 시간 동안 지금 이동량에서 대상 쪽 이동량으로 보간, 그 뒤 대상을 따라간다.
///       대상 없음·스위치 꺼짐·오너 아님이면 옮기지 않는다(옮겨 둔 상태가 있으면 복귀 — 새 간파가 이전 상태를 대체).</item>
/// <item>판정 실패·서버 거부·끊김 = <see cref="Return"/>(그 순간 복귀). 대상이 사라지면 마지막 위치를 두고 복귀.</item>
/// <item>판정 성공 = <see cref="Succeeded"/> — 내 성공 슬로우가 보이면 그 복귀 구간 시작에, 끝내 안 보이면(첫 승인자 아님 등) 성공 프로필 시간표로 복귀.</item>
/// <item>보간 시작점은 늘 지금 이동량 — 대체·복귀가 이어져도 튀지 않는다. 시간은 호출측이 실제 시간(unscaled)으로 넘긴다.</item>
/// </list>
/// </summary>
public sealed class InterruptCameraCenterTracker
{
    InterruptCameraCenterState _state;
    float _ratio;
    float _returnDuration;
    // 지금 보간 — 시작 이동량에서 목표(유지 = 대상 쪽, 복귀 = 0)까지.
    Vector3 _fromShift;
    float _blendStart;
    float _blendDuration;
    Vector3 _lastShift;
    Vector3 _lastTargetOffset;
    bool _hasTargetOffset;
    // 성공 복귀 — 성공 슬로우를 기다린다. 슬로우가 안 보이면 _successReturnAt(실제 시간)에 복귀.
    bool _awaitsSuccessReturn;
    float _successReturnAt;
    uint _staleSlowTriggerId;
    uint _seenSlowTriggerId;

    public InterruptCameraCenterState State => _state;

    /// <summary>판정 성공 뒤 성공 슬로우 복귀를 기다리는 중.</summary>
    public bool AwaitsSuccessReturn => _state == InterruptCameraCenterState.Centering && _awaitsSuccessReturn;

    /// <summary>마지막으로 돌려준 이동량(월드, 수평).</summary>
    public Vector3 LastShift => _lastShift;

    /// <summary>
    /// [오너] 시도 cue(근접 = 누른 순간, 대쉬 = 대시에 실린 순간 — 191 예측). 대상 쪽으로 옮기기 시작했으면 true.
    /// 옮기지 않는 시도면(대상 없음 등) 옮겨 둔 상태를 복귀시킨다.
    /// </summary>
    public bool Begin(InterruptAttemptForecast forecast, bool hasTarget, bool switchOn, bool isOwner, float ratio,
        float moveDuration, float returnDuration, float now)
    {
        _returnDuration = Mathf.Max(0f, returnDuration);
        _awaitsSuccessReturn = false;
        if (!hasTarget || !InterruptCameraCenterRules.Applies(forecast, switchOn, isOwner))
        {
            Return(now);
            return false;
        }

        _state = InterruptCameraCenterState.Centering;
        _ratio = ratio;
        _hasTargetOffset = false;
        StartBlend(now, moveDuration);
        return true;
    }

    /// <summary>
    /// [오너] 판정 성공 — 대상 중심을 유지하고 성공 슬로우 복귀 구간에 맞춰 복귀한다.
    /// <paramref name="fallbackReturnDelay"/> = 내 성공 슬로우가 끝내 안 보일 때 지금부터 복귀까지(실제 시간 초, <see cref="InterruptCameraCenterRules.SuccessReturnDelay"/>).
    /// <paramref name="staleSlowTriggerId"/> = 지금 돌고 있는 내 앞 성공 슬로우 번호(이번 성공 것이 아니다 — 연속 간파), 없으면 0.
    /// </summary>
    public void Succeeded(float now, float fallbackReturnDelay, uint staleSlowTriggerId)
    {
        if (_state != InterruptCameraCenterState.Centering)
            return;

        _awaitsSuccessReturn = true;
        _successReturnAt = now + Mathf.Max(0f, float.IsNaN(fallbackReturnDelay) ? 0f : fallbackReturnDelay);
        _staleSlowTriggerId = staleSlowTriggerId;
        _seenSlowTriggerId = 0;
    }

    /// <summary>
    /// [오너] 매 프레임 — 지금 이 화면의 내 성공 슬로우(<paramref name="mySuccessSlowTriggerId"/>, 없으면 0)와 그 단계.
    /// 보이면 그 복귀 구간 시작에 복귀, 보였다가 사라지면(덮임·리셋) 복귀.
    /// </summary>
    public void ObserveSuccessSlow(uint mySuccessSlowTriggerId, InterruptCameraCenterPhase slowPhase, float now)
    {
        if (!AwaitsSuccessReturn)
            return;

        bool live = mySuccessSlowTriggerId != 0 && mySuccessSlowTriggerId != _staleSlowTriggerId;
        if (live)
        {
            _seenSlowTriggerId = mySuccessSlowTriggerId;
            if (Decide(slowPhase).Return == InterruptCameraReturnTiming.Now)
                Return(now);
            return;
        }

        if (_seenSlowTriggerId != 0)
            Return(now);
    }

    /// <summary>[오너] 지금 복귀를 시작한다(판정 실패·서버 거부·끊김·대상 소실). 옮기지 않았으면 아무것도 안 한다.</summary>
    public void Return(float now)
    {
        _awaitsSuccessReturn = false;
        if (_state != InterruptCameraCenterState.Centering)
            return;

        _state = InterruptCameraCenterState.Returning;
        StartBlend(now, _returnDuration);
    }

    /// <summary>[오너] 성공 복귀를 기다리는 중이 아니면 복귀(시전 거부 등 cue 없는 종료 — 성공 유지는 그대로).</summary>
    public void ReturnUnlessSucceeded(float now)
    {
        if (!AwaitsSuccessReturn)
            Return(now);
    }

    /// <summary>즉시 원래대로(다른 카메라 연출이 카메라를 잡음·꺼짐) — 이동량 0, 보간 없음.</summary>
    public void Cancel()
    {
        _state = InterruptCameraCenterState.Idle;
        _awaitsSuccessReturn = false;
        _hasTargetOffset = false;
        _lastShift = Vector3.zero;
    }

    /// <summary>
    /// 매 프레임 — 이번 프레임 카메라 이동량(월드, 수평 — y 0). <paramref name="baseCenter"/> = 평소 중심(카메라 추적 대상 위치),
    /// <paramref name="targetAlive"/>·<paramref name="targetPosition"/> = 간파 대상(사라졌으면 false — 마지막 위치를 두고 복귀).
    /// </summary>
    public Vector3 Step(float now, Vector3 baseCenter, bool targetAlive, Vector3 targetPosition)
    {
        if (_state == InterruptCameraCenterState.Idle)
            return _lastShift = Vector3.zero;

        if (_state == InterruptCameraCenterState.Centering)
        {
            if (targetAlive)
            {
                Vector3 offset = Decide(InterruptCameraCenterPhase.Click, baseCenter, targetPosition).Center - baseCenter;
                offset.y = 0f;
                _lastTargetOffset = offset;
                _hasTargetOffset = true;
            }
            else
            {
                Return(now);
            }

            if (AwaitsSuccessReturn && _seenSlowTriggerId == 0 && now >= _successReturnAt)
                Return(now);
        }

        float t = _blendDuration > 0f ? Mathf.Clamp01((now - _blendStart) / _blendDuration) : 1f;
        float eased = Mathf.SmoothStep(0f, 1f, t);
        if (_state == InterruptCameraCenterState.Returning)
        {
            if (t >= 1f)
            {
                Cancel();
                return _lastShift;
            }

            return _lastShift = Vector3.Lerp(_fromShift, Vector3.zero, eased);
        }

        Vector3 goal = _hasTargetOffset ? _lastTargetOffset : _fromShift;
        return _lastShift = t >= 1f ? goal : Vector3.Lerp(_fromShift, goal, eased);
    }

    void StartBlend(float now, float duration)
    {
        _fromShift = _lastShift;
        _blendStart = now;
        _blendDuration = float.IsNaN(duration) ? 0f : Mathf.Max(0f, duration);
    }

    // 대상 쪽 목표 중심 — 판단은 순수 규칙 하나(스위치·오너는 Begin 에서 이미 걸렀다).
    InterruptCameraCenterDecision Decide(InterruptCameraCenterPhase phase, Vector3 baseCenter = default, Vector3 targetPosition = default) =>
        InterruptCameraCenterRules.Decide(InterruptAttemptForecast.Strong, phase, true, _ratio, true, baseCenter, targetPosition);
}
