using System;

/// <summary>허수아비 인터럽트 연습 상태. 서버가 정하고 NetworkVariable 로 복제한다(값은 끝에만 추가).</summary>
public enum TrainingDummyInterruptState : byte
{
    /// <summary>인터럽트를 받는다. 스폰 기본값이고 시간 만료가 없다.</summary>
    Interruptible = 0,
    /// <summary>인터럽트 성공 직후 그로기. 인터럽트 공격은 데미지만 들어간다.</summary>
    Groggy = 1,
    /// <summary>그로기가 끝나고 다시 열리기 전 대기. 인터럽트 공격은 데미지만 들어간다.</summary>
    Idle = 2,
}

/// <summary>
/// 허수아비 인터럽트 순환 — [인터럽트 가능] → (성공) → [그로기] → [idle] → [인터럽트 가능] …
///
/// 순수 C# 이다(MonoBehaviour 아님). <see cref="TrainingDummyRegen"/> 과 같은 이유로 EditMode 에서 순서를 고정한다.
/// 판정은 몬스터의 <see cref="CounterWindow"/> 를 그대로 쓴다 — 같은 1회 소비 규칙(성공하면 창이 즉시 닫힌다).
///
/// 🔴 인터럽트 가능 창은 <b>무기한</b>이다. 창을 아주 길게 열고 <c>TickAndDetectExpiry</c> 를 부르지 않는다.
///    상호작용이 없으면 계속 열려 있어야 연습이 된다.
/// </summary>
public sealed class TrainingDummyInterruptCycle
{
    // 창은 Tick 하지 않으므로 이 값은 "0 보다 크다"는 의미만 있다(CounterWindow 는 0 이하면 열지 않는다).
    const float UnboundedWindow = float.MaxValue;

    readonly float _groggySeconds;
    readonly float _idleSeconds;
    readonly CounterWindow _window = new CounterWindow();

    float _remaining;

    public TrainingDummyInterruptCycle(float groggySeconds, float idleSeconds)
    {
        _groggySeconds = Math.Max(0f, groggySeconds);
        _idleSeconds = Math.Max(0f, idleSeconds);
        Reset();
    }

    public TrainingDummyInterruptState State { get; private set; }

    public bool IsInterruptible => State == TrainingDummyInterruptState.Interruptible;

    /// <summary>현재 상태의 남은 시간(초). 인터럽트 가능 상태는 무기한이라 0.</summary>
    public float Remaining => IsInterruptible ? 0f : Math.Max(0f, _remaining);

    /// <summary>인터럽트 가능 상태로 되돌린다(스폰 기본값).</summary>
    public void Reset()
    {
        State = TrainingDummyInterruptState.Interruptible;
        _remaining = 0f;
        _window.Open(UnboundedWindow);
    }

    /// <summary>
    /// 인터럽트 공격 하나를 판정한다. 인터럽트 가능 상태에서만 true — 그 즉시 그로기로 넘어간다.
    /// 그로기·idle 중이면 false(호출측은 데미지만 처리한다).
    /// </summary>
    public bool TryInterrupt()
    {
        if (!_window.TryConsumeInterrupt())
            return false;

        State = TrainingDummyInterruptState.Groggy;
        _remaining = _groggySeconds;
        return true;
    }

    /// <summary>
    /// 시간을 흘린다. <b>이번 호출에서 상태가 바뀌었으면 true</b> — 호출측이 복제값을 그때만 쓰도록.
    /// 한 틱이 여러 단계를 넘기면(idle 0 등) 남는 시간을 다음 단계로 넘겨 끝 상태 하나로 수렴한다.
    /// </summary>
    /// <param name="deltaTime">서버 틱 간격. 음수는 0 으로 본다.</param>
    public bool Tick(float deltaTime)
    {
        if (IsInterruptible)
            return false;

        TrainingDummyInterruptState before = State;
        _remaining -= Math.Max(0f, deltaTime);

        while (!IsInterruptible && _remaining <= 0f)
        {
            float overflow = -_remaining;
            if (State == TrainingDummyInterruptState.Groggy)
            {
                State = TrainingDummyInterruptState.Idle;
                _remaining = _idleSeconds - overflow;
            }
            else
            {
                Reset();
            }
        }

        return State != before;
    }
}
