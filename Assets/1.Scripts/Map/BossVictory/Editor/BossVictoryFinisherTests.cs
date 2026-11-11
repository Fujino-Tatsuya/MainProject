using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출(PLAN-boss-victory-glitch Q9) — 캐릭터마다 고른 처치 연출 스킬의 시작 상태 클립에 Skill End 이벤트가 있어야 한다.
/// End 가 처치자·보스가 0.1 로 느려지는 기본 신호다('느려짐 시작' 마커가 꺼졌을 때 — 대체 타이머 없음).
/// 스킬 칸 = 그 캐릭터 프로필(267, <see cref="BossVictoryProfile.finisherSlot"/> — 표에 없으면 기본 프로필).
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryFinisher).
/// </summary>
public sealed class BossVictoryFinisherTests
{
    static readonly object[] Variants =
    {
        new object[] { "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab", TutorialCharacter.Paladin },
        new object[] { "Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab", TutorialCharacter.Gunner },
        new object[] { "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab", TutorialCharacter.Assassin },
    };

    [TestCaseSource(nameof(Variants))]
    public void 처치_연출_스킬_클립에_End_이벤트가_있다(string path, TutorialCharacter character)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        var settings = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>("Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset");
        Assert.IsNotNull(settings, "설정 에셋");

        PlayerSkillSlot slot = settings.ProfileFor(character).finisherSlot;
        PlayerSkillController controller = prefab.GetComponentInChildren<PlayerSkillController>(true);
        Assert.IsNotNull(controller, "PlayerSkillController");
        PlayerSkillBase skill = controller.GetSkill(slot);
        Assert.IsNotNull(skill, $"{prefab.name} {slot} 칸 스킬");
        Assert.IsNotNull(skill.Data, $"{skill.name} 데이터");
        Assert.IsFalse(string.IsNullOrEmpty(skill.Data.AnimatorStateName), $"{skill.name} 애니 상태 이름");

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.IsNotNull(animator != null ? animator.runtimeAnimatorController : null, "Animator 컨트롤러");
        AnimationClip clip = AnimationEventRequirements.FindStateClip(animator.runtimeAnimatorController, skill.Data.AnimatorStateName);
        Assert.IsNotNull(clip, $"{skill.Data.AnimatorStateName} 상태 클립");
        Assert.IsTrue(AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)SkillAnimationEventType.End),
            $"{prefab.name} 처치 연출 스킬 {skill.name}({slot}) 클립 {clip.name} 에 Skill End 이벤트가 없다");
    }
}
