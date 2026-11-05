using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>카메라 대상 중심(할 일 196)이 보는 간파 단계 — 클릭부터 결론·성공 슬로우 복귀까지.</summary>
public enum InterruptCameraCenterPhase : byte
{
    /// <summary>간파 클릭 순간(대쉬 간파 = 대시에 실린 순간)부터 결론 전까지.</summary>
    Click = 0,
    /// <summary>오너 판정 실패(191 — 그 순간 연출 종료).</summary>
    JudgeFailed = 1,
    /// <summary>서버 거부(서버 2차 검증 실패·선착순 밀림·종료).</summary>
    ServerRejected = 2,
    /// <summary>성공 슬로우 진행 중 — 히트스톱·진입·유지 단계.</summary>
    SuccessSlow = 3,
    /// <summary>성공 슬로우 복귀 구간(유지 끝 이후).</summary>
    SuccessSlowExit = 4,
}

/// <summary>카메라 대상 중심의 복귀 시점.</summary>
public enum InterruptCameraReturnTiming : byte
{
    /// <summary>옮기지 않는다 — 복귀할 것도 없다(대상 없음·스위치 꺼짐·오너 아님).</summary>
    None = 0,
    /// <summary>대상 중심을 유지한다 — 아직 복귀하지 않는다.</summary>
    Hold = 1,
    /// <summary>지금 복귀를 시작한다(평소 중심으로, 복귀 시간 칸만큼).</summary>
    Now = 2,
}

/// <summary>카메라 대상 중심 판단 결과 — 목표 중심과 복귀 시점.</summary>
public readonly struct InterruptCameraCenterDecision
{
    /// <summary>목표 중심 — 대상 쪽으로 옮기면 평소 중심과 대상 위치 사이 비율 보간, 아니면 평소 중심 그대로.</summary>
    public readonly Vector3 Center;
    public readonly InterruptCameraReturnTiming Return;

    public InterruptCameraCenterDecision(Vector3 center, InterruptCameraReturnTiming returnTiming)
    {
        Center = center;
        Return = returnTiming;
    }

    /// <summary>대상 쪽으로 옮긴 상태를 유지하는가.</summary>
    public bool Centers => Return == InterruptCameraReturnTiming.Hold;
}

/// <summary>
/// 간파 클릭 즉시 카메라를 간파 대상 중심으로(할 일 196) — 판단만 모은 순수 규칙. 상태가 없고 MonoBehaviour 와 분리(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>강·약 예측 모두 대상 중심(Q1). 대상 없음·스위치 꺼짐·오너 아님 = 그대로(Q4).</item>
/// <item>판정 실패·서버 거부 = 그 순간 복귀 시작. 성공 = 성공 슬로우 복귀 구간에 맞춰 복귀(Q3).</item>
/// <item>줌은 따로(A3 오너 줌) — 여기는 중심만(Q5). 시간 칸은 실제 시간(<see cref="InterruptSlowMotionSettings"/>).</item>
/// </list>
/// 문서: Docs/tech/interrupt-presentation.md(단계 4에서 카메라 절 추가 예정).
/// </summary>
/// <summary>서버 성공 사건을 받은 화면의 카메라 대상 중심 처리(11-05 제압 간파 전용).</summary>
public enum InterruptCameraCenterSuccessAction : byte
{
    /// <summary>그대로 — 일반·대쉬 성공, 또는 오너가 판정 순간 이미 제압으로 옮겼다(중복 1회).</summary>
    None = 0,
    /// <summary>지금 대상 쪽으로 옮긴다 — 팀원 화면, 또는 오너 예측이 제압을 놓쳤다(서버 보정).</summary>
    Begin = 1,
    /// <summary>지금 복귀 — 오너가 제압으로 옮겼는데 서버 단계는 제압이 아니다(서버 보정).</summary>
    Return = 2,
}

public static class InterruptCameraCenterRules
{
    /// <summary>이 성공 단계에 대상 중심을 적용하는가(11-05) — 스위치 켬 ∧ 제압 간파. 일반·대쉬는 안 움직인다.</summary>
    public static bool AppliesOnSuccess(InterruptPresentationTier tier, bool switchOn) =>
        switchOn && tier == InterruptPresentationTier.Suppress;

    /// <summary>
    /// 서버 성공 사건(전 피어, 제압 표식 bit0) 처리(11-05). <paramref name="ownerAlreadyCentered"/> = 이 화면이 발동자이고 판정 순간 제압으로 이미 옮겼다
    /// (오너 판정 cue 가 이 성공을 처리 ∧ 판정 단계 제압). 그러면 서버 단계가 제압이면 그대로(한 번만), 아니면 복귀. 아니면 제압일 때만 옮긴다.
    /// </summary>
    public static InterruptCameraCenterSuccessAction OnServerSuccess(InterruptPresentationTier serverTier, bool switchOn, bool ownerAlreadyCentered)
    {
        if (ownerAlreadyCentered)
            return serverTier == InterruptPresentationTier.Suppress ? InterruptCameraCenterSuccessAction.None : InterruptCameraCenterSuccessAction.Return;
        return AppliesOnSuccess(serverTier, switchOn) ? InterruptCameraCenterSuccessAction.Begin : InterruptCameraCenterSuccessAction.None;
    }

