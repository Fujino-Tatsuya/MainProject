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

    MonsterTimeController _monsterTimeController;

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

        ApplyOptionalSpeed(bt, "WalkSpeed", moveSpeed, out WalkSpeed);
        ApplyOptionalSpeed(bt, "ChaseSpeed", chaseSpeed, out ChaseSpeed);

        _monsterTimeController = GetComponent<MonsterTimeController>();
    }

    /// <summary>
    /// 이동 속도 블랙보드 변수를 <b>있으면</b> 채운다.
    ///
    /// <c>WalkSpeed</c>·<c>ChaseSpeed</c>는 그래프마다 있을 수도, 없을 수도 있는 <b>선택 변수</b>다
    /// (No.23은 <c>WalkSpeed</c>만 쓰고, <c>Enemy/CommonMeleeRobot</c>은 둘 다 쓴다). 예전에는 부재를
    /// 경고로 띄웠는데, 그게 "이름을 맞춰야 할 버그"처럼 보여서 위험했다 — <c>TwentyThree.prefab</c>의
    /// <c>chaseSpeed</c>는 0이라, 경고만 보고 그래프에 변수를 만들어 이름을 맞추면 <b>추격 속도 0으로
    /// 보스가 제자리에 굳는다</b>. 그래서 부재는 조용히 넘기고, 반대로 <b>그래프가 쓰는데 값이 0인</b>
    /// 경우만 경고한다 — 그쪽이 실제로 "적이 안 움직인다"로 터지는 조건이다.
    /// </summary>
    void ApplyOptionalSpeed(BehaviorGraphAgent bt, string variableName, float value,
                            out BlackboardVariable<float> cached)
    {
        if (!bt.BlackboardReference.GetVariable<float>(variableName, out cached))
            return;   // 이 그래프는 그 속도 개념을 쓰지 않는다 — 정상이다.

        if (value <= 0f)
        {
            Edit.LogWarning(
                $"[Enemy] {name}의 {variableName}로 넣을 값이 {value}입니다 — BT가 이 값을 그대로 이동 " +
                "속도로 쓰므로 해당 상태에서 제자리에 멈춥니다. 프리팹의 속도 값을 채우세요.", this);
        }

        cached.Value = value;
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);
        _monsterTimeController?.HitStop(0.25f);
        // 그로기 체크..
    }
}
