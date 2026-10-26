using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 판정·종료·입력 창을 클립 이벤트로 정하는 동작(스킬 RequiredAnimationEvents·평타 스텝)이 요구한 이벤트가
/// 실제 플레이어 프리팹의 애니메이터 컨트롤러·클립에 다 있는가(PLAN-anim-event-timing P0-1). 이벤트가 빠지면 그 시점이 안 오고 안전망으로만 끝난다.
/// </summary>
public sealed class PlayerAnimationEventRequirementTests
{
    private const string PlayerPrefabFolder = "Assets/2.Prefabs/Player";

    [Test]
    public void HasEvent_MatchesFunctionAndIntParameter()
    {
        AnimationClip clip = new AnimationClip();
        try
        {
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = 0f, functionName = nameof(PlayerAnimationEventRelay.HandleSkillEvent), intParameter = 1 },
            });

            Assert.That(AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), 1), Is.True);
            Assert.That(AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), 0), Is.False, "int 다름");
            Assert.That(AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent), 1), Is.False, "함수 다름");
            Assert.That(AnimationEventRequirements.HasEvent(null, nameof(PlayerAnimationEventRelay.HandleSkillEvent), 1), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(clip);
        }
    }

    [Test]
    public void FindMissing_ReportsClipWithoutRequiredEvent()
    {
        AnimationClip clip = new AnimationClip { name = "NoEvents" };
        try
        {
            List<AnimationEventRequirement> requirements = new List<AnimationEventRequirement>
            {
                AnimationEventRequirement.ForAttackClip(clip, DefaultAttackAnimationEventType.ComboWindowOpen),
            };
            List<string> missing = new List<string>();

            int checkedCount = AnimationEventRequirements.FindMissing(requirements, null, missing);

            Assert.That(checkedCount, Is.EqualTo(1));
            Assert.That(missing.Count, Is.EqualTo(1));
            StringAssert.Contains("NoEvents", missing[0]);
        }
        finally
        {
            Object.DestroyImmediate(clip);
        }
    }

    // Animation 창에서 enum 이름(Custom1)으로 고른 이벤트 = intParameter 3 저장 → 같은 요구를 만족한다.
    [Test]
    public void FindMissing_AcceptsEventPickedByEnumName()
    {
        AnimationClip clip = new AnimationClip { name = "EnumPicked" };
        try
        {
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent
                {
                    time = 0f, functionName = nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent),
                    intParameter = (int)DefaultAttackAnimationEventType.ComboWindowClose,
                },
            });
            List<AnimationEventRequirement> requirements = new List<AnimationEventRequirement>
            {
                AnimationEventRequirement.ForAttackClip(clip, DefaultAttackAnimationEventType.ComboWindowClose),
                AnimationEventRequirement.ForAttackClip(clip, DefaultAttackAnimationEventType.End),
            };
            List<string> missing = new List<string>();

            Assert.That(AnimationEventRequirements.FindMissing(requirements, null, missing), Is.EqualTo(2));
            Assert.That(missing.Count, Is.EqualTo(1));
            StringAssert.Contains("End=3", missing[0]);
        }
        finally
        {
            Object.DestroyImmediate(clip);
        }
    }

    // 팔라딘 우클릭(Garen_Parry Hit·End)·E(준비 Custom0 · 해제 Custom1·End + 반격 SubSkillCounter Hit·End)가 실제로 대조되는가(PLAN-anim-event-timing P1-③·④).
    [Test]
    public void PaladinInterruptAndSub_DeclareAndHaveEventTimingEvents()
    {
        const string paladinPath = PlayerPrefabFolder + "/Paladin/Player_Paladin.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paladinPath);
        Assert.That(prefab, Is.Not.Null, paladinPath);

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;

        AssertRequirements(prefab.GetComponentInChildren<FirstMeleeInterruptSkill>(true), controller, 2,
            nameof(FirstMeleeInterruptSkill));
        AssertRequirements(prefab.GetComponentInChildren<FirstMeleeSubSkill>(true), controller, 5,
            nameof(FirstMeleeSubSkill));
    }

    private static void AssertRequirements(
        IAnimationEventRequirementSource source, RuntimeAnimatorController controller, int expectedCount, string label)
    {
        Assert.That(source, Is.Not.Null, label + " 가 팔라딘 프리팹에 없다");

        List<AnimationEventRequirement> requirements = new List<AnimationEventRequirement>();
        source.CollectAnimationEventRequirements(requirements);
        List<string> missing = new List<string>();

        Assert.That(AnimationEventRequirements.FindMissing(requirements, controller, missing), Is.EqualTo(expectedCount), label + " 요구 수");
        Assert.That(missing, Is.Empty, label + " 누락 이벤트:\n" + string.Join("\n", missing));
    }

    // 이벤트 함수 파라미터가 enum 이라 Animation 창에서 이름으로 고른다. 저장은 intParameter 그대로 — 요구 검사·저작 메뉴·기존 클립이 같은 값을 본다.
    [Test]
    public void RelayEventFunctions_TakeEnumWithoutOverloads()
    {
        AssertSingleEnumParameter(nameof(PlayerAnimationEventRelay.HandleSkillEvent), typeof(SkillAnimationEventType));
        AssertSingleEnumParameter(nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent), typeof(DefaultAttackAnimationEventType));

        Assert.That(AnimationEventRequirement.ForSkillState("S", SkillAnimationEventType.Custom1).IntParameter, Is.EqualTo(3));
        Assert.That(AnimationEventRequirement.ForAttackClip(null, DefaultAttackAnimationEventType.End).IntParameter, Is.EqualTo(3));
    }

    private static void AssertSingleEnumParameter(string methodName, System.Type enumType)
    {
        System.Reflection.MethodInfo[] overloads = System.Array.FindAll(
            typeof(PlayerAnimationEventRelay).GetMethods(), m => m.Name == methodName);
        Assert.That(overloads.Length, Is.EqualTo(1), methodName + " 오버로드가 있으면 이벤트 인스펙터 목록에서 빠진다");

        System.Reflection.ParameterInfo[] parameters = overloads[0].GetParameters();
        Assert.That(parameters.Length, Is.EqualTo(1), methodName);
        Assert.That(parameters[0].ParameterType, Is.EqualTo(enumType), methodName);
    }

    // [Unity 동작 확인] 이벤트 인스펙터(AnimationWindowEventInspector, 내부 API)가 enum 파라미터 함수를 목록에 올리는가.
    // Unity 업그레이드로 내부 API 가 바뀌면 Ignore — 그때는 Animation 창에서 직접 확인한다.
    [Test]
    public void AnimationEventInspector_ListsRelayEnumFunctions()
    {
        System.Type inspector = typeof(EditorWindow).Assembly.GetType("UnityEditor.AnimationWindowBuiltin.AnimationWindowEventInspector");
        System.Type mapType = inspector?.GetNestedType("AnimationMethodMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        System.Reflection.MethodInfo collect = inspector?.GetMethod("CollectSupportedMethods",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (mapType == null || collect == null || collect.GetParameters().Length != 4)
            Assert.Ignore("AnimationWindowEventInspector.CollectSupportedMethods 내부 API 가 바뀌었다 — Animation 창에서 직접 확인");

        System.Type listType = typeof(List<>).MakeGenericType(mapType);
        System.Reflection.FieldInfo methodInfoField = mapType.GetField("methodInfo");
        GameObject go = new GameObject("RelayInspectorProbe");
        try
        {
            go.AddComponent<PlayerAnimationEventRelay>();
            System.Collections.IList supported = (System.Collections.IList)System.Activator.CreateInstance(listType);
            collect.Invoke(null, new object[]
            {
                go, supported, System.Activator.CreateInstance(listType), System.Activator.CreateInstance(listType),
            });

            List<string> names = new List<string>();
            foreach (object map in supported)
                names.Add(((System.Reflection.MethodInfo)methodInfoField.GetValue(map)).Name);

            Assert.That(names, Does.Contain(nameof(PlayerAnimationEventRelay.HandleSkillEvent)));
            Assert.That(names, Does.Contain(nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent)));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // [Unity 동작 확인] 클립에 저장된 intParameter 로 enum 파라미터 함수가 실제 호출되는가. 에디트 모드 Animator.Update 도 이벤트를 쏜다.
    // 실제 릴레이도 같은 오브젝트에 붙여 — 파라미터 형이 안 맞으면 Unity 가 에러 로그를 내 테스트가 실패한다.
    [Test]
    public void AnimatorDispatchesStoredIntParameterToEnumFunctions()
    {
        AnimationClip clip = new AnimationClip { name = "EnumEventProbe" };
        UnityEditor.Animations.AnimatorController controller = new UnityEditor.Animations.AnimatorController();
        GameObject go = new GameObject("EnumEventProbe");
        try
        {
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = 0.1f, functionName = nameof(PlayerAnimationEventRelay.HandleSkillEvent), intParameter = 2 },
                new AnimationEvent { time = 0.2f, functionName = nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent), intParameter = 1 },
            });
            controller.AddLayer("Base");
            controller.layers[0].stateMachine.AddState("S").motion = clip;

            Animator animator = go.AddComponent<Animator>();
            go.AddComponent<PlayerAnimationEventRelay>();
            EnumAnimationEventProbe probe = go.AddComponent<EnumAnimationEventProbe>();
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            for (int i = 0; i < 10; i++)
                animator.Update(0.05f);

            Assert.That(probe.Skill, Is.EqualTo(new List<SkillAnimationEventType> { SkillAnimationEventType.Custom0 }));
            Assert.That(probe.Attack, Is.EqualTo(new List<DefaultAttackAnimationEventType> { DefaultAttackAnimationEventType.ComboWindowOpen }));
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(clip);
        }
    }

    // 전수 검사 — Legacy 보관본은 스폰되지 않으므로 제외한다(Docs/tech/player-prefabs.md).
    [Test]
    public void PlayerPrefabs_HaveEveryDeclaredAnimationEvent()
    {
        int prefabCount = 0;
        int requirementCount = 0;
        List<string> failures = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Legacy/"))
                continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<Player>() == null)
                continue;

            prefabCount++;
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;

            foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(behaviour is IAnimationEventRequirementSource source))
                    continue;

                List<AnimationEventRequirement> requirements = new List<AnimationEventRequirement>();
                source.CollectAnimationEventRequirements(requirements);
                List<string> missing = new List<string>();
                requirementCount += AnimationEventRequirements.FindMissing(requirements, controller, missing);

                foreach (string line in missing)
                    failures.Add($"{path} · {behaviour.GetType().Name}: {line}");
            }
        }

        Assert.That(prefabCount, Is.GreaterThan(0), $"{PlayerPrefabFolder} 아래 Player 프리팹을 하나도 못 찾았다");
        Assert.That(requirementCount, Is.GreaterThan(0), "선언된 요구 이벤트가 하나도 없다 — 검사가 아무것도 보지 않았다");
        Assert.That(failures, Is.Empty, "누락 이벤트:\n" + string.Join("\n", failures));
    }
}

// AnimatorDispatchesStoredIntParameterToEnumFunctions 전용 — 릴레이와 같은 이름·같은 enum 시그니처로 받은 값을 적는다.
public sealed class EnumAnimationEventProbe : MonoBehaviour
{
    public readonly List<SkillAnimationEventType> Skill = new List<SkillAnimationEventType>();
    public readonly List<DefaultAttackAnimationEventType> Attack = new List<DefaultAttackAnimationEventType>();

    public void HandleSkillEvent(SkillAnimationEventType eventType) => Skill.Add(eventType);
    public void HandleDefaultAttackEvent(DefaultAttackAnimationEventType eventType) => Attack.Add(eventType);
}
