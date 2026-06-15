using UnityEngine;

public class Player : Unit
{
    [Header("초기화 값")]
    [SerializeField] int attackDamage;
    [SerializeField] float moveSpeed;
    [SerializeField] float attackSpeed;
    [SerializeField] int maxHp;
    [SerializeField] int defense;
    [SerializeField] int maxShield;

    [Header("상태 확인용")]
    [SerializeField] int currentHp;
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense, maxShield);
    }


    void Update()
    {
        currentHp = _currentHp.Value;
    }
}
