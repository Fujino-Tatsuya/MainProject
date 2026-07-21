using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "CheckArrivalInChargeController", story: "Arrival in [ChargeController] is [Operator] [Value]", category: "Conditions", id: "c9e25ac1e0a178fe8222fdbb2154da60")]
public partial class CheckArrivalInChargeControllerCondition : Condition
{
    [SerializeReference] public BlackboardVariable<ChargeController> ChargeController;
    [Comparison(comparisonType: ComparisonType.Boolean)]
    [SerializeReference] public BlackboardVariable<ConditionOperator> Operator;
    [SerializeReference] public BlackboardVariable<bool> Value;

    public override bool IsTrue()
    {
        if (!CheckValid())
            return false;

        bool result = ChargeController.Value.IsReached == Value.Value;

        return result;
    }

    bool CheckValid()
    {
        if (ChargeController.Value == null)
        {
            Debug.LogError("[BT] ChargeController script is null.");
            return false;
        }

        return true;
    }
}
