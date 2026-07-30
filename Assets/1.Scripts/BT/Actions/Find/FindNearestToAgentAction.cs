using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "FindNearestToAgent", story: "Find nearest in [TargetGroup] from [Agent] into [TargetPlayer]", category: "Action/Find", id: "a1e9d4708c3b45f2b9d716e0c4835f21")]
public partial class FindNearestToAgentAction : Action
{
    [SerializeReference] public BlackboardVariable<List<GameObject>> TargetGroup;
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<GameObject> TargetPlayer;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Vector3 origin = Agent.Value.transform.position;
        GameObject nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (GameObject candidate in TargetGroup.Value)
        {
            if (candidate == null)
                continue;

            float sqrDistance =
                (candidate.transform.position - origin).sqrMagnitude;

            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = candidate;
            }
        }

        TargetPlayer.Value = nearest;
        return Status.Success;
    }

    bool CheckValid()
    {
        if (Agent?.Value == null)
        {
            Debug.LogError("[BT] FindNearestToAgent: Agent is null.");
            return false;
        }

        if (TargetGroup?.Value == null)
        {
            Debug.LogError("[BT] FindNearestToAgent: TargetGroup list is null.");
            return false;
        }

        return true;
    }
}
