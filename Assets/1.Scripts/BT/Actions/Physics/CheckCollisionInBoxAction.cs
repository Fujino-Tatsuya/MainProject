using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Check Collision In Box",
    story: "Check [Agent] BoxCollider collisions with [Tag]",
    description: "Checks for collisions inside the Agent's BoxCollider using OverlapBox." +
    "\nIf an object matching the tag is found, it is stored in [CollidedObject].",
    category: "Action/Physics",
    id: "3f8a52c19b7d4e06a1c5d2b8e94f7a60")]
public partial class CheckCollisionInBoxAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<string> Tag;
    [Tooltip("[Out Value] This field is assigned with the collided object, if a collision was found.")]
    [SerializeReference] public BlackboardVariable<GameObject> CollidedObject;

    protected override Status OnStart()
    {
        if (Agent.Value == null)
        {
            LogFailure("No agent set.");
            return Status.Failure;
        }

        BoxCollider boxCollider = Agent.Value.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            LogFailure("No BoxCollider found on the agent.");
            return Status.Failure;
        }

        // BoxCollider의 로컬 center/size를 월드 기준으로 변환해 OverlapBox에 사용한다.
        UnityEngine.Transform boxTransform = boxCollider.transform;
        Vector3 worldCenter = boxTransform.TransformPoint(boxCollider.center);
        Vector3 worldHalfExtents = Vector3.Scale(boxCollider.size, boxTransform.lossyScale) * 0.5f;

        Collider[] hitColliders = Physics.OverlapBox(worldCenter, worldHalfExtents, boxTransform.rotation);

        for (int i = 0; i < hitColliders.Length; i++)
        {
            Collider hitCollider = hitColliders[i];

            // 자기 자신(에이전트 및 하위 콜라이더)은 제외한다.
            if (hitCollider.transform.IsChildOf(Agent.Value.transform))
            {
                continue;
            }

            if (Tag != null && !string.IsNullOrEmpty(Tag.Value) && !hitCollider.CompareTag(Tag.Value))
            {
                continue;
            }

            CollidedObject.Value = hitCollider.gameObject;
            return Status.Success;
        }

        return Status.Failure;
    }
}
