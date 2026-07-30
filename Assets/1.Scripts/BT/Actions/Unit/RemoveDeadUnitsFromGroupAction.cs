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

    // 명부(roster) — 한 번 본 유닛은 죽어도 여기서 빼지 않는다.
    //
    // ⚠️ 예전에는 이 목록이 곧 "살아 있는 유닛"이어서, 죽는 순간 명부에서도 지웠다.
    //    그러면 부활해서 CurrentHealth > 0 이 되어도 **다시 넣어 줄 경로가 없다** →
    //    TargetGroup 이 영구히 비고, GetPlayerCount 가 0을 반환하고,
    //    그 카운트를 보는 조건이 IsOpen 을 계속 false 로 되돌려 BT가 재개되지 않았다
    //    (1인 플레이에서 부활해도 보스가 안 움직이던 원인).
    //    명부는 유지하고 TargetGroup 만 "살아 있는 부분집합"으로 매 프레임 맞춘다.
    private readonly List<Unit> roster = new List<Unit>();

    protected override Status OnStart()
    {
        roster.Clear();
        AbsorbGroupMembers();
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (TargetGroup?.Value == null)
            return Status.Running;

        // FindGroupsByTag 가 다시 돌아 목록이 채워졌으면 새 멤버를 명부에 흡수한다.
        AbsorbGroupMembers();

        for (int i = roster.Count - 1; i >= 0; i--)
        {
            Unit unit = roster[i];

            // 파괴된 유닛만 명부에서 제거한다(사망은 제거 사유가 아니다 — 부활할 수 있다).
            if (unit == null)
            {
                roster.RemoveAt(i);
                continue;
            }

            bool alive = unit.CurrentHealth > 0;
            bool inGroup = TargetGroup.Value.Contains(unit.gameObject);

            if (!alive && inGroup)
            {
                TargetGroup.Value.Remove(unit.gameObject);
            }
            else if (alive && !inGroup)
            {
                TargetGroup.Value.Add(unit.gameObject);
                Edit.Log(
                    $"[BT/진단] RemoveDeadUnitsFromGroup — {unit.name} 이 살아나 그룹에 복귀했다 " +
                    $"(그룹 {TargetGroup.Value.Count}명).");
            }
        }

        return Status.Running;
    }

    // TargetGroup 에 있는데 명부에 없는 것을 명부로 옮긴다. Unit 이 없는 오브젝트는 건너뛴다.
    private void AbsorbGroupMembers()
    {
        if (TargetGroup?.Value == null)
        {
            Debug.LogError("[BT] RemoveDeadUnitsFromGroup: TargetGroup list is null.");
            return;
        }

        foreach (GameObject obj in TargetGroup.Value)
        {
            if (obj == null)
                continue;

            Unit unit = obj.GetComponent<Unit>();
            if (unit == null)
            {
                // 예전에는 여기서 return 해 나머지를 통째로 포기했다 — 한 개의 잘못된 항목이
                // 명부 전체를 비워 같은 교착을 만든다. 그 항목만 건너뛴다.
                Edit.LogError(
                    $"[BT] RemoveDeadUnitsFromGroup: {obj.name} 에 Unit 컴포넌트가 없어 건너뜁니다.");
                continue;
            }

            if (!roster.Contains(unit))
                roster.Add(unit);
        }
    }
}
