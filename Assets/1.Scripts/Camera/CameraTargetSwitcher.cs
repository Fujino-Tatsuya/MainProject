using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using VeyTrace.RuntimeSafety;

// MapScene camera manager. It creates one render camera and two identical
// Cinemachine cameras, then switches their priorities for normal/fall views.
[DataTableSheet("SlowMotion", Order = 1)]
public class CameraTargetSwitcher : MonoBehaviour
{
    public static CameraTargetSwitcher Active { get; private set; }

    // 현재 카메라가 따라가는 대상(=플레이어). 없으면 null.
    // FogManager 의 층 디밍이 플레이어 y 기준선을 잡는 데 사용한다.
    public Transform CurrentFollowTarget => GetCurrentTarget();
    public Camera GameplayCamera => gameplayCamera;

    private const string CameraFollowTargetTag = "CameraFollowTarget";
    private const int ActiveCameraPriority = 100;
    private const int InactiveCameraPriority = 0;

    [SerializeField] private GameObject mainCameraPrefab;
    [SerializeField] private GameObject followCameraPrefab;
    // 블렌드 길이는 카메라 연출 정렬 값이라 테이블에 내지 않는다 — 이 컴포넌트가 내는 건 슬로우 줌 배율뿐.
    [DataTableIgnore]
    [SerializeField, Min(0f)] private float toFloatBlendDuration = 0.2f;
    [DataTableIgnore]
    [SerializeField, Min(0f)] private float toFollowBlendDuration = 0.35f;
    [Tooltip("내가 일으킨 인터럽트 슬로우의 유지 단계에서 플레이어 카메라 거리 배율. [0.1, 1] — 1 이면 줌 없음 (PLAN-interrupt-slowmo D10)")]
    [SerializeField, Range(SlowMotionCameraZoom.MinZoomFactor, 1f)] private float slowMotionZoomFactor = 0.85f;

    // 생성한 리그 인스턴스를 들고 있어야 EnsureCameraRig가 멱등해진다(중복 생성 방지).
    private GameObject mainCameraInstance;
    private GameObject followCameraInstance;
    private GameObject floatCameraInstance;
    private Camera gameplayCamera;
    private CinemachineBrain cameraBrain;
    private CinemachineCamera playerCamera;       // 리그 안의 vcam (런타임에 채워짐)
    private CinemachineCamera floatCamera;
    private FloatFollowTarget floatFollowTarget;
    private readonly List<Transform> cameraFollowTargets = new();
    private int currentTargetIndex = -1;
    private CinemachineFollow playerCameraFollow;
    // 줌은 원래 오프셋에 배율을 곱해서 쓴다 — 매 프레임 현재 값에 곱하면 오차가 쌓인다.
    private Vector3 basePlayerFollowOffset;
    private float slowMotionZoomDepth;
    private float appliedZoomMultiplier = 1f;
    private Quaternion fixedCameraRotation;
    private bool hasFixedCameraRotation;
    private PlayerLifeCycleController ownerLifeCycle;

    public bool IsInFallView { get; private set; }
    public bool IsSpectatorMode { get; private set; }

    private void Awake()
    {
        Active = this;
    }

    private void OnDestroy()
    {
        UnbindOwnerLifeCycle();

        if (Active == this)
        {
            Active = null;
        }
    }

    // Called when the owner player spawns.
    public void FocusOwnerPlayer()
    {
        EnsureCameraRig();
        SelectOwnerPlayerTarget();
        BindOwnerLifeCycleFromCurrentTarget();
        RuntimeSceneServiceCoordinator.Reconcile();
    }

    // Freezes the proxy at the selected target's current world Y and starts
    // following only that target's X/Z coordinates.
    public void EnterFallView()
    {
        Transform target = GetCurrentTarget();
        if (target != null)
        {
            EnterFallView(target.position.y);
        }
    }

    // Allows the life/fall system to supply its last known safe target height.
    public void EnterFallView(float fixedWorldY)
    {
        EnsureCameraRig();

        Transform target = GetCurrentTarget();
        if (target == null || floatCamera == null || floatFollowTarget == null)
        {
            return;
        }

        floatFollowTarget.SetSource(target);
        floatFollowTarget.SetFixedWorldY(fixedWorldY);
        ApplyBlendDuration(toFloatBlendDuration);

        playerCamera.Priority = InactiveCameraPriority;
        floatCamera.Priority = ActiveCameraPriority;
        IsInFallView = true;
    }

    public void ReturnToPlayerView()
    {
        if (playerCamera == null || floatCamera == null)
        {
            return;
        }

        ApplyBlendDuration(toFollowBlendDuration);
        floatCamera.Priority = InactiveCameraPriority;
        playerCamera.Priority = ActiveCameraPriority;
        IsInFallView = false;
    }

