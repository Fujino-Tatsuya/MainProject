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

    [Header("\nEnemy 전용 상태")]
    [SerializeField] int _groggyCount;
    [SerializeField] int _maxGroggyCount;

    BlackboardVariable<float> WalkSpeed;
    BlackboardVariable<float> ChaseSpeed;
    BlackboardVariable<bool> IsGroggy;
    BlackboardVariable<int> GroggyCount;
    BlackboardVariable<int> MaxGroggyCount;


    public override void OnNetworkSpawn()
    {
        // ⚠️ base 호출이 빠져 있었다. Unit.OnNetworkSpawn이 HP/쉴드 복제 구독과 HitFlash(피격 빨간
        // 틴트) 자동 부착을 담당하므로, 이게 없으면 **보스만** 피격 표시가 안 나온다.
        // 전 피어에서 실행돼야 하는 로컬 연출이라 IsServer 게이트보다 먼저 호출한다.
        base.OnNetworkSpawn();

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

        if (_maxGroggyCount != 0)
        {
            if (!bt.BlackboardReference.GetVariable<int>("GroggyCount", out GroggyCount))
                Edit.LogAssertion("[Enemy] 해당 BT의 Blackboard에서 GroggyCount 변수를 얻어오는 것에 실패했습니다.");
            else
                GroggyCount.Value = _groggyCount;

            if (!bt.BlackboardReference.GetVariable<int>("MaxGroggyCount", out MaxGroggyCount))
                Edit.LogAssertion("[Enemy] 해당 BT의 Blackboard에서 MaxGroggyCount 변수를 얻어오는 것에 실패했습니다.");
            else
                MaxGroggyCount.Value = _maxGroggyCount;

            if (!bt.BlackboardReference.GetVariable<bool>("IsGroggy", out IsGroggy))
                Edit.LogAssertion("[Enemy] 해당 BT의 Blackboard에서 IsGroggy 변수를 얻어오는 것에 실패했습니다.");
        }
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);

        // 그로기 체크..
    }
}
