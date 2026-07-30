using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "GetPlayerCount", story: "Set [TotalPlayerNumber] To Count Of [TargetGroup]", category: "Action/Unit", id: "d1a4f8b2e6c04a7c9b3e5d7f10248ac6")]
public partial class GetPlayerCountAction : Action
{
    [SerializeReference] public BlackboardVariable<List<GameObject>> TargetGroup;
    [SerializeReference] public BlackboardVariable<int> TotalPlayerNumber;

    protected override Status OnStart()
    {
        if (TargetGroup == null || TotalPlayerNumber == null)
        {
            Debug.LogError("[BT] GetPlayerCountAction: TargetGroup and TotalPlayerNumber must not be null.");
            return Status.Failure;
        }

        if (TargetGroup.Value == null)
        {
            Debug.LogError("[BT] GetPlayerCountAction: TargetGroup list is null.");
            return Status.Failure;
        }

        TotalPlayerNumber.Value = TargetGroup.Value.Count;

        return Status.Success;
    }
}
