using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "GetRamdomAttackType", story: "Get [AttackType] From [BaseAttackChoice]", category: "Action/Attack", id: "bc8f56b5e42cb592fc1b3f794594ee67")]
public partial class GetRamdomAttackTypeAction : Action
{
    [SerializeReference] public BlackboardVariable AttackType;
    [SerializeReference] public BlackboardVariable<BaseAttackChoice> BaseAttackChoice;
    [SerializeReference] public BlackboardVariable<float> CurrentDistance;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Type attackType = AttackType.ObjectValue.GetType();

        int res = BaseAttackChoice.Value.GetRandomAttack(CurrentDistance.Value);
        AttackType.ObjectValue = Enum.ToObject(attackType, res);

        return Status.Success;
    }

    bool CheckValid()
    {
        Type attackType = AttackType.ObjectValue.GetType();
        if (!attackType.IsEnum)
        {
            Debug.LogError("[BT] AttackType is not Enum.");
            return false;
        }

        if (BaseAttackChoice == null)
        {
            Debug.LogError("[BT] BaseAttackChoice is null");
            return false;
        }

        return true;
    }
}