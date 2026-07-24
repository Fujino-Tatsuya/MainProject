using UnityEngine;
using Unity.Netcode;

public class WellsAnimEvents : NetworkBehaviour
{
    [SerializeField] BombLauncher bombLauncher;

    public void ThrowBombEvent()
    {
        if (IsServer)
            bombLauncher.BombThrow();
    }

    public void BombDestroyEvent()
    {
        if (IsServer)
            bombLauncher.BombDestroy();
    }
}
