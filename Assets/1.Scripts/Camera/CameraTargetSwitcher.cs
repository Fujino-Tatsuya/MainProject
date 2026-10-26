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
    // 연출용 임시 추적 대상(보스 엘리베이터 칸 안 카메라 지점). 있는 동안 플레이어 vcam 의 Follow 를 이것이 쥔다.
    private Transform temporaryFollow;

    public bool IsInFallView { get; private set; }
    public bool IsSpectatorMode { get; private set; }
    public bool HasTemporaryFollow => temporaryFollow != null;

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

    // ── 연출 카메라(보스 등장 시네마틱 — PLAN-boss-encounter-cutscene) ───────────────
    // 연출 쪽이 만든 vcam 의 Priority 를 플레이어·낙하 vcam(100) 위로 올렸다 내린다 — 브레인이 블렌드한다.
    // 이 컴포넌트가 전환마다 DefaultBlend 시간을 덮어쓰므로(ApplyBlendDuration) 진입·복귀 블렌드 시간은 여기서 넣는다.
    private const int CinematicCameraPriority = 200;
    private CinemachineCamera cinematicCamera;

    /// <summary>연출 카메라 중인가(로컬).</summary>
    public bool IsInCinematic => cinematicCamera != null;

    /// <summary>
    /// 게임플레이 카메라의 <b>고정</b> 회전(팔로우 오프셋 기준, 요 −45°·피치 ≈60°). 렌더 카메라 transform 은 연출·낙하 vcam 이 움직이므로
    /// "화면 방향"이 필요한 연출은 이 값을 쓴다(보스 등장 시네마틱 — 연출 카메라가 자기 자신을 기준으로 삼아 요가 틀어지던 문제, 경석 10-23 Play).
    /// 리그가 없으면 false.
    /// </summary>
    public bool TryGetGameplayViewRotation(out Quaternion rotation)
    {
        EnsureCameraRig();
        CacheFixedCameraRotation();
        rotation = fixedCameraRotation;
        return hasFixedCameraRotation;
    }

    /// <summary>연출 vcam 으로 전환한다. <paramref name="blendSeconds"/> 0 = 컷(암전 직후 등).</summary>
    public void EnterCinematic(CinemachineCamera camera, float blendSeconds)
    {
        if (camera == null)
        {
            return;
        }

        EnsureCameraRig();
        ApplyBlendDuration(blendSeconds);
        if (cinematicCamera != null && cinematicCamera != camera)
        {
            cinematicCamera.Priority = InactiveCameraPriority;
        }

        cinematicCamera = camera;
        camera.Priority = CinematicCameraPriority;
    }

    /// <summary>연출 vcam 에서 지금 플레이어(또는 낙하·관전) vcam 으로 돌아온다. 그 vcam 이 아니면 무시. 멱등.</summary>
    public void ExitCinematic(CinemachineCamera camera, float blendSeconds)
    {
        if (camera == null || cinematicCamera != camera)
        {
            return;
        }

        ApplyBlendDuration(blendSeconds);
        camera.Priority = InactiveCameraPriority;
        cinematicCamera = null;
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
            if (networkObject != null && IsOwnerFollowCandidate(networkObject.IsSpawned, networkObject.IsOwner))
            {
                SetTarget(i);
                return;
            }
        }
    }

    /// <summary>
    /// 오너 추적 후보인가 — 스폰돼 있고 내 것. 로비 캐릭터 교체(할 일 48 C12)는 옛 PlayerObject 를 디스폰한 같은 프레임에
    /// 새 Variant 를 세우므로, 파괴 직전의 옛 오브젝트(IsSpawned=false, IsOwner 는 여전히 참)가 태그 검색에 남는다 — 그걸 건너뛴다.
    /// 새 오너 플레이어는 <see cref="Player"/>.OnNetworkSpawn → <see cref="FocusOwnerPlayer"/> 로 다시 붙는다.
    /// </summary>
    public static bool IsOwnerFollowCandidate(bool isSpawned, bool isOwner)
    {
        return isSpawned && isOwner;
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
            slowActive ? timeScale.CurrentProfileScale : 1f); // 지금 재생 중인 프로필(시도·성공) 기준

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

    /// <summary>
    /// 연출 동안 플레이어 vcam 이 <paramref name="target"/> 을 따라가게 한다(보스 엘리베이터 하강~상승 — 칸 안 카메라 지점).
    /// 추적 대상 목록·<see cref="CurrentFollowTarget"/>(포그·벽 가림 기준)은 그대로 플레이어다. 낙하 뷰 중이면 플레이어 뷰로 돌린다.
    /// </summary>
    public void SetTemporaryFollow(Transform target)
    {
        if (target == null)
        {
            return;
        }

        EnsureCameraRig();
        if (playerCamera == null)
        {
            return;
        }

        temporaryFollow = target;
        playerCamera.Follow = target;
        // 컷 — 보스 엘리베이터는 암전 중에 넘기므로, 밝아질 때 이미 새 대상을 보고 있어야 한다(감쇠로 쓸고 지나가지 않게).
        playerCamera.PreviousStateIsValid = false;
        if (IsInFallView)
        {
            ReturnToPlayerView();
        }
    }

    /// <summary>임시 추적을 끝내고 현재 대상(플레이어·관전 대상)으로 컷 복귀한다 — 그 사이 순간이동한 대상까지 쓸고 지나가지 않게.</summary>
    public void ClearTemporaryFollow()
    {
        if (temporaryFollow == null)
        {
            return;
        }

        temporaryFollow = null;
        if (playerCamera == null)
        {
            return;
        }

        playerCamera.Follow = GetCurrentTarget();
        playerCamera.PreviousStateIsValid = false;
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
        // 임시 추적 중엔 대상만 바꿔 두고 화면은 연출을 유지한다 — ClearTemporaryFollow 가 이 대상으로 돌아온다.
        if (temporaryFollow == null)
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
