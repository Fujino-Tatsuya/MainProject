using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "CheckHealthPercent", story: "HealthPercent in [Unit] is [Operator] [Percent]", category: "Conditions", id: "829bd9816eb2b7d8a887901ac34e3f09")]
public partial class CheckHealthPercentCondition : Condition
{
    [SerializeReference] public BlackboardVariable<Unit> Unit;
    [Comparison(comparisonType: ComparisonType.All)]
    [SerializeReference] public BlackboardVariable<ConditionOperator> Operator;
    [SerializeReference] public BlackboardVariable<float> Percent;

    public override bool IsTrue()
    {
        if(!CheckValid())
            return false;

        float currentHp = Unit.Value.CurrentHealth;
        float maxHp = Unit.Value.MaxHp;

        float currentHpPercent = currentHp / maxHp * 100f;

        return ConditionUtils.Evaluate(currentHpPercent, Operator, Percent.Value);
    }

    bool CheckValid()
    {
        if (Unit.Value == null)
        {
            Debug.LogError("[BT] Unit script is not assigned");
            return false;
        }

        return true;
    }
}
