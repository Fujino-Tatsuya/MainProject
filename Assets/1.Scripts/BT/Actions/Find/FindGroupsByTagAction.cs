using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "FindGroupsByTag", story: "Find [TargetGroups] with tag [Name]", category: "Action/Find", id: "dcfd8f3a27636930fe93290ff4749ee3")]
public partial class FindGroupsByTagAction : Action
{
    [SerializeReference] public BlackboardVariable<List<GameObject>> TargetGroups;
    [SerializeReference] public BlackboardVariable<string> Name;
    [SerializeReference] public BlackboardVariable<bool> onlyCountRoot;


    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        GameObject[] targets = GameObject.FindGameObjectsWithTag(Name.Value);
        TargetGroups.Value.Clear();

        if (onlyCountRoot.Value == true)
        {
            HashSet<GameObject> hash = new HashSet<GameObject>();
            foreach (GameObject obj in targets)
            {
                hash.Add(obj.transform.root.gameObject);
            }

            foreach (GameObject obj in hash)
            {
                TargetGroups.Value.Add(obj);
            }

            hash.Clear();
            return Status.Success;
        }

        foreach (GameObject obj in targets)
        {
            TargetGroups.Value.Add(obj);
        }
        return Status.Success;
    }

    bool CheckValid()
    {
        if (TargetGroups?.Value == null)
        {
            Debug.LogError("[BT] FindGroupsByTag: TargetGroups list is null.");
            return false;
        }

        if (string.IsNullOrEmpty(Name?.Value))
        {
            Debug.LogError("[BT] FindGroupsByTag: Name(tag) is null or empty.");
            return false;
        }

        return true;
    }
}

