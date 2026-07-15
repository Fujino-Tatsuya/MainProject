using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Default Attack Data")]
public class DefaultAttackData : ScriptableObject
{
    [SerializeField] private DefaultAttackChainPolicy chainPolicy = DefaultAttackChainPolicy.Loop;
    [SerializeField] private LayerMask hittableLayers;
    // 오버랩 판정 한 번에 수집할 수 있는 최대 콜라이더 수 (NonAlloc 버퍼 크기).
    [SerializeField] private int maxHitResults = 16;
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
    public DefaultAttackStep[] AttackSteps => attackSteps;
}
