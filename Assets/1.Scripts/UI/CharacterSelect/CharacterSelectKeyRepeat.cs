using UnityEngine;

/// <summary>
/// 캐릭터 선택 패널 이동 키 길게 누르기(할 일 60·60.1) — 매 프레임 "뒤로(-1)·앞으로(+1) 키가 눌려 있는가"를 받아 이번 프레임 몇 칸 갈지 낸다(MonoBehaviour 밖 — EditMode 테스트 대상).
/// 같은 방향 키 여러 개(W·↑·A·←)는 부르는 쪽이 하나로 묶어 넘긴다. 시각은 부르는 쪽 시계(패널 = unscaledTime).
///
/// - 처음 누른 순간 1칸 → <see cref="DelaySeconds"/> 뒤 첫 반복 → 이후 간격마다 1칸. 한 프레임 최대 1칸(밀린 간격은 버린다).
/// - 반복 간격은 <see cref="StartIntervalSeconds"/> 에서 시작해 홀드 시간에 비례해 줄어 대기 + <see cref="RampSeconds"/> 시점에 <see cref="MinIntervalSeconds"/> 에 닿고 그 뒤 유지.
///   다음 간격 = Lerp(시작, 최소, clamp01((칸을 낸 시점 홀드 시간 - 대기) / 램프)). 홀드 시간은 그 방향 반복이 (다시) 시작된 시각 기준.
/// - 다른 방향을 새로 누르면 즉시 그쪽 1칸 + 대기·가속부터 다시. 둘 다 눌려 있으면 나중에 누른 쪽이 이긴다(같은 프레임에 둘 다 누르면 뒤로 — 기존 키 검사 순서).
/// - 이기던 키를 떼고 다른 쪽이 아직 눌려 있으면 그 방향으로 대기·가속부터 다시(즉시 1칸 없음). 둘 다 떼면 멈춤.
/// - <see cref="Reset"/> 뒤에는 그때 눌려 있던 키를 뗐다가 새로 누를 때까지 무시한다(패널 열기·닫기·입력 무시 조건).
/// </summary>
public sealed class CharacterSelectKeyRepeat
{
    public float DelaySeconds = 0.25f;
    public float StartIntervalSeconds = 0.25f;
    public float MinIntervalSeconds = 0.1f;
    public float RampSeconds = 0.75f;

    private bool _backHeld;
    private bool _forwardHeld;
    private bool _backSuppressed;
    private bool _forwardSuppressed;
    private int _direction; // 지금 반복 중인 방향(-1·0·+1)
    private float _holdStart; // 지금 방향 반복(대기·가속)이 시작된 시각
    private float _nextAt;

    /// <summary>지금 반복 중인 방향(-1 = 뒤로, +1 = 앞으로, 0 = 없음).</summary>
    public int Direction => _direction;

    /// <summary>상태를 비운다. 지금 눌려 있는 키는 뗐다가 새로 누를 때까지 무시.</summary>
    public void Reset()
    {
        _backHeld = false;
        _forwardHeld = false;
        _backSuppressed = true;
        _forwardSuppressed = true;
        _direction = 0;
    }

    /// <summary>이번 프레임 입력 — 옮길 칸(-1·0·+1).</summary>
    public int Tick(bool backHeld, bool forwardHeld, float now)
    {
        if (!backHeld) _backSuppressed = false;
        if (!forwardHeld) _forwardSuppressed = false;
        bool back = backHeld && !_backSuppressed;
        bool forward = forwardHeld && !_forwardSuppressed;

        bool backPressed = back && !_backHeld;
        bool forwardPressed = forward && !_forwardHeld;
        _backHeld = back;
        _forwardHeld = forward;

        // 새로 누른 방향 — 즉시 1칸 + 대기·가속부터.
        int pressed = backPressed ? -1 : forwardPressed ? 1 : 0;
        if (pressed != 0)
        {
            _direction = pressed;
            _holdStart = now;
            _nextAt = now + DelaySeconds;
            return pressed;
        }

        if (_direction != 0 && !IsHeld(_direction))
        {
            // 이기던 키를 뗐다 — 다른 쪽이 남아 있으면 그쪽으로 대기·가속부터(즉시 1칸 없음).
            _direction = IsHeld(-_direction) ? -_direction : 0;
            _holdStart = now;
            _nextAt = now + DelaySeconds;
            return 0;
        }

        if (_direction == 0 || now < _nextAt)
            return 0;

        float stepAt = _nextAt;
        _nextAt = stepAt + IntervalAt(stepAt);
        if (_nextAt <= now)
            _nextAt = now + IntervalAt(now); // 프레임이 밀렸다 — 몰아서 가지 않는다
        return _direction;
    }

    /// <summary>시각 <paramref name="stepAt"/> 에 칸을 냈을 때 다음 칸까지 간격.</summary>
    private float IntervalAt(float stepAt)
    {
        float ramped = stepAt - _holdStart - DelaySeconds;
        float t = RampSeconds > 0f ? Mathf.Clamp01(ramped / RampSeconds) : (ramped >= 0f ? 1f : 0f);
        return Mathf.Lerp(StartIntervalSeconds, MinIntervalSeconds, t);
    }

    private bool IsHeld(int direction) => direction < 0 ? _backHeld : _forwardHeld;
}
