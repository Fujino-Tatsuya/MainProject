using UnityEngine;
using Unity.Behavior;

public class Enemy : Unit
{
    [Header("초기화 값")]
    [SerializeField] int attackDamage;
    [SerializeField] float moveSpeed;
    [SerializeField] float chaseSpeed;
    [SerializeField] float attackSpeed;
    [SerializeField] int maxHp;
    [SerializeField] int defense;

    BlackboardVariable<float> WalkSpeed;
    BlackboardVariable<float> ChaseSpeed;

    [Header("시간제어 컴포넌트")]
    [SerializeField] MonsterTimeController _monsterTimeController;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense);

        BehaviorGraphAgent bt = GetComponent<BehaviorGraphAgent>();
        if (bt == null)
            Edit.LogAssertion("[Enemy] BehaviorGraphAgent를 얻어오는 것을 실패했습니다.");

        if (!bt.BlackboardReference.GetVariable<float>("WalkSpeed", out WalkSpeed))
            Edit.LogWarning("[Enemy] 해당 BT의 Blackboard에서 WalkSpeed 변수를 얻어오는 것에 실패했습니다.");
        else
            WalkSpeed.Value = moveSpeed;

        if (!bt.BlackboardReference.GetVariable<float>("ChaseSpeed", out ChaseSpeed))
            Edit.LogWarning("[Enemy] 해당 BT의 Blackboard에서 ChaseSpeed 변수를 얻어오는 것에 실패했습니다.");
        else
            ChaseSpeed.Value = chaseSpeed;
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);

        // 그로기 체크..
    }
}
