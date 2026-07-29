using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;
using System.Collections.Generic;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "SetNumberWithTag", story: "[TotalNumber] With [Tag]", category: "Action/Find", id: "ad4d750b0263e71a9f0470f683742d8e")]
public partial class SetNumberWithTagAction : Action
{
    [SerializeReference] public BlackboardVariable<int> TotalNumber;
    [SerializeReference] public BlackboardVariable<string> Tag;
    [SerializeReference] public BlackboardVariable<bool> onlyCountRoot;

    protected override Status OnStart()
    {
        if(!CheckValid())
        {
            return Status.Failure;
        }

        GameObject[] objects = GameObject.FindGameObjectsWithTag(Tag.Value);
        TotalNumber.Value = objects.Length;

        if (onlyCountRoot.Value == true)
        {
            HashSet<GameObject> hash = new HashSet<GameObject>();
            foreach (GameObject obj in objects)
            {
                hash.Add(obj.transform.root.gameObject);
            }
            TotalNumber.Value = hash.Count;
            hash.Clear();
        }

        return Status.Success;
    }

    bool CheckValid()
    {
        if (TotalNumber == null)
        {
            Debug.LogError("[BT] TotalNumber is null");
            return false;
        }
        if (Tag == null)
        {
            Debug.LogError("[BT] Tag is null");
            return false;
        }
        return true;
    }
}

