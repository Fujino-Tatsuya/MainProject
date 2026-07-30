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

        // ⚠️ Mode가 선언만 되고 읽히지 않아, 투척용으로 놓은 노드까지 Hold로 동작했다. 그래서 투척은
        // 애니메이션 이벤트에만 의존했고, BT 주기가 끊기면 조용히 안 나갔다.
        // Wells.asset의 두 노드는 현재 둘 다 Hold이므로 이 변경만으로 동작은 달라지지 않는다.
        // 투척 노드를 Mode: Throw로 바꾸면 BT가 투척을 주도한다(그래프 수정은 담당자 몫).
        BombActionMode mode = Mode != null ? Mode.Value : BombActionMode.Hold;

        switch (mode)
        {
            case BombActionMode.Hold:
                BombLauncher.Value.BombHold();
                break;

            case BombActionMode.Throw:
                BombLauncher.Value.BombThrow();
                break;
        }

        return Status.Success;
    }
}
