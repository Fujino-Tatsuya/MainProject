using UnityEngine;

// 할 일 196 단계 2 — 카메라 대상 중심 적용. 디렉터 쪽 196 변경은 이 파일에 모은다(본문·OwnerJudgeCues 는 호출 한 줄씩만).
// 상태는 디렉터가 들고, 카메라(CameraTargetSwitcher)가 오너 줌(A3)과 같은 방식으로 매 프레임 이동량을 묻는다.
// 11-05 — 제압 간파 성공 순간에만, 모든 화면(오너·팀원·호스트·클라). 오너 = 판정 순간(219 오너 제압 예측), 팀원·보정 = 서버 성공 사건 제압 표식(bit0).
// 새 RPC 없음. 각 화면은 자기 카메라만, 자기 플레이어가 따르는 지점 기준으로 옮긴다(같은 칸 값).
public sealed partial class InterruptProductionDirector
{
    private readonly InterruptCameraCenterTracker _cameraCenter = new InterruptCameraCenterTracker();
    // 이번 제압 간파 대상 — 카메라가 따라간다. 사라지면 복귀. 못 찾았으면 고정 점(적중 지점)을 본다.
    private Transform _cameraCenterTarget;
    private Vector3 _cameraCenterPoint;
    private bool _cameraCenterHasPoint;
    // 이번 중심 이동을 일으킨 발동자 — 그 사람의 성공 슬로우 복귀 구간에 맞춰 복귀한다.
    private ulong _cameraCenterCaster;

    /// <summary>카메라 대상 중심 상태(할 일 196 — 확인·테스트용).</summary>
    public InterruptCameraCenterState CameraCenterState => _cameraCenter.State;

    /// <summary>
    /// 이 화면 카메라 대상 중심 이동량(할 일 196, 11-05 부터 팀원 화면도) — 카메라가 매 프레임 묻고 플레이어 vcam 팔로우 오프셋에 더한다(월드, 수평).
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
        if (alive)
            return _cameraCenter.Step(now, followPoint.Value, true, target.position);
        // 대상을 못 찾아 고정 점으로 시작했으면 그 점, 대상이 있다가 사라졌으면 복귀.
        bool usePoint = target is null && _cameraCenterHasPoint;
        return _cameraCenter.Step(now, followPoint.Value, usePoint, usePoint ? _cameraCenterPoint : Vector3.zero);
    }

    // [오너] 판정 성공 cue — 판정 단계가 제압(219 오너 제압 예측)이면 지금 옮긴다. 대상이 없으면(랩 재현 등) 적중 지점.
    // 발동자 = 나. 내 앞 성공 슬로우가 아직 돌고 있으면(연속 간파) 그 번호는 이번 것이 아니다.
    private void CameraCenterOwnerJudged(InterruptPresentationTier tier, ulong viewer, GameObject target, Vector3 flashPosition, float now)
    {
        if (InterruptCameraCenterRules.AppliesOnSuccess(tier, cameraCenter))
            BeginSuccessCameraCenter(viewer, target, flashPosition, CasterSuccessSlowTriggerId(viewer), now);
    }

    /// <summary>
    /// [각 피어] 서버 성공 사건(HandleStarted, 테스트도 부른다) — 제압 표식이면 옮긴다. 오너가 판정 순간 이미 제압으로 옮겼으면(<paramref name="ownerAlreadyCentered"/>) 한 번만,
    /// 서버 단계가 다르면 보정(<see cref="InterruptCameraCenterRules.OnServerSuccess"/>). 그 성공 슬로우 Started 안에서 불려 지금 돌고 있는 슬로우가 이번 것이다(앞 번호 없음).
    /// </summary>
    public void CameraCenterServerSucceeded(InterruptPresentationTier serverTier, bool ownerAlreadyCentered, ulong casterClientId,
        Vector3 targetPosition, float now)
    {
        switch (InterruptCameraCenterRules.OnServerSuccess(serverTier, cameraCenter, ownerAlreadyCentered))
        {
            case InterruptCameraCenterSuccessAction.Begin:
                BeginSuccessCameraCenter(casterClientId, FindPeerSuccessTarget(casterClientId, targetPosition), targetPosition, 0, now);
                break;
            case InterruptCameraCenterSuccessAction.Return:
                CameraCenterReturn(now);
                break;
        }
    }

    private void BeginSuccessCameraCenter(ulong casterClientId, GameObject target, Vector3 fallbackPoint, uint staleSlowTriggerId, float now)
    {
        InterruptSlowMotionSettings settings = SlowMotion;
        bool started = _cameraCenter.BeginSuccess(true, settings.CameraCenterRatio, settings.CameraCenterMoveDuration,
            settings.CameraCenterReturnDuration, InterruptCameraCenterRules.SuccessReturnDelay(InterruptProductionRules.SelectSuccessProfile(true, this)),
            staleSlowTriggerId, now);
        _cameraCenterTarget = started && target != null ? target.transform : null;
        _cameraCenterPoint = fallbackPoint;
        _cameraCenterHasPoint = started && target == null;
        _cameraCenterCaster = casterClientId;
    }

    // 팀원 화면 — 슬로우 메시지는 대상을 싣지 않는다(143-3). 발동자 간파 스킬이 이 피어의 복제 위치로 대상을 다시 찾는다. 못 찾으면 null(고정 점).
    private GameObject FindPeerSuccessTarget(ulong casterClientId, Vector3 targetPosition)
    {
        PlayerInterruptSkillBase skill = FindInterruptSkill(casterClientId);
        ulong id = skill != null ? skill.FindPeerSuccessTargetId(targetPosition) : 0;
        return id != 0 ? FindSpawnedObject(id) : null;
    }

    private void CameraCenterReturn(float now) => _cameraCenter.Return(now);

    // [오너] 서버 거부 — 내 제압 간파로 옮긴 것만 복귀(팀원 제압으로 옮긴 중이면 그대로).
    private void CameraCenterOwnerRejected(ulong viewer, float now)
    {
        if (_cameraCenterCaster == viewer)
            CameraCenterReturn(now);
    }

    private void CancelCameraCenter()
    {
        _cameraCenter.Cancel();
        _cameraCenterTarget = null;
        _cameraCenterHasPoint = false;
    }

    // 매 프레임(디렉터 Update) — 성공 복귀를 기다리면 이 화면의 발동자 성공 슬로우 단계를 넘긴다.
    private void TickCameraCenter(float now)
    {
        if (!_cameraCenter.AwaitsSuccessReturn)
            return;

        uint triggerId = CasterSuccessSlowTriggerId(_cameraCenterCaster);
        _cameraCenter.ObserveSuccessSlow(triggerId,
            triggerId != 0 ? InterruptCameraCenterRules.SuccessPhaseOf(_timeScale.CurrentPhase) : InterruptCameraCenterPhase.SuccessSlowExit,
            now);
    }

    // 이 화면에서 돌고 있는 그 발동자의 성공 슬로우 번호(없으면 0).
    private uint CasterSuccessSlowTriggerId(ulong casterClientId)
    {
        if (_timeScale == null || !_timeScale.IsActive ||
            _timeScale.CurrentPurpose != SlowMotionPurpose.Success || _timeScale.CurrentTriggerClientId != casterClientId)
            return 0;

        return _timeScale.CurrentTriggerId;
    }
}
