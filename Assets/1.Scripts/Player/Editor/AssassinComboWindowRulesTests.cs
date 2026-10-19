using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 할 일 47 연장·정정 — 어쌔신 평타 다음 타 연결: 예약 창(클립 ComboWindowOpen~Close 이벤트, 가붕이와 같다 — PLAN-anim-event-timing P2-1)
// + End 순간 눌림(거너 기본 공격과 같은 홀드).
public sealed class AssassinComboWindowRulesTests
{
    private const string BasicAttackDataPath = "Assets/9.ScriptableObject/Player/Assassin/AssassinBasicAttackData.asset";
    private const string ControllerPath = "Assets/4.Animations/Player/Assassin/AssassinAnimatorController.controller";

    // 창은 (마지막) 판정과 같은 프레임 또는 그 뒤에 열리고, End 전에 닫힌다 — 1~4타·강타·변신 묶음 모두 창 이벤트가 하나씩.
    [Test]
    public void ClipWindowOpensAfterHitAndClosesBeforeEnd()
    {
        foreach ((string label, AnimationClip clip) in StepClips())
        {
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
            Assert.That(Count(events, DefaultAttackAnimationEventType.ComboWindowOpen), Is.EqualTo(1), $"{label} 열림 1개");
            Assert.That(Count(events, DefaultAttackAnimationEventType.ComboWindowClose), Is.EqualTo(1), $"{label} 닫힘 1개");
            Assert.That(Count(events, DefaultAttackAnimationEventType.End), Is.EqualTo(1), $"{label} End 1개");

            float open = TimeOf(events, DefaultAttackAnimationEventType.ComboWindowOpen);
            float close = TimeOf(events, DefaultAttackAnimationEventType.ComboWindowClose);
            float end = TimeOf(events, DefaultAttackAnimationEventType.End);
            Assert.That(open, Is.GreaterThanOrEqualTo(LastTimeOf(events, DefaultAttackAnimationEventType.Hit) - 1e-4f),
                $"{label} 창은 (마지막) 판정 뒤에 연다");
            Assert.That(close, Is.GreaterThan(open), $"{label} 닫힘 > 열림");
            Assert.That(close, Is.LessThan(end), $"{label} End 전에 닫는다");
        }
    }

    // 안전망(클립 원래 길이 + 여유)이 End 이벤트의 실제 시각(클립 시각 ÷ 상태 speed)보다 뒤 — 정상 종료를 안전망이 앞지르지 않는다.
    [Test]
    public void SafetyNetIsAfterEndEventRealTime()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.That(controller, Is.Not.Null, ControllerPath);

