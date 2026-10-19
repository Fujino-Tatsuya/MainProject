using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 동작이 클립에 요구하는 애니메이션 이벤트 하나. 판정·종료·입력 창·연출 시점은 클립 이벤트가 정한다 —
/// 이벤트가 빠지면 그 시점이 조용히 안 오므로(안전망만 남는다) 선언해 두고 에디터 경고·EditMode 테스트로 잡는다.
/// 클립을 직접 아는 동작(평타 스텝)은 <see cref="Clip"/>, 애니메이터 상태 이름만 아는 동작(스킬 animatorStateName)은 <see cref="StateName"/>.
/// </summary>
public readonly struct AnimationEventRequirement
{
    public readonly AnimationClip Clip;
    public readonly string StateName;
    public readonly string FunctionName;
    public readonly int IntParameter;
    public readonly string EventName;

    private AnimationEventRequirement(AnimationClip clip, string stateName, string functionName, int intParameter, string eventName)
    {
        Clip = clip;
        StateName = stateName;
        FunctionName = functionName;
        IntParameter = intParameter;
        EventName = eventName;
    }

    /// <summary>스킬 — <see cref="PlayerAnimationEventRelay.HandleSkillEvent"/>(int) 를 상태 클립에서 찾는다.</summary>
    public static AnimationEventRequirement ForSkillState(string stateName, SkillAnimationEventType eventType) =>
        new AnimationEventRequirement(null, stateName, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)eventType,
            eventType.ToString());

    /// <summary>평타 — <see cref="PlayerAnimationEventRelay.HandleDefaultAttackEvent"/>(int) 를 스텝 클립에서 찾는다.</summary>
    public static AnimationEventRequirement ForAttackClip(AnimationClip clip, DefaultAttackAnimationEventType eventType) =>
        new AnimationEventRequirement(clip, null, nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent), (int)eventType,
            eventType.ToString());

    public string Describe() =>
        $"{FunctionName}(int={IntParameter}, {EventName}) @ " +
        (Clip != null ? $"클립 '{Clip.name}'" : $"상태 '{StateName}'");
}

/// <summary>클립에 의존하는 동작(스킬·평타)이 자기 요구 이벤트를 내놓는다. 에디터 경고와 EditMode 전수 검사가 같은 목록을 본다.</summary>
public interface IAnimationEventRequirementSource
{
    void CollectAnimationEventRequirements(List<AnimationEventRequirement> requirements);
}

/// <summary>요구 이벤트 대조. 상태 이름 → 클립 해석은 에디터에서만 된다(런타임 컨트롤러는 상태별 모션을 노출하지 않는다).</summary>
public static class AnimationEventRequirements
{
    public static bool HasEvent(AnimationClip clip, string functionName, int intParameter)
    {
        if (clip == null)
            return false;

        foreach (AnimationEvent clipEvent in clip.events)
        {
            if (clipEvent.functionName == functionName && clipEvent.intParameter == intParameter)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 요구 목록 중 클립에 없는 것을 <paramref name="missing"/> 에 설명으로 담는다. 반환 = 대조한 요구 수.
    /// 상태 이름 요구는 <paramref name="controller"/> 에서 클립을 찾는다 — 에디터 밖이거나 상태가 없으면 그것도 누락으로 본다.
    /// </summary>
    public static int FindMissing(
        IReadOnlyList<AnimationEventRequirement> requirements, RuntimeAnimatorController controller, List<string> missing)
    {
        int checkedCount = 0;
        for (int i = 0; i < requirements.Count; i++)
        {
            AnimationEventRequirement requirement = requirements[i];
            AnimationClip clip = requirement.Clip != null ? requirement.Clip : FindStateClip(controller, requirement.StateName);
            checkedCount++;

            if (clip == null)
                missing.Add($"{requirement.Describe()} — 클립을 찾지 못했다");
            else if (!HasEvent(clip, requirement.FunctionName, requirement.IntParameter))
                missing.Add(requirement.Describe() + (requirement.Clip == null ? $" (클립 '{clip.name}')" : ""));
        }

        return checkedCount;
    }

    /// <summary>
    /// [에디터] 누락 이벤트마다 경고를 낸다(DefaultAttackController 의 ComboWindowOpen 누락 경고와 같은 방식).
    /// 이벤트가 없으면 그 시점(판정·종료·창)이 오지 않고 안전망 시간으로만 끝난다.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void WarnMissing(IAnimationEventRequirementSource source, Animator animator, Object context)
    {
        if (source == null)
            return;

        List<AnimationEventRequirement> requirements = new List<AnimationEventRequirement>();
        source.CollectAnimationEventRequirements(requirements);
        if (requirements.Count == 0)
            return;

        List<string> missing = new List<string>();
        FindMissing(requirements, animator != null ? animator.runtimeAnimatorController : null, missing);
        foreach (string line in missing)
        {
            Edit.LogWarning(
                $"[Player] {source.GetType().Name} 에 필요한 애니메이션 이벤트가 없습니다: {line}. " +
                "그 시점(판정·종료·입력 창)이 오지 않고 안전망 시간으로만 끝납니다.",
                context);
        }
    }

    /// <summary>[에디터] 컨트롤러의 모든 레이어·하위 상태 머신에서 이름이 같은 상태의 클립. 에디터 밖이면 null.</summary>
    public static AnimationClip FindStateClip(RuntimeAnimatorController controller, string stateName)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(stateName))
            return null;

        UnityEditor.Animations.AnimatorController editorController = controller as UnityEditor.Animations.AnimatorController;
        if (editorController == null && controller is AnimatorOverrideController overrideController)
            editorController = overrideController.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (editorController == null)
            return null;

        foreach (UnityEditor.Animations.AnimatorControllerLayer layer in editorController.layers)
        {
            AnimationClip clip = FindStateClip(layer.stateMachine, stateName);
            if (clip != null)
                return controller is AnimatorOverrideController ov ? ov[clip] : clip;
        }
#endif
        return null;
    }

#if UNITY_EDITOR
    private static AnimationClip FindStateClip(UnityEditor.Animations.AnimatorStateMachine machine, string stateName)
    {
        if (machine == null)
            return null;

        foreach (UnityEditor.Animations.ChildAnimatorState child in machine.states)
        {
            if (child.state.name == stateName)
                return child.state.motion as AnimationClip;
        }

        foreach (UnityEditor.Animations.ChildAnimatorStateMachine child in machine.stateMachines)
        {
            AnimationClip clip = FindStateClip(child.stateMachine, stateName);
            if (clip != null)
                return clip;
        }

        return null;
    }
#endif
}
