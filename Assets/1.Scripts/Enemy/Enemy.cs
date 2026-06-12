using UnityEngine;
using Unity.Behavior;

public class Enemy : Unit
{
    [Header("초기화 값")]
    [SerializeField] int attackDamage;
    [SerializeField] float moveSpeed;
    [SerializeField] float attackSpeed;
    [SerializeField] int maxHp;
    [SerializeField] int defense;
    [SerializeField] int maxShield;

    BlackboardVariable<float> WalkSpeed;

    void Awake()
    {
        if (!IsServer) return;

        Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense, maxShield);
    }


    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        BehaviorGraphAgent bt = GetComponent<BehaviorGraphAgent>();
        if (bt == null)
            Debug.LogError("BehaviorGraphAgent를 얻어오는 것을 실패했습니다.");

        if (!bt.BlackboardReference.GetVariable<float>("WalkSpeed", out WalkSpeed))
            Debug.LogError("해당 BT의 Blackboard에서 WalkSpeed 변수를 얻어오는 것에 실패했습니다.");

        WalkSpeed.Value = moveSpeed;
    }
}
