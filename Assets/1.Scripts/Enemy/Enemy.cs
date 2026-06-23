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

    [Header("Enemy 전용 상태")]
    [SerializeField] int _groggyCount;
    [SerializeField] int _maxGroggyCount;

    BlackboardVariable<float> WalkSpeed;
    BlackboardVariable<bool> IsGroggy;
    BlackboardVariable<int> GroggyCount;
    BlackboardVariable<int> MaxGroggyCount;


    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense, maxShield);

        BehaviorGraphAgent bt = GetComponent<BehaviorGraphAgent>();
        if (bt == null)
            Debug.LogError("BehaviorGraphAgent를 얻어오는 것을 실패했습니다.");

        if (!bt.BlackboardReference.GetVariable<float>("WalkSpeed", out WalkSpeed))
            Debug.LogError("해당 BT의 Blackboard에서 WalkSpeed 변수를 얻어오는 것에 실패했습니다.");

        if (_maxGroggyCount != 0)
        {
            if (!bt.BlackboardReference.GetVariable<int>("GroggyCount", out GroggyCount))
                Debug.LogError("해당 BT의 Blackboard에서 GroggyCount 변수를 얻어오는 것에 실패했습니다.");

            if (!bt.BlackboardReference.GetVariable<int>("MaxGroggyCount", out MaxGroggyCount))
                Debug.LogError("해당 BT의 Blackboard에서 MaxGroggyCount 변수를 얻어오는 것에 실패했습니다.");

            if (!bt.BlackboardReference.GetVariable<bool>("IsGroggy", out IsGroggy))
                Debug.LogError("해당 BT의 Blackboard에서 IsGroggy 변수를 얻어오는 것에 실패했습니다.");
        }

        WalkSpeed.Value = moveSpeed;
        GroggyCount.Value = _groggyCount;
        MaxGroggyCount.Value = _maxGroggyCount;

        TakeDamage(60);
    }

    public override void TakeDamage(int damage)
    {
        base.TakeDamage(damage);

        // 그로기 체크..
    }
}
