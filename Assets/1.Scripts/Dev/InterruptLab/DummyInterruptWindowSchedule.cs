using System;

/// <summary>디버그 랩 허수아비 창 모드(할 일 139).</summary>
public enum DummyInterruptWindowMode
{
    /// <summary>무기한 열림 — 기존 동작(성공 → 회복 → 다시 무기한 인터럽트 가능).</summary>
    Unbounded = 0,
    /// <summary>주기 — 열림 길이·닫힘 길이를 반복한다.</summary>
    Periodic = 1,
}

/// <summary>
/// 디버그 랩 허수아비 창의 주기 타이밍(할 일 139). 순수 C# — 규칙을 EditMode 에서 고정한다.
/// <see cref="DebugDummyInterruptWindowMode"/> 가 <see cref="ITrainingDummyWindowGate"/> 로 허수아비에 건다.
///
/// 🔴 규칙(2026-10-29 정함):
/// <list type="number">
/// <item><b>경계 시각 = 바뀐 뒤 단계.</b> 열림 경과가 정확히 열림 길이가 되면 그 순간부터 닫힘이다
///       (<see cref="InterruptWindowHistory.StateAt"/> 의 경계 규칙과 같다). 한 틱이 경계를 넘으면 넘친 시간은 다음 단계로 이월한다.</item>
/// <item><b>길이 0.</b> 열림 0 = 창이 열리지 않는다(항상 닫힘, 둘 다 0 포함). 닫힘 0 = 계속 열림.
///       이때 단계 경과는 0 으로 둔다 — 길이를 다시 0 보다 크게 하면 그 단계 처음부터.</item>
/// <item><b>도중 길이 변경.</b> 지금 단계와 경과는 유지하고 새 길이로 비교한다. 경과가 이미 새 길이 이상이면
///       경과를 새 길이로 눌러 다음 Tick 에 바로 다음 단계로 넘긴다(넘친 경과는 이월하지 않는다).</item>
/// <item><b>모드 전환 = 위상 리셋.</b> 주기로 바꾸면 열림 단계 처음부터. 무기한으로 바꾸면 항상 열림.
///       같은 모드를 다시 고르면 아무것도 안 한다.</item>
/// <item><b>회복 중엔 시계가 멈춘다.</b> 인터럽트 성공 → 회복(취약·대기)이 끝나면 <b>닫힘 단계 처음부터</b> 다시 시작한다
///       — 성공 직후 잘린 열림이 다시 나오지 않게(관측 창 길이가 열림 길이로 모인다). 회복이 끝난 틱의 시간은 버린다.</item>
/// </list>
/// 시간축은 호출측이 넘기는 서버 <c>Time.deltaTime</c> — 허수아비 회복 순환과 같다(디버그 배속·슬로우를 따른다).
/// </summary>
public sealed class DummyInterruptWindowSchedule
{
    public const float DefaultOpenSeconds = 0.6f;
    public const float DefaultClosedSeconds = 1.5f;

    bool _wasRecovering;

    public DummyInterruptWindowSchedule(
        DummyInterruptWindowMode mode = DummyInterruptWindowMode.Unbounded,
        float openSeconds = DefaultOpenSeconds,
        float closedSeconds = DefaultClosedSeconds)
    {
        Mode = mode;
        OpenSeconds = Math.Max(0f, openSeconds);
        ClosedSeconds = Math.Max(0f, closedSeconds);
        RestartPhase(open: true);
    }

    public DummyInterruptWindowMode Mode { get; private set; }
    public float OpenSeconds { get; private set; }
    public float ClosedSeconds { get; private set; }

    /// <summary>주기 모드에서 지금 열림 단계인가.</summary>
    public bool IsOpenPhase { get; private set; }

    /// <summary>지금 단계에 들어온 뒤 경과(초).</summary>
    public float PhaseElapsed { get; private set; }

    /// <summary>회복 중이 아닐 때 창이 열려 있어야 하는가(마지막 Tick 기준).</summary>
    public bool IsOpen => Mode == DummyInterruptWindowMode.Unbounded || IsOpenPhase;

    /// <summary>모드를 바꾼다. 바뀌면 위상 리셋(주기 = 열림 단계 처음부터).</summary>
    public void SetMode(DummyInterruptWindowMode mode)
    {
        if (mode == Mode)
            return;

        Mode = mode;
        RestartPhase(open: true);
    }

    /// <summary>길이를 바꾼다(음수 = 0). 지금 단계·경과 유지, 이미 넘었으면 다음 Tick 에 넘어간다.</summary>
    public void SetDurations(float openSeconds, float closedSeconds)
    {
        OpenSeconds = Math.Max(0f, openSeconds);
        ClosedSeconds = Math.Max(0f, closedSeconds);
        PhaseElapsed = Math.Min(PhaseElapsed, CurrentPhaseLength);
        Normalize();
    }

    /// <summary>
    /// 시간을 흘린다(서버 틱마다). 반환 = 회복 중이 아닐 때 창이 열려 있어야 하는가.
    /// <paramref name="recovering"/> 이면 시계를 멈추고, 회복이 끝난 첫 Tick 에 닫힘 단계 처음부터 다시 시작한다.
    /// </summary>
    public bool Tick(float deltaTime, bool recovering)
    {
        if (recovering)
        {
            _wasRecovering = true;
            return IsOpen;
        }

        if (_wasRecovering)
        {
            _wasRecovering = false;
            if (Mode == DummyInterruptWindowMode.Periodic)
            {
                RestartPhase(open: false);
                return IsOpen;
            }
        }

        if (Mode == DummyInterruptWindowMode.Periodic)
            Advance(Math.Max(0f, deltaTime));
        return IsOpen;
    }

    float CurrentPhaseLength => IsOpenPhase ? OpenSeconds : ClosedSeconds;

    void RestartPhase(bool open)
    {
        IsOpenPhase = open;
        PhaseElapsed = 0f;
        Normalize();
    }

    void Advance(float deltaTime)
    {
        PhaseElapsed += deltaTime;
        if (Normalize())
            return;

        // 한 틱이 여러 주기를 넘겨도 루프가 길어지지 않게 — 온 주기는 위상을 바꾸지 않는다.
        float period = OpenSeconds + ClosedSeconds;
        if (PhaseElapsed >= period)
            PhaseElapsed %= period;

        while (PhaseElapsed >= CurrentPhaseLength)
        {
            PhaseElapsed -= CurrentPhaseLength;
            IsOpenPhase = !IsOpenPhase;
        }
    }

    // 길이 0 규칙. 처리했으면 true(단계 고정, 경과 0).
    bool Normalize()
    {
        if (OpenSeconds <= 0f)
        {
            IsOpenPhase = false;
            PhaseElapsed = 0f;
            return true;
        }

        if (ClosedSeconds <= 0f)
        {
            IsOpenPhase = true;
            PhaseElapsed = 0f;
            return true;
        }

        return false;
    }
}
