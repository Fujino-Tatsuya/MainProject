using UnityEngine;
using Unity.Netcode;

public class NetworkKinematicOption : NetworkBehaviour
{
    void Awake()
    {
        if (IsServer)
        {
            GetComponent<Rigidbody>().isKinematic = false;
        }
        else
        {
            GetComponent<Rigidbody>().isKinematic = true;
        }
    }
}
