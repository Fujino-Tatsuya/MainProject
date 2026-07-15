using Unity.Netcode;
using UnityEngine;

public class AttackTriggerRelay : NetworkBehaviour
{
    [SerializeField] ColliderBasicAttack colliderBasicAttack;
    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        colliderBasicAttack.OnAttackTriggerEnter(other);
    }

    void OnTriggerStay(Collider other)
    {
        if (!IsServer) return;
        colliderBasicAttack.OnAttackTriggerStay(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        colliderBasicAttack.OnAttackTriggerExit(other);
    }
}
