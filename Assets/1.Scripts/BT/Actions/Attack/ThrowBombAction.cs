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

        // ⚠️ 생성맵 바닥은 "Ground"가 아니라 **Default** 레이어다(존 프리팹 전수 확인: 바닥·벽 전부 layer 0).
        // BT 블랙보드에 적힌 Ground 이름만 쓰면 레이캐스트가 빗나가고, 빗나가면 target.y가 투척 높이로
        // 남아 **폭탄이 공중에 착지**한다(Launch는 중력 없이 _targetPos로 보간해 그 지점에서 멈춘다).
        // 같은 원인으로 JumpController·BombLauncher에서 이미 두 번 터졌다 → 여기서도 Default를 함께 포함한다.
        //
        // 원점을 살짝 띄우는 이유: target.y가 바닥면과 같거나 미세하게 아래면 표면에서 시작한 광선이
        // MeshCollider를 놓친다. 위에서 아래로 훑어야 안정적으로 맞는다.
        int groundMask = LayerMask.GetMask(Ground) | LayerMask.GetMask("Default", "Ground");
        const float probeUp = 2f;
        const float probeDistance = 200f;

        if (Physics.Raycast(target + Vector3.up * probeUp, Vector3.down, out RaycastHit hit,
                            probeDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            target.y = hit.point.y;
        }
        else
        {
            Edit.LogWarning(
                $"[BT] 폭탄 착지 지점 아래에서 바닥을 찾지 못했습니다({target}) — 투척 높이를 그대로 씁니다. " +
                "폭탄이 공중에 착지하면 이 로그를 먼저 확인할 것.");
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

