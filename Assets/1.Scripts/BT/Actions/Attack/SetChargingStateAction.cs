using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "SetChargingState", story: "Set ChargingState [Script] [Start]", category: "Action/Attack", id: "a48e0dbf03d772d1406a78d0f6c833dd")]
public partial class SetChargingStateAction : Action
{
    [SerializeReference] public BlackboardVariable<ChargeController> Script;
    [SerializeReference] public BlackboardVariable<bool> Start;
    [SerializeReference] public BlackboardVariable<int> PlayerCount;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        if (Start.Value == true)
            Script.Value.StartCharge(PlayerCount.Value);
        else
            Script.Value.EndCharge();

        return Status.Success;
    }

    bool CheckValid()
    {
        if (Script.Value == null)
        {
            Debug.LogError("[BT] Script is null");
            return false;
        }

        if (PlayerCount.Value <= 0)
        {
            Debug.LogError("[BT] PlayerCount should be more than zero");
            return false;
        }

        return true;
    }
}

