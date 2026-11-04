#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;

/// <summary>오너 판정 순간 캡처 한 건(할 일 139 → 191 5단계) — 판정 시점 허수아비 창 설정·실제 상태와 판정 위치 → 대상 거리·각도.</summary>
public struct InterruptCalibrationJudgeCapture
{
    public float Time;      // 판정 시각(Time.time)
    public float RealTime;  // 판정 시각(Time.realtimeSinceStartup)

    public CalibrationWindowMode? WindowMode;
    public float? WindowOpenLength;
    public float? WindowClosedLength;
    public bool? ActualWindowOpen;
    public float? ActualOpenElapsed;
    public float? ActualRemaining;

    public float? TargetDistance;
    public float? TargetAngle;
}

/// <summary>
/// 답 대기 표본 하나(할 일 139 단계 3 → 단계 4 질문 창). 질문 창은 결과 시각을 기준으로 성공 연출·실패 복귀가 끝난 뒤 묻고,
/// 답을 받으면 <see cref="InterruptCalibrationRecorder.Commit"/> 한다.
/// </summary>
public readonly struct InterruptCalibrationCompleted
{
    public InterruptCalibrationCompleted(InterruptCalibrationSample sample, bool succeeded, float judgeTime, float judgeRealTime)
    {
        Sample = sample;
        Succeeded = succeeded;
        JudgeTime = judgeTime;
        JudgeRealTime = judgeRealTime;
    }

    public InterruptCalibrationSample Sample { get; }
    /// <summary>최종 성공(오너 판정 성공 ∧ 서버 승인).</summary>
    public bool Succeeded { get; }
    public float JudgeTime { get; }
    public float JudgeRealTime { get; }
}

/// <summary>
/// 간파 보정 표본 짝짓기(할 일 139 → 191 5단계 용도 변경) — 순수 C#. 캡처 컴포넌트(<see cref="InterruptCalibrationCapture"/>)가 값을 넣는다.
///
/// 🔴 규칙(2026-11-02, 191 5단계):
/// <list type="number">
/// <item><b>시도 식별 = QA 기록 한 줄.</b> 예측·판정·서버 결과의 짝짓기는 <see cref="InterruptQaTracker"/> 가 시전자 id·창 id 로 한다 — 옛 '대기 표본 하나' 규칙은 없앴다.</item>
/// <item><b>판정 순간 캡처(<see cref="NoteJudged"/>)</b>는 시전자별로 들고 있다가, 그 시전자의 기록 줄이 닫히면(<see cref="OnRecord"/>) 붙여 표본을 만든다.
///       서버 결과는 판정보다 늦게 오므로(승인·거부) 실제 창 상태는 판정 순간 값이다.</item>
/// <item><b>판정까지 간 줄만 표본.</b> 판정 전 종료(끊김·버림·시전 거부)는 버리고 <see cref="DroppedCount"/> 로 센다.</item>
/// <item><b>대쉬 간파만.</b> 근접 간파는 <see cref="IncludeMelee"/> 가 꺼져 있으면 무시한다(버림 수에도 안 넣는다).</item>
/// </list>
/// </summary>
public sealed class InterruptCalibrationPairing
{
    readonly Dictionary<ulong, InterruptCalibrationJudgeCapture> _judged = new Dictionary<ulong, InterruptCalibrationJudgeCapture>();

    /// <summary>근접 간파도 기록한다(기본 꺼짐 — 대쉬 간파만).</summary>
    public bool IncludeMelee { get; set; }

    /// <summary>판정 전에 끝나 표본이 되지 못한 줄 수.</summary>
    public int DroppedCount { get; private set; }

    /// <summary>[오너] 판정 순간 캡처 — 같은 시전자의 이전 캡처는 바꾼다.</summary>
    public void NoteJudged(ulong actorId, in InterruptCalibrationJudgeCapture capture) => _judged[actorId] = capture;

    /// <summary>QA 기록 한 줄이 닫혔다. 판정까지 간 대쉬 간파(또는 근접 허용)면 표본을 만든다.</summary>
    public bool OnRecord(InterruptQaRecord record, out InterruptCalibrationCompleted completed)
    {
        completed = default;
        if (record == null || !Accepts(record.ViaDash))
            return false;

        bool judged = record.Judge == InterruptQaJudge.Success || record.Judge == InterruptQaJudge.Failure ||
                      record.Judge == InterruptQaJudge.NoTarget;
        bool hasCapture = _judged.TryGetValue(record.ActorId, out InterruptCalibrationJudgeCapture capture);
        _judged.Remove(record.ActorId);
        if (!judged)
        {
            DroppedCount++;
            return false;
        }

        var sample = new InterruptCalibrationSample { Record = record };
        if (hasCapture)
        {
            sample.RealTime = capture.RealTime;
            sample.WindowMode = capture.WindowMode;
            sample.WindowOpenLength = capture.WindowOpenLength;
            sample.WindowClosedLength = capture.WindowClosedLength;
            sample.ActualWindowOpen = capture.ActualWindowOpen;
            sample.ActualOpenElapsed = capture.ActualOpenElapsed;
            sample.ActualRemaining = capture.ActualRemaining;
            sample.TargetDistance = capture.TargetDistance;
            sample.TargetAngle = capture.TargetAngle;
        }

        completed = new InterruptCalibrationCompleted(sample, record.FinalSuccess, capture.Time, capture.RealTime);
        return true;
    }

    /// <summary>들고 있는 판정 캡처를 버린다(씬 나감·랩 끔).</summary>
    public void Clear() => _judged.Clear();

    bool Accepts(bool viaDash) => viaDash || IncludeMelee;

    /// <summary>허수아비 창 모드 → 표본 열 값.</summary>
    public static CalibrationWindowMode ToCalibrationMode(DummyInterruptWindowMode mode) =>
        mode == DummyInterruptWindowMode.Periodic ? CalibrationWindowMode.Periodic : CalibrationWindowMode.Indefinite;

    /// <summary>음수(모름) → null.</summary>
    public static float? KnownOrNull(float value) => value >= 0f ? value : (float?)null;
}
#endif
