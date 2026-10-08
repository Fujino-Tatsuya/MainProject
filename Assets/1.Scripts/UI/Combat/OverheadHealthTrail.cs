using UnityEngine;

/// <summary>
/// 체력 감소 잔상. 체력이 줄면 잔상이 이전 값에 머물다가(지연) 일정 속도로 따라 내려오고,
/// 회복하면 즉시 체력에 맞춘다. 값 단위는 체력량 — 실드로 스케일이 바뀌어도 잔상이 생기지 않는다.
/// </summary>
public sealed class OverheadHealthTrail
{
    float _lastTarget;
    float _holdRemaining;
    bool _initialized;

    /// <summary>현재 잔상이 가리키는 체력량.</summary>
    public float Value { get; private set; }

    /// <summary>즉시 target 으로 맞추고 지연을 지운다.</summary>
    public void Snap(float target)
    {
        Value = target;
        _lastTarget = target;
        _holdRemaining = 0f;
        _initialized = true;
    }

    /// <param name="target">현재 체력.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    /// <param name="holdDelay">감소 직후 잔상이 머무는 시간(초). 새로 맞을 때마다 처음부터.</param>
    /// <param name="speed">지연 후 내려오는 속도(체력량/초).</param>
    public float Step(float target, float deltaTime, float holdDelay, float speed)
    {
        if (!_initialized || target >= Value)
        {
            Snap(target);
            return Value;
        }

        if (target < _lastTarget)
            _holdRemaining = Mathf.Max(0f, holdDelay);
        else if (_holdRemaining > 0f)
            _holdRemaining -= Mathf.Max(0f, deltaTime);
        else
            Value = Mathf.MoveTowards(Value, target, Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime));

        _lastTarget = target;
        return Value;
    }
}
