using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Default Attack Data")]
[DataTableSheet("Paladin", Order = 0)]
public class DefaultAttackData : ScriptableObject
{
    [SerializeField] private DefaultAttackChainPolicy chainPolicy = DefaultAttackChainPolicy.Loop;
    [SerializeField] private LayerMask hittableLayers;
    // 오버랩 판정 한 번에 수집할 수 있는 최대 콜라이더 수 (NonAlloc 버퍼 크기).
    [SerializeField, DataTableIgnore] private int maxHitResults = 16;
    // 평타 재생 속도 배율. 애니메이터 AttackSpeed 파라미터로 Default_Attack0..N 에 곱해져
    // 클립 이벤트(Hit·콤보 창·End)와 루트모션이 함께 빨라진다 — 타당 전진 거리는 그대로.
    [SerializeField, Min(0.1f)] private float attackSpeed = 1f;
    [SerializeField] private DefaultAttackStep[] attackSteps =
    {
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep()
    };

    public DefaultAttackChainPolicy ChainPolicy => chainPolicy;
    public LayerMask HittableLayers => hittableLayers;
    public int MaxHitResults => maxHitResults;
    public float AttackSpeed => attackSpeed;
    public DefaultAttackStep[] AttackSteps => attackSteps;
}
