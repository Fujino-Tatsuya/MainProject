using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 할 일 91 A5 — 팔라딘 E 를 준비(SubSkillGuard)·방어중(SubSkillGuardIdle)·해제(SubSkillGuardEnd)·반격(SubSkillCounter)으로 나눠
// 공격속도는 준비·해제·반격 상태의 속도 파라미터(AttackSpeedRate)로만 탄다. 방어중 루프와 그 고정 전환은 공속과 무관하다.
// 이벤트 위치(Custom0·Custom1·End)는 사용자 튜닝 대상 임시값이라 값이 아니라 순서만 지킨다(Docs/tech/suppress-haste.md).
public sealed class FirstMeleeGuardAttackSpeedTests
{
    private const string ControllerPath = "Assets/4.Animations/Player/Garen/GarenAnimatorController.controller";
    private const string GuardStartClipPath = "Assets/4.Animations/Player/Garen/Garen_E_GuardStart.anim";
    private const string GuardEndClipPath = "Assets/4.Animations/Player/Garen/Garen_E_GuardEnd.anim";
    private const string GuardState = "SubSkillGuard";
    private const string GuardIdleState = "SubSkillGuardIdle";
    private const string GuardEndState = "SubSkillGuardEnd";
    private const string CounterState = "SubSkillCounter";

    private static readonly string[] OtherPlayerControllers =
    {
        "Assets/4.Animations/Player/Gunner/GunnerAnimatorController.controller",
        "Assets/4.Animations/Player/Assassin/AssassinAnimatorController.controller",
    };

