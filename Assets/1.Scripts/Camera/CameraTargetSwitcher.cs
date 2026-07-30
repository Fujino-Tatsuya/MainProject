using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using VeyTrace.RuntimeSafety;

// MapScene camera manager. It creates one render camera and two identical
// Cinemachine cameras, then switches their priorities for normal/fall views.
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
    [SerializeField, Min(0f)] private float toFloatBlendDuration = 0.2f;
    [SerializeField, Min(0f)] private float toFollowBlendDuration = 0.35f;

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
