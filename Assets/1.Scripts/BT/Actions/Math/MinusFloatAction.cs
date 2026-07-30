using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Minus_Float", story: "Minus [A] and [B] is [C]", category: "Action/Math", id: "c1d94a3e57f2b8064a9e0d3c62b5f817")]
public partial class MinusFloatAction : Action
{
    [SerializeReference] public BlackboardVariable<float> A;
    [SerializeReference] public BlackboardVariable<float> B;
    [SerializeReference] public BlackboardVariable<float> C;

    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        C.Value = A.Value - B.Value;
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
