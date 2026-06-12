using UnityEngine;
using Unity.Netcode;

namespace BaseNetCode
{
    public class BaseNetworkBehaviour : NetworkBehaviour
    {
        protected bool IsNetworkActive =>
     NetworkManager.Singleton != null &&
     NetworkManager.Singleton.IsListening &&
     IsSpawned;

        protected bool HasStateAuthority =>
            !IsNetworkActive || IsServer;
    }
}

