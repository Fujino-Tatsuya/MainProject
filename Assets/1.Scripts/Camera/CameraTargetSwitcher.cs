using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

public class CameraTargetSwitcher : NetworkBehaviour
{
    [SerializeField]
    private CinemachineCamera playerCamera;
    private void Awake()
    {
       Initailize();
    }

    private void Initailize()
    {
        if (playerCamera == null)
        {
            playerCamera = GameObject.Find("PlayerFollowCamera").GetComponent<CinemachineCamera>();
        }

        if (playerCamera != null)
        {
            playerCamera.Priority = 100;
        }
    }
}
