using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Plus_Float", story: "Plus [A] and [B] is [C]", category: "Action/Math", id: "8e5372fc13030653b873d10ae9ac6cae")]
public partial class PlusFloatAction : Action
{
    [SerializeReference] public BlackboardVariable<float> A;
    [SerializeReference] public BlackboardVariable<float> B;
    [SerializeReference] public BlackboardVariable<float> C;

    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        C.Value = A.Value + B.Value;
        return Status.Success;
    }

    bool CheckValid()
    {
        if (C == null)
        {
            Debug.LogError("[BT] 계산 결과값을 저장할 변수가 없습니다.");
            return false;
        }
        return true;
    }
}

