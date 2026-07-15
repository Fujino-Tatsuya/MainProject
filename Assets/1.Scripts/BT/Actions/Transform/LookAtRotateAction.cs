using System;
using Unity.AppUI.Core;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "LookAtRotate", story: "[Agent] Rotate Looking at [Target]", category: "Action/Transform", id: "5211dbf2e6692c5b9dfc18d68e9a2c1e")]
public partial class LookAtRotateAction : Action
{
    [SerializeReference] public BlackboardVariable<Transform> Agent;
    [SerializeReference] public BlackboardVariable<Transform> Target;
    [SerializeReference] public BlackboardVariable<float> Duration;
    [SerializeReference] public BlackboardVariable<bool> LimitToYAxis = new BlackboardVariable<bool>(false);

    Quaternion m_StartRotation;
    Quaternion m_EndRotation;
    float t = 0f;
    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        t = 0f;

        m_StartRotation = Agent.Value.rotation;
        Vector3 targetPosition = Target.Value.position;
        if (LimitToYAxis.Value)
            targetPosition.y = Agent.Value.position.y;

        Vector3 dir = targetPosition - Agent.Value.position;
        if (dir.sqrMagnitude < 0.0001f)
            return Status.Success;   // 겹쳐 있으면 회전할 필요 없음

        m_EndRotation = Quaternion.LookRotation(dir, Vector3.up);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        t += Time.deltaTime / Duration.Value;
        float clampedT = Mathf.Clamp01(t);

        Agent.Value.rotation = Quaternion.Slerp(m_StartRotation, m_EndRotation, clampedT);

        if (clampedT >= 1f)
        {
            return Status.Success;
        }

        return Status.Running;
    }

    bool CheckValid()
    {
        if (Agent.Value == null || Target.Value == null)
        {
            LogFailure($"Missing Agent or Target.");
            return false;
        }

        if(Duration.Value <= 0f)
        {
            LogFailure($"Invalid Duration.");
            return false;
        }

        return true;
    }
}
