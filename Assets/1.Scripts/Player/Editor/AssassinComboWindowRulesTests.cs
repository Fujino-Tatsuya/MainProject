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
    // 할 일 256 부터 눌림은 오너가 End 순간 자기 입력으로 본다(서버 눌림 신호·신선도 검사 없음).

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

            Assert.That(AssassinComboWindowRules.ShouldContinueAtEnd(false, true), Is.True, "눌림 — 다음 타");
            step = model.Begin(now);
        }
    }

    // ── 할 일 256 — 오너 예측: 상태 Speed 복제·모드 확인·2차 검증 시각표 ──

    // 데이터의 상태 Speed 복제값 = 컨트롤러 상태 Speed(원본). 컨트롤러만 고치면 서버 기대 간격이 틀어진다.
    [Test]
    public void DataStateSpeedMatchesControllerStateSpeed()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.That(controller, Is.Not.Null, ControllerPath);

        foreach ((string label, AssassinBasicAttackStepData step) in Steps())
        {
            float controllerSpeed = StateSpeedOf(controller, step.Clip);
            Assert.That(controllerSpeed, Is.GreaterThan(0f), $"{label} 클립을 쓰는 상태가 없다");
            Assert.That(step.AnimatorStateSpeed, Is.EqualTo(controllerSpeed).Within(1e-4f),
                $"{label} AssassinBasicAttackData 상태 Speed 복제 {step.AnimatorStateSpeed} ≠ 컨트롤러 {controllerSpeed}");
        }
    }

    [Test]
    public void ClaimedModeMustMatchServer_NormalOverServerEnhancedAccepted()
    {
        var normal = AssassinBasicAttackMode.Normal;
        var enhanced = AssassinBasicAttackMode.Enhanced;
        var transformed = AssassinBasicAttackMode.Transformed;

        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(normal, normal, true), Is.True);
        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(enhanced, enhanced, true), Is.True);
        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(transformed, transformed, true), Is.True);
        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(normal, enhanced, true), Is.True, "복제 지연 — 일반으로 수락, 강화는 남음");

        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(enhanced, normal, true), Is.False, "서버엔 강화 없음(이미 소모)");
        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(transformed, normal, true), Is.False);
        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(normal, transformed, true), Is.False);
        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(enhanced, transformed, true), Is.False);
        Assert.That(AssassinOwnerPredictionRules.AcceptsClaimedMode(transformed, transformed, false), Is.False, "변신 종료 대기");
    }

    [Test]
    public void VerifyContinuesOnlyWithinSameMode()
    {
        var normal = AssassinBasicAttackMode.Normal;
        var enhanced = AssassinBasicAttackMode.Enhanced;
        Assert.That(AssassinOwnerPredictionRules.ContinuesForVerify(true, true, normal, normal), Is.True);
        Assert.That(AssassinOwnerPredictionRules.ContinuesForVerify(true, true, normal, enhanced), Is.False);
        Assert.That(AssassinOwnerPredictionRules.ContinuesForVerify(true, false, normal, normal), Is.False);
        Assert.That(AssassinOwnerPredictionRules.ContinuesForVerify(false, true, normal, normal), Is.False);
        Assert.That(AssassinOwnerPredictionRules.VerifyStepCount(normal, 4), Is.EqualTo(4));
        Assert.That(AssassinOwnerPredictionRules.VerifyStepCount(enhanced, 4), Is.EqualTo(1));
        Assert.That(AssassinOwnerPredictionRules.VerifyStepCount(AssassinBasicAttackMode.Transformed, 4), Is.EqualTo(1));
    }

    // 정상 홀드 순환(일반 4타 ×2 + 1, 변신 묶음 3번 = 히트 4개씩)은 기대 간격 그대로 들어와도 전부 통과한다(공속 1, 여유 0).
    // 기대 간격보다 0.05초 빠른 히트는 걸린다.
    [Test]
    public void NormalAndTransformedLoops_PassAtExpectedInterval_FailWhenFaster()
    {
        AssassinBasicAttackData data = AssetDatabase.LoadAssetAtPath<AssassinBasicAttackData>(BasicAttackDataPath);
        Assert.That(data, Is.Not.Null, BasicAttackDataPath);

        var normal = new BasicAttackStepTiming[data.NormalSteps.Length];
        for (int i = 0; i < normal.Length; i++)
            normal[i] = Timing(data.NormalSteps[i]);
        var transformed = new[] { Timing(data.TransformedStep) };
        Assert.That(transformed[0].HitCount, Is.EqualTo(data.TransformedStep.HitCount), "변신 묶음 Hit 이벤트 수 = hitCount");

        RunLoop(normal, 9, "일반");
        RunLoop(transformed, 3, "변신 묶음");

        // 2타 히트를 기대 간격보다 0.05초 일찍 — 데미지 없음.
        var verifier = new BasicAttackServerVerifier();
        var settings = new BasicAttackVerifySettings(0f, 0.5f);
        verifier.BeginSwing(1, 0, normal.Length, true, false);
        Assert.That(verifier.VerifyHit(0, 1, 0, 0, normal, 1f, settings), Is.EqualTo(BasicAttackHitVerdict.Accepted));
        verifier.BeginSwing(2, 1, normal.Length, true, true);
        double early = BasicAttackVerifyRules.ExpectedInterval(normal[0], 0, normal[1], 0, false, false) - 0.05;
        Assert.That(verifier.VerifyHit(early, 2, 1, 0, normal, 1f, settings), Is.EqualTo(BasicAttackHitVerdict.TooFast));
    }

    private static void RunLoop(BasicAttackStepTiming[] timings, int swings, string label)
    {
        var verifier = new BasicAttackServerVerifier();
        var settings = new BasicAttackVerifySettings(0f, 0.5f);
        double now = 0;
        byte serial = 0;
        int previous = -1;
        int previousHit = 0;
        for (int n = 0; n < swings; n++)
        {
            int index = n % timings.Length;
            serial++;
            Assert.That(verifier.BeginSwing(serial, index, timings.Length, true, n > 0), Is.True, $"{label} {n + 1}번째 타 순서");
            for (int h = 0; h < timings[index].HitCount; h++)
            {
                if (previous >= 0)
                    now += BasicAttackVerifyRules.ExpectedInterval(timings[previous], previousHit, timings[index], h, h > 0, false) + 1e-4;
                Assert.That(verifier.VerifyHit(now, serial, index, h, timings, 1f, settings), Is.EqualTo(BasicAttackHitVerdict.Accepted),
                    $"{label} {n + 1}번째 타 히트 {h}");
                previous = index;
                previousHit = h;
            }
        }
    }

    private static BasicAttackStepTiming Timing(AssassinBasicAttackStepData step) =>
        BasicAttackStepTiming.FromClip(step.Clip, nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent),
            (int)DefaultAttackAnimationEventType.Hit, (int)DefaultAttackAnimationEventType.End, step.AnimatorStateSpeed);

    private static IEnumerable<(string, AssassinBasicAttackStepData)> Steps()
    {
        AssassinBasicAttackData data = AssetDatabase.LoadAssetAtPath<AssassinBasicAttackData>(BasicAttackDataPath);
        Assert.That(data, Is.Not.Null, BasicAttackDataPath);

        for (int i = 0; i < data.NormalSteps.Length; i++)
            yield return ($"{i + 1}타", data.NormalSteps[i]);
        yield return ("강타", data.EnhancedStep);
        yield return ("변신 묶음", data.TransformedStep);
    }
}
