using Unity.Netcode;
using UnityEngine;

public class BaseWeapon : MonoBehaviour
{
    [SerializeField] protected int damage = 0;
    public int Damage { get { return damage; } }

    [SerializeField] protected bool isGroggyAttack = false;
    public bool IsGroggyAttack { get { return isGroggyAttack; } }

    [SerializeField] protected LayerMask layerMask;

    protected bool IsServer =>
    NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
