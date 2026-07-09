using System;
using UnityEngine;

public class AttackEventArgs : EventArgs
{
    BaseAttack _baseWeapon;
    public BaseAttack BaseWeapon { get { return _baseWeapon; } }

    public AttackEventArgs(BaseAttack baseWeapon)
    {
        _baseWeapon = baseWeapon;
    }

    public AttackEventArgs(int damage)
    {
        _damage = damage;
    }
}
