using UnityEngine;
using Unity.Netcode;

public class TwentyThreeAnimEvents : NetworkBehaviour
{
    [SerializeField] GrabController grabController;
    void Start()
    {

    }

    public void GrabEvent()
    {
        if(IsServer)
            grabController.Detect();
    }
}
