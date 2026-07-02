using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ResetVelocity", story: "Reset [NavMeshAgent] Velocity", category: "Action/Navigation", id: "98ee10b7aacf7da63dd31e0f3c24ba47")]
public partial class ResetVelocityAction : Action
{
    [SerializeReference] public BlackboardVariable<NavMeshAgent> NavMeshAgent;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        NavMeshAgent.Value.velocity = Vector3.zero;
        return Status.Success;
    }

    bool CheckValid()
    {
        if (NavMeshAgent.Value == null)
        {
            Debug.LogError("NavMeshAgent is null");
            return false;
        }

        return true;
    }
}

