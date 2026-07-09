using System;
using UnityEngine;

public class AttackEventArgs : EventArgs
{
    BaseAttack _baseAttack;
    public BaseAttack BaseAttack { get { return _baseAttack; } }

    public AttackEventArgs(BaseAttack baseAttack)
    {
        _baseAttack = baseAttack;
    }
}
