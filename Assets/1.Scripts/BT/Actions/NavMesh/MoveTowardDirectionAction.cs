using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Move Toward Direction", story: "[Agent] Move Toward Specific [Direction]", category: "Action/Navigation", id: "abfca904987819ba19fc80d4257ff168")]
public partial class MoveTowardDirectionAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<Vector3> Direction;
    [SerializeReference] public BlackboardVariable<float> Speed;

    NavMeshAgent navMeshAgent;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        navMeshAgent = Agent.Value.GetComponent<NavMeshAgent>();
        if (navMeshAgent == null)
        {
            Debug.LogError("[BT] Agent does not include NavMeshAgent component");
            return Status.Failure;
        }



        return Status.Running;
    }

    bool CheckValid()
    {
        if (Agent.Value == null)
        {
            Debug.LogError("[BT] Agent is null");
            return false;
        }

        if (Direction.Value == null)
        {
            Debug.LogError("[BT] Direction is null");
            return false;
        }

        return true;
    }
}

