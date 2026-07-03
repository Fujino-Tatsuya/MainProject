using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;
using System.Collections.Generic;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Set Position Through Raycast", story: "Set [Agent] [Position] With [Target] Direction", category: "Action/Transform", id: "5cfd139b612d936d1d6199a3a2435669")]
public partial class SetPositionThroughRaycastAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<Vector3> Position;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<List<string>> CollisionLayer;

    // 벽 표면 점 근처에서 도달 가능한 NavMesh 지점을 찾을 때의 탐색 반경.
    [SerializeReference] public BlackboardVariable<float> MaxDistance = new BlackboardVariable<float>(3f);

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        Vector3 direction = Target.Value.transform.position - Agent.Value.transform.position;
        direction.y = 0f; // Ignore vertical difference for horizontal direction
        direction = direction.normalized;

        if (!Physics.Raycast(Agent.Value.transform.position, direction, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask(CollisionLayer.Value.ToArray())))
        {
            Debug.LogError("Failed to Raycast");
            return Status.Failure;
        }

        // 벽 표면 점(hit.point)은 NavMesh 바깥이라 그대로 목적지로 쓰면 NavMeshAgent가
        // 부분경로로 벽 앞에서 멈춰 Navigate가 Success를 반환하지 못한다.
        // 벽 점 근처의 도달 가능한 NavMesh 지점을 목적지로 사용한다.
        Vector3 wallPoint = hit.point;

        // 에이전트가 걸을 수 있는 영역만 대상으로 삼는다(없으면 전체 영역).
        int areaMask = NavMesh.AllAreas;
        NavMeshAgent navAgent = Agent.Value.GetComponentInChildren<NavMeshAgent>();
        if (navAgent != null)
            areaMask = navAgent.areaMask;

        if (NavMesh.SamplePosition(wallPoint, out NavMeshHit navHit, MaxDistance.Value, areaMask))
        {
            Position.Value = navHit.position;
        }
        else
        {
            // NavMesh를 못 찾으면 원시 벽 점으로 폴백한다.
            // Failure를 반환하면 상위 Sequence가 즉시 종료돼 Idle 정리 블록이 스킵되고
            // Dead로 전파되므로, 여기서는 Success를 유지한다.
            Position.Value = wallPoint;
            Debug.LogWarning($"SamplePosition failed near {wallPoint} (maxDistance={MaxDistance.Value}), using raw wall point.");
        }

        if (NavMesh.Raycast(Agent.Value.transform.position, Position.Value, out NavMeshHit navMeshHit, areaMask))
        {
            Position.Value = navMeshHit.position;
        }

        Debug.Log($"SetPositionThroughRaycastAction: Position set to {Position.Value}");

        return Status.Success;
    }

    bool CheckValid()
    {
        if (Agent.Value == null)
        {
            Debug.LogError("Agent is null");
            return false;
        }

        if (Target.Value == null)
        {
            Debug.LogError("Target is null");
            return false;
        }

        return true;
    }
}
