using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "Is Current AnimState Equal Too StateName", story: "Is CurrentAnimState in [Layer] [Animator] Equal To [StateName]", category: "Conditions", id: "747cbb07ed14cc2b708b3097bdce42e9")]
public partial class IsCurrentAnimStateEqualTooStateNameCondition : Condition
{
    [SerializeReference] public BlackboardVariable<string> StateName;
    [SerializeReference] public BlackboardVariable<Animator> Animator;
    [SerializeReference] public BlackboardVariable<int> Layer;
    int stateHash;

    public override void OnStart()
    {
        stateHash = UnityEngine.Animator.StringToHash(StateName.Value);
    }

    public override bool IsTrue()
    {
        if (!CheckValid())
            return false;

        AnimatorStateInfo stateInfo = Animator.Value.GetCurrentAnimatorStateInfo(Layer.Value);
        if(stateInfo.shortNameHash != stateHash)
            return false;

        return true;
    }

    bool CheckValid()
    {
        if (StateName.Value == "")
        {
            Debug.LogError("[BT] Please Fill in StateName");
            return false;
        }

        if (Animator.Value == null)
        {
            Debug.LogError("[BT] Animator is null");
            return false;
        }

        return true;
    }
}
