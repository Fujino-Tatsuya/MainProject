using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerSetTrigger_int", story: "[Server] Trigger [State]", category: "Action/Server", id: "1fe68e4e8c5cbf8d5bbcbe068b15012d")]
public partial class ServerSetTriggerIntAction : Action
{
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;
    [SerializeReference] public BlackboardVariable<int> State;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Server.Value.ServerSetTrigger(State.Value);

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

        return true;
    }
}

