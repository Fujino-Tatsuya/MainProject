using System;
using Unity.Behavior;
using Unity.Netcode;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ThrowBomb", story: "[ThrowLocalDirection] [ThrowDistance] [FlyingDuration] [ArcHeight] [BombInstance]", category: "Action/Attack", id: "bff418a9564c15fe3504a661a441b8a5")]
public partial class ThrowBombAction : Action
{
    [SerializeReference] public BlackboardVariable<Vector3> ThrowLocalDirection;
    [SerializeReference] public BlackboardVariable<float> ThrowDistance;
    [SerializeReference] public BlackboardVariable<float> FlyingDuration;
    [SerializeReference] public BlackboardVariable<float> ArcHeight;
    [SerializeReference] public BlackboardVariable<GameObject> BombInstance;
    [SerializeReference] public BlackboardVariable<GameObject> Agent;
    [SerializeReference] public BlackboardVariable<string> Ground;

    BombController _bombController;
    protected override Status OnStart()
    {
        if(!CheckValid())
            return Status.Failure;

        Transform agentTransform = Agent.Value.transform;
        Vector3 dir = agentTransform.TransformDirection(ThrowLocalDirection.Value).normalized;
        Vector3 throwVector = dir * ThrowDistance.Value;
        Vector3 target = agentTransform.position + throwVector;

        RaycastHit hit;
        if (Physics.Raycast(target, Vector3.down, out hit, Mathf.Infinity, LayerMask.GetMask(Ground)))
        {
            target.y = hit.point.y;
        }

        //BombInstance.Value.GetComponent<NetworkObject>().TryRemoveParent(true);
        _bombController.Launch(target, FlyingDuration.Value, ArcHeight.Value);

        return Status.Success;
    }

    bool CheckValid()
    {
        if (BombInstance.Value == null)
        {
            Debug.LogError("BombInstance is null");
            return false;
        }

        _bombController = BombInstance.Value.GetComponent<BombController>();
        if (_bombController == null)
        {
            Debug.LogError("BombInstance doesn't include BombController component");
            return false;
        }

        if (Agent.Value == null)
        {
            Debug.LogError("Agent is null");
            return false;
        }

        if (ThrowDistance.Value <= 0)
        {
            Debug.LogError("ThrowDistance is under than 0");
            return false;
        }

        if (FlyingDuration.Value <= 0)
        {
            Debug.LogError("FlyingDuration is under than 0");
            return false;
        }

        if (ArcHeight.Value <= 0)
        {
            Debug.LogError("ArcHeight is under than 0");
            return false;
        }

        if (ThrowLocalDirection.Value == Vector3.zero)
        {
            Debug.LogError("ThrowLocalDirection is 0");
            return false;
        }
        return true;
    }
}

