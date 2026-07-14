using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ServerSetAnimInteger_string_int", story: "[Server] Change [State] to [Value]", category: "Action/Server", id: "49bfe3154fd54f92c73bc14f8083c279")]
public partial class ServerSetAnimIntegerStringIntAction : Action
{
    [SerializeReference] public BlackboardVariable<string> State;
    [SerializeReference] public BlackboardVariable<int> Value;
    [SerializeReference] public BlackboardVariable<ServerSetAnimState> Server;

    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        Server.Value.ServerSetInteger(State.Value, Value.Value);

        return Status.Success;
    }

    bool CheckValid()
    {
        if (Server == null)
        {
            Debug.LogError("[BT] ServerSetAnimState is not assigned.");
            return false;
        }

        if(State == null)
        {
            Debug.LogError("[BT] State variable is not assigned.");
            return false;
        }

        if(Value == null)
        {
            Debug.LogError("[BT] Value variable is not assigned.");
            return false;
        }

        return true;
    }
}
