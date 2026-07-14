using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Set Agent Dash Mode", story: "Set [NavMeshAgent] Dash Mode [Enable]", category: "Action/Navigation", id: "b3f1c7a2d84e49f0a6c2e91b5d07a3c4")]
public partial class SetAgentDashModeAction : Action
{
    [SerializeReference] public BlackboardVariable<NavMeshAgent> NavMeshAgent;
    [SerializeReference] public BlackboardVariable<bool> Enable;

    // 대쉬 중 사용할 가속도. 목표 속도/방향에 즉시 도달하도록 충분히 크게 잡는다.
    [SerializeReference] public BlackboardVariable<float> DashAcceleration = new BlackboardVariable<float>(999f);

    // 대쉬 종료 시 복원할 평상시 값 (프리팹 기본값과 맞출 것).
    [SerializeReference] public BlackboardVariable<float> NormalAcceleration = new BlackboardVariable<float>(8f);

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        NavMeshAgent agent = NavMeshAgent.Value;

        if (Enable.Value)
        {
            // 기존 관성이 대쉬 방향에 섞여 치우치는 것을 방지한다.
            agent.velocity = Vector3.zero;
            agent.acceleration = DashAcceleration.Value;
            // 돌진 중에는 회피 조향이 방향을 밀어내지 않도록 끈다.
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            // 도착 지점 부근에서 감속하지 않고 속도를 유지한다.
            agent.autoBraking = false;
        }
        else
        {
            agent.velocity = Vector3.zero;
            agent.acceleration = NormalAcceleration.Value;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.autoBraking = true;
        }

        return Status.Success;
    }

    bool CheckValid()
    {
        if (NavMeshAgent.Value == null)
        {
            Debug.LogError("[BT] NavMeshAgent is null.");
            return false;
        }
        return true;
    }
}
