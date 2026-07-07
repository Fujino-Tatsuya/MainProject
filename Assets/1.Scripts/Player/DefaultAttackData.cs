using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Default Attack Data")]
public class DefaultAttackData : ScriptableObject
{
    [SerializeField] private DefaultAttackComboInputType comboInputType = DefaultAttackComboInputType.HoldAutoRepeat;
    [SerializeField] private ColliderInfo defaultHitbox;
    [SerializeField] private LayerMask hittableLayers;
    [SerializeField] private int maxHitResults = 16;
    [SerializeField] private int damageOverride;
    [SerializeField] private DefaultAttackStep[] attackSteps =
    {
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep()
    };

    public DefaultAttackComboInputType ComboInputType => comboInputType;
    public ColliderInfo DefaultHitbox => defaultHitbox;
    public LayerMask HittableLayers => hittableLayers;
    public int MaxHitResults => maxHitResults;
    public int DamageOverride => damageOverride;
    public DefaultAttackStep[] AttackSteps => attackSteps;
}
