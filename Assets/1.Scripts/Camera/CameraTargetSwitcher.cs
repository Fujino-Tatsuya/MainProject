using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// MapScene camera manager. It creates one render camera and two identical
// Cinemachine cameras, then switches their priorities for normal/fall views.
public class CameraTargetSwitcher : MonoBehaviour
{
    public static CameraTargetSwitcher Active { get; private set; }

    private const string CameraFollowTargetTag = "CameraFollowTarget";
    private const int ActiveCameraPriority = 100;
    private const int InactiveCameraPriority = 0;

    [SerializeField] private GameObject mainCameraPrefab;
    [SerializeField] private GameObject followCameraPrefab;
    [SerializeField, Min(0f)] private float toFloatBlendDuration = 0.2f;
    [SerializeField, Min(0f)] private float toFollowBlendDuration = 0.35f;

    private CinemachineBrain cameraBrain;
    private CinemachineCamera playerCamera;
    private CinemachineCamera floatCamera;
    private FloatFollowTarget floatFollowTarget;
    private readonly List<Transform> cameraFollowTargets = new();
    private int currentTargetIndex = -1;
    private CinemachineFollow playerCameraFollow;
    private Quaternion fixedCameraRotation;
    private bool hasFixedCameraRotation;

    public bool IsInFallView { get; private set; }

    private void Awake()
    {
        Active = this;
    }

    private void OnDestroy()
    {
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
        if (playerCamera != null && floatCamera != null)
        {
            return;
        }

        if (mainCameraPrefab == null || followCameraPrefab == null)
        {
            Edit.LogWarning("[Camera] Camera rig prefabs are not assigned (mainCamera/follow).");
            return;
        }

        GameObject mainCameraInstance = Instantiate(mainCameraPrefab, transform);
        cameraBrain = mainCameraInstance.GetComponentInChildren<CinemachineBrain>(true);

        GameObject followInstance = Instantiate(followCameraPrefab, transform);
        followInstance.name = "PlayerFollowCamera";
        playerCamera = followInstance.GetComponentInChildren<CinemachineCamera>(true);

        GameObject floatInstance = Instantiate(followCameraPrefab, transform);
        floatInstance.name = "PlayerFollowFloatCamera";
        floatCamera = floatInstance.GetComponentInChildren<CinemachineCamera>(true);

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

        GameObject proxyObject = new("FloatFollowTarget");
        proxyObject.transform.SetParent(transform, false);
        floatFollowTarget = proxyObject.AddComponent<FloatFollowTarget>();
        floatCamera.Follow = proxyObject.transform;

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
        RemoveMissingTargets();

        GameObject[] followTargetObjects = GameObject.FindGameObjectsWithTag(CameraFollowTargetTag);
        foreach (GameObject followTargetObject in followTargetObjects)
        {
            Transform followTarget = followTargetObject.transform;
            if (!cameraFollowTargets.Contains(followTarget))
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

    private void RemoveMissingTargets()
    {
        for (int i = cameraFollowTargets.Count - 1; i >= 0; i--)
        {
            if (cameraFollowTargets[i] == null)
            {
                cameraFollowTargets.RemoveAt(i);
            }
        }

        if (currentTargetIndex >= cameraFollowTargets.Count)
        {
            currentTargetIndex = cameraFollowTargets.Count - 1;
        }
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
