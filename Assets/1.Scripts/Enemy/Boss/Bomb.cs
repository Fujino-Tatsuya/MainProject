using UnityEngine;
using System;


    
public class Bomb : Unit
{
    [SerializeField] AttackType attackType;

    public EventHandler<AttackEventArgs> OnTriggered;

    void OnTriggerEnter(Collider other)
    {
        BaseAttack baseWeapon = other.GetComponent<BaseAttack>();
        if (baseWeapon == null) return;

        if (baseWeapon.AttackType == attackType)
        {
            OnTriggered?.Invoke(this, new AttackEventArgs(baseWeapon));
        }
    }
}
