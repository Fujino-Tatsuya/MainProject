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

        // 바닥 판정은 GroundProbe로 통일한다 — BT 블랙보드의 Ground 이름만 쓰면 생성맵 바닥(Default)을
        // 구조적으로 못 맞히고, 보스 자기 히트박스(Default 레이어)를 바닥으로 오인하는 문제도 있다.
        if (GroundProbe.TryFindGround(target, LayerMask.GetMask(Ground), out RaycastHit hit, out string report))
        {
            target.y = GroundProbe.SurfaceY(hit);
        }
        else
        {
            Edit.LogWarning($"[BT] 폭탄 착지 지점을 못 찾아 투척 높이를 그대로 씁니다({target}) — {report}");
        }

        //BombInstance.Value.GetComponent<NetworkObject>().TryRemoveParent(true);
        _bombController.Launch(target, FlyingDuration.Value, ArcHeight.Value);

        return Status.Success;
    }

    bool CheckValid()
    {
        if (BombInstance.Value == null)
        {
            Debug.LogError("[BT] BombInstance is null");
            return false;
        }

        _bombController = BombInstance.Value.GetComponent<BombController>();
        if (_bombController == null)
        {
            Debug.LogError("[BT] BombInstance doesn't include BombController component");
            return false;
        }

        if (Agent.Value == null)
        {
            Debug.LogError("[BT] Agent is null");
            return false;
        }

        if (ThrowDistance.Value <= 0)
        {
            Debug.LogError("[BT] ThrowDistance is under than 0");
            return false;
        }

        if (FlyingDuration.Value <= 0)
        {
            Debug.LogError("[BT] FlyingDuration is under than 0");
            return false;
        }

        if (ArcHeight.Value <= 0)
        {
            Debug.LogError("[BT] ArcHeight is under than 0");
            return false;
        }

        if (ThrowLocalDirection.Value == Vector3.zero)
        {
            Debug.LogError("[BT] ThrowLocalDirection is 0");
            return false;
        }
        return true;
    }
}

