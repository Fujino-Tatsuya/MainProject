using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class Hurtbox : MonoBehaviour
{
    [SerializeField] private Unit ownerUnit;

    private IAttackReceiver attackReceiver;

    public Unit OwnerUnit => ownerUnit;

    private void Awake()
    {
        ResolveOwner();
    }

    private void OnValidate()
    {
        ResolveOwner();
    }

    public bool TryGetOwner(out Unit unit)
    {
        ResolveReferences();
        unit = ownerUnit;
        return unit != null;
    }

    public bool TryGetReceiver(out IAttackReceiver receiver)
    {
        ResolveReferences();
        receiver = attackReceiver;
        return receiver != null;
    }

    public bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (!TryGetReceiver(out IAttackReceiver receiver))
        {
            Debug.LogError($"Hurtbox '{name}' has no attack receiver.", this);
            return false;
        }

        return receiver.ReceiveAttack(attackInfo, hitContext);
    }

    private void ResolveOwner()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (ownerUnit != null)
        {
            attackReceiver = ownerUnit;
            return;
        }

        ownerUnit = GetComponentInParent<Unit>();
        if (ownerUnit != null)
        {
            attackReceiver = ownerUnit;
            return;
        }

        attackReceiver = GetComponentInParent<IAttackReceiver>();
    }
}
