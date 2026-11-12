using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 타임라인 키(PLAN-boss-victory-glitch 255 후속 6) — 곡선·구간 찾기·정렬/삽입·중간 키 자르기 ·
/// 옛 페이드 클립 → 세기 키 이관(Evaluate 같음) · 옛 2점 카메라 컷 → 화면 키(자세 같음) · Catmull-Rom 이 키를 지남.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryKeys).
/// </summary>
public sealed class BossVictoryKeysTests
{
    const float Eps = 1e-4f;

    static BossVictoryKey K(float u, float v, BossVictoryKeyCurve c = BossVictoryKeyCurve.Linear) => BossVictoryKey.Make(u, v, c);

    [Test]
    public void 곡선_모양()
    {
        foreach (BossVictoryKeyCurve c in System.Enum.GetValues(typeof(BossVictoryKeyCurve)))
        {
            Assert.AreEqual(0f, BossVictoryKeys.Ease(c, 0f), Eps, $"{c} 시작 0");
            Assert.AreEqual(1f, BossVictoryKeys.Ease(c, 1f), Eps, $"{c} 끝 1");
        }
        Assert.AreEqual(0.5f, BossVictoryKeys.Ease(BossVictoryKeyCurve.Linear, 0.5f), Eps);
        Assert.AreEqual(0.5f, BossVictoryKeys.Ease(BossVictoryKeyCurve.EaseInOut, 0.5f), Eps);
        Assert.AreEqual(0.25f, BossVictoryKeys.Ease(BossVictoryKeyCurve.EaseIn, 0.5f), Eps);
        Assert.AreEqual(0.75f, BossVictoryKeys.Ease(BossVictoryKeyCurve.EaseOut, 0.5f), Eps);
        Assert.AreEqual(0f, BossVictoryKeys.Ease(BossVictoryKeyCurve.Step, 0.999f), Eps, "계단 = 다음 키 전까지 이 키 값");
    }

    [Test]
    public void 옛_카메라_곡선과_같은_번호()
    {
        // 옛 컷 ease(0~3) 를 (int) 로 옮긴다 — 번호·모양이 같아야 한다.
        foreach (BossVictoryCameraCuts.Ease e in System.Enum.GetValues(typeof(BossVictoryCameraCuts.Ease)))
            for (float x = 0f; x <= 1f; x += 0.05f)
                Assert.AreEqual(BossVictoryCameraCuts.Evaluate(e, x), BossVictoryKeys.Ease((BossVictoryKeyCurve)(int)e, x), Eps, $"{e} {x}");
    }

    [Test]
    public void 구간_찾기와_값()
    {
        var keys = new List<BossVictoryKey> { K(0f, 0f), K(0.5f, 1f), K(1f, 0.2f) };
        BossVictoryKeys.Segment(keys, 0.25f, out int i, out float local);
        Assert.AreEqual(0, i);
        Assert.AreEqual(0.5f, local, Eps);
        BossVictoryKeys.Segment(keys, 0.75f, out i, out local);
        Assert.AreEqual(1, i);
        Assert.AreEqual(0.5f, local, Eps);
        Assert.AreEqual(0.5f, BossVictoryKeys.Value(keys, 0.25f), Eps);
        Assert.AreEqual(1f, BossVictoryKeys.Value(keys, 0.5f), Eps, "키 위치 = 키 값");
        Assert.AreEqual(0.6f, BossVictoryKeys.Value(keys, 0.75f), Eps);
        Assert.AreEqual(0f, BossVictoryKeys.Value(keys, -1f), Eps, "앞 = 첫 키");
        Assert.AreEqual(0.2f, BossVictoryKeys.Value(keys, 2f), Eps, "뒤 = 끝 키");
        Assert.AreEqual(0.7f, BossVictoryKeys.Value(new List<BossVictoryKey> { K(0f, 0.7f) }, 0.4f), Eps, "키 1개 = 고정");
        Assert.AreEqual(0f, BossVictoryKeys.Value(null, 0.4f), Eps);
    }

    [Test]
    public void 계단_구간은_다음_키까지_유지()
    {
        var keys = new List<BossVictoryKey> { K(0f, 0.3f, BossVictoryKeyCurve.Step), K(1f, 0.9f) };
        Assert.AreEqual(0.3f, BossVictoryKeys.Value(keys, 0.99f), Eps);
        Assert.AreEqual(0.9f, BossVictoryKeys.Value(keys, 1f), Eps);
    }

    [Test]
    public void 같은_시각_키_둘은_뒤_키로_넘어간다()
    {
        var keys = new List<BossVictoryKey> { K(0f, 0f), K(0.5f, 0.2f), K(0.5f, 0.8f), K(1f, 1f) };
        Assert.AreEqual(0.8f, BossVictoryKeys.Value(keys, 0.5f), Eps);
        Assert.AreEqual(0.1f, BossVictoryKeys.Value(keys, 0.25f), Eps);
    }

