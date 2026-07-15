using UnityEngine;
using System;

public class Bomb : MonoBehaviour, IAttackReceiver
{
    [SerializeField] AttackType attackType;

    public event Action<AttackInfo, AttackHitContext> OnTriggered;

    public bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (attackInfo.attackType != attackType)
            return false;

        OnTriggered?.Invoke(attackInfo, hitContext);
        return true;
    }

    // Legacy trigger bridge. Hurtbox -> IAttackReceiver is the active hit path.
    // void OnTriggerEnter(Collider other)
    // {
    //     BaseAttack baseAttack = other.GetComponent<BaseAttack>();
    //     if (baseAttack == null) return;
    //
    //     if (baseAttack.AttackType == attackType)
    //     {
    //         AttackInfo attackInfo = new AttackInfo(baseAttack.Damage, baseAttack.AttackType, baseAttack.IsGroggyAttack);
    //         AttackHitContext hitContext = new AttackHitContext(baseAttack.transform.position, baseAttack.transform, other);
    //         OnTriggered?.Invoke(attackInfo, hitContext);
    //     }
    // }
}