        foreach ((string label, AnimationClip clip) in StepClips())
        {
            float speed = StateSpeedOf(controller, clip);
            Assert.That(speed, Is.GreaterThan(0f), $"{label} 클립을 쓰는 상태가 없다");

            float endRealTime = TimeOf(AnimationUtility.GetAnimationEvents(clip), DefaultAttackAnimationEventType.End) / speed;
            Assert.That(endRealTime, Is.LessThan(clip.length), $"{label} End {endRealTime:F3}s ≥ 안전망 {clip.length:F3}s");
        }
    }

    private static IEnumerable<(string, AnimationClip)> StepClips()
    {
        AssassinBasicAttackData data = AssetDatabase.LoadAssetAtPath<AssassinBasicAttackData>(BasicAttackDataPath);
        Assert.That(data, Is.Not.Null, BasicAttackDataPath);

        for (int i = 0; i < data.NormalSteps.Length; i++)
            yield return ($"{i + 1}타", data.NormalSteps[i].Clip);
        yield return ("강타", data.EnhancedStep.Clip);
        yield return ("변신 묶음", data.TransformedStep.Clip);
    }

    private static bool IsEvent(AnimationEvent e, DefaultAttackAnimationEventType type) =>
        e.functionName == nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent) && e.intParameter == (int)type;

    private static int Count(AnimationEvent[] events, DefaultAttackAnimationEventType type)
    {
        int count = 0;
        foreach (AnimationEvent e in events)
        {
            if (IsEvent(e, type))
                count++;
        }

        return count;
    }

    private static float TimeOf(AnimationEvent[] events, DefaultAttackAnimationEventType type)
    {
        foreach (AnimationEvent e in events)
        {
            if (IsEvent(e, type))
                return e.time;
        }

        return float.NaN;
    }

    private static float LastTimeOf(AnimationEvent[] events, DefaultAttackAnimationEventType type)
    {
        float last = float.NegativeInfinity;
        foreach (AnimationEvent e in events)
        {
            if (IsEvent(e, type))
                last = Mathf.Max(last, e.time);
        }

        return last;
    }

    private static float StateSpeedOf(AnimatorController controller, AnimationClip clip)
    {
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                if (child.state.motion == clip)
                    return child.state.speed;
            }
        }

        return 0f;
    }

    // ── 할 일 47 정정 — End 순간 다음 타 = 창 예약 || 눌림(거너와 같은 홀드 + 창) ──

    private const float StaleAfter = 0.5f;      // AssassinBasicAttack.HoldStaleSeconds
    private const float HoldSignalGap = 0.1f;   // AssassinBasicAttack.HoldSignalInterval

    [Test]
    public void HeldAtEndContinues()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: false, held: true), Is.True);
    }

    [Test]
    public void WindowQueueContinuesEvenAfterRelease()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: true, held: false), Is.True);
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: true, held: true), Is.True);
    }

    [Test]
    public void NeitherQueuedNorHeldEnds()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(queued: false, held: false), Is.False);
    }

    // 꾹 누르고만 있으면 1→2→3→4→1 이 계속 이어진다 — 유예 0 이어도 End 같은 순간 이어지니 4타 뒤 1타로 돈다.
    [Test]
    public void HoldingOnlyLoopsThroughFourStepsAndBackToFirst()
    {
        var model = new AssassinComboModel(4, 0f);
        float now = 0f;
        int step = model.Begin(now);

        for (int i = 0; i < 9; i++)
        {
            Assert.That(step, Is.EqualTo(i % 4), $"{i + 1}번째 타");
            now += 0.35f;
            model.Complete(step, now);

            bool held = AssassinComboWindowRules.IsHoldFresh(true, now - HoldSignalGap, now, StaleAfter);
            Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(false, held), Is.True, "눌림 — 다음 타");
            step = model.Begin(now);
        }
    }

    // 예전 홀드 방식의 "처음 누른 입력을 떼지 않아야" 조건은 없다 — 뗐다 다시 눌러 들고 있으면 End 순간 눌림으로 잇는다.
    [Test]
    public void ReleasedThenRepressedAndHeldContinues()
    {
        // 0.05 누름 → 0.12 뗌 → 0.2 다시 누름(각각 변경 즉시 신호). 서버가 아는 상태 = 마지막 변경(눌림, 0.2).
        const float end = 0.3f;
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(
            false, AssassinComboWindowRules.IsHoldFresh(true, 0.2f, end, StaleAfter)), Is.True);
    }

    [Test]
    public void ReleasedBeforeEndWithoutQueueEnds()
    {
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(
            false, AssassinComboWindowRules.IsHoldFresh(false, 0.29f, 0.3f, StaleAfter)), Is.False);
    }

    // 주기 신호가 끊기면(뗌 신호 유실 등) 안 눌림으로 본다 — 무한히 이어지지 않게. 경계는 포함.
    [Test]
    public void StaleHoldSignalCountsAsReleased()
    {
        Assert.That(AssassinComboWindowRules.IsHoldFresh(true, 1f, 1.5f, StaleAfter), Is.True, "경계");
        Assert.That(AssassinComboWindowRules.IsHoldFresh(true, 1f, 1.51f, StaleAfter), Is.False, "끊김");
        Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(
            false, AssassinComboWindowRules.IsHoldFresh(true, 1f, 2f, StaleAfter)), Is.False);
    }
}
