using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Set Position To Target", story: "Set [Position] To [Target]", category: "Action/Transform", id: "4e133024911a15394fb68199822e9bd9")]
public partial class SetPositionToTargetAction : Action
{
    [SerializeReference] public BlackboardVariable<Vector3> Position;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Position.Value = Target.Value.transform.position;

        return Status.Success;
    }

    bool CheckValid()
    {
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

