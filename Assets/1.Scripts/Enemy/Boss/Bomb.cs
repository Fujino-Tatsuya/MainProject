using UnityEngine;
using System;


    
public class Bomb : MonoBehaviour
{
    [SerializeField] AttackType attackType;

    public EventHandler<AttackEventArgs> OnTriggered;

    void OnTriggerEnter(Collider other)
    {
        BaseAttack baseAttack = other.GetComponent<BaseAttack>();
        if (baseAttack == null) return;

        if (baseAttack.AttackType == attackType)
        {
            OnTriggered?.Invoke(this, new AttackEventArgs(baseAttack));
        }
    }
}