    private static AnimatorController LoadController(string path)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        Assert.That(controller, Is.Not.Null, path);
        return controller;
    }

    private static AnimationClip LoadClip(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        Assert.That(clip, Is.Not.Null, path);
        return clip;
    }

    private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
    {
        foreach (ChildAnimatorState child in machine.states)
            yield return child.state;
        foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            foreach (AnimatorState state in AllStates(child.stateMachine))
                yield return state;
    }

    private static AnimatorState FindState(AnimatorController controller, string stateName)
    {
        foreach (AnimatorControllerLayer layer in controller.layers)
            foreach (AnimatorState state in AllStates(layer.stateMachine))
                if (state.name == stateName)
                    return state;

        Assert.Fail($"{controller.name} 에 상태 {stateName} 가 없다");
        return null;
    }

    private static bool UsesRateParameter(AnimatorState state) =>
        state.speedParameterActive && state.speedParameter == PlayerAttackSpeedAnimator.RateParameter;

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

    // ── 스킬 선언 ──

    [Test]
    public void Skill_NotAnimatorSpeed_ButSafetyNetScales()
    {
        var go = new GameObject("GuardAttackSpeedProbe");
        try
        {
            FirstMeleeSubSkill skill = go.AddComponent<FirstMeleeSubSkill>();
            Assert.That(skill.ScalesWithAttackSpeed, Is.False, "Animator.speed 배율 대상 아님 — 방어중까지 빨라진다");
            Assert.That(skill.ScalesSafetyNetWithAttackSpeed, Is.True, "준비·해제·반격이 감속으로 길어지니 안전망은 감속만큼 늘린다");
            Assert.That(skill.EndsOnClipEvent, Is.False, "시작 상태에 End 가 없다 — 안전망은 maxActiveDuration 기준");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ── 컨트롤러: 상태별 적용/제외 ──

    [TestCase(GuardState)]
    [TestCase(GuardEndState)]
    [TestCase(CounterState)]
    public void PrepReleaseCounter_UseRateParameter(string stateName)
    {
        AnimatorState state = FindState(LoadController(ControllerPath), stateName);
        Assert.That(UsesRateParameter(state), Is.True, $"{stateName} 속도 파라미터 = {PlayerAttackSpeedAnimator.RateParameter}");
    }

    [Test]
    public void GuardIdle_ExcludedFromRateParameter()
    {
        AnimatorState idle = FindState(LoadController(ControllerPath), GuardIdleState);
        Assert.That(idle.speedParameterActive, Is.False, "방어중은 공속과 무관");
    }

    [Test]
    public void BaseStateSpeeds_Kept()
    {
        AnimatorController controller = LoadController(ControllerPath);
        Assert.That(FindState(controller, GuardState).speed, Is.EqualTo(2f), "준비 상태 Speed 2 유지 — 배율은 그 위에 곱한다");
        Assert.That(FindState(controller, GuardEndState).speed, Is.EqualTo(2f), "해제 상태 Speed 2 유지");
        Assert.That(FindState(controller, GuardIdleState).speed, Is.EqualTo(1f));
    }

    // 준비 → 방어중, 방어중 → 해제 전환은 초 단위 고정 길이 — 방어 창 실제 길이가 공속과 무관해지는 조건.
    [Test]
    public void GuardTransitions_FixedDuration()
    {
        AnimatorController controller = LoadController(ControllerPath);
        AssertFixedTransition(FindState(controller, GuardState), GuardIdleState);
        AssertFixedTransition(FindState(controller, GuardIdleState), GuardEndState);
    }

    private static void AssertFixedTransition(AnimatorState from, string toName)
    {
        foreach (AnimatorStateTransition transition in from.transitions)
        {
            if (transition.destinationState != null && transition.destinationState.name == toName)
            {
                Assert.That(transition.hasFixedDuration, Is.True, $"{from.name} → {toName} 고정 길이");
                return;
            }
        }

        Assert.Fail($"{from.name} → {toName} 전환이 없다");
    }

    // 파라미터 기본값 1 — 쓰기 전(스폰 직후)이나 다른 상태에서 배율이 새지 않는다. 다른 상태는 이 파라미터를 쓰지 않는다.
    [Test]
    public void RateParameter_DefaultOne_OnlyGuardStatesUseIt()
    {
        AnimatorController controller = LoadController(ControllerPath);

        AnimatorControllerParameter parameter = System.Array.Find(
            controller.parameters, p => p.name == PlayerAttackSpeedAnimator.RateParameter);
        Assert.That(parameter, Is.Not.Null, PlayerAttackSpeedAnimator.RateParameter);
        Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
        Assert.That(parameter.defaultFloat, Is.EqualTo(1f));

        var allowed = new HashSet<string> { GuardState, GuardEndState, CounterState };
        foreach (AnimatorControllerLayer layer in controller.layers)
            foreach (AnimatorState state in AllStates(layer.stateMachine))
                if (UsesRateParameter(state))
                    Assert.That(allowed, Does.Contain(state.name), $"{state.name} 가 {PlayerAttackSpeedAnimator.RateParameter} 를 쓴다");
    }

    [Test]
    public void OtherCharacterControllers_DoNotUseRateParameter()
    {
        foreach (string path in OtherPlayerControllers)
        {
            AnimatorController controller = LoadController(path);
            foreach (AnimatorControllerLayer layer in controller.layers)
                foreach (AnimatorState state in AllStates(layer.stateMachine))
                    Assert.That(UsesRateParameter(state), Is.False, $"{path} · {state.name}");
        }
    }

    // ── 실제 재생: 배율 2 에서 준비·해제·반격은 두 배, 방어중은 그대로 ──

    [TestCase(GuardState, 2f)]
    [TestCase(GuardEndState, 2f)]
    [TestCase(CounterState, 2f)]
    [TestCase(GuardIdleState, 1f)]
    public void Playback_ScalesOnlyOutsideGuardIdle(string stateName, float expectedRatio)
    {
        float atOne = NormalizedTimeAfter(stateName, 1f, 0.02f);
        float atTwo = NormalizedTimeAfter(stateName, 2f, 0.02f);

        Assert.That(atOne, Is.GreaterThan(0f), $"{stateName} 재생이 진행되지 않았다");
        Assert.That(atTwo / atOne, Is.EqualTo(expectedRatio).Within(1e-3f), stateName);
    }

    private static float NormalizedTimeAfter(string stateName, float rate, float seconds)
    {
        AnimatorController controller = LoadController(ControllerPath);
        var go = new GameObject("GuardPlaybackProbe");
        try
        {
            Animator animator = go.AddComponent<Animator>();
            // 클립 이벤트 수신자 — 없으면 "AnimationEvent has no receiver" 에러 로그로 테스트가 실패한다.
            go.AddComponent<EnumAnimationEventProbe>();
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.SetFloat(PlayerAttackSpeedAnimator.RateParameter, rate);
            animator.Play(stateName, 0, 0f);
            animator.Update(0f);
            animator.Update(seconds);

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            Assert.That(info.IsName(stateName), Is.True, $"{stateName} 상태가 아니다");
            return info.normalizedTime;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ── 이벤트 순서(위치 값은 튜닝 대상) ──

    [Test]
    public void GuardStart_Custom0_InsideClip()
    {
        AnimationClip clip = LoadClip(GuardStartClipPath);
        float open = EventTime(clip, SkillAnimationEventType.Custom0);
        Assert.That(open, Is.GreaterThanOrEqualTo(0f).And.LessThan(clip.length),
            "창 열림은 준비 클립 안 — 끝(exitTime 1) 뒤면 방어중으로 넘어가 버려 안 온다");
    }

    [Test]
    public void GuardEnd_Custom1BeforeEnd_EndInsideClip()
    {
        AnimationClip clip = LoadClip(GuardEndClipPath);
        float close = EventTime(clip, SkillAnimationEventType.Custom1);
        float end = EventTime(clip, SkillAnimationEventType.End);

        Assert.That(close, Is.LessThan(end), "창 닫힘 → 종료 순서 — 같은 시각 두 이벤트의 발화 순서에 기대지 않는다");
        Assert.That(end, Is.LessThan(clip.length), "End 는 클립 안 — 끝 이후면 Idle 전환과 겹쳐 안 올 수 있다");
    }
}
