using System;
using UnityEngine;

public class AttackEventArgs : EventArgs
{
    BaseWeapon _baseWeapon;
    public BaseWeapon BaseWeapon { get { return _baseWeapon; } }

    int _damage;
    public int Damage { get { return _damage; } }

    public AttackEventArgs(BaseWeapon baseWeapon)
    {
        _baseWeapon = baseWeapon;
    }

    public AttackEventArgs(int damage)
    {
        _damage = damage;
    }
}