    /// <summary>
    /// 이 단계의 목표 중심·복귀 시점. <paramref name="baseCenter"/> = 평소 중심(옮기기 전 카메라가 따르는 지점),
    /// <paramref name="targetPosition"/> = 예측이 고른 간파 대상 위치, <paramref name="ratio"/> = 대상 쪽 이동 비율([0, 1]로 자른다 — 1 = 대상이 정중앙).
    /// </summary>
    public static InterruptCameraCenterDecision Decide(InterruptAttemptForecast forecast, InterruptCameraCenterPhase phase,
        bool switchOn, float ratio, bool isOwner, Vector3 baseCenter, Vector3 targetPosition)
    {
        if (!Applies(forecast, switchOn, isOwner))
            return new InterruptCameraCenterDecision(baseCenter, InterruptCameraReturnTiming.None);

        switch (phase)
        {
            case InterruptCameraCenterPhase.Click:
            case InterruptCameraCenterPhase.SuccessSlow:
                return new InterruptCameraCenterDecision(CenterToward(baseCenter, targetPosition, ratio), InterruptCameraReturnTiming.Hold);
            default:
                return new InterruptCameraCenterDecision(baseCenter, InterruptCameraReturnTiming.Now);
        }
    }

    /// <summary>이 시도에 대상 중심을 적용하는가 — 스위치 켬·오너·대상 있음(강·약).</summary>
    public static bool Applies(InterruptAttemptForecast forecast, bool switchOn, bool isOwner) =>
        switchOn && isOwner && (forecast == InterruptAttemptForecast.Strong || forecast == InterruptAttemptForecast.Weak);

    /// <summary>
    /// 평소 중심 = 카메라가 실제로 따라간 지점(196 후속) — 플레이어 실제 위치가 아니다. 팔로우 감쇠(PositionDamping)가 있어 이동 중·대쉬·
    /// 슬로우(감쇠도 느려짐) 때 카메라가 플레이어보다 뒤처지므로, 플레이어 기준으로 옮기면 뒤처짐만큼 대상에서 빗나간다.
    /// 따라간 지점 = vcam 위치(State.RawPosition) − 지금 적용 중인 팔로우 오프셋(원래 오프셋 × 줌 배율 + 중심 이동량, World Space 바인딩).
    /// </summary>
    public static Vector3 FollowedPoint(Vector3 cameraRawPosition, Vector3 baseFollowOffset, float appliedZoomMultiplier, Vector3 appliedCenterShift) =>
        cameraRawPosition - (baseFollowOffset * appliedZoomMultiplier + appliedCenterShift);

    /// <summary>평소 중심과 대상 위치 사이 비율 보간. 비율은 [0, 1]로 자르고 NaN 은 0(그대로).</summary>
    public static Vector3 CenterToward(Vector3 baseCenter, Vector3 targetPosition, float ratio) =>
        Vector3.Lerp(baseCenter, targetPosition, ClampRatio(ratio));

    /// <summary>
    /// 성공 슬로우 시작부터 복귀 시작까지(실제 시간 초) = 히트스톱 + 진입 + 유지 — 슬로우 복귀 구간 시작과 같은 순간.
    /// 프로필이 없으면 0(바로 복귀).
    /// </summary>
    public static float SuccessReturnDelay(SlowMotionProfile successProfile) =>
        successProfile == null
            ? 0f
            : successProfile.ClampedHitStopDuration + successProfile.ClampedEnterDuration + successProfile.ClampedHoldDuration;

    /// <summary>성공 슬로우 시작 뒤 <paramref name="elapsed"/>(실제 시간 초) 시점의 단계 — 복귀 시작 시점 이상이면 복귀 구간.</summary>
    public static InterruptCameraCenterPhase SuccessPhaseAt(float elapsed, SlowMotionProfile successProfile) =>
        elapsed >= SuccessReturnDelay(successProfile) ? InterruptCameraCenterPhase.SuccessSlowExit : InterruptCameraCenterPhase.SuccessSlow;

    /// <summary>
    /// 이 화면에서 돌고 있는 성공 슬로우 단계 → 카메라 단계(단계 2) — 히트스톱·진입·유지 = 유지, 복귀(실패 복귀 포함)·끝남 = 복귀 구간.
    /// 서버가 실제로 건 프로필(히트스톱 칸 A1 반영)을 따르므로 시간표(<see cref="SuccessPhaseAt"/>)보다 이쪽을 먼저 쓴다.
    /// </summary>
    public static InterruptCameraCenterPhase SuccessPhaseOf(SlowMotionPhase slowPhase) =>
        slowPhase == SlowMotionPhase.HitStop || slowPhase == SlowMotionPhase.Enter || slowPhase == SlowMotionPhase.Hold
            ? InterruptCameraCenterPhase.SuccessSlow
            : InterruptCameraCenterPhase.SuccessSlowExit;

    private static float ClampRatio(float ratio) => float.IsNaN(ratio) ? 0f : Mathf.Clamp01(ratio);
}
