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

public struct AttackHitContext
{
    public Vector3 sourcePosition;
    public Transform sourceTransform;
    public Collider hitCollider;

    public AttackHitContext(Vector3 sourcePosition, Transform sourceTransform = null, Collider hitCollider = null)
    {
        this.sourcePosition = sourcePosition;
        this.sourceTransform = sourceTransform;
        this.hitCollider = hitCollider;
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

        if (!IsInTargetLayer(hit))
            return false;

        Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
        if (hurtbox != null)
            return TryResolveHit(hurtbox, hit, overrideDamage);

        GameObject target = hit.transform.root.gameObject;
        Unit unit = hit.GetComponentInParent<Unit>();
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

        AttackInfo attackInfo = CreateAttackInfo(overrideDamage);

        return unit.ReceiveAttack(attackInfo, CreateHitContext(null));
    }

    protected bool TryResolveHit(Hurtbox hurtbox, int? overrideDamage = null)
    {
        if (!IsServer || hurtbox == null)
            return false;

        AttackInfo attackInfo = CreateAttackInfo(overrideDamage);
        return hurtbox.ReceiveAttack(attackInfo, CreateHitContext(null));
    }

    protected bool TryResolveHit(Hurtbox hurtbox, Collider hit, int? overrideDamage = null)
    {
        if (!IsServer || hurtbox == null)
            return false;

        AttackInfo attackInfo = CreateAttackInfo(overrideDamage);
        return hurtbox.ReceiveAttack(attackInfo, CreateHitContext(hit));
    }

    protected bool TryGetHurtbox(Collider hit, out Hurtbox hurtbox)
    {
        hurtbox = null;
        if (hit == null || !IsInTargetLayer(hit))
            return false;

        hurtbox = hit.GetComponentInParent<Hurtbox>();
        return hurtbox != null;
    }

    protected bool IsInTargetLayer(int layer)
    {
        return (targetLayer.value & (1 << layer)) != 0;
    }

    protected bool IsInTargetLayer(Collider hit)
    {
        if (hit == null)
            return false;

        if (IsInTargetLayer(hit.gameObject.layer))
            return true;

        return IsInTargetLayer(hit.transform.root.gameObject.layer);
    }

    private AttackInfo CreateAttackInfo(int? overrideDamage)
    {
        return overrideDamage.HasValue
            ? new AttackInfo(overrideDamage.Value, attackType, isGroggyAttack)
            : _attackInfo;
    }

    private AttackHitContext CreateHitContext(Collider hit)
    {
        return new AttackHitContext(transform.position, transform, hit);
    }
}
