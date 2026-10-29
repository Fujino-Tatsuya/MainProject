#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using BeaverLobby.Player.Dash;

/// <summary>오너 입력 순간 캡처 한 건(할 일 139 단계 3) — 예측 입력·결과와 그때 대상까지 거리·각도.</summary>
public struct InterruptCalibrationOwnerCapture
{
    public bool ViaDash;
    public float PressAge;
    public DashInterruptPredictInput Input;
    public DashInterruptPrediction Prediction;
    public float? TargetDistance;
    public float? TargetAngle;
}

/// <summary>서버 판정 캡처 한 건(할 일 139 단계 3) — 결과·판정 시각과 판정 시점 허수아비 창 설정·실제 상태.</summary>
public struct InterruptCalibrationJudgeCapture
{
    public bool ViaDash;
    public bool Succeeded;
    public float Time;      // 판정 시각(Time.time)
    public float RealTime;  // 판정 시각(Time.realtimeSinceStartup)

    public CalibrationWindowMode? WindowMode;
    public float? WindowOpenLength;
    public float? WindowClosedLength;
    public bool? ActualWindowOpen;
    public float? ActualOpenElapsed;
    public float? ActualRemaining;
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
    public bool Succeeded { get; }
    public float JudgeTime { get; }
    public float JudgeRealTime { get; }
}

/// <summary>
/// 간파 시도 한 번 = 표본 하나 짝짓기(할 일 139 단계 3). 순수 C# — 캡처 컴포넌트(<see cref="InterruptCalibrationCapture"/>)가 값을 넣는다.
///
/// 🔴 규칙(2026-10-29 정함):
/// <list type="number">
/// <item><b>시도 식별 = 대기 표본 하나.</b> 오너 요청에는 시전 번호가 없어(서버 슬로우 발동 번호는 서버 쪽에만 있다) 대기 중인 표본을 하나만 둔다.
///       오너 예측·서버 예측은 들어온 순서와 상관없이 그 표본에 붙는다(솔로 호스트는 오너 → 서버 순, 뒤바뀌어도 같다).</item>
/// <item><b>같은 쪽이 두 번 오면 새 시도.</b> 대기 표본에 이미 오너(또는 서버) 예측이 있는데 또 오면 앞 표본은 판정이 안 온 것으로 보고 버리고 새로 연다.</item>
/// <item><b>판정 = 완성.</b> 서버 판정이 오면 대기 표본을 닫아 <c>OnJudged</c> 가 완성 표본을 돌려준다. 빠진 쪽(오너·서버 예측) 칸은 빈 칸.
///       대기 표본 없이 온 판정(예측이 하나도 없었다)은 버린다.</item>
/// <item><b>판정 누락 = 버림.</b> 대기 표본이 연 뒤 <see cref="PendingTimeout"/>(게임 시간, 기본 3초)이 지나도 판정이 안 오면 버린다(판정 전 종료·넉백 등).
///       버린 수는 <see cref="DroppedCount"/>.</item>
/// <item><b>재예측(할 일 139-2)은 대기 표본에 덧붙는다.</b> 같은 쪽 예측이 이미 붙은 대기 표본의 재예측 여부·최종 예측만 바꾸고 새 표본을 열지 않는다(원래 예측 칸 그대로).</item>
/// <item><b>대쉬 간파만.</b> 근접 간파(viaDash = false)는 <see cref="IncludeMelee"/> 가 꺼져 있으면 셋 다 무시한다(대기 표본에 영향 없음).</item>
/// </list>
/// 시간은 호출측이 넘기는 게임 시간(Time.time) — 시도 슬로우·디버그 배속을 따라 늘어나므로 판정까지 걸리는 시간과 같은 축이다.
/// </summary>
public sealed class InterruptCalibrationPairing
{
    public const float DefaultPendingTimeout = 3f;

    InterruptCalibrationSample _pending;
    bool _hasOwner;
    bool _hasServer;
    float _openedAt;

    /// <summary>근접 간파도 기록한다(기본 꺼짐 — 이번 범위는 대쉬 간파만).</summary>
    public bool IncludeMelee { get; set; }

    /// <summary>판정 없이 기다리는 최대 시간(게임 초).</summary>
    public float PendingTimeout { get; set; } = DefaultPendingTimeout;

    /// <summary>짝을 못 지어 버린 수 — 판정 누락 대기 표본 + 대기 표본 없이 온 판정.</summary>
    public int DroppedCount { get; private set; }

    public bool HasPending => _pending != null;

