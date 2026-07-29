using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

/// <summary>
/// 지정한 애니메이션 State가 재생을 마칠 때까지(normalizedTime >= 1) 대기하는 Action.
///
/// 기존처럼 클립 재생시간(초)을 하드코딩해 대기하는 대신, Animator의 실제 재생 진행도를
/// 기준으로 삼는다. 따라서 HitStop/SlowMotion으로 animator.speed를 조정해도 normalizedTime
/// 진행이 함께 느려져, 실제 애니메이션과 상태 전환 타이밍이 어긋나지 않는다.
///
/// 전환(transition)을 고려하여:
/// - 목표 State로 전이 중이면 '다음 상태'를 목표로 간주해 조기에 진입을 인식한다.
/// - 목표 State에서 다른 곳으로 전이 중이면 '현재 상태'가 아직 목표이므로 진입 유지로 본다.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Wait For AnimState", story: "Wait For [StateName] in [Layer] [Animator]", category: "Action/Animation", id: "a7f3c1e290b84d6f8c25e0139bd7a4f2")]
public partial class WaitForAnimStateAction : Action
{
    [SerializeReference] public BlackboardVariable<Animator> Animator;
    [SerializeReference] public BlackboardVariable<int> Layer;
    [SerializeReference] public BlackboardVariable<string> StateName;

    Animator anim;
    int layer;

    int hash;
    bool entered;


    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        anim = Animator.Value;
        layer = Layer.Value;

        hash = UnityEngine.Animator.StringToHash(StateName.Value);
        entered = false;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        bool isState = IsTargetState(out AnimatorStateInfo info);

        if (!isState)
            // 진입한 적 있으면 상태를 벗어난 것(=완료), 아니면 아직 진입 대기 중.
            return entered ? Status.Success : Status.Running;

        entered = true;
        return info.normalizedTime >= 1f ? Status.Success : Status.Running;
    }

    /// <summary>
    /// 현재(또는 전이 중이라면 다음) 애니메이션 상태가 목표 State인지 판정하고,
    /// 해당 AnimatorStateInfo를 out으로 반환한다.
    /// </summary>
    bool IsTargetState(out AnimatorStateInfo info)
    {
        if (anim.IsInTransition(layer))
        {
            // 목표 State로 전이 중이면 조기에 진입으로 인식한다.
            AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(layer);
            if (next.shortNameHash == hash)
            {
                info = next;
                return true;
            }
        }

        info = anim.GetCurrentAnimatorStateInfo(layer);
        return info.shortNameHash == hash;
    }

    bool CheckValid()
    {
        if (Animator == null || Animator.Value == null)
        {
            Edit.LogError("[BT] Animator is null");
            return false;
        }

        if (StateName == null || StateName.Value == "")
        {
            Edit.LogError("[BT] Please Fill in StateName");
            return false;
        }

        return true;
    }
}
