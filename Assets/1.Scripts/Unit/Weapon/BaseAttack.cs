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

public struct AttackInfo
{
    public int damage;
    public AttackType attackType;
    public bool isGroggyAttack;

    public AttackInfo(int damage, AttackType attackType = AttackType.None, bool isGroggyAttack = false)
    {
        this.damage = Mathf.Max(0, damage);
        this.attackType = attackType;
        this.isGroggyAttack = isGroggyAttack;
    }
}

public class BaseAttack : MonoBehaviour
{
    [SerializeField] protected int damage = 0;
    public int Damage { get { return damage; } }

    [SerializeField] protected bool isGroggyAttack = false;
    public bool IsGroggyAttack { get { return isGroggyAttack; } }

    [SerializeField] protected LayerMask targetLayer;

    [SerializeField] protected AttackType attackType = AttackType.None;
    public AttackType AttackType { get { return attackType; } }

    protected AttackInfo _attackInfo;

    protected bool IsServer =>
        NetworkManager.Singleton == null ||
        !NetworkManager.Singleton.IsListening ||
        NetworkManager.Singleton.IsServer;

    protected void InitializeAttackInfo()
    {
        _attackInfo = new AttackInfo(damage, attackType, isGroggyAttack);
    }

    public void SetDamageSnapshot(int value)
    {
        damage = Mathf.Max(0, value);
        InitializeAttackInfo();
    }

    public void SetTargetLayer(LayerMask value)
    {
        targetLayer = value;
    }

    public void SetAttackType(AttackType value)
    {
        attackType = value;
        InitializeAttackInfo();
    }

    protected bool TryResolveHit(Collider hit, int? overrideDamage = null)
    {
        if (!IsServer || hit == null)
            return false;

        GameObject target = hit.transform.root.gameObject;
        if ((targetLayer.value & (1 << target.layer)) == 0)
            return false;

        Unit unit = target.GetComponent<Unit>();
        if (unit == null)
        {
            Debug.LogError($"해당 오브젝트, {target.name}에 Unit 컴포넌트가 부착되어있지 않습니다.", this);
            return false;
        }

        return TryResolveHit(unit, overrideDamage);
    }

    protected bool TryResolveHit(Unit unit, int? overrideDamage = null)
    {
        if (!IsServer || unit == null)
            return false;

        AttackInfo attackInfo = overrideDamage.HasValue
            ? new AttackInfo(overrideDamage.Value, attackType, isGroggyAttack)
            : _attackInfo;

        unit.TakeDamage(attackInfo);
        return true;
    }
}
