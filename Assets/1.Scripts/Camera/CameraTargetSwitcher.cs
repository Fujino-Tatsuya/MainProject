using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraTargetSwitcher : MonoBehaviour
{
    private const string CameraFollowTargetTag = "CameraFollowTarget";

    [SerializeField]
    private CinemachineCamera playerCamera;

    private readonly List<Transform> cameraFollowTargets = new();
    private int currentTargetIndex = -1;
    private CinemachineFollow playerCameraFollow;
    private Quaternion fixedCameraRotation;
    private bool hasFixedCameraRotation;

    private void Awake()
    {
        Initialize();
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

    private void Initialize()
    {
        FindPlayerCamera();

        if (playerCamera != null)
        {
            playerCamera.Priority = 100;
            CacheFixedCameraRotation();
            ClearLookAtTarget();
        }

        CacheFollowTargets();
        SetTarget(0);
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

    private void CacheFollowTargets()
    {
        cameraFollowTargets.Clear();
        RefreshFollowTargets();
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
        if (playerCamera == null)
        {
            FindPlayerCamera();
        }

        if (playerCamera == null || targetIndex < 0 || targetIndex >= cameraFollowTargets.Count)
        {
            return;
        }

        currentTargetIndex = targetIndex;
        playerCamera.Follow = cameraFollowTargets[currentTargetIndex];
        ClearLookAtTarget();
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

    private void FindPlayerCamera()
    {
        if (playerCamera != null)
        {
            return;
        }

        GameObject playerCameraObject = GameObject.Find("PlayerFollowCamera");
        if (playerCameraObject != null)
        {
            playerCamera = playerCameraObject.GetComponent<CinemachineCamera>();
            if (playerCamera != null)
            {
                CacheFixedCameraRotation();
                ClearLookAtTarget();
            }
        }
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
        if (!hasFixedCameraRotation || playerCamera == null)
        {
            return;
        }

        playerCamera.transform.rotation = fixedCameraRotation;
    }

    private void ClearLookAtTarget()
    {
        if (playerCamera == null)
        {
            return;
        }

        playerCamera.Target.CustomLookAtTarget = true;
        playerCamera.Target.LookAtTarget = null;
    }
}
