using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Set Animtor Enum", story: "Set [Parameter] in [Animator] to [Value]", category: "Action/Animation", id: "96848efbfa62fbd8106e20afe1491daa")]
public partial class SetAnimtorEnumAction : Action
{
    [SerializeReference] public BlackboardVariable<string> Parameter;
    [SerializeReference] public BlackboardVariable<Animator> Animator;
    [SerializeReference] public BlackboardVariable Value;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        int value = Convert.ToInt32(Value.ObjectValue);
        Animator.Value.SetInteger(Parameter.Value, value);

        return Status.Success;
    }

    bool CheckValid()
    {
        if (Parameter == null)
        {
            Debug.LogError("Parameter is null");
            return false;
        }

        if (Animator == null)
        {
            Debug.LogError("Animator is null");
            return false;
        }

        if (Value == null)
        {
            Debug.LogError("Value is null");
            return false;
        }

        Type type = Value.ObjectValue.GetType();
        if (!type.IsEnum)
        {
            Debug.LogError("Value Type is not Enum");
            return false;
        }

        return true;
    }
}

