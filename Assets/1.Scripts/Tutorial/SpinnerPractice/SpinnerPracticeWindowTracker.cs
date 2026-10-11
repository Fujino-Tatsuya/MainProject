using System;

/// <summary><see cref="SpinnerPracticeWindowTracker"/> 가 한 번의 관찰에서 내는 사건들. 한 호출에 둘 이상 설 수 있다.</summary>
[Flags]
public enum SpinnerWindowEvent
{
    None = 0,
    Opened = 1 << 0,
    /// <summary>간파 성공으로 닫혔다.</summary>
    Succeeded = 1 << 1,
    /// <summary>성공 없이 닫혔다(만료 · 공격 취소 · 모드 전환 정지 포함).</summary>
    Missed = 1 << 2,
}

/// <summary>
/// 간파 창 열림/닫힘을 관찰해 "놓침"과 연속 놓침 수를 센다(순수 C#).
///
/// 입력은 둘이다 — 매 틱 창이 열려 있는지(<see cref="Observe"/>)와 서버 간파 성공 통보(<see cref="NotifySucceeded"/>).
/// 성공이 먼저 오면 그 창은 성공으로 닫힌 것이고, 그 뒤 닫힘 관찰은 아무 사건도 내지 않는다.
/// 🔴 성공 통보가 열림 관찰보다 먼저 와도(같은 프레임에 열리고 바로 간파) 열림을 함께 낸다 — 창 하나가 빠지지 않게.
/// </summary>
public sealed class SpinnerPracticeWindowTracker
{
    bool _open;

    /// <summary>마지막으로 관찰한 창 상태.</summary>
    public bool IsOpen => _open;

    /// <summary>연속 놓침 수. 성공하면 0 으로 돌아간다.</summary>
    public int ConsecutiveMisses { get; private set; }

    public int WindowsOpened { get; private set; }
    public int TotalSuccesses { get; private set; }
    public int TotalMisses { get; private set; }

    /// <summary>이번 틱의 창 상태를 넣는다. 열림/닫힘 경계에서만 사건이 난다.</summary>
    public SpinnerWindowEvent Observe(bool windowOpen)
    {
        if (windowOpen == _open) return SpinnerWindowEvent.None;

        _open = windowOpen;
        if (windowOpen)
        {
            WindowsOpened++;
            return SpinnerWindowEvent.Opened;
        }

        ConsecutiveMisses++;
        TotalMisses++;
        return SpinnerWindowEvent.Missed;
    }

    /// <summary>서버 간파 성공 통보. 지금 창을 성공으로 닫는다(연속 놓침 0).</summary>
    public SpinnerWindowEvent NotifySucceeded()
    {
        SpinnerWindowEvent events = SpinnerWindowEvent.Succeeded;
        if (!_open)
        {
            WindowsOpened++;
            events |= SpinnerWindowEvent.Opened;
        }

        _open = false;
        ConsecutiveMisses = 0;
        TotalSuccesses++;
        return events;
    }

    /// <summary>집계를 비운다(연습 모드 재진입). 지금 창 상태는 그대로 둔다 — 열린 창의 닫힘은 이어서 센다.</summary>
    public void ResetCounts()
    {
        ConsecutiveMisses = 0;
        WindowsOpened = 0;
        TotalSuccesses = 0;
        TotalMisses = 0;
    }
}
