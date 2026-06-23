using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Return Running", story: "Running", category: "Action/Return", id: "e75ee4de8241d46c2ff0527aa08cc2b2")]
public partial class ReturnRunningAction : Action
{

    protected override Status OnStart()
    {
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        return Status.Running;
    }
}

