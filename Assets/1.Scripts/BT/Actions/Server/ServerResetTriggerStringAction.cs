using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerResetTrigger_string", story: "[Server] ResetTrigger [State]", category: "Action/Server", id: "3e0e30211eabe21d38d1eee4b815a67a")]
public partial class ServerResetTriggerStringAction : Action
{
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;
    [SerializeReference] public BlackboardVariable<string> State;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Server.Value.ServerResetTrigger(State.Value);

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

        return true;
    }
}

