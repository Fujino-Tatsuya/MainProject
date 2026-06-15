using UnityEngine;
using Unity.Netcode;

public class TwentyThreeAnimEvents : NetworkBehaviour
{
    [SerializeField] GrabController grabController;
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
}
