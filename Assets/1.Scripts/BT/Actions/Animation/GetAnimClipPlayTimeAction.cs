using System;
using Unity.Behavior;
using UnityEngine;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Get AnimClip PlayTime", story: "Get [AnimClip] in [Animator] and Set [PlayTime]", category: "Action/Animation", id: "1b83c36f0b4957bf9f5e79e2cbd52b54")]
public partial class GetAnimClipPlayTimeAction : Action
{
    [SerializeReference] public BlackboardVariable<string> AnimClip;
    [SerializeReference] public BlackboardVariable<Animator> Animator;
    [SerializeReference] public BlackboardVariable<float> PlayTime;

    // 애니메이션 배속 파라미터
    [SerializeReference] public BlackboardVariable<string> Multiplier;

    // State에서 잘라낸 normalized 구간 (예: 0 ~ 100)
    [SerializeReference] public BlackboardVariable<float> ClipStart;
    [SerializeReference] public BlackboardVariable<float> ClipEnd;

    float animSpeed;

    protected override Status OnStart()
    {
        if (!CheckValid())
            return Status.Failure;

        animSpeed = 1f;

        RuntimeAnimatorController controller = Animator.Value.runtimeAnimatorController;

        AnimationClip targetClip = null;
        foreach (var clip in controller.animationClips)
        {
            if (clip.name == AnimClip.Value)
            {
                targetClip = clip;
                break;
            }
        }

        if (targetClip == null)
            return Status.Failure;

        float baseLength = targetClip.length;   // 애니메이션 클립의 기본 재생 길이

        // 잘라낸 구간의 길이
        float trimmedLength = baseLength * (ClipEnd.Value - ClipStart.Value) / 100f;

        // speed를 고려한 실제 재생 시간
        float realPlayTime = trimmedLength / animSpeed;

        PlayTime.Value = realPlayTime;

        return Status.Success;
    }

    bool CheckValid()
    {
        if (AnimClip.Value == "")
        {
            Debug.LogError("[BT] AnimClip is null");
            return false;
        }

        if (Animator.Value == null)
        {
            Debug.LogError("[BT] Animator is null");
            return false;
        }

        if (PlayTime == null)
        {
            Debug.LogError("[BT] PlayTime is null");
            return false;
        }

        if (Multiplier.Value != "")
        {
            animSpeed = Animator.Value.GetFloat(Multiplier.Value);
        }

        if (ClipEnd.Value == 0f)
        {
            ClipEnd.Value = 100f;
        }

        return true;
    }
}