    [Test]
    public void 정렬_삽입_중간_키_자르기()
    {
        var keys = new List<BossVictoryKey> { K(0f, 0f), K(0.7f, 1f), K(0.3f, 2f), K(1f, 3f) };
        Assert.IsTrue(BossVictoryKeys.Sort(keys));
        Assert.AreEqual(new[] { 0f, 0.3f, 0.7f, 1f }, keys.ConvertAll(k => k.u).ToArray());
        Assert.IsFalse(BossVictoryKeys.Sort(keys), "정렬돼 있으면 안 바꾼다");

        int at = BossVictoryKeys.Insert(keys, K(0.5f, 9f));
        Assert.AreEqual(2, at);
        Assert.AreEqual(0.5f, keys[2].u);

        Assert.AreEqual(0f, BossVictoryKeys.ClampMiddleU(keys, 0, 0.4f), "시작 키 = 0 고정");
        Assert.AreEqual(1f, BossVictoryKeys.ClampMiddleU(keys, keys.Count - 1, 0.4f), "끝 키 = 1 고정");
        Assert.AreEqual(0.3f, BossVictoryKeys.ClampMiddleU(keys, 2, 0.1f), Eps, "앞 키를 못 넘는다");
        Assert.AreEqual(0.7f, BossVictoryKeys.ClampMiddleU(keys, 2, 0.95f), Eps, "뒤 키를 못 넘는다");
        Assert.AreEqual(0.6f, BossVictoryKeys.ClampMiddleU(keys, 2, 0.6f), Eps);
    }

    // 옛 페이드 식 그대로(키 없는 클립) vs 이관한 키 — 같은 시각 같은 값.
    static void AssertSameShape(BossVictoryClip legacy, string label)
    {
        Assert.IsFalse(legacy.HasKeys);
        BossVictoryClip keyed = legacy;
        Assert.IsTrue(keyed.MigrateFadesToKeys(), label);
        Assert.IsTrue(keyed.HasKeys);
        Assert.AreEqual(0, BossVictoryKeys.Sort(keyed.keys) ? 1 : 0, "이관 키는 정렬돼 있다");
        Assert.AreEqual(0f, keyed.keys[0].u);
        Assert.AreEqual(1f, keyed.keys[keyed.keys.Count - 1].u);
        for (int i = -2; i <= 202; i++)
        {
            float t = legacy.start + legacy.length * i / 200f;
            Assert.AreEqual(legacy.Evaluate(t), keyed.Evaluate(t), Eps, $"{label} t={t}");
        }
        Assert.IsFalse(keyed.MigrateFadesToKeys(), "두 번째는 그대로");
    }

    [Test]
    public void 페이드_클립_이관은_값이_같다()
    {
        AssertSameShape(BossVictoryClip.Make(0.2f, 1f, 0.3f, 0.4f, 0.8f), "인·아웃");
        AssertSameShape(BossVictoryClip.Make(0f, 0.5f), "페이드 없음");
        AssertSameShape(BossVictoryClip.Make(1f, 0.6f, 0.2f, 0f, 1f), "인만");
        AssertSameShape(BossVictoryClip.Make(1f, 0.6f, 0f, 0.25f, 0.5f), "아웃만");
        AssertSameShape(BossVictoryClip.Make(1f, 0.6f, 0.3f, 0.3f, 1f), "인 + 아웃 = 길이");
        AssertSameShape(BossVictoryClip.Make(0.5f, 0.08f, 0.02f, 0.04f, 0.6f), "짧은 펄스");
    }

    [Test]
    public void 겹친_페이드는_꼭대기와_양_끝이_같다()
    {
        BossVictoryClip legacy = BossVictoryClip.Make(0f, 1f, 0.8f, 0.6f, 1f);
        BossVictoryClip keyed = legacy;
        keyed.MigrateFadesToKeys();
        Assert.AreEqual(3, keyed.keys.Count);
        float peakT = keyed.keys[1].u * legacy.length;
        Assert.AreEqual(legacy.Evaluate(peakT), keyed.Evaluate(peakT), Eps, "만나는 지점");
        Assert.AreEqual(0f, keyed.Evaluate(0f), Eps);
        Assert.AreEqual(0f, keyed.Evaluate(1f), Eps);
    }

