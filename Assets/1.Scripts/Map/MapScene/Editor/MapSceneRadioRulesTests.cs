using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 메인 1~4구역 무전·2구역 적 소개 순수 규칙(PLAN-main-radio-zone1-4) — 판마다 1회 · 소개 연출과의 관계 · 3구역 거리 · 2구역 구도·대사 시간.
/// </summary>
public sealed class MapSceneRadioRulesTests
{
    // ─── 판마다 1회 ─────────────────────────────────────────────────────

    [Test]
    public void 같은_무전은_판마다_한_번()
    {
        var log = new MapSceneRadioLog();
        Assert.IsTrue(log.TryPlay(TutorialGuideIds.MainZ2Clear));
        Assert.IsFalse(log.TryPlay(TutorialGuideIds.MainZ2Clear), "중복 사건·영역 재진입");
        Assert.IsTrue(log.HasPlayed(TutorialGuideIds.MainZ2Clear));
        Assert.IsTrue(log.TryPlay(TutorialGuideIds.MainZ3Clear), "다른 id 는 따로");
    }

    [Test]
    public void 빈_id_는_틀지_않는다()
    {
        var log = new MapSceneRadioLog();
        Assert.IsFalse(log.TryPlay(null));
        Assert.IsFalse(log.TryPlay(""));
    }

    // ─── 표시 ───────────────────────────────────────────────────────────

    [Test]
    public void 줄은_시작부터_길이_동안만_보인다()
    {
        Assert.IsFalse(MapSceneRadioRules.ShouldShow(10d, 3f, 9.9d, false, null), "지연 중");
        Assert.IsTrue(MapSceneRadioRules.ShouldShow(10d, 3f, 10d, false, null));
        Assert.IsTrue(MapSceneRadioRules.ShouldShow(10d, 3f, 12.9d, false, null));
        Assert.IsFalse(MapSceneRadioRules.ShouldShow(10d, 3f, 13d, false, null), "끝남 — 늦게 붙은 클라도 지난 줄은 안 본다");
    }

    [Test]
    public void 소개_연출_중엔_숨긴다()
    {
        Assert.IsFalse(MapSceneRadioRules.ShouldShow(10d, 3f, 11d, true, new List<double> { 5d }));
    }

    [Test]
    public void 줄이_시작된_뒤_소개가_시작되면_그_줄은_버린다()
    {
        // 10초에 시작한 무전 · 11초에 소개 시작 → 12초에 소개가 끝나도 다시 나오지 않는다(명세 §8.1).
        var starts = new List<double> { 11d };
        Assert.IsFalse(MapSceneRadioRules.ShouldShow(10d, 5f, 12d, false, starts));
    }

    [Test]
    public void 소개가_끝난_뒤_시작한_줄은_보인다()
    {
        // 소개는 5초에 시작해 이미 끝났다 · 무전은 10초 시작(예: 다리 연결).
        Assert.IsTrue(MapSceneRadioRules.ShouldShow(10d, 3f, 11d, false, new List<double> { 5d }));
    }

    // ─── 3구역 상대 거리 ────────────────────────────────────────────────

    [Test]
    public void 존3_상대는_수평_거리_안이고_같은_층일_때만()
    {
        Assert.IsTrue(MapSceneZone3SightRules.InSight(Vector3.zero, new Vector3(10f, 0f, 9f), 14f));
        Assert.IsFalse(MapSceneZone3SightRules.InSight(Vector3.zero, new Vector3(10f, 0f, 11f), 14f), "14.9m");
        Assert.IsFalse(MapSceneZone3SightRules.InSight(Vector3.zero, new Vector3(3f, 6f, 0f), 14f), "다른 층");
    }

    // ─── 2구역 구도·시간 ────────────────────────────────────────────────

    [Test]
    public void 적_무리_구도는_범위_중심과_감싸는_반경()
    {
        var points = new List<Vector3> { new Vector3(0f, 1f, 0f), new Vector3(6f, 3f, 8f) };
        Assert.IsTrue(MapSceneZone2IntroRules.TryFrame(points, 1f, out Vector3 center, out float radius));
        Assert.AreEqual(3f, center.x, 1e-4f);
        Assert.AreEqual(2f, center.y, 1e-4f);
        Assert.AreEqual(4f, center.z, 1e-4f);
        Assert.AreEqual(5f, radius, 1e-4f);
    }

    [Test]
    public void 적이_없으면_구도_실패_한_마리면_최소_반경()
    {
        Assert.IsFalse(MapSceneZone2IntroRules.TryFrame(new List<Vector3>(), 1f, out _, out _));
        Assert.IsTrue(MapSceneZone2IntroRules.TryFrame(new List<Vector3> { Vector3.one }, 2.5f, out _, out float radius));
        Assert.AreEqual(2.5f, radius, 1e-4f);
    }

    [Test]
    public void 기본값_2구역_경고는_적_샷_도착_뒤_시작해_복귀_전에_끝난다()
    {
        // 기본 문구 '잠깐, 저 녀석들 우리를 발견했어.|조심해.' = 2조각(최대 폭 안).
        var data = ScriptableObject.CreateInstance<MapSceneZone2IntroData>();
        try
        {
            CinematicLineTiming line = data.Line;
            float arrival = CinematicTimeline.ArrivalTime(data.Shots, line.shotIndex);
            Assert.Greater(data.LineStart, arrival - 1e-4f, "적 구도 도착 → 경고 순서(명세 §6.3)");
            Assert.IsTrue(MapSceneZone2IntroRules.LineEndsBeforeReturn(data.LineStart, 2, line.chunkSeconds, line.gapSeconds,
                                                                       data.IntroSeconds, data.LineTailSeconds),
                          $"대사 끝 {data.LineStart + CinematicLineSchedule.TotalSeconds(2, line.chunkSeconds, line.gapSeconds):0.##}초 + 여유 > 복귀 {data.IntroSeconds:0.##}초");
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void 대사가_복귀보다_길면_실패로_본다()
    {
        Assert.IsFalse(MapSceneZone2IntroRules.LineEndsBeforeReturn(1.6f, 2, 2.2f, 0.2f, 6.0f, 0.4f));
        Assert.IsTrue(MapSceneZone2IntroRules.LineEndsBeforeReturn(1.6f, 2, 2.2f, 0.2f, 6.6f, 0.4f));
    }

    [Test]
    public void 기본값_일반_무전_표는_명세_무전을_전부_든다()
    {
        var data = ScriptableObject.CreateInstance<MapSceneRadioData>();
        try
        {
            var ids = new HashSet<string>();
            foreach (MapSceneRadioLineTiming line in data.Lines) ids.Add(line.lineId);
            foreach (string id in new[] { TutorialGuideIds.MainZ1Enter, TutorialGuideIds.MainZ2Clear, TutorialGuideIds.MainZ3Enemy,
                                          TutorialGuideIds.MainZ3Clear, TutorialGuideIds.Z4BridgeOpen, TutorialGuideIds.Z4ExitOpen })
                Assert.IsTrue(ids.Contains(id), id);
            Assert.AreEqual(TutorialGuideIds.MainZ2Enter, data.TimingOf(TutorialGuideIds.MainZ2Enter).lineId, "표에 없으면 기본값에 id 만 바꿔 준다");
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }
}
