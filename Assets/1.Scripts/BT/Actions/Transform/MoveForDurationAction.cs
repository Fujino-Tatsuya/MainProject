using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Move For Duration", story: "[Agent] Move to [NewPosition] For [Duration]", category: "Action/Transform", id: "748ff5c486212c8001239a31393233c1")]
public partial class MoveForDurationAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<Vector3> NewPosition;
    [SerializeReference] public BlackboardVariable<float> Duration;

    float timer = 0f;
    float distance = 0f;
    float speed = 0f;
    Vector3 direction;
    Transform agentTransform;
    NavMeshAgent navMeshAgent;
    Rigidbody rigidbody;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        agentTransform = Agent.Value.transform;
        navMeshAgent = Agent.Value.GetComponent<NavMeshAgent>();
        rigidbody = Agent.Value.GetComponent<Rigidbody>();
        timer = 0f;

        distance = Vector3.Distance(NewPosition.Value, agentTransform.position);
        speed = distance / Duration.Value;

        direction = (NewPosition.Value - agentTransform.position).normalized;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        timer += Time.deltaTime;
        if (timer >= Duration.Value)
        {
            MoveTo(NewPosition.Value);
            return Status.Success;
        }

        MoveBy(direction * speed * Time.deltaTime);

        return Status.Running;
    }

    protected override void OnEnd()
    {
    }

    void MoveBy(Vector3 delta)
    {
        if (CanUseNavMeshAgent())
        {
            navMeshAgent.Move(delta);
            return;
        }

        if (rigidbody != null)
        {
            rigidbody.MovePosition(rigidbody.position + delta);
            return;
        }

        agentTransform.position += delta;
    }

    void MoveTo(Vector3 position)
    {
        if (CanUseNavMeshAgent())
        {
            navMeshAgent.Warp(position);
            return;
        }

        if (rigidbody != null)
        {
            rigidbody.MovePosition(position);
            return;
        }

        agentTransform.position = position;
    }

    bool CanUseNavMeshAgent()
    {
        return navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;
    }

    bool CheckValid()
    {
        if (Agent.Value == null)
        {
            Debug.LogError("[BT] Agent is null");
            return false;
        }

        if (Duration.Value <= 0f)
        {
            Debug.LogError("[BT] Duration is null");
            return false;
        }

        return true;
    }
}