    private void EnsureCameraRig()
    {
        if (playerCamera != null && floatCamera != null && gameplayCamera != null)
        {
            return;
        }

        if (mainCameraPrefab == null || followCameraPrefab == null)
        {
            Edit.LogWarning("[Camera] Camera rig prefabs are not assigned (mainCamera/follow).");
            return;
        }

        // 부모를 this.transform으로 주면 MapScene 소속으로 생성되어 소스/로딩 씬 언로드에
        // 휩쓸리지 않는다. 생성한 인스턴스에서 직접 찾고 Camera.main은 사용하지 않는다.
        // 인스턴스를 필드로 들고 재사용해야 재호출 시 리그가 중복 생성되지 않는다.
        if (mainCameraInstance == null)
            mainCameraInstance = Instantiate(mainCameraPrefab, transform);
        if (followCameraInstance == null)
        {
            followCameraInstance = Instantiate(followCameraPrefab, transform);
            followCameraInstance.name = "PlayerFollowCamera";
        }
        if (floatCameraInstance == null)
        {
            floatCameraInstance = Instantiate(followCameraPrefab, transform);
            floatCameraInstance.name = "PlayerFollowFloatCamera";
        }

        gameplayCamera =
            mainCameraInstance.GetComponentInChildren<Camera>(true);
        if (gameplayCamera == null)
        {
            Edit.LogWarning(
                $"[Camera] Main camera prefab has no {nameof(Camera)}. " +
                $"prefab={mainCameraPrefab.name}");
        }

        cameraBrain = mainCameraInstance.GetComponentInChildren<CinemachineBrain>(true);

        playerCamera = followCameraInstance.GetComponentInChildren<CinemachineCamera>(true);
        floatCamera = floatCameraInstance.GetComponentInChildren<CinemachineCamera>(true);

        if (playerCamera == null || floatCamera == null)
        {
            Edit.LogWarning(
                $"[Camera] Follow camera prefab has no {nameof(CinemachineCamera)}. prefab={followCameraPrefab.name}");
            return;
        }

        if (cameraBrain == null)
        {
            Edit.LogWarning(
                $"[Camera] Main camera prefab has no {nameof(CinemachineBrain)}. prefab={mainCameraPrefab.name}");
        }

        // 리그 생성이 멱등해야 하므로 프록시도 한 번만 만든다.
        if (floatFollowTarget == null)
        {
            GameObject proxyObject = new("FloatFollowTarget");
            proxyObject.transform.SetParent(transform, false);
            floatFollowTarget = proxyObject.AddComponent<FloatFollowTarget>();
        }
        floatCamera.Follow = floatFollowTarget.transform;

        playerCamera.Priority = ActiveCameraPriority;
        floatCamera.Priority = InactiveCameraPriority;

        CacheFixedCameraRotation();
        ApplyFixedCameraRotation(floatCamera);
        ClearLookAtTarget(playerCamera);
        ClearLookAtTarget(floatCamera);
    }

    private void SelectOwnerPlayerTarget()
    {
        RefreshFollowTargets();

        for (int i = 0; i < cameraFollowTargets.Count; i++)
        {
            NetworkObject networkObject = cameraFollowTargets[i].GetComponentInParent<NetworkObject>();
            if (networkObject != null && networkObject.IsOwner)
            {
                SetTarget(i);
                return;
            }
        }
    }

    private void Update()
    {
        UpdateSlowMotionZoom();

        if (!IsSpectatorMode)
        {
            return;
        }

        EnsureValidSpectatorTarget();

        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
        {
            SwitchToPreviousTarget();
        }

        if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
        {
            SwitchToNextTarget();
        }
    }

    // 오너 슬로우 줌인(D10). GlobalTimeScale(-100)이 이번 프레임 배율을 정한 뒤, Cinemachine(LateUpdate)보다 먼저 돈다.
    private void UpdateSlowMotionZoom()
    {
        if (playerCameraFollow == null)
        {
            return;
        }

        GlobalTimeScale timeScale = GlobalTimeScale.Instance;
        NetworkManager networkManager = NetworkManager.Singleton;
        bool sessionRunning = timeScale != null && networkManager != null && networkManager.IsListening;
        bool slowActive = sessionRunning && timeScale.IsActive;

        float targetDepth = SlowMotionCameraZoom.TargetDepth(
            slowActive,
            slowActive && timeScale.CurrentTriggerClientId == networkManager.LocalClientId,
            sessionRunning && !IsInFallView && !IsSpectatorMode,
            slowActive ? timeScale.CurrentScale : 1f,
            slowActive ? timeScale.InterruptProfile.ClampedScale : 1f);

        // 감쇠는 실시간 — 슬로우 중에도 발동자 전환 줌아웃이 같은 속도로 끝난다.
        slowMotionZoomDepth = SlowMotionCameraZoom.StepDepth(slowMotionZoomDepth, targetDepth, Time.unscaledDeltaTime);
        float multiplier = SlowMotionCameraZoom.OffsetMultiplier(slowMotionZoomDepth, slowMotionZoomFactor);

        // 정확 비교 — 근사 비교면 복귀 끝의 1 직전 값에서 멈춰 원래 오프셋으로 돌아오지 않는다.
        if (multiplier == appliedZoomMultiplier)
        {
            return;
        }

        // 원래 오프셋에서 다시 계산한다 — 복귀가 끝나면(배율 1) 정확히 원래 값으로 돌아온다.
        playerCameraFollow.FollowOffset = basePlayerFollowOffset * multiplier;
        appliedZoomMultiplier = multiplier;
    }

