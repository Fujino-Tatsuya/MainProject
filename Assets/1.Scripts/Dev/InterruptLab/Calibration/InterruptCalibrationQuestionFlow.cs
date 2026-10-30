#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Collections.Generic;

/// <summary>질문 창 답(할 일 139 단계 4).</summary>
public enum CalibrationAnswer
{
    ShouldSucceed, // 성공이어야 했음(true)
    ShouldFail,    // 실패여야 했음(false)
    Skip,          // 건너뛰기(Esc) — 기록 안 함
}

/// <summary>질문 흐름 단계.</summary>
public enum CalibrationQuestionPhase
{
    Idle,    // 답 대기 표본 없음
    Waiting, // 표본은 있다 — 결과가 다 보이길 기다리는 중(시간은 흐른다)
    Asking,  // 시간을 멈추고 묻는 중
}

/// <summary>답 하나를 처리한 결과 — <see cref="Commit"/> 이면 <see cref="Value"/> 로 기록한다.</summary>
public readonly struct CalibrationAnswerOutcome
{
    public CalibrationAnswerOutcome(InterruptCalibrationCompleted answered, bool commit, bool value)
    {
        Answered = answered;
        Commit = commit;
        Value = value;
    }

    public InterruptCalibrationCompleted Answered { get; }
    public bool Commit { get; }
    public bool Value { get; }
}

/// <summary>
/// 간파 예측 보정 질문 흐름(할 일 139 단계 4) — 순수 C#. 질문 창 컴포넌트(<see cref="InterruptCalibrationQuestionPanel"/>)가 실시간·시간 배율 상태를 넣는다.
///
/// 🔴 규칙(2026-10-29 정함):
/// <list type="number">
/// <item><b>묻는 시점.</b> 판정 실시간(<see cref="InterruptCalibrationCompleted.JudgeRealTime"/>) + <see cref="SettleDelay"/>(기본 0.5초 실시간)이 지났고
///       시간 배율이 평소 상태(슬로우·히트스톱 끝)일 때. 평소로 안 돌아와도 판정 + <see cref="MaxWait"/>(기본 3초 실시간)가 지나면 그냥 묻는다.</item>
/// <item><b>답.</b> true/false = 기록(Commit), Skip(Esc) = 기록 안 함·<see cref="SkippedCount"/>. 묻는 중이 아니면 답은 무시한다.</item>
/// <item><b>대기열.</b> 기다리거나 묻는 중에 다음 표본이 오면 FIFO 로 줄 세운다(최대 <see cref="QueueCapacity"/>, 기본 8). 넘치면 새로 온 표본을 버리고
///       <see cref="OverflowDroppedCount"/> 를 센다. 답을 받으면 다음 표본이 대기로 올라온다 — 판정이 이미 오래전이라 보통 곧바로 다시 묻는다.</item>
/// <item><b>정리.</b> <see cref="Clear"/>(씬 나감·비활성) = 묻는 중·대기열 표본을 기록 없이 버린다(건너뜀으로 세지 않는다).</item>
/// </list>
/// </summary>
public sealed class InterruptCalibrationQuestionFlow
{
    public const float DefaultSettleDelay = 0.5f;
    public const float DefaultMaxWait = 3f;
    public const int DefaultQueueCapacity = 8;

    readonly Queue<InterruptCalibrationCompleted> _queue = new Queue<InterruptCalibrationCompleted>();
    InterruptCalibrationCompleted _current;

    /// <summary>판정 뒤 결과가 다 보일 때까지 기다리는 실시간(초).</summary>
    public float SettleDelay { get; set; } = DefaultSettleDelay;

    /// <summary>판정 뒤 이만큼(실시간 초) 지나면 시간 배율이 평소로 안 돌아와도 묻는다.</summary>
    public float MaxWait { get; set; } = DefaultMaxWait;

    /// <summary>현재 표본 뒤에 줄 세울 수 있는 최대 개수.</summary>
    public int QueueCapacity { get; set; } = DefaultQueueCapacity;

    public CalibrationQuestionPhase Phase { get; private set; } = CalibrationQuestionPhase.Idle;
    public bool IsAsking => Phase == CalibrationQuestionPhase.Asking;

    /// <summary>기다리거나 묻는 중인 표본. <see cref="Phase"/> 가 Idle 이면 의미 없다.</summary>
    public InterruptCalibrationCompleted Current => _current;

    public int QueuedCount => _queue.Count;
    public int AnsweredCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int OverflowDroppedCount { get; private set; }

