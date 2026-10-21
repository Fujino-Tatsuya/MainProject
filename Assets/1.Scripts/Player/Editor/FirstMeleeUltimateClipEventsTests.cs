using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 할 일 52.1 — 팔라딘 R 은 마무리 낙뢰(Custom0)·피해(Hit)·종료(End)를 클립 이벤트로만 정한다(타이머 대체 없음).
// 이벤트 방식으로 선언돼 있고, End 가 컨트롤러 안전망(클립 실제 길이 + padding)보다 앞에 오는지 지킨다.
// 클립에 요구 이벤트가 다 있는지는 PlayerAnimationEventRequirementTests 전수 검사가 본다.
public sealed class FirstMeleeUltimateClipEventsTests
{
    private const string ClipPath = "Assets/4.Animations/Player/Garen/Garen_Ultimate_Skill.anim";
    private const string DataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeUltimateSkillData.asset";
    private const string ControllerPath = "Assets/4.Animations/Player/Garen/GarenAnimatorController.controller";
    private const string SpeedParameter = "UltimateSpeed";
    // PlayerSkillController.endFallbackPadding 기본값(Player_Paladin.prefab 도 0.1).
    private const float EndFallbackPadding = 0.1f;

    private static AnimationClip LoadClip()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        Assert.That(clip, Is.Not.Null, ClipPath);
        return clip;
    }

    private static float EventTime(AnimationClip clip, SkillAnimationEventType eventType)
    {
        foreach (AnimationEvent clipEvent in clip.events)
        {
            if (clipEvent.functionName == nameof(PlayerAnimationEventRelay.HandleSkillEvent) &&
                clipEvent.intParameter == (int)eventType)
                return clipEvent.time;
        }

        Assert.Fail($"{clip.name} 에 {eventType} 이벤트가 없다");
        return 0f;
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == stateName)
                return child.state;
        }

        foreach (ChildAnimatorStateMachine child in machine.stateMachines)
        {
            AnimatorState state = FindState(child.stateMachine, stateName);
            if (state != null)
                return state;
        }

        return null;
    }

    [Test]
    public void DeclaresEventTiming()
    {
        var go = new GameObject("UltimateProbe");
        try
        {
            FirstMeleeUltimateSkill skill = go.AddComponent<FirstMeleeUltimateSkill>();

            Assert.That(skill.RequiredAnimationEvents, Is.EquivalentTo(new[]
            {
                SkillAnimationEventType.Hit, SkillAnimationEventType.End, SkillAnimationEventType.Custom0,
            }));
            Assert.That(skill.EndsOnClipEvent, Is.True, "End 를 요구하니 이벤트 방식 — 안전망은 클립 길이");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EventsAreOrderedStrikeHitEnd()
    {
        AnimationClip clip = LoadClip();

        float strike = EventTime(clip, SkillAnimationEventType.Custom0);
        float hit = EventTime(clip, SkillAnimationEventType.Hit);
        float end = EventTime(clip, SkillAnimationEventType.End);

        Assert.That(strike, Is.LessThanOrEqualTo(hit), "마무리 낙뢰는 피해 전에 쏜다");
        Assert.That(end, Is.GreaterThan(hit), "End 는 Hit 뒤 — 같은 시각 두 이벤트의 발화 순서에 기대지 않는다");
    }

    // R 은 한 번 재생 클립이다 — 안전망은 클립 끝(길이 ÷ 배율) + padding 이고, 끝에 머무는 동안 더 늘지 않는다.
    [Test]
    public void ClipPlaysOnce()
    {
        AnimationClip clip = LoadClip();

        Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.False, "Loop Time 꺼짐");
        Assert.That(clip.isLooping, Is.False);
    }

    // 런타임은 상태 진입 뒤 AnimatorStateInfo 로 읽는 값을, 여기서는 에디터 컨트롤러에서 같은 공식으로 구한다.
    [Test]
    public void EndComesBeforeClipSafetyNet()
    {
        AnimationClip clip = LoadClip();
        var data = AssetDatabase.LoadAssetAtPath<FirstMeleeUltimateSkillData>(DataPath);
        Assert.That(data, Is.Not.Null, DataPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.That(controller, Is.Not.Null, ControllerPath);

        AnimatorState state = null;
        foreach (AnimatorControllerLayer layer in controller.layers)
            state ??= FindState(layer.stateMachine, data.AnimatorStateName);
        Assert.That(state, Is.Not.Null, $"상태 {data.AnimatorStateName}");
        Assert.That(state.motion, Is.SameAs(clip), "상태 모션이 R 클립");
        Assert.That(state.speedParameterActive && state.speedParameter == SpeedParameter, Is.True,
            $"상태 속도 파라미터 = {SpeedParameter}(시전 때 animationSpeed 가 들어간다)");

        float speed = state.speed * data.AnimationSpeed;
        float safetyNet = PlayerSkillSafetyNet.Seconds(
            true, data.MaxActiveDuration, EndFallbackPadding, true, clip.length, state.speed, data.AnimationSpeed, 1f, out bool usedClip);
        float endRealTime = EventTime(clip, SkillAnimationEventType.End) / speed;

        Assert.That(usedClip, Is.True, "클립 길이 기준 안전망");
        Assert.That(safetyNet, Is.EqualTo(clip.length / speed + EndFallbackPadding).Within(1e-4f), "클립 길이 ÷ 실제 속도 + padding");
        Assert.That(endRealTime, Is.LessThan(safetyNet),
            $"End 실제 {endRealTime:F3}s 가 안전망 {safetyNet:F3}s 뒤 — 안전망이 먼저 끊어 낙뢰가 걷힌다");
    }

    [Test]
    public void StickEffectEventIsKept()
    {
        AnimationClip clip = LoadClip();

        bool hasStick = false;
        foreach (AnimationEvent clipEvent in clip.events)
            hasStick |= clipEvent.functionName == "PlayEffect" && clipEvent.stringParameter == "Stick";

        Assert.That(hasStick, Is.True, "칼 꽂기 PlayEffect(\"Stick\") 이벤트");
    }
}