    [Test]
    public void 기본_타임라인은_모두_키이고_효과_세기가_예전과_같다()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        foreach (BossVictoryTrack track in BossVictoryTimeline.Tracks)
        {
            if (!BossVictoryTimeline.HasFades(track)) continue;
            foreach (BossVictoryClip c in tl.Clips(track))
            {
                Assert.IsTrue(c.HasKeys, $"{track} 키");
                Assert.AreEqual(0f, c.fadeIn);
                Assert.AreEqual(0f, c.fadeOut);
            }
        }
        foreach (BossVictoryCameraCuts.Cut cut in tl.cameraCutShots) Assert.IsTrue(cut.HasKeys, "컷 키");
        Assert.AreEqual(0, tl.MigrateKeys(), "다시 이관 = 0");
    }

    [Test]
    public void 키_복사는_목록을_따로_쓴다()
    {
        BossVictoryClip a = BossVictoryClip.Make(0f, 1f, 0.2f, 0.2f);
        a.MigrateFadesToKeys();
        BossVictoryClip b = a.WithCopiedKeys();
        b.keys.Add(K(1f, 0f));
        Assert.AreNotEqual(a.keys.Count, b.keys.Count);

        BossVictoryCameraCuts.Cut c = BossVictoryCameraCuts.Cut.Default;
        BossVictoryCameraCuts.Cut d = c.WithCopiedKeys();
        d.keys.Add(d.keys[0]);
        Assert.AreEqual(1, c.keys.Count);
    }

    static BossVictoryCameraCuts.Cut Legacy(bool hasEnd, BossVictoryCameraCuts.Ease ease) => new BossVictoryCameraCuts.Cut
    {
        anchor = BossVictoryCameraCuts.Anchor.Killer,
        startPosition = new Vector3(1f, 2f, -5f), startRotation = Quaternion.Euler(10f, 20f, 0f), startFov = 35f,
        hasEnd = hasEnd, endPosition = new Vector3(-3f, 6f, 2f), endRotation = Quaternion.Euler(40f, -30f, 0f), endFov = 55f,
        ease = ease, hideWalls = true,
    };

    [Test]
    public void 옛_2점_컷_이관은_자세가_같다()
    {
        var s = new BossVictoryCameraCuts.Subjects { Killer = new Vector3(3f, 0f, 1f), BossBase = new Vector3(-2f, 0f, 9f) };
        foreach (bool hasEnd in new[] { false, true })
        foreach (BossVictoryCameraCuts.Ease e in System.Enum.GetValues(typeof(BossVictoryCameraCuts.Ease)))
        {
            BossVictoryCameraCuts.Cut legacy = Legacy(hasEnd, e);
            BossVictoryCameraCuts.Cut keyed = legacy;
            Assert.IsTrue(keyed.MigrateToKeys());
            Assert.AreEqual(hasEnd ? 2 : 1, keyed.keys.Count, "끝 없으면 키 1개 = 고정");
            Assert.AreEqual(hasEnd, keyed.Moves);
            for (float u = 0f; u <= 1.0001f; u += 0.05f)
            {
                BossVictoryCameraCuts.Pose(legacy, s, u, out Vector3 p0, out Quaternion r0, out float f0);
                BossVictoryCameraCuts.Pose(keyed, s, u, out Vector3 p1, out Quaternion r1, out float f1);
                Assert.Less(Vector3.Distance(p0, p1), Eps, $"{hasEnd} {e} {u} 위치");
                Assert.Less(Quaternion.Angle(r0, r1), 0.01f, $"{hasEnd} {e} {u} 회전");
                Assert.AreEqual(f0, f1, Eps, $"{hasEnd} {e} {u} FOV");
            }
            Assert.IsFalse(keyed.MigrateToKeys(), "두 번째는 그대로");
        }
    }

    [Test]
    public void 중간_키를_지나고_부드러운_경로도_키를_지난다()
    {
        var keys = new List<BossVictoryCameraCuts.CutKey>
        {
            BossVictoryCameraCuts.CutKey.Make(0f, new Vector3(0f, 0f, 0f), Quaternion.identity, 30f, BossVictoryKeyCurve.Linear),
            BossVictoryCameraCuts.CutKey.Make(0.4f, new Vector3(4f, 2f, 0f), Quaternion.Euler(0f, 90f, 0f), 50f, BossVictoryKeyCurve.Linear),
            BossVictoryCameraCuts.CutKey.Make(1f, new Vector3(4f, 2f, 6f), Quaternion.Euler(0f, 180f, 0f), 40f, BossVictoryKeyCurve.Linear),
        };
        foreach (bool smooth in new[] { false, true })
        {
            for (int i = 0; i < keys.Count; i++)
            {
                BossVictoryCameraCuts.LocalPose(keys, smooth, keys[i].u, out Vector3 p, out Quaternion r, out float fov);
                Assert.Less(Vector3.Distance(p, keys[i].position), Eps, $"smooth={smooth} 키 {i} 위치");
                Assert.AreEqual(keys[i].fov, fov, Eps, $"smooth={smooth} 키 {i} FOV");
            }
        }
        BossVictoryCameraCuts.LocalPose(keys, false, 0.2f, out Vector3 lin, out _, out float linFov);
        Assert.Less(Vector3.Distance(lin, new Vector3(2f, 1f, 0f)), Eps, "직선 = 구간 가운데");
        Assert.AreEqual(40f, linFov, Eps);
        BossVictoryCameraCuts.LocalPose(keys, true, 0.2f, out Vector3 curved, out _, out _);
        Assert.Greater(Vector3.Distance(lin, curved), 0.01f, "부드러운 경로는 직선에서 벗어난다");

        var two = keys.GetRange(0, 2);
        BossVictoryCameraCuts.LocalPose(two, true, 0.2f, out Vector3 a, out _, out _);
        BossVictoryCameraCuts.LocalPose(two, false, 0.2f, out Vector3 b, out _, out _);
        Assert.Less(Vector3.Distance(a, b), Eps, "키 2개면 부드러운 경로 = 직선");
    }
}
