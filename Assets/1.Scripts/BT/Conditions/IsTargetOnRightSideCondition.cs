using System;
using Unity.Behavior;
using Unity.VisualScripting;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "Is Target on RightSide", story: "Is [Target] on the RightSide From [Agent]", category: "Conditions", id: "3c2ee5d421e03f7ce1b21689ff754966")]
public partial class IsTargetOnRightSideCondition : Condition
{
    [SerializeReference] public BlackboardVariable<Transform> Target;
    [SerializeReference] public BlackboardVariable<Transform> Agent;

    public override bool IsTrue()
    {
        if (Target == null)
            Debug.LogError("[BT] Target is null");

        if (Agent == null)
            Debug.LogError("[BT] Agent is null");

        Vector3 agentRightVector = Agent.Value.transform.right;
        Vector3 targetVector = Vector3.Normalize(Target.Value.position - Agent.Value.position);

        float dir = Vector3.Dot(agentRightVector, targetVector);

        bool res = (dir >= 0) ? true : false;
        return res;
    }

}
