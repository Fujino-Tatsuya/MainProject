using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 할 일 52.1 — 팔라딘 R 은 마무리 낙뢰(Custom0)·피해(Hit)·종료(End)를 클립 이벤트로만 정한다(타이머 대체 없음).
// 실제 클립에 이벤트가 있고, End 가 컨트롤러 안전망(maxActiveDuration + padding)보다 앞에 오는지 지킨다.
public sealed class FirstMeleeUltimateClipEventsTests
{
    private const string ClipPath = "Assets/4.Animations/Player/Garen/Garen_Ultimate_Skill.anim";
    private const string DataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeUltimateSkillData.asset";
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

    [Test]
    public void ClipHasAllUltimateEvents()
    {
        AnimationClip clip = LoadClip();

        foreach (SkillAnimationEventType eventType in FirstMeleeUltimateSkill.UltimateClipEvents)
            Assert.That(FirstMeleeUltimateSkill.HasSkillClipEvent(clip, eventType), Is.True, $"{eventType} 누락");
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

    [Test]
    public void EndComesBeforeSafetyNet()
    {
        AnimationClip clip = LoadClip();
        var data = AssetDatabase.LoadAssetAtPath<FirstMeleeUltimateSkillData>(DataPath);
        Assert.That(data, Is.Not.Null, DataPath);

        float endRealTime = EventTime(clip, SkillAnimationEventType.End) / data.AnimationSpeed;
        float safetyNet = data.MaxActiveDuration + EndFallbackPadding;

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
