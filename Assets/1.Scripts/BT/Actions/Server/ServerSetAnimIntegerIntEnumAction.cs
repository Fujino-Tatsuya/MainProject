using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerSetAnimInteger_int_enum", story: "[Server] Change [State] to [Value]", category: "Action/Server", id: "e97c760e9eacc71d9b1363390908fde1")]
public partial class ServerSetAnimIntegerIntEnumAction : Action
{
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;
    [SerializeReference] public BlackboardVariable<int> State;
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
            Debug.LogError("ServerSetAnimState is not assigned.");
            return false;
        }

        if (State == null)
        {
            Debug.LogError("State variable is not assigned.");
            return false;
        }

        if (Value == null)
        {
            Debug.LogError("Value variable is not assigned.");
            return false;
        }

        Type valueType = Value.ObjectValue.GetType();
        if (!valueType.IsEnum)
        {
            Debug.LogError("Value variable must be an enum.");
            return false;
        }

        return true;
    }
}