    /// <summary>답 대기 표본을 넣는다.</summary>
    public void Enqueue(in InterruptCalibrationCompleted completed)
    {
        if (completed.Sample == null)
            return;

        if (Phase == CalibrationQuestionPhase.Idle)
        {
            _current = completed;
            Phase = CalibrationQuestionPhase.Waiting;
            return;
        }

        if (_queue.Count >= Math.Max(0, QueueCapacity))
        {
            OverflowDroppedCount++;
            return;
        }

        _queue.Enqueue(completed);
    }

    /// <summary>
    /// 매 프레임. 기다리던 표본을 이번에 묻기 시작하면 true(호출측이 시간을 멈춘다).
    /// <paramref name="timeSettled"/> = 시간 배율이 평소 상태(슬로우·히트스톱 없음).
    /// </summary>
    public bool Tick(float realNow, bool timeSettled)
    {
        if (Phase != CalibrationQuestionPhase.Waiting)
            return false;

        if (!ShouldAsk(realNow, _current.JudgeRealTime, SettleDelay, MaxWait, timeSettled))
            return false;

        Phase = CalibrationQuestionPhase.Asking;
        return true;
    }

    /// <summary>판정 뒤 <paramref name="settleDelay"/> 가 지났고, 평소 상태이거나 <paramref name="maxWait"/> 가 지났다.</summary>
    public static bool ShouldAsk(float realNow, float judgeRealTime, float settleDelay, float maxWait, bool timeSettled)
    {
        float since = realNow - judgeRealTime;
        float delay = Math.Max(0f, settleDelay);
        if (since < delay)
            return false;
        return timeSettled || since >= Math.Max(delay, maxWait);
    }

    /// <summary>묻는 중일 때 답을 처리하고 다음 표본을 대기로 올린다. 묻는 중이 아니면 false.</summary>
    public bool Answer(CalibrationAnswer answer, out CalibrationAnswerOutcome outcome)
    {
        outcome = default;
        if (Phase != CalibrationQuestionPhase.Asking)
            return false;

        bool? value = ToUserAnswer(answer);
        outcome = new CalibrationAnswerOutcome(_current, value.HasValue, value ?? false);
        if (value.HasValue)
            AnsweredCount++;
        else
            SkippedCount++;

        Advance();
        return true;
    }

    /// <summary>묻는 중·대기열 표본을 기록 없이 버린다. 개수는 그대로 둔다.</summary>
    public void Clear()
    {
        _queue.Clear();
        _current = default;
        Phase = CalibrationQuestionPhase.Idle;
    }

    /// <summary>true = 성공이어야 했음, false = 실패여야 했음, null = 건너뛰기.</summary>
    public static bool? ToUserAnswer(CalibrationAnswer answer) => answer switch
    {
        CalibrationAnswer.ShouldSucceed => true,
        CalibrationAnswer.ShouldFail => false,
        _ => null,
    };

    void Advance()
    {
        if (_queue.Count > 0)
        {
            _current = _queue.Dequeue();
            Phase = CalibrationQuestionPhase.Waiting;
            return;
        }

        _current = default;
        Phase = CalibrationQuestionPhase.Idle;
    }
}

/// <summary>
/// 질문 중 시간 정지의 배율 보관(할 일 139 단계 4) — 순수 C#. 멈출 때 배율을 한 번만 기억하고, 풀 때 그 값을 돌려준다.
/// 멈춘 동안 다른 계층(<see cref="GlobalTimeScale"/>)이 배율을 다시 써도 호출측이 매 프레임 마지막에 <see cref="HeldScale"/>(0)을 덮어쓴다.
/// </summary>
public sealed class CalibrationTimePause
{
    public const float HeldScale = 0f;

    float _saved = 1f;

    public bool IsPaused { get; private set; }

    /// <summary>멈춘 순간의 배율. 멈추지 않았으면 1.</summary>
    public float SavedScale => IsPaused ? _saved : 1f;

    /// <summary>멈춘다. 이미 멈췄으면 처음 기억한 배율을 유지하고 false.</summary>
    public bool Begin(float currentScale)
    {
        if (IsPaused)
            return false;

        // 0 이하(다른 누군가 멈춰 둔 값)를 기억하면 풀어도 멈춘 채가 된다 — 그땐 1 로 돌려놓는다.
        _saved = currentScale > 0f ? currentScale : 1f;
        IsPaused = true;
        return true;
    }

    /// <summary>푼다. 돌려놓을 배율(멈춘 순간 값). 멈춘 적 없으면 null.</summary>
    public float? End()
    {
        if (!IsPaused)
            return null;

        IsPaused = false;
        return _saved;
    }
}
#endif
