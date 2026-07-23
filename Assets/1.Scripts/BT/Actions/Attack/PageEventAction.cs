using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "PageEvent", story: "[AttackChoice] PageEvent [Page]", category: "Action/Attack", id: "d2b7e4c1a9f3486ba05c7d1e8f60b3a2")]
public partial class PageEventAction : Action
{
    [SerializeReference] public BlackboardVariable<BaseAttackChoice> AttackChoice;
    [SerializeReference] public BlackboardVariable<int> Page;

    protected override Status OnStart()
    {
        if (AttackChoice.Value == null)
        {
            Debug.LogError("[BT] AttackChoice is null");
            return Status.Failure;
        }

        AttackChoice.Value.PageEvent(Page.Value);

        return Status.Success;
    }
}
