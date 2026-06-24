using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;
using Unity.Netcode;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Instantiate Network Object", story: "Instantiate Network [Object]", category: "Action/GameObject", id: "6d4e0207d3cd9ee04fccb055f575eaf0")]
public partial class InstantiateNetworkObjectAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Object;
    NetworkObject _networkObject;

    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        _networkObject.Spawn();
        return Status.Success;
    }

    bool CheckValid()
    {
        if (Object.Value == null)
        {
            Debug.LogError("Object is null");
            return false;
        }

        _networkObject = Object.Value.GetComponent<NetworkObject>();
        if (_networkObject == null)
        {
            Debug.LogError("The Object doesn't include 'NetworkObject' component!");
            return false;
        }

        return true;
    }
}

