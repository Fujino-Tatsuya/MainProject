using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "IncreaseUnitShield", story: "Increase Shield [Amount] in [Unit]", category: "Action/Unit", id: "0d74cffb8a2990f83be2ab36f2924b3e")]
public partial class IncreaseUnitShieldAction : Action
{
    [SerializeReference] public BlackboardVariable<int> Amount;
    [SerializeReference] public BlackboardVariable<Unit> Unit;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Unit.Value.IncreaseShield(Amount.Value);
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

