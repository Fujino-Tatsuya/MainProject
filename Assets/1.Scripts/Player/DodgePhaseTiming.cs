using UnityEngine;

/// <summary>Dodge 클립 구간(할 일 92-5).</summary>
public enum DodgePhase
{
    Crouch,   // 웅크리기 0 ~ crouchEnd
    Airborne, // 공중 crouchEnd ~ airEnd
    Landing   // 착지 airEnd ~ end
}

/// <summary>
/// 대시 지속 안에 Dodge 클립 세 구간(웅크리기·공중·착지)을 모두 넣는 순수 규칙(할 일 92-5).
/// 공중 = 대시 지속 × airborneRatio, 나머지를 웅크리기:착지 = 프레임 길이 비로 나눈다.
/// 구간 재생 속도 = 그 구간 클립 길이(초) ÷ 배정 시간(초) — 대시 끝에 클립이 끝 프레임에 닿는다.
/// MonoBehaviour 아님 — EditMode 테스트 대상.
/// </summary>
public readonly struct DodgePhaseTiming
{
    public readonly float CrouchEndClipTime; // 초
    public readonly float AirEndClipTime;
    public readonly float EndClipTime;
    public readonly float CrouchSeconds;     // 대시 지속 중 배정 시간(초)
    public readonly float AirborneSeconds;
    public readonly float LandingSeconds;
    public readonly bool IsValid;

    public DodgePhaseTiming(int crouchEndFrame, int airEndFrame, int endFrame, float clipFps, float airborneRatio, float dashDuration)
    {
        IsValid = clipFps > 0f && dashDuration > 0f && endFrame > 0 &&
                  0 <= crouchEndFrame && crouchEndFrame <= airEndFrame && airEndFrame <= endFrame;
        if (!IsValid)
        {
            CrouchEndClipTime = AirEndClipTime = EndClipTime = 0f;
            CrouchSeconds = AirborneSeconds = LandingSeconds = 0f;
            return;
        }

        CrouchEndClipTime = crouchEndFrame / clipFps;
        AirEndClipTime = airEndFrame / clipFps;
        EndClipTime = endFrame / clipFps;

        int crouchFrames = crouchEndFrame;
        int landingFrames = endFrame - airEndFrame;
        int restFrames = crouchFrames + landingFrames;
        float ratio = restFrames > 0 ? Mathf.Clamp01(airborneRatio) : 1f; // 웅크리기·착지가 없으면 공중이 전부
        float rest = dashDuration * (1f - ratio);

        AirborneSeconds = dashDuration * ratio;
        CrouchSeconds = restFrames > 0 ? rest * crouchFrames / restFrames : 0f;
        LandingSeconds = restFrames > 0 ? rest * landingFrames / restFrames : 0f;
    }

    public float Duration => CrouchSeconds + AirborneSeconds + LandingSeconds;

    /// <summary>대시 경과(초)의 구간. 경계는 다음 구간으로 넘어간다(0 → 웅크리기, 지속 이상 → 착지).</summary>
    public DodgePhase PhaseAt(float elapsed)
    {
        if (elapsed < CrouchSeconds)
            return DodgePhase.Crouch;
        if (elapsed < CrouchSeconds + AirborneSeconds)
            return DodgePhase.Airborne;
        return DodgePhase.Landing;
    }

    /// <summary>구간 재생 속도 = 구간 클립 길이 ÷ 배정 시간. 배정 시간이 0 인 구간은 0(지나가지 않는다).</summary>
    public float SpeedOf(DodgePhase phase) => phase switch
    {
        DodgePhase.Crouch => Rate(CrouchEndClipTime, CrouchSeconds),
        DodgePhase.Airborne => Rate(AirEndClipTime - CrouchEndClipTime, AirborneSeconds),
        _ => Rate(EndClipTime - AirEndClipTime, LandingSeconds),
    };

    /// <summary>대시 경과(초)에 맞는 클립 시간(초). 0 미만은 0, 지속 이상은 끝 프레임에서 멈춘다.</summary>
    public float ClipTimeAt(float elapsed)
    {
        if (!IsValid || elapsed <= 0f)
            return 0f;

        float airStart = CrouchSeconds;
        float landStart = CrouchSeconds + AirborneSeconds;
        if (elapsed < airStart)
            return elapsed / CrouchSeconds * CrouchEndClipTime;
        if (elapsed < landStart)
            return CrouchEndClipTime + (elapsed - airStart) / AirborneSeconds * (AirEndClipTime - CrouchEndClipTime);
        if (elapsed < Duration && LandingSeconds > 0f)
            return AirEndClipTime + (elapsed - landStart) / LandingSeconds * (EndClipTime - AirEndClipTime);
        return EndClipTime;
    }

    /// <summary>대시 경과(초)에 맞는 클립 프레임.</summary>
    public float ClipFrameAt(float elapsed, float clipFps) => ClipTimeAt(elapsed) * clipFps;

    /// <summary>
    /// 한 프레임(경과 from → to) 동안 Animator 에 줄 재생 속도. 한 구간 안이면 그 구간 속도 그대로,
    /// 구간 경계를 걸치면 그 사이 클립 진행량 ÷ 프레임 시간(평균) — 구간이 1 프레임보다 짧아도 클립 위치가 어긋나지 않는다.
    /// 끝 프레임에 닿은 뒤는 0(끝 프레임에서 멈춤).
    /// </summary>
    public float FrameSpeed(float from, float to)
    {
        if (!IsValid || to <= from)
            return 0f;
        if (from >= Duration)
            return 0f;

        DodgePhase phase = PhaseAt(from);
        if (to <= Duration && PhaseAt(to) == phase)
            return SpeedOf(phase);

        return (ClipTimeAt(to) - ClipTimeAt(from)) / (to - from);
    }

    private static float Rate(float clipSeconds, float allottedSeconds) =>
        allottedSeconds > 0f ? clipSeconds / allottedSeconds : 0f;
}
