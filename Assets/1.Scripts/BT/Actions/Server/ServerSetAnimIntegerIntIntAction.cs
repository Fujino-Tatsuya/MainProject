using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerSetAnimInteger_int_int", story: "[Server] Change [State] to [Value]", category: "Action/Server", id: "bbda56975b855388c85d25f16fa86fe0")]
public partial class ServerSetAnimIntegerIntIntAction : Action
{
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;
    [SerializeReference] public BlackboardVariable<int> State;
    [SerializeReference] public BlackboardVariable<int> Value;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Server.Value.ServerSetInteger(State.Value, Value.Value);

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

        return true;
    }
}

