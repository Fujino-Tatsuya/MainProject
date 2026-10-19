using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>거너 클립 애니 이벤트 저작의 시간 환산·병합 규칙 고정 (PLAN-anim-event-timing P3 ①).</summary>
public sealed class GunnerAnimEventAuthoringTests
{
    const string Fn = "HandleSkillEvent";

    [Test]
    public void ClipTime_ScalesByStateSpeed_AndClampsToClip()
    {
        Assert.That(AnimEventTiming.ToClipTime(0.2f, 1f, 1.2f), Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That(AnimEventTiming.ToClipTime(0.2f, 2f, 1.2f), Is.EqualTo(0.4f).Within(1e-5f), "속도 2 = 클립이 두 배로 진행");
        Assert.That(AnimEventTiming.ToClipTime(1f, 2f, 1.2f), Is.EqualTo(1.2f), "클립 끝을 넘지 않는다");
        Assert.That(AnimEventTiming.ToClipTime(-1f, 1f, 1.2f), Is.EqualTo(0f));
    }

    [Test]
    public void Normalized_IsFractionOfClipLength()
    {
        Assert.That(AnimEventTiming.ToNormalized(0.3f, 0.9f), Is.EqualTo(1f / 3f).Within(1e-5f));
        Assert.That(AnimEventTiming.ToNormalized(0.3f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void Merge_AddsMissing_SortedByTime()
    {
        var wanted = new List<(int, float)> { (1, 0.5f), (0, 0.2f) };
        AnimationEvent[] result = AnimEventTiming.Merge(null, Fn, wanted, false, out int added, out int moved);

        Assert.That(added, Is.EqualTo(2));
        Assert.That(moved, Is.EqualTo(0));
        Assert.That(result[0].intParameter, Is.EqualTo(0));
        Assert.That(result[1].intParameter, Is.EqualTo(1));
        Assert.That(result[1].functionName, Is.EqualTo(Fn));
    }

    [Test]
    public void Merge_KeepsUserMovedEvent_UnlessReset()
    {
        var existing = new[] { new AnimationEvent { functionName = Fn, intParameter = 1, time = 0.8f } };
        var wanted = new List<(int, float)> { (1, 0.5f) };

        AnimationEvent[] kept = AnimEventTiming.Merge(existing, Fn, wanted, false, out int added, out int moved);
        Assert.That(added + moved, Is.EqualTo(0), "재실행 = 바뀌는 것 없음");
        Assert.That(kept[0].time, Is.EqualTo(0.8f));

        AnimationEvent[] reset = AnimEventTiming.Merge(existing, Fn, wanted, true, out added, out moved);
        Assert.That(moved, Is.EqualTo(1));
        Assert.That(reset[0].time, Is.EqualTo(0.5f));
    }

    [Test]
    public void Merge_LeavesOtherFunctionsAndTypesAlone()
    {
        var existing = new[]
        {
            new AnimationEvent { functionName = "PlayEffect", stringParameter = "Muzzle", time = 0.1f },
            new AnimationEvent { functionName = Fn, intParameter = 2, time = 0f },
        };
        var wanted = new List<(int, float)> { (1, 0.3f) };

        AnimationEvent[] result = AnimEventTiming.Merge(existing, Fn, wanted, true, out int added, out int moved);

        Assert.That(added, Is.EqualTo(1));
        Assert.That(moved, Is.EqualTo(0));
        Assert.That(result.Length, Is.EqualTo(3));
        Assert.That(System.Array.Exists(result, e => e.functionName == "PlayEffect"), Is.True);
    }
}
