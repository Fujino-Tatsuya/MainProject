using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "HoldBomb", story: "[BombInstance] Follow [BombSocket]", category: "Action/Attack", id: "ab6a7805afa55650f9659e28a5767364")]
public partial class HoldBombAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> BombInstance;
    [SerializeReference] public BlackboardVariable<GameObject> BombSocket;

    BombController bombController = null;
    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        bombController.Hold(BombSocket.Value.transform);

        return Status.Success;
    }

    bool CheckValid()
    {
        if (BombInstance.Value == null)
        {
            Debug.LogError("BombInstance is null");
            return false;
        }

        if (BombSocket.Value == null)
        {
            Debug.LogError("BombSocket is null");
            return false;
        }

        bombController = BombInstance.Value.GetComponent<BombController>();
        if (bombController == null)
        {
            Debug.LogError("bombController is null");
            return false;
        }

        return true;
    }
}