    public void SwitchToNextTarget()
    {
        SwitchTarget(1);
    }

    public void SwitchToPreviousTarget()
    {
        SwitchTarget(-1);
    }

    /// <summary>
    /// 로컬 Player가 PermanentDead일 때만 호출되는 관전 진입점.
    /// 서버 상태를 변경하지 않고 이 클라이언트의 Camera Follow 대상만 전환한다.
    /// </summary>
    public void SetSpectatorMode(bool enabled)
    {
        if (IsSpectatorMode == enabled)
        {
            return;
        }

        IsSpectatorMode = enabled;
        EnsureCameraRig();
        Transform lastFollowTarget = GetCurrentTarget();

        if (!enabled)
        {
            SelectOwnerPlayerTarget();
            ReturnToPlayerView();
            return;
        }

        RefreshFollowTargets();
        if (cameraFollowTargets.Count == 0)
        {
            FreezeAtLastFollowPosition(lastFollowTarget);
            return;
        }

        // PermanentDead인 기존 오너는 후보 필터에서 제거되므로 첫 유효 대상을 자동 선택한다.
        SwitchTarget(1);
        ReturnToPlayerView();
    }

    private void SwitchTarget(int direction)
    {
        if (playerCamera == null)
        {
            return;
        }

        RefreshFollowTargets();

        if (cameraFollowTargets.Count == 0)
        {
            currentTargetIndex = -1;
            return;
        }

        int nextTargetIndex = currentTargetIndex < 0
            ? GetInitialTargetIndex(direction)
            : (currentTargetIndex + direction + cameraFollowTargets.Count) % cameraFollowTargets.Count;

        SetTarget(nextTargetIndex);
    }

    private int GetInitialTargetIndex(int direction)
    {
        return direction < 0 ? cameraFollowTargets.Count - 1 : 0;
    }

    private void RefreshFollowTargets()
    {
        Transform currentTarget = GetCurrentTarget();
        RemoveInvalidTargets();

        GameObject[] followTargetObjects = GameObject.FindGameObjectsWithTag(CameraFollowTargetTag);
        foreach (GameObject followTargetObject in followTargetObjects)
        {
            Transform followTarget = followTargetObject.transform;
            if (IsValidFollowTarget(followTarget) &&
                !cameraFollowTargets.Contains(followTarget))
            {
                cameraFollowTargets.Add(followTarget);
            }
        }

        if (currentTarget != null)
        {
            currentTargetIndex = cameraFollowTargets.IndexOf(currentTarget);
        }
    }

    private void SetTarget(int targetIndex)
    {
        if (playerCamera == null || targetIndex < 0 || targetIndex >= cameraFollowTargets.Count)
        {
            return;
        }

        currentTargetIndex = targetIndex;
        Transform target = cameraFollowTargets[currentTargetIndex];
        playerCamera.Follow = target;
        floatFollowTarget?.SetSource(target);
        ClearLookAtTarget(playerCamera);
        RestoreFixedCameraRotation();
    }

    private void RemoveInvalidTargets()
    {
        for (int i = cameraFollowTargets.Count - 1; i >= 0; i--)
        {
            if (!IsValidFollowTarget(cameraFollowTargets[i]))
            {
                cameraFollowTargets.RemoveAt(i);
            }
        }

        if (currentTargetIndex >= cameraFollowTargets.Count)
        {
            currentTargetIndex = cameraFollowTargets.Count - 1;
        }
    }

