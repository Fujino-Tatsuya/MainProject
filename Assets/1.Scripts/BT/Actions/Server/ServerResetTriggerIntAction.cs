using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerResetTrigger_int", story: "[Server] ResetTrigger [State]", category: "Action/Server", id: "f8afe922b804c87b9730d5b2d381f8a6")]
public partial class ServerResetTriggerIntAction : Action
{
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;
    [SerializeReference] public BlackboardVariable<int> State;

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

