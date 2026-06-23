using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "CheckDefeatInChargeController", story: "Defeat in [ChargeController] is [Operator] to [Value]", category: "Conditions", id: "2a9fa31e4ecb0c77d376bc32e3000953")]
public partial class CheckDefeatInChargeControllerCondition : Condition
{
    [SerializeReference] public BlackboardVariable<ChargeController> ChargeController;
    [Comparison(comparisonType: ComparisonType.Boolean)]
    [SerializeReference] public BlackboardVariable<ConditionOperator> Operator;
    [SerializeReference] public BlackboardVariable<bool> Value;

    public override bool IsTrue()
    {
        if(!CheckValid())
            return false;

        bool result = ChargeController.Value.IsDefeated == Value.Value;

        return result;
    }

    bool CheckValid()
    {
        if (ChargeController.Value == null)
        {
            Debug.LogError("ChargeController script is null.");
            return false;
        }

        return true;
    }
}
