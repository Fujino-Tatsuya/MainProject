using UnityEngine;
using System;


    
public class Bomb : MonoBehaviour
{
    [SerializeField] AttackType attackType;

    public EventHandler<AttackEventArgs> OnTriggered;

    void OnTriggerEnter(Collider other)
    {
        BaseWeapon baseWeapon = other.GetComponent<BaseWeapon>();
        if (baseWeapon == null) return;

        if (baseWeapon.AttackType == attackType)
        {
            OnTriggered?.Invoke(this, new AttackEventArgs(baseWeapon));
        }
    }
}
