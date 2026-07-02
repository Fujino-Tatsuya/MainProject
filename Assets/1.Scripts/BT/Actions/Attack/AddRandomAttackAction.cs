using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Add RandomAttack", story: "Add [BasicAttackType] to [BasicAttackChoice]", category: "Action/Attack", id: "afad3ec8c04f70f9e7e914c16688641a")]
public partial class AddRandomAttackAction : Action
{
    [SerializeReference] public BlackboardVariable BasicAttackType;
    [SerializeReference] public BlackboardVariable<BaseAttackChoice> BasicAttackChoice;

    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        BasicAttackChoice.Value.AddType((System.Enum)BasicAttackType.ObjectValue);
        return Status.Success;
    }

    bool CheckValid()
    {
        if (BasicAttackChoice.Value == null)
        {
            Debug.LogError("BasicAttackChoice is null. Please assign a valid BasicAttackChoice.");
            return false;
        }
        return true;
    }
}

