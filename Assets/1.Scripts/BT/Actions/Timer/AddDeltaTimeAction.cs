using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "AddDeltaTime", story: "Add DeltaTime to [Var]", category: "Action/Timer", id: "af1f9a2ed0b2759b8912e5c851d741dd")]
public partial class AddDeltaTimeAction : Action
{
    [SerializeReference] public BlackboardVariable<float> Var;

    protected override Status OnStart()
    {
        if (Var == null)
        {
            Debug.LogError("Var is null");
            return Status.Failure;
        }

        Var.Value += Time.deltaTime;

        return Status.Success;
    }

}

