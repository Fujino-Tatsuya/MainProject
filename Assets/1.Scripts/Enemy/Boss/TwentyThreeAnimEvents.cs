using UnityEngine;
using Unity.Netcode;

public class TwentyThreeAnimEvents : NetworkBehaviour
{
    [SerializeField] GrabController grabController;
    [SerializeField] JumpController jumpController;

    void Start()
    {

    }

    public void TryGrabEvent()
    {
        if(IsServer)
            grabController.Detect();
    }

    public void ThrowEvent()
    {
        if (IsServer)
            grabController.Throw();
    }

    public void SetTargetEvent()
    {
        if (IsServer)
            jumpController.SetTarget();
    }

    public void FallEvent()
    {
        if (IsServer)
            jumpController.ShowMyMeshClientRpc(true);
    }

    public void OnLandedEvent()
    {
        if (IsServer)
            jumpController.OnLanded();
    }
}
