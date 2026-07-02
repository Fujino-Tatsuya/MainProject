using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Set Position Through Raycast", story: "Set [Agent] [Position] With [Target] Direction", category: "Action/Transform", id: "5cfd139b612d936d1d6199a3a2435669")]
public partial class SetPositionThroughRaycastAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<Vector3> Position;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        //Vector3 direction = (Target.Value.transform.position - Agent.Value.transform.position).normalized;
        
        //if (!Physics.Raycast(Agent.Value.transform.position, direction, out RaycastHit hit, Mathf.Infinity))
        //{
        //    Debug.LogError("Failed to Raycast");
        //    return Status.Failure;
        //}

        if (NavMesh.Raycast(Agent.Value.transform.position, Position.Value, out NavMeshHit navMeshHit, areaMask))
        {
            Position.Value = navMeshHit.position;
        }

        Debug.Log($"SetPositionThroughRaycastAction: Position set to {Position.Value}");

        return Status.Success;
    }

    bool CheckValid()
    {
        if (Agent.Value == null)
        {
            Debug.LogError("Agent is null");
            return false;
        }

        if (Position.Value == null)
        {
            Debug.LogError("Position is null");
            return false;
        }

        if (Target.Value == null)
        {
            Debug.LogError("Target is null");
            return false;
        }

        return true;
    }
}

