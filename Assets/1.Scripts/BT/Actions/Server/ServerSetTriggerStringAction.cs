using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerSetTrigger_string", story: "[Server] Trigger [State]", category: "Action/Server", id: "96f0148c769f785d94139722a353d70c")]
public partial class ServerSetTriggerStringAction : Action
{
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;
    [SerializeReference] public BlackboardVariable<string> State;
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

