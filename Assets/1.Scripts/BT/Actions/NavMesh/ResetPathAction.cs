using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ResetPath", story: "Reset [NavMeshAgent] Path", category: "Action/Navigation", id: "699534503a5d3097231ca9a5e7ba4866")]
public partial class ResetPathAction : Action
{
    [SerializeReference] public BlackboardVariable<NavMeshAgent> NavMeshAgent;

    protected override Status OnStart()
    {
        if(!CheckValid())
                return Status.Failure;

        NavMeshAgent.Value.ResetPath();
        return Status.Success;
    }
    
    bool CheckValid()
    {
        if (NavMeshAgent.Value == null)
        {
            Debug.LogError("NavMeshAgent is null.");
            return false;
        }
        return true;
    }
}

