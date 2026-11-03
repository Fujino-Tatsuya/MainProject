using BeaverLobby.Player.Dash;
using UnityEngine;

// 할 일 196 단계 2 — 카메라 대상 중심 적용. 디렉터 쪽 196 변경은 이 파일에 모은다(본문·OwnerJudgeCues 는 호출 한 줄씩만).
// 상태는 디렉터가 들고(오너 cue 를 받는 곳), 카메라(CameraTargetSwitcher)가 오너 줌(A3)과 같은 방식으로 매 프레임 이동량을 묻는다.
public sealed partial class InterruptProductionDirector
{
    private readonly InterruptCameraCenterTracker _cameraCenter = new InterruptCameraCenterTracker();
    // 이번 시도의 간파 대상(예측이 고른 캐시 대상) — 카메라가 따라간다. 사라지면 복귀.
    private Transform _cameraCenterTarget;

    /// <summary>카메라 대상 중심 상태(할 일 196 — 확인·테스트용).</summary>
    public InterruptCameraCenterState CameraCenterState => _cameraCenter.State;

    /// <summary>
    /// [오너] 카메라 대상 중심 이동량(할 일 196) — 카메라가 매 프레임 묻고 플레이어 vcam 팔로우 오프셋에 더한다(월드, 수평).
    /// <paramref name="followPoint"/> = 평소 중심 = 카메라가 실제로 따라간 지점(<see cref="InterruptCameraCenterRules.FollowedPoint"/>, 196 후속),
    /// 추적 대상이 없으면 null. <paramref name="cameraBusy"/> = 다른 연출이 카메라를 잡고 있음
    /// (연출 vcam·낙하 뷰·관전·임시 추적) — 그러면 진행 중인 중심 이동을 즉시 버리고 0(그 연출 뒤엔 평소 중심 그대로).
    /// 인스턴스가 없으면 0.
    /// </summary>
    public static Vector3 OwnerCameraCenterShift(Vector3? followPoint, bool cameraBusy) =>
        Instance != null ? Instance.StepCameraCenter(followPoint, cameraBusy, Time.unscaledTime) : Vector3.zero;

    /// <summary>[테스트] 이 인스턴스 기준 <see cref="OwnerCameraCenterShift"/>.</summary>
    public Vector3 StepCameraCenter(Vector3? followPoint, bool cameraBusy, float now)
    {
        if (_cameraCenter.State == InterruptCameraCenterState.Idle)
            return Vector3.zero;

        if (cameraBusy || !followPoint.HasValue)
        {
            CancelCameraCenter();
            return Vector3.zero;
        }

        Transform target = _cameraCenterTarget;
        bool alive = target != null && target.gameObject.activeInHierarchy;
        return _cameraCenter.Step(now, followPoint.Value, alive, alive ? target.position : Vector3.zero);
    }

    // 시도 cue(근접 = 누른 순간, 대쉬 = 대시에 실린 순간) — 오너 cue 경로만 부르므로 늘 오너. 새 시도가 이전 상태를 대체한다.
    private void BeginCameraCenter(InterruptAttemptForecast forecast, GameObject target, float now)
    {
        InterruptSlowMotionSettings settings = SlowMotion;
        bool started = _cameraCenter.Begin(forecast, target != null, cameraCenter, true, settings.CameraCenterRatio,
            settings.CameraCenterMoveDuration, settings.CameraCenterReturnDuration, now);
        _cameraCenterTarget = started ? target.transform : null;
    }

    // 판정 성공 — 성공 슬로우 복귀 구간까지 유지. 내 앞 성공 슬로우가 아직 돌고 있으면(연속 간파) 그 번호는 이번 것이 아니다.
    private void CameraCenterSucceeded(float now)
    {
        _cameraCenter.Succeeded(now, InterruptCameraCenterRules.SuccessReturnDelay(InterruptSuccessProfile), MySuccessSlowTriggerId());
    }

    private void CameraCenterReturn(float now) => _cameraCenter.Return(now);

    private void CancelCameraCenter()
    {
        _cameraCenter.Cancel();
        _cameraCenterTarget = null;
    }

    // 매 프레임(디렉터 Update) — 성공 복귀를 기다리면 이 화면의 내 성공 슬로우 단계를 넘긴다.
    private void TickCameraCenter(float now)
    {
        if (!_cameraCenter.AwaitsSuccessReturn)
            return;

        uint triggerId = MySuccessSlowTriggerId();
        _cameraCenter.ObserveSuccessSlow(triggerId,
            triggerId != 0 ? InterruptCameraCenterRules.SuccessPhaseOf(_timeScale.CurrentPhase) : InterruptCameraCenterPhase.SuccessSlowExit,
            now);
    }

    // 이 화면에서 돌고 있는 내 성공 슬로우 번호(없으면 0).
    private uint MySuccessSlowTriggerId()
    {
        if (_timeScale == null || _networkManager == null || !_timeScale.IsActive ||
            _timeScale.CurrentPurpose != SlowMotionPurpose.Success || _timeScale.CurrentTriggerClientId != _networkManager.LocalClientId)
            return 0;

        return _timeScale.CurrentTriggerId;
    }
}
