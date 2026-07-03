using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[Condition(
    name: "Check Collision In Box",
    category: "Conditions",
    story: "[Agent] BoxCollider overlaps object with [Tag]",
    id: "9c4e71a8d25f4b3e8a60c1f5b7d2e943")]
public partial class CheckCollisionInBoxCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<string> Tag;

    public override bool IsTrue()
    {
        if (Agent.Value == null)
        {
            return false;
        }

        BoxCollider boxCollider = Agent.Value.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            return false;
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

            return true;
        }

        return false;
    }
}