    /// <summary>[오너] 입력 순간 예측.</summary>
    public void OnOwnerPredicted(in InterruptCalibrationOwnerCapture capture, float now)
    {
        if (!Accepts(capture.ViaDash))
            return;

        Tick(now);
        if (_pending == null || _hasOwner)
            Open(now);

        _pending.PressAge = capture.PressAge;
        _pending.SetOwnerPrediction(capture.Input, capture.Prediction);
        _pending.TargetDistance = capture.TargetDistance;
        _pending.TargetAngle = capture.TargetAngle;
        _hasOwner = true;
    }

    /// <summary>[서버] 승인 시점 예측(시도 슬로우 강·약, 대상 없음 = 슬로우 없음).</summary>
    public void OnServerPredicted(bool viaDash, InterruptAttemptForecast forecast, float now)
    {
        if (!Accepts(viaDash))
            return;

        Tick(now);
        if (_pending == null || _hasServer)
            Open(now);

        _pending.SetServerPrediction(forecast);
        _hasServer = true;
    }

    /// <summary>[오너] 대시 도중 재예측이 약 → 강으로 올렸다(할 일 139-2). 오너 예측이 붙은 대기 표본에만 — 원래 예측 칸은 그대로 둔다.</summary>
    public void OnOwnerRepredicted(bool viaDash, InterruptAttemptForecast forecast, float now)
    {
        if (!Accepts(viaDash))
            return;

        Tick(now);
        if (_pending == null || !_hasOwner)
            return;

        _pending.OwnerRepredicted = true;
        _pending.OwnerFinalForecast = forecast;
    }

    /// <summary>[서버] 대시 도중 재예측이 약 → 강 시도 슬로우로 다시 걸었다(할 일 139-2). 서버 예측이 붙은 대기 표본에만.</summary>
    public void OnServerRepredicted(bool viaDash, InterruptAttemptForecast forecast, float now)
    {
        if (!Accepts(viaDash))
            return;

        Tick(now);
        if (_pending == null || !_hasServer)
            return;

        _pending.ServerRepredicted = true;
        _pending.ServerFinalForecast = forecast;
    }

    /// <summary>[서버] 판정. 대기 표본이 있으면 닫아 완성 표본을 돌려준다.</summary>
    public bool OnJudged(in InterruptCalibrationJudgeCapture capture, out InterruptCalibrationCompleted completed)
    {
        completed = default;
        if (!Accepts(capture.ViaDash))
            return false;

        Tick(capture.Time);
        if (_pending == null)
        {
            DroppedCount++;
            return false;
        }

        InterruptCalibrationSample sample = _pending;
        _pending = null;

        sample.RealTime = capture.RealTime;
        sample.WindowMode = capture.WindowMode;
        sample.WindowOpenLength = capture.WindowOpenLength;
        sample.WindowClosedLength = capture.WindowClosedLength;
        sample.ServerSucceeded = capture.Succeeded;
        sample.ActualWindowOpen = capture.ActualWindowOpen;
        sample.ActualOpenElapsed = capture.ActualOpenElapsed;
        sample.ActualRemaining = capture.ActualRemaining;

        completed = new InterruptCalibrationCompleted(sample, capture.Succeeded, capture.Time, capture.RealTime);
        return true;
    }

    /// <summary>판정 누락 대기 표본을 버린다(매 프레임 불러도 된다).</summary>
    public void Tick(float now)
    {
        if (_pending != null && now - _openedAt > PendingTimeout)
            Drop();
    }

    /// <summary>대기 표본을 버린다(씬 나감·랩 끔). 버림 수에 넣는다.</summary>
    public void Clear()
    {
        if (_pending != null)
            Drop();
    }

    bool Accepts(bool viaDash) => viaDash || IncludeMelee;

    void Open(float now)
    {
        if (_pending != null)
            Drop();

        _pending = new InterruptCalibrationSample();
        _hasOwner = false;
        _hasServer = false;
        _openedAt = now;
    }

    void Drop()
    {
        _pending = null;
        DroppedCount++;
    }

    /// <summary>허수아비 창 모드 → 표본 열 값.</summary>
    public static CalibrationWindowMode ToCalibrationMode(DummyInterruptWindowMode mode) =>
        mode == DummyInterruptWindowMode.Periodic ? CalibrationWindowMode.Periodic : CalibrationWindowMode.Indefinite;

    /// <summary>음수(모름) → null.</summary>
    public static float? KnownOrNull(float value) => value >= 0f ? value : (float?)null;
}
#endif
