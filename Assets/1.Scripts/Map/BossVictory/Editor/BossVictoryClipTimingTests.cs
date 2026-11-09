using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — 클립 노드 '클립 시간 ↔ 타임라인 초' 변환(255 후속 — 타임라인과 실제 시각 어긋남) — 표본 없음 = 배율 1 ·
/// 구간 선형 · 역변환 왕복 · 표본 밖 = 끝 구간 배율 · 멈춤/되돌림 표본 버림.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryClipTiming).
/// </summary>
public sealed class BossVictoryClipTimingTests
{
    const float Eps = 1e-4f;

    // 팔라딘 R 실측과 비슷한 모양 — 앞 0.5초는 ×2, 그 뒤 ×3.3(속도 파라미터 + 맞은 뒤 배율).
    static List<Vector2> Samples() => new List<Vector2>
    {
        new Vector2(0f, 0f),
        new Vector2(0.5f, 1f),
        new Vector2(1.8f, 4.933f),
    };

    [Test]
    public void 표본_없으면_배율_1()
    {
        Assert.AreEqual(2.5f, BossVictoryClipTiming.ClipToTimeline(null, 2.5f), Eps);
        Assert.AreEqual(2.5f, BossVictoryClipTiming.TimelineToClip(new List<Vector2>(), 2.5f), Eps);
        Assert.AreEqual(1f, BossVictoryClipTiming.LastRate(null), Eps);
    }

    [Test]
    public void 구간마다_선형으로_타임라인_초()
    {
        List<Vector2> s = Samples();
        Assert.AreEqual(0.25f, BossVictoryClipTiming.ClipToTimeline(s, 0.5f), Eps);
        Assert.AreEqual(1.8f, BossVictoryClipTiming.ClipToTimeline(s, 4.933f), Eps, "148f 끝 자르기 = 노드 시작 뒤 1.8초(배율 1 가정이면 4.93초)");
        Assert.AreEqual(0.5f + 1.3f * 0.5f, BossVictoryClipTiming.ClipToTimeline(s, 1f + 3.933f * 0.5f), Eps);
    }

    [Test]
    public void 역변환_왕복()
    {
        List<Vector2> s = Samples();
        foreach (float clip in new[] { 0f, 0.3f, 1f, 2.2f, 4.933f, 5.5f })
            Assert.AreEqual(clip, BossVictoryClipTiming.TimelineToClip(s, BossVictoryClipTiming.ClipToTimeline(s, clip)), Eps, $"클립 {clip}");
    }

    [Test]
    public void 표본_밖은_끝_구간_배율()
    {
        List<Vector2> s = Samples();
        float rate = 3.933f / 1.3f;
        Assert.AreEqual(rate, BossVictoryClipTiming.LastRate(s), Eps);
        Assert.AreEqual(1.8f + 1f / rate, BossVictoryClipTiming.ClipToTimeline(s, 5.933f), Eps);
        Assert.AreEqual(4.933f + 0.2f * rate, BossVictoryClipTiming.TimelineToClip(s, 2f), Eps);

        var late = new List<Vector2> { new Vector2(0.2f, 0.1f), new Vector2(0.4f, 0.5f) };
        Assert.AreEqual(0.1f, BossVictoryClipTiming.ClipToTimeline(late, 0f), Eps, "첫 표본 앞 = 배율 1");
        Assert.AreEqual(0f, BossVictoryClipTiming.TimelineToClip(late, 0f), Eps, "클립 시간은 0 밑으로 안 간다");
    }

    [Test]
    public void 멈춤과_되돌림_표본은_버린다()
    {
        var s = new List<Vector2>();
        Assert.IsTrue(BossVictoryClipTiming.Append(s, 0f, 0f));
        Assert.IsTrue(BossVictoryClipTiming.Append(s, 0.1f, 0.2f));
        Assert.IsFalse(BossVictoryClipTiming.Append(s, 0.2f, 0.2f), "멈춤(클립 시간 그대로)");
        Assert.IsFalse(BossVictoryClipTiming.Append(s, 0.3f, 0.1f), "되돌림");
        Assert.IsFalse(BossVictoryClipTiming.Append(s, 0.1f, 0.4f), "타임라인 초가 안 늘었다");
        Assert.IsTrue(BossVictoryClipTiming.Append(s, 0.4f, 0.5f));
        Assert.AreEqual(3, s.Count);
    }

    [Test]
    public void 표본_키는_노드와_클립마다()
    {
        Assert.AreEqual("Finisher:Garen_R", BossVictoryClipTiming.Key(false, "Garen_R"));
        Assert.AreEqual("BossHit:Hit", BossVictoryClipTiming.Key(true, "Hit"));
    }
}
