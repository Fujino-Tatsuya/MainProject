using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ReturnFail", story: "Failure", category: "Action/Return", id: "3b25f01f7ceaac6d89d5c243d2fb4029")]
public partial class ReturnFailAction : Action
{

    protected override Status OnStart()
    {
        return Status.Failure;
    }

}

