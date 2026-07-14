using System;
using Unity.Behavior;
using Unity.Properties;
using Unity.VisualScripting;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Remove RandomAttack", story: "Remove [BasicAttackType] to [BaseAttackChoice]", category: "Action/Attack", id: "5e887726eda93bbd69c256ade9e100ac")]
public partial class RemoveRandomAttackAction : Action
{
    [SerializeReference] public BlackboardVariable BasicAttackType;
    [SerializeReference] public BlackboardVariable<BaseAttackChoice> BaseAttackChoice;

    // 블랙보드 바인딩이 끊겼을 때 GetComponent 폴백에 사용할 에이전트(Self).
    [SerializeReference] public BlackboardVariable<GameObject> Agent;

    protected override Status OnStart()
    {
        BaseAttackChoice choice = ResolveAttackChoice();
        if (choice == null)
            return Status.Failure;

        choice.RemoveType(BasicAttackType.ObjectValue.ConvertTo<System.Enum>());
        return Status.Success;
    }

    BaseAttackChoice ResolveAttackChoice()
    {
        // 진단: 변수 자체가 null(바인딩 유실)인지, 값만 null(원본 미할당)인지 구분한다.
        if (BaseAttackChoice == null)
            Edit.LogWarning("[BT] RemoveRandomAttack: BaseAttackChoice variable itself is null (binding lost).");
        else if (BaseAttackChoice.Value == null)
            Edit.LogWarning("[BT] RemoveRandomAttack: BaseAttackChoice value is null (source variable not assigned).");
        else
            return BaseAttackChoice.Value;

        // 폴백: 에이전트에서 직접 컴포넌트를 찾는다.
        if (Agent != null && Agent.Value != null)
        {
            BaseAttackChoice found = Agent.Value.GetComponentInChildren<BaseAttackChoice>();
            if (found != null)
            {
                Edit.LogWarning($"[BT] RemoveRandomAttack: fallback resolved BaseAttackChoice from {Agent.Value.name}.");
                return found;
            }
        }

        Debug.LogError("[BT] BasicAttackChoice is null. Please assign a valid BaseAttackChoice.");
        return null;
    }
}
