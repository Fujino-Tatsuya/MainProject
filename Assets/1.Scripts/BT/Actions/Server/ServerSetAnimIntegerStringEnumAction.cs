using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerSetAnimInteger_string_enum", story: "[Server] Change [State] to [Value]", category: "Action/Server", id: "0523473a22858b5d24061ddfe9f30ed6")]
public partial class ServerSetAnimIntegerStringEnumAction : Action
{
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;
    [SerializeReference] public BlackboardVariable<string> State;
    [SerializeReference] public BlackboardVariable Value;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Server.Value.ServerSetInteger(State.Value, (System.Enum)Value.ObjectValue);
        return Status.Success;
    }

    bool CheckValid()
    {
        if (Server == null)
        {
            Debug.LogError("[BT] ServerSetAnimState is not assigned.");
            return false;
        }

        if (State == null)
        {
            Debug.LogError("[BT] State variable is not assigned.");
            return false;
        }

        if (Value == null)
        {
            Debug.LogError("[BT] Value variable is not assigned.");
            return false;
        }

        Type valueType = Value.ObjectValue.GetType();

        if (!valueType.IsEnum)
        {
            Debug.LogError($"[BT] Value must be an enum type, but got {valueType}");
            return false;
        }

        return true;
    }
}

