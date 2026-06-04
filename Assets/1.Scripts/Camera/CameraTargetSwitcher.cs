using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class CameraTargetSwitcher : MonoBehaviour
{
    [SerializeField]
    private CinemachineCamera playerCamera;

    private CinemachineBrain cinemachineBrain;

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
