using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "CalculateDistance", story: "Calculate [Distance] From [A] to [B]", category: "Action/Calculate", id: "c6094f669a264c8b8d9e27db44cc3919")]
public partial class CalculateDistanceAction : Action
{
    [SerializeReference] public BlackboardVariable<float> Distance;
    [SerializeReference] public BlackboardVariable<Transform> A;
    [SerializeReference] public BlackboardVariable<Transform> B;
    protected override Status OnStart()
    {
        if (!CheckValid())
        {
            return Status.Failure;
        }

        Distance.Value = Vector3.Distance(A.Value.position, B.Value.position);

        return Status.Success;
    }

    bool CheckValid()
    {
        if (Distance == null || A == null || B == null)
        {
            Debug.LogError("[BT] CalculateDistanceAction: One or more variables are not assigned.");
            return false;
        }

        return true;
    }
}

