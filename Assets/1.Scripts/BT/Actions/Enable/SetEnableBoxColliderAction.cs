using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "SetEnableBoxCollider", story: "Set [gameObject] BoxCollider [Enable]", category: "Action/Enable", id: "443cac9f89d764ffa64b850e259a8028")]
public partial class SetEnableBoxColliderAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> gameObject;
    [SerializeReference] public BlackboardVariable<bool> Enable;

    protected override Status OnStart()
    {
        if (gameObject == null || gameObject.Value == null)
        {
            Debug.LogError("[BT] GameObject is null.");
            return Status.Failure;
        }

        var boxCollider = gameObject.Value.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            Debug.LogError("[BT] BoxCollider component not found on the GameObject.");
            return Status.Failure;
        }

        boxCollider.enabled = Enable.Value;
        return Status.Success;
    }
}