using UnityEngine;

public class StatusEffectController : MonoBehaviour
{
    [SerializeField] private StatusEffectType activeEffects = StatusEffectType.None;
    [SerializeField] private float moveSpeedMultiplier = 1f;

    public StatusEffectType ActiveEffects => activeEffects;
    public bool BlocksMovement => Has(StatusEffectType.Stunned) || Has(StatusEffectType.Rooted) || Has(StatusEffectType.Airborne);
    public bool BlocksAttack => Has(StatusEffectType.Stunned) || Has(StatusEffectType.Airborne);
    public bool BlocksInterrupt => Has(StatusEffectType.Stunned) || Has(StatusEffectType.Rooted) || Has(StatusEffectType.Airborne) || Has(StatusEffectType.Debilitated);
    public bool BlocksSkill => Has(StatusEffectType.Stunned) || Has(StatusEffectType.Silenced) || Has(StatusEffectType.Airborne);
    public bool HasSuperArmor => Has(StatusEffectType.SuperArmor);
    public float MoveSpeedMultiplier => Mathf.Max(0f, moveSpeedMultiplier);

    public bool Has(StatusEffectType effect)
    {
        return (activeEffects & effect) != 0;
    }

    public void SetEffects(StatusEffectType effects)
    {
        activeEffects = effects;
    }
}
