using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ReturnSuccess", story: "Success", category: "Action/Return", id: "8636bc0885e80582330b839329fa91a2")]
public partial class ReturnSuccessAction : Action
{

    protected override Status OnStart()
    {
        return Status.Success;
    }

}

