using System;
using UnityEngine;

public class AttackEventArgs : EventArgs
{
    BaseWeapon _baseWeapon;
    public BaseWeapon BaseWeapon { get { return _baseWeapon; } }

    public AttackEventArgs(BaseWeapon baseWeapon)
    {
        _baseWeapon = baseWeapon;
    }
}
