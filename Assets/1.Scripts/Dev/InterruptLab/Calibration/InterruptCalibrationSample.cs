#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>간파 예측 보정 씬(할 일 139)의 허수아비 간파 창 모드.</summary>
public enum CalibrationWindowMode
{
    Indefinite, // 무기한 열림(현재 동작 — 증상 재현)
    Periodic,   // 열림·닫힘 길이를 번갈아
}

/// <summary>
/// 간파 예측 보정 표본 한 개(할 일 139 '기록' 절) — 대쉬 간파 한 번의 오너 예측·서버 예측·서버 판정·실제 창 상태·사용자 답.
/// MonoBehaviour 없는 순수 데이터다. 아직 캡처 안 된 값은 null 이고 CSV 에서 빈 칸이 된다.
/// <see cref="Seq"/>·<see cref="UserAnswer"/> 는 <see cref="InterruptCalibrationRecorder.Commit"/> 이 채운다 — 답을 건너뛴(Esc) 표본은 기록하지 않는다.
/// 시각은 같은 피어의 Time.time(초), <see cref="RealTime"/> 만 실시간(Time.realtimeSinceStartup).
/// </summary>
public sealed class InterruptCalibrationSample
{
    public int Seq;
    public float? RealTime;

    // 허수아비 창 설정
    public CalibrationWindowMode? WindowMode;
    public float? WindowOpenLength;
    public float? WindowClosedLength;

    /// <summary>대시 입력 → 우클릭 간격(초).</summary>
    public float? PressAge;

    /// <summary>오너 시전 순간 예측 입력 — 필드 그대로(음수 = 예측기 기준 '모름').</summary>
    public DashInterruptPredictInput? PredictInput;

    // 판정 위치·대상
    public Vector3? ResolvePosition;
    public float? TargetDistance;
    public float? TargetAngle;

    // 오너 예측 결과 — 누른 순간(원래 예측). 재예측(할 일 139-2)은 아래 두 칸에 따로 둔다.
    public float? PredictedResolveTime;
    public float? PredictedWindowRemaining; // 음수 = 예측기 기준 '모름'
    public InterruptAttemptForecast? OwnerForecast;
    public DashInterruptPredictReason? OwnerReason;
    /// <summary>대시 도중 재예측이 오너 예측을 약 → 강으로 올렸다.</summary>
    public bool? OwnerRepredicted;
    /// <summary>오너 최종 예측(재예측이 없으면 원래 예측과 같다) — 오너 연출이 마지막으로 따른 값.</summary>
    public InterruptAttemptForecast? OwnerFinalForecast;

    /// <summary>서버 시도 슬로우 강/약(승인 때). 대상 없음(슬로우 없음)은 NoTarget.</summary>
    public InterruptAttemptForecast? ServerForecast;
    /// <summary>대시 도중 서버 재예측이 약 → 강 시도 슬로우로 다시 걸었다.</summary>
    public bool? ServerRepredicted;
    /// <summary>서버 최종 시도 슬로우(재예측이 없으면 승인 때와 같다).</summary>
    public InterruptAttemptForecast? ServerFinalForecast;

    /// <summary>서버 판정 성공/실패.</summary>
    public bool? ServerSucceeded;

    // 판정 시점 실제 창 상태
    public bool? ActualWindowOpen;
    public float? ActualOpenElapsed;
    public float? ActualRemaining;

    /// <summary>사용자 답 — true = 성공이어야 했음, false = 실패여야 했음.</summary>
    public bool? UserAnswer;

    /// <summary>오너 예측 입력·결과를 한 번에 채운다. 판정 위치 = 예상 대시 끝.</summary>
    public void SetOwnerPrediction(in DashInterruptPredictInput input, in DashInterruptPrediction prediction)
    {
        PredictInput = input;
        ResolvePosition = prediction.PredictedEndPosition;
        PredictedResolveTime = prediction.PredictedResolveTime;
        PredictedWindowRemaining = prediction.WindowRemaining;
        OwnerForecast = prediction.Forecast;
        OwnerReason = prediction.Reason;
        OwnerRepredicted = false;
        OwnerFinalForecast = prediction.Forecast;
    }

    /// <summary>서버 승인 때 예측을 채운다 — 최종 = 승인 때, 재예측 없음.</summary>
    public void SetServerPrediction(InterruptAttemptForecast forecast)
    {
        ServerForecast = forecast;
        ServerRepredicted = false;
        ServerFinalForecast = forecast;
    }

    /// <summary>강 = true, 약 = false, 대상 없음·값 없음 = null(일치율 분모에서 뺀다).</summary>
    public static bool? ForecastAsBool(InterruptAttemptForecast? forecast) => forecast switch
    {
        InterruptAttemptForecast.Strong => true,
        InterruptAttemptForecast.Weak => false,
        _ => null,
    };
}
#endif
