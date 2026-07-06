using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public enum AttackType
{
    None,
    Default,
    Q,
    E,
    R
}

public struct AttackInfo
{
    public readonly int damage;
    public readonly bool isGroggyAttack;
    public readonly AttackType type;

    public AttackInfo(int damage, bool isGroggyAttack, AttackType type)
    {
        this.damage = damage;
        this.isGroggyAttack = isGroggyAttack;
        this.type = type;
    }

    public AttackInfo(int damage)
    {
        this.damage = damage;
        this.isGroggyAttack = false;
        this.type = AttackType.None;
    }
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

    protected AttackInfo _attackInfo;

    protected virtual void InitializeAttackInfo()
    {
        _attackInfo = new AttackInfo(damage, isGroggyAttack, attackType);
    }

    protected bool IsServer =>
    NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
