using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// MapScene에 배치되는 카메라 매니저.
//  - 오너 플레이어가 스폰될 때(Player.OnNetworkSpawn → FocusOwnerPlayer) 카메라 리그를 Instantiate하고,
//  - 내가 Owner인 플레이어를 따라가게 하며,
//  - '[' / ']' 로 팔로우 대상을 전환한다(스위칭은 MapScene 전용).
public class CameraTargetSwitcher : MonoBehaviour
{
    public static CameraTargetSwitcher Active { get; private set; }

    private const string CameraFollowTargetTag = "CameraFollowTarget";

    [SerializeField] private GameObject mainCameraPrefab;

    [SerializeField] private GameObject followCameraPrefab;

    private CinemachineCamera playerCamera;       // 리그 안의 vcam (런타임에 채워짐)
    private readonly List<Transform> cameraFollowTargets = new();
    private int currentTargetIndex = -1;
    private CinemachineFollow playerCameraFollow;
    private Quaternion fixedCameraRotation;
    private bool hasFixedCameraRotation;

    private void Awake()
    {
        Active = this;
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    // 오너 플레이어가 스폰될 때 호출된다. 리그가 없으면 만들고, 오너를 초기 대상으로 잡는다.
    public void FocusOwnerPlayer()
    {
        EnsureCameraRig();
        SelectOwnerPlayerTarget();
    }

    private void EnsureCameraRig()
    {
        if (playerCamera != null)
        {
            return;
        }

        if (mainCameraPrefab == null || followCameraPrefab == null)
        {
            Debug.LogWarning("Camera rig prefabs are not assigned (mainCamera/follow).");
            return;
        }

        // 활성 씬(소스/로딩 씬)이 아니라, 매니저(CameraSwitcher)가 사는 씬(MapScene)에서 바로 생성한다.
        // 부모를 this.transform으로 주면 그 씬 소속으로 태어나므로, 소스 씬 언로드에 휩쓸려 파괴되지 않는다.
        Instantiate(mainCameraPrefab, transform); // 렌더링 카메라 + CinemachineBrain
        GameObject followInstance = Instantiate(followCameraPrefab, transform); // 팔로우 vcam

        playerCamera = followInstance.GetComponentInChildren<CinemachineCamera>(true);
        if (playerCamera == null)
        {
            Debug.LogWarning($"Follow camera prefab has no {nameof(CinemachineCamera)}. prefab={followCameraPrefab.name}");
            return;
        }

        playerCamera.Priority = 100;
        CacheFixedCameraRotation();
        ClearLookAtTarget();
    }

    // 내가 Owner인 플레이어의 팔로우 타겟을 카메라 대상으로 설정한다.
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
