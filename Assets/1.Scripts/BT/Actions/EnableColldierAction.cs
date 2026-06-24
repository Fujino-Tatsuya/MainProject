using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "EnableColldier", story: "Set Collider [Active] through [Script]", category: "Action/Physics", id: "cdc83cb164efbb387a2a19e8020f6066")]
public partial class EnableColldierAction : Action
{
    [SerializeReference] public BlackboardVariable<bool> Active;
    [SerializeReference] public BlackboardVariable<EnableCollider> Script;
    protected override Status OnStart()
    {
        if (Script == null)
        {
            Debug.LogError("Script is null");
            return Status.Failure;
        }

        Script.Value.SetEnableCollider(Active.Value);
        return Status.Success;
    }

}

