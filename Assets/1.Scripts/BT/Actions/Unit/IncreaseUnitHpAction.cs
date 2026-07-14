using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "IncreaseUnitHp", story: "Increase Hp [Amount] in [Unit]", category: "Action/Unit", id: "8178bff9843203a7ca110789a42ef0c8")]
public partial class IncreaseUnitHpAction : Action
{
    [SerializeReference] public BlackboardVariable<int> Amount;
    [SerializeReference] public BlackboardVariable<Unit> Unit;

    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        Unit.Value.HealHp(Amount.Value);
        return Status.Success;
    }

    bool CheckValid()
    {
        if (Amount.Value == 0)
        {
            Debug.LogError("[BT] Increasing Amount is 0.");
            return false;
        }

        if (Unit.Value == null)
        {
            Debug.LogError("[BT] Unit script is null.");
            return false;
        }

        return true;
    }
}

