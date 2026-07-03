using Unity.Netcode;
using UnityEngine;

public enum AttackType
{
    None,
    Default,
    Q,
    E,
    R
}

public class BaseWeapon : MonoBehaviour
{
    [SerializeField] protected int damage = 0;
    public int Damage { get { return damage; } }

    [SerializeField] protected bool isGroggyAttack = false;
    public bool IsGroggyAttack { get { return isGroggyAttack; } }

    [SerializeField] protected LayerMask targetLayer;

    [SerializeField] protected AttackType attackType = AttackType.None;
    public AttackType AttackType { get { return attackType; } }

    protected bool IsServer =>
    NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
