using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "GetSpawnPoint", story: "Get [SpawnPoint] From [SpawnPointer]", category: "Action/Transform", id: "6e8bdb9d21be74a87717802fd600842b")]
public partial class GetSpawnPointAction : Action
{
    [SerializeReference] public BlackboardVariable<Vector3> SpawnPoint;
    [SerializeReference] public BlackboardVariable<SpawnPointer> SpawnPointer;

    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        SpawnPoint.Value = SpawnPointer.Value.SpawnPoint;
        return Status.Success;
    }

    bool CheckValid()
    {
        if (SpawnPointer.Value == null)
        {
            Debug.LogError("[BT] SpawnPoiner is null");
            return false;
        }
        return true;
    }
}

