using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "BombAction", story: "[BombLauncher] bomb action", category: "Action/Attack", id: "c1a2b3d4e5f607182930a4b5c6d7e8f9")]
public partial class BombAction : Action
{
    [BlackboardEnum]
    public enum BombActionMode
    {
        Hold,
        Throw
    }

    // Unity Behavior 노드 인스펙터는 public 멤버만 수집하므로 public이어야 노드에 노출됩니다.
    [SerializeReference] public BlackboardVariable<BombActionMode> Mode;
    [SerializeReference] public BlackboardVariable<BombLauncher> BombLauncher;

    protected override Status OnStart()
    {
        if (BombLauncher.Value == null)
        {
            Debug.LogError("[BT] BombLauncher is null");
            return Status.Failure;
        }

        BombLauncher.Value.BombHold();

        return Status.Success;
    }
}
