using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "SetNumberWithTag", story: "[TotalNumber] With [Tag]", category: "Action/Find", id: "ad4d750b0263e71a9f0470f683742d8e")]
public partial class SetNumberWithTagAction : Action
{
    [SerializeReference] public BlackboardVariable<int> TotalNumber;
    [SerializeReference] public BlackboardVariable<string> Tag;

    protected override Status OnStart()
    {
        if(!CheckValid())
        {
            return Status.Failure;
        }

        GameObject[] objects = GameObject.FindGameObjectsWithTag(Tag.Value);
        TotalNumber.Value = objects.Length;

        return Status.Success;
    }

    bool CheckValid()
    {
        if (TotalNumber == null)
        {
            Debug.LogError("TotalNumber is null");
            return false;
        }
        if (Tag == null)
        {
            Debug.LogError("Tag is null");
            return false;
        }
        return true;
    }
}

