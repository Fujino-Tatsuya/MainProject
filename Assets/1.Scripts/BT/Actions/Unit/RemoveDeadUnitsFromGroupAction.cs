using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "RemoveDeadUnitsFromGroup", story: "Remove dead units from [TargetGroup]", category: "Action/Unit", id: "b3f7c1a94d2e4f8ab6c05e91d7a2f430")]
public partial class RemoveDeadUnitsFromGroupAction : Action
{
    [SerializeReference] public BlackboardVariable<List<GameObject>> TargetGroup;

    private readonly List<Unit> cachedUnits = new List<Unit>();

    protected override Status OnStart()
    {
        CacheUnits();
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        for (int i = cachedUnits.Count - 1; i >= 0; i--)
        {
            Unit unit = cachedUnits[i];
            if (unit != null && unit.CurrentHealth > 0)
                continue;

            cachedUnits.RemoveAt(i);

            if (unit != null)
                TargetGroup.Value.Remove(unit.gameObject);
        }

        return Status.Running;
    }

    private void CacheUnits()
    {
        cachedUnits.Clear();

        if (TargetGroup?.Value == null)
        {
            Debug.LogError("[BT] RemoveDeadUnitsFromGroup: TargetGroup list is null.");
            return;
        }

        foreach (GameObject obj in TargetGroup.Value)
        {
            Unit unit = obj.GetComponent<Unit>();
            if (unit == null)
            {
                Edit.LogError("[BT] RemoveDeadUnitsFromGroup: 해당 오브젝트는 Unit 컴포넌트를 소유하고 있지 않습니다.");
                return;
            }
            cachedUnits.Add(unit);
        }
    }
}
