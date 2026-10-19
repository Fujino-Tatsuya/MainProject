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

    // 팔라딘 우클릭(Garen_Parry Hit·End)·E(방어 Custom0·Custom1 + 반격 SubSkillCounter Hit·End)가 실제로 대조되는가(PLAN-anim-event-timing P1-③·④).
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
        AssertRequirements(prefab.GetComponentInChildren<FirstMeleeSubSkill>(true), controller, 4,
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
