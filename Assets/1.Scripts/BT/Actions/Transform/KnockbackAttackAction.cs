using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Knockback Attack", story: "[Target] [KnobackAttack]", category: "Action/Attack", id: "68b3c395ab6c0d366aa9c01eb2b26a4e")]
public partial class KnockbackAttackAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<KnockbackAttack> KnobackAttack;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        KnobackAttack.Value.ApplyKnockbackAttack(Target.Value);
        return Status.Success;
    }

    bool CheckValid()
    {
        if (Target.Value == null)
        {
            Debug.LogError("Target is null");
            return false;
        }

        if (KnobackAttack.Value == null)
        {
            Debug.LogError("KnobackAttack script is null");
            return false;
        }

        return true;
    }
}

