#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using BeaverLobby.Player.Dash;

/// <summary>간파 예측 보정 씬(할 일 139)의 허수아비 간파 창 모드.</summary>
public enum CalibrationWindowMode
{
    Indefinite, // 무기한 열림
    Periodic,   // 열림·닫힘 길이를 번갈아
}

/// <summary>
/// 간파 보정 표본 한 개(할 일 139 → 191 5단계 '예측 vs 판정 vs 서버') — 간파 한 번의 QA 기록 한 줄(<see cref="InterruptQaRecord"/>:
/// 예측·오너 판정·서버 결과·어긋난 이유)에 판정 시점 허수아비 창 설정·실제 창 상태·대상 거리·각도·사람 답을 덧붙인 것.
/// MonoBehaviour 없는 순수 데이터다. 아직 캡처 안 된 값은 null 이고 CSV 에서 빈 칸이 된다.
/// <see cref="Seq"/>·<see cref="UserAnswer"/> 는 <see cref="InterruptCalibrationRecorder.Commit"/> 이 채운다 — 답을 건너뛴(Esc) 표본은 기록하지 않는다.
/// </summary>
public sealed class InterruptCalibrationSample
{
    public int Seq;
    /// <summary>오너 판정 실시간(Time.realtimeSinceStartup).</summary>
    public float? RealTime;

    // 허수아비 창 설정
    public CalibrationWindowMode? WindowMode;
    public float? WindowOpenLength;
    public float? WindowClosedLength;

    /// <summary>예측·판정·서버 결과·어긋난 이유(QA 기록 한 줄).</summary>
    public InterruptQaRecord Record;

    // 오너 판정 순간 판정 위치 → 대상
    public float? TargetDistance;
    public float? TargetAngle;

    // 판정 시점 실제 창 상태(허수아비 게이트)
    public bool? ActualWindowOpen;
    public float? ActualOpenElapsed;
    public float? ActualRemaining;

    /// <summary>사용자 답 — true = 성공이어야 했음, false = 실패여야 했음.</summary>
    public bool? UserAnswer;

    public InterruptAttemptForecast? Forecast => Record?.Forecast;

    /// <summary>오너 판정 성공 = true, 실패 = false, 대상 없음·판정 없음 = null.</summary>
    public bool? JudgedSuccess => Record == null ? null
        : Record.Judge == InterruptQaJudge.Success ? true
        : Record.Judge == InterruptQaJudge.Failure ? false
        : (bool?)null;

    /// <summary>서버 결과 — 승인 = true, 거부·신고 안 함 = false, 응답 없음·대기 = null.</summary>
    public bool? ServerSucceeded => Record == null ? null
        : Record.IsApproved ? true
        : Record.Server == InterruptQaServer.Rejected || Record.Server == InterruptQaServer.NotClaimed ? false
        : (bool?)null;

    /// <summary>강 = true, 약 = false, 대상 없음·값 없음 = null(일치율 분모에서 뺀다).</summary>
    public static bool? ForecastAsBool(InterruptAttemptForecast? forecast) => forecast switch
    {
        InterruptAttemptForecast.Strong => true,
        InterruptAttemptForecast.Weak => false,
        _ => null,
    };
}
#endif
