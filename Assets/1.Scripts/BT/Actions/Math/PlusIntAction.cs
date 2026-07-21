using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Plus_int", story: "Set [A] Plus [B] To [C]", category: "Action/Math", id: "7d0163e8f7931fec3d435f2e5baf186a")]
public partial class PlusIntAction : Action
{
    [SerializeReference] public BlackboardVariable<int> A;
    [SerializeReference] public BlackboardVariable<int> B;
    [SerializeReference] public BlackboardVariable<int> C;

    protected override Status OnStart()
    {
        if (!CheckValid())
        {
            return Status.Failure;
        }

        C.Value = A.Value + B.Value;

        return Status.Success;
    }

    bool CheckValid()
    {
        if (A == null || B == null || C == null)
        {
            Debug.LogError("[BT] PlusIntAction: A, B, and C must not be null.");
            return false;
        }
        return true;
    }
}