    private bool IsValidFollowTarget(Transform followTarget)
    {
        if (followTarget == null || !followTarget.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (!IsSpectatorMode)
        {
            return true;
        }

        PlayerLifeCycleController lifeCycle =
            followTarget.GetComponentInParent<PlayerLifeCycleController>();
        return lifeCycle != null && IsSpectatorCandidate(lifeCycle.State);
    }

    private void BindOwnerLifeCycleFromCurrentTarget()
    {
        Transform target = GetCurrentTarget();
        BindOwnerLifeCycle(
            target != null
                ? target.GetComponentInParent<PlayerLifeCycleController>()
                : null);
    }

    private void BindOwnerLifeCycle(PlayerLifeCycleController lifeCycle)
    {
        if (ownerLifeCycle == lifeCycle)
        {
            return;
        }

        UnbindOwnerLifeCycle();
        ownerLifeCycle = lifeCycle;

        if (ownerLifeCycle != null)
        {
            ownerLifeCycle.LifeStateChanged += HandleOwnerLifeStateChanged;
            SetSpectatorMode(ownerLifeCycle.State == PlayerLifeState.PermanentDead);
        }
        else
        {
            SetSpectatorMode(false);
        }
    }

    private void UnbindOwnerLifeCycle()
    {
        if (ownerLifeCycle == null)
            return;

        ownerLifeCycle.LifeStateChanged -= HandleOwnerLifeStateChanged;
        ownerLifeCycle = null;
    }

    private static bool IsSpectatorCandidate(PlayerLifeState state)
    {
        return state == PlayerLifeState.Alive ||
            state == PlayerLifeState.Soul;
    }

    private void HandleOwnerLifeStateChanged(
        PlayerLifeState previousState,
        PlayerLifeState currentState)
    {
        SetSpectatorMode(currentState == PlayerLifeState.PermanentDead);
    }

    private void EnsureValidSpectatorTarget()
    {
        Transform lastFollowTarget = GetCurrentTarget();
        if (IsValidFollowTarget(lastFollowTarget))
        {
            return;
        }

        RefreshFollowTargets();
        if (cameraFollowTargets.Count == 0)
        {
            FreezeAtLastFollowPosition(lastFollowTarget);
            return;
        }

        SetTarget(0);
        ReturnToPlayerView();
    }

    private void FreezeAtLastFollowPosition(Transform lastFollowTarget)
    {
        if (playerCamera == null || floatCamera == null || floatFollowTarget == null)
        {
            return;
        }

        // 추락 사망으로 이미 Float View라면 proxy의 마지막 안전 높이/위치를 그대로 보존한다.
        if (!IsInFallView && lastFollowTarget != null)
        {
            floatFollowTarget.transform.position = lastFollowTarget.position;
        }

        floatFollowTarget.SetSource(null);
        playerCamera.Priority = InactiveCameraPriority;
        floatCamera.Priority = ActiveCameraPriority;
        IsInFallView = true;
    }

    private Transform GetCurrentTarget()
    {
        if (currentTargetIndex < 0 || currentTargetIndex >= cameraFollowTargets.Count)
        {
            return null;
        }

        return cameraFollowTargets[currentTargetIndex];
    }

    private void CacheFixedCameraRotation()
    {
        if (hasFixedCameraRotation || playerCamera == null)
        {
            return;
        }

        playerCameraFollow = playerCamera.GetComponent<CinemachineFollow>();
        if (playerCameraFollow != null)
        {
            basePlayerFollowOffset = playerCameraFollow.FollowOffset;
        }

        if (playerCameraFollow != null && playerCameraFollow.FollowOffset.sqrMagnitude > Mathf.Epsilon)
        {
            fixedCameraRotation = Quaternion.LookRotation(-playerCameraFollow.FollowOffset.normalized, Vector3.up);
        }
        else
        {
            fixedCameraRotation = playerCamera.transform.rotation;
        }

        hasFixedCameraRotation = true;
        RestoreFixedCameraRotation();
    }

    private void RestoreFixedCameraRotation()
    {
        ApplyFixedCameraRotation(playerCamera);
        ApplyFixedCameraRotation(floatCamera);
    }

    private void ApplyFixedCameraRotation(CinemachineCamera camera)
    {
        if (!hasFixedCameraRotation || camera == null)
        {
            return;
        }

        camera.transform.rotation = fixedCameraRotation;
    }

    private void ApplyBlendDuration(float duration)
    {
        if (cameraBrain == null)
        {
            return;
        }

        CinemachineBlendDefinition blend = cameraBrain.DefaultBlend;
        if (blend.Style == CinemachineBlendDefinition.Styles.Cut)
        {
            blend.Style = CinemachineBlendDefinition.Styles.EaseInOut;
        }

        blend.Time = Mathf.Max(0f, duration);
        cameraBrain.DefaultBlend = blend;
    }

    private static void ClearLookAtTarget(CinemachineCamera camera)
    {
        if (camera == null)
        {
            return;
        }

        camera.Target.CustomLookAtTarget = true;
        camera.Target.LookAtTarget = null;
    }
}
