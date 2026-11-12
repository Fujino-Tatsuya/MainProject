using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 4단계 카메라 컷(PLAN-boss-victory-glitch) — 컷 고르기·기본 목록 · 타임라인 '카메라 컷' 트랙(255: 트랙 구조·컷 값 짝·길이 자동·옛 cuts 이관) ·
/// 시작→끝 화면(255 후속 2: 기준 좌표계 왕복·축 방향·보간·곡선·끝 없으면 고정·시각 t 진행) · 컷 블렌드(255 후속 8: 구간·곡선·앞 컷 길이 제한·첫 컷 게임 카메라·0 = 기존).
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryCameraCut).
/// </summary>
public sealed class BossVictoryCameraCutTests
{
    const string AssetPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";

    static BossVictoryCameraCuts.Cut C(BossVictoryCameraCuts.Anchor anchor = BossVictoryCameraCuts.Anchor.Middle) =>
        new BossVictoryCameraCuts.Cut { anchor = anchor, startPosition = new Vector3(0f, 2f, -5f), startRotation = Quaternion.identity, startFov = 40f, hideWalls = true };

    static List<BossVictoryClip> Starts(params float[] starts)
    {
        var list = new List<BossVictoryClip>();
        foreach (float s in starts) list.Add(BossVictoryClip.Make(s, 0.1f));
        return list;
    }

    static BossVictoryCameraCuts.Subjects S() => new BossVictoryCameraCuts.Subjects
    {
        Killer = Vector3.zero,
        BossBase = new Vector3(0f, 0f, 10f),
    };

    [Test]
    public void 첫_컷_전은_게임_카메라()
    {
        var cuts = Starts(0.33f, 1.67f);
        Assert.AreEqual(-1, BossVictoryCameraCuts.ActiveIndex(cuts, 0.2f));
        Assert.AreEqual(-1, BossVictoryCameraCuts.ActiveIndex(cuts, -1f));
        Assert.AreEqual(-1, BossVictoryCameraCuts.ActiveIndex(null, 5f));
    }

    [Test]
    public void 시작이_지난_컷_중_가장_늦은_것_순서_무관()
    {
        var cuts = Starts(2.6f, 0.33f, 1.67f);
        Assert.AreEqual(1, BossVictoryCameraCuts.ActiveIndex(cuts, 0.33f));
        Assert.AreEqual(2, BossVictoryCameraCuts.ActiveIndex(cuts, 2f));
        Assert.AreEqual(0, BossVictoryCameraCuts.ActiveIndex(cuts, 9f));
    }

    // ── 255 후속 2: 시작 → 끝 화면(기준 좌표계) ──

    [Test]
    public void 기준_좌표계_축은_처치자에서_보스_방향()
    {
        // 처치자 (0,0,0) → 보스 (10,0,0) — 축 = 월드 +X. 기준 +Z 로 1m = 월드 +X 로 1m.
        var s = new BossVictoryCameraCuts.Subjects { Killer = Vector3.zero, BossBase = new Vector3(10f, 0f, 0f) };
        BossVictoryCameraCuts.ToWorld(BossVictoryCameraCuts.Anchor.Killer, s, Vector3.forward, Quaternion.identity, out Vector3 p, out Quaternion r);
        Assert.Less(Vector3.Distance(new Vector3(1f, 0f, 0f), p), 1e-4f, $"{p:F6}");   // 부동소수 오차(정확 비교 X)
        Assert.Greater(Vector3.Dot(r * Vector3.forward, Vector3.right), 0.999f, "기준 정면 = 처치자에서 보스를 봄(옛 yaw 0 과 같은 정의)");
        BossVictoryCameraCuts.ToWorld(BossVictoryCameraCuts.Anchor.Middle, s, Vector3.zero, Quaternion.identity, out p, out _);
        Assert.AreEqual(new Vector3(5f, 0f, 0f), p, "가운데");
        BossVictoryCameraCuts.ToWorld(BossVictoryCameraCuts.Anchor.Boss, s, Vector3.up, Quaternion.identity, out p, out _);
        Assert.AreEqual(new Vector3(10f, 1f, 0f), p, "보스");
    }

    [Test]
    public void 상대_변환_왕복_그리고_다른_자리에서_같은_구도()
    {
        var a = new BossVictoryCameraCuts.Subjects { Killer = new Vector3(3f, 0f, -2f), BossBase = new Vector3(-4f, 0.5f, 7f) };
        Vector3 wp = new Vector3(1f, 6f, -9f);
        Quaternion wr = Quaternion.Euler(25f, 130f, 3f);
        foreach (BossVictoryCameraCuts.Anchor anchor in System.Enum.GetValues(typeof(BossVictoryCameraCuts.Anchor)))
        {
            BossVictoryCameraCuts.ToLocal(anchor, a, wp, wr, out Vector3 lp, out Quaternion lr);
            BossVictoryCameraCuts.ToWorld(anchor, a, lp, lr, out Vector3 p, out Quaternion r);
            Assert.Less(Vector3.Distance(wp, p), 1e-4f, anchor + " 위치 왕복");
            Assert.Greater(Mathf.Abs(Quaternion.Dot(wr, r)), 0.99999f, anchor + " 회전 왕복");

            // 다른 자리·방향에서 죽어도 기준점에서 본 구도(거리·축에 대한 각)가 같다.
            var b = new BossVictoryCameraCuts.Subjects { Killer = new Vector3(50f, 2f, 50f), BossBase = new Vector3(50f, 2f, 40f) };
            BossVictoryCameraCuts.ToWorld(anchor, b, lp, lr, out Vector3 pb, out Quaternion rb);
            Vector3 originA = BossVictoryCameraCuts.AnchorPoint(anchor, a), originB = BossVictoryCameraCuts.AnchorPoint(anchor, b);
            Assert.AreEqual(Vector3.Distance(wp, originA), Vector3.Distance(pb, originB), 1e-3f, anchor + " 기준점 거리 같음");
            float camToAxisA = Vector3.SignedAngle(BossVictoryCameraCuts.Axis(a), wr * Vector3.forward, Vector3.up);
            float camToAxisB = Vector3.SignedAngle(BossVictoryCameraCuts.Axis(b), rb * Vector3.forward, Vector3.up);
            Assert.AreEqual(camToAxisA, camToAxisB, 1e-2f, anchor + " 축 기준 시선 방위 같음");
        }
    }

    [Test]
    public void 끝_화면이_없으면_고정()
    {
        var cut = C(BossVictoryCameraCuts.Anchor.Killer);
        cut.endPosition = new Vector3(9f, 9f, 9f);
        cut.endFov = 80f;
        BossVictoryCameraCuts.Pose(cut, S(), 0f, out Vector3 p0, out Quaternion r0, out float f0);
        BossVictoryCameraCuts.Pose(cut, S(), 0.7f, out Vector3 p1, out Quaternion r1, out float f1);
        Assert.AreEqual(p0, p1);
        Assert.AreEqual(f0, f1);
        Assert.AreEqual(new Vector3(0f, 2f, -5f), p0, "처치자 (0,0,0)·축 +Z — 기준 = 월드");
        Assert.AreEqual(40f, f0);
    }

    [Test]
    public void 시작에서_끝으로_보간_위치_회전_FOV()
    {
        var cut = C(BossVictoryCameraCuts.Anchor.Killer);
        cut.hasEnd = true;
        cut.endPosition = new Vector3(0f, 2f, 5f);
        cut.endRotation = Quaternion.Euler(0f, 90f, 0f);
        cut.endFov = 60f;
        cut.ease = BossVictoryCameraCuts.Ease.Linear;
        BossVictoryCameraCuts.Pose(cut, S(), 0f, out Vector3 p, out Quaternion r, out float f);
        Assert.AreEqual(new Vector3(0f, 2f, -5f), p);
        Assert.AreEqual(40f, f, 1e-4f);
        BossVictoryCameraCuts.Pose(cut, S(), 0.5f, out p, out r, out f);
        Assert.Less(Vector3.Distance(new Vector3(0f, 2f, 0f), p), 1e-4f);
        Assert.AreEqual(50f, f, 1e-4f);
        Assert.AreEqual(45f, Quaternion.Angle(Quaternion.identity, r), 1e-2f);
        BossVictoryCameraCuts.Pose(cut, S(), 1f, out p, out r, out f);
        Assert.Less(Vector3.Distance(cut.endPosition, p), 1e-4f);
        Assert.AreEqual(60f, f, 1e-4f);
        BossVictoryCameraCuts.Pose(cut, S(), 3f, out Vector3 pOver, out _, out _);
        Assert.Less(Vector3.Distance(cut.endPosition, pOver), 1e-4f, "진행은 0~1 로 자른다");
    }

    [Test]
    public void 이동_곡선()
    {
        Assert.AreEqual(0.5f, BossVictoryCameraCuts.Evaluate(BossVictoryCameraCuts.Ease.Linear, 0.5f), 1e-6f);
        Assert.AreEqual(0.25f, BossVictoryCameraCuts.Evaluate(BossVictoryCameraCuts.Ease.EaseIn, 0.5f), 1e-6f);
        Assert.AreEqual(0.75f, BossVictoryCameraCuts.Evaluate(BossVictoryCameraCuts.Ease.EaseOut, 0.5f), 1e-6f);
        Assert.AreEqual(0.5f, BossVictoryCameraCuts.Evaluate(BossVictoryCameraCuts.Ease.EaseInOut, 0.5f), 1e-6f);
        Assert.Less(BossVictoryCameraCuts.Evaluate(BossVictoryCameraCuts.Ease.EaseInOut, 0.2f), 0.2f, "ease in/out — 처음 느림");
        Assert.Greater(BossVictoryCameraCuts.Evaluate(BossVictoryCameraCuts.Ease.EaseInOut, 0.8f), 0.8f, "끝 느림");
        foreach (BossVictoryCameraCuts.Ease e in System.Enum.GetValues(typeof(BossVictoryCameraCuts.Ease)))
        {
            Assert.AreEqual(0f, BossVictoryCameraCuts.Evaluate(e, -1f), 1e-6f, e + " 0");
            Assert.AreEqual(1f, BossVictoryCameraCuts.Evaluate(e, 2f), 1e-6f, e + " 1");
        }
    }

    [Test]
    public void 컷_진행은_시각_t_기준_다음_컷_시작까지()
    {
        var tl = new BossVictoryTimeline { lobbyTransitionTime = 5f };
        tl.cameraCut.AddRange(Starts(1f, 3f));
        Assert.AreEqual(0f, tl.CameraCutProgress(0, 1f), 1e-6f);
        Assert.AreEqual(0.5f, tl.CameraCutProgress(0, 2f), 1e-6f);
        Assert.AreEqual(1f, tl.CameraCutProgress(0, 3f), 1e-6f);
        Assert.AreEqual(0.5f, tl.CameraCutProgress(1, 4f), 1e-6f, "마지막 컷 = 로비 전환까지");
        Assert.AreEqual(0.5f, tl.CameraCutProgress(0, 2f), 1e-6f, "되감아도 같은 t 면 같은 값");
    }

    [Test]
    public void 퇴화한_회전은_단위로()
    {
        var cut = C();
        cut.startRotation = default;   // 직렬화 기본값 (0,0,0,0)
        BossVictoryCameraCuts.Pose(cut, S(), 0f, out _, out Quaternion r, out _);
        Assert.Greater(Vector3.Dot(r * Vector3.forward, Vector3.forward), 0.999f);
    }

    [Test]
    public void 기본_목록은_비어_있어_게임_카메라만_쓴다()
    {
        var settings = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            BossVictoryTimeline tl = settings.timeline;
            Assert.AreEqual(0, tl.cameraCut.Count);
            Assert.AreEqual(0, tl.cameraCutShots.Count);
            Assert.AreEqual(-1, tl.ActiveCameraCut(3f));
        }
        finally { Object.DestroyImmediate(settings); }
    }

    // ── 255: 타임라인 '카메라 컷' 트랙 ──

    [Test]
    public void 카메라_컷_트랙_구조()
    {
        var tl = new BossVictoryTimeline();
        CollectionAssert.Contains(BossVictoryTimeline.Tracks, BossVictoryTrack.CameraCut);
        Assert.AreSame(tl.cameraCut, tl.Clips(BossVictoryTrack.CameraCut));
        Assert.AreEqual(BossVictoryTimeline.TrackKind.Cut, BossVictoryTimeline.KindOf(BossVictoryTrack.CameraCut));
        Assert.IsFalse(BossVictoryTimeline.HasFades(BossVictoryTrack.CameraCut), "컷은 시작만 — 페이드·세기 없음");
        Assert.AreEqual("카메라 컷", BossVictoryTimeline.TrackLabel(BossVictoryTrack.CameraCut));
    }

    [Test]
    public void 컷은_다음_컷_시작까지_유지_클립_길이와_무관()
    {
        var tl = new BossVictoryTimeline();
        tl.cameraCut.Add(BossVictoryClip.Make(0.5f, 0.05f));   // 길이가 짧아도
        tl.cameraCut.Add(BossVictoryClip.Make(2f, 0.05f));
        Assert.AreEqual(-1, tl.ActiveCameraCut(0.4f));
        Assert.AreEqual(0, tl.ActiveCameraCut(1.5f), "클립 끝(0.55)이 지나도 다음 컷 전까지 유지");
        Assert.AreEqual(1, tl.ActiveCameraCut(9f), "마지막 컷 = 끝까지");
    }

    [Test]
    public void 컷_값은_클립과_같은_번호_모자라면_기본값_넘치면_버림()
    {
        var tl = new BossVictoryTimeline();
        tl.cameraCut.AddRange(Starts(0.3f, 1f));
        var close = C(BossVictoryCameraCuts.Anchor.Boss);
        close.hideWalls = false;
        tl.cameraCutShots.Add(close);
        Assert.AreEqual(BossVictoryCameraCuts.Anchor.Boss, tl.CameraCutShot(0).anchor);
        Assert.IsTrue(tl.CameraCutShot(1).hideWalls, "모자란 번호 = 기본값(벽 숨김 켬)");
        Assert.IsTrue(tl.SyncCameraCutShots());
        Assert.AreEqual(2, tl.cameraCutShots.Count);
        Assert.IsFalse(tl.cameraCutShots[0].hideWalls, "있던 값은 그대로");
        tl.cameraCut.RemoveAt(1);
        Assert.IsTrue(tl.SyncCameraCutShots());
        Assert.AreEqual(1, tl.cameraCutShots.Count);
        Assert.IsFalse(tl.SyncCameraCutShots(), "맞으면 안 바꾼다");
    }

    [Test]
    public void 새_컷_기본값은_벽_숨김_켬()
    {
        BossVictoryCameraCuts.Cut d = BossVictoryCameraCuts.Cut.Default;
        Assert.IsTrue(d.hideWalls);
        Assert.AreEqual(1, d.keys.Count, "기본 = 고정 컷(키 1개)");
        Assert.IsFalse(d.Moves);
        Assert.AreEqual(BossVictoryCameraCuts.Anchor.Middle, d.anchor);
        BossVictoryCameraCuts.CutKey k = d.keys[0];
        Assert.That(k.fov, Is.InRange(10f, 90f));
        // 옛 기본 와이드(거리 16·내려다보는 각 22·방위 50·초점 높이 1.5) — 초점에서 16m, 초점을 바라본다.
        Vector3 focus = Vector3.up * 1.5f;
        Assert.AreEqual(16f, Vector3.Distance(k.position, focus), 1e-3f);
        Assert.Greater(Vector3.Dot(k.rotation * Vector3.forward, (focus - k.position).normalized), 0.999f);
    }

    [Test]
    public void 컷_길이_자동은_다음_컷_시작까지_마지막은_로비_전환까지()
    {
        var tl = new BossVictoryTimeline { lobbyTransitionTime = 5f };
        tl.cameraCut.AddRange(Starts(2f, 0.5f, 4.98f));   // 순서 무관
        tl.FitCameraCutLengths();
        Assert.AreEqual(2.98f, tl.cameraCut[0].length, 1e-5f);
        Assert.AreEqual(1.5f, tl.cameraCut[1].length, 1e-5f);
        Assert.AreEqual(BossVictoryTimeline.MinCutLength, tl.cameraCut[2].length, 1e-5f, "로비 전환 직전 컷 = 최소 길이");
        Assert.AreEqual(1, tl.ActiveCameraCut(1f), "길이 맞춤이 판정을 안 바꾼다");
    }

    // 옛 BossVictoryGlitchSettings.cuts(시작 시각 + 컷 값 배열)는 255 에서 삭제했다. 이관 손실 0 = 에셋에 남은 옛 값이 없어야 한다
    // (삭제 시점 에셋 = 'cuts: []'). 값이 있었다면 지우기 전에 카메라 컷 트랙으로 옮겨야 했다 — 이 테스트가 그걸 막는다.
    [Test]
    public void 옛_cuts_배열은_삭제됐고_에셋에_옮길_값이_남아_있지_않다()
    {
        Assert.IsNull(typeof(BossVictoryGlitchSettings).GetField("cuts"), "옛 cuts 칸은 삭제(카메라 컷 트랙으로 대체)");
        string yaml = File.ReadAllText(AssetPath);
        Match m = Regex.Match(yaml, @"^  cuts:(.*)$", RegexOptions.Multiline);
        if (m.Success)
            Assert.AreEqual("[]", m.Groups[1].Value.Trim(), "에셋에 옛 컷 값이 있다 — 카메라 컷 트랙으로 옮긴 뒤 지울 것");
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(AssetPath);
        Assert.IsNotNull(settings);
        BossVictoryProfile p = settings.ProfileFor(TutorialCharacter.None);   // 타임라인 = 프로필(267)
        Assert.AreEqual(p.timeline.cameraCut.Count, p.timeline.cameraCutShots.Count, "컷 값 개수 = 컷 클립 개수");
    }

    // ── 컷 블렌드(255 후속 8) ──

    static BossVictoryTimeline BlendTimeline(float blend0, float blend1, BossVictoryKeyCurve curve = BossVictoryKeyCurve.Linear)
    {
        var tl = new BossVictoryTimeline { lobbyTransitionTime = 5f };
        tl.cameraCut.AddRange(Starts(1f, 2f));
        BossVictoryCameraCuts.Cut a = BossVictoryCameraCuts.Cut.Default, b = BossVictoryCameraCuts.Cut.Default;
        a.blendSeconds = blend0; a.blendCurve = curve;
        b.blendSeconds = blend1; b.blendCurve = curve;
        tl.cameraCutShots.Add(a);
        tl.cameraCutShots.Add(b);
        return tl;
    }

    [Test]
    public void 블렌드_0_이면_기존과_같다()
    {
        BossVictoryTimeline tl = BlendTimeline(0f, 0f);
        for (float t = 0f; t < 5f; t += 0.05f)
        {
            Assert.AreEqual(-1, tl.BlendingCameraCut(t, out float w), $"t {t}");
            Assert.AreEqual(1f, w);
        }
        Assert.AreEqual(0f, BossVictoryCameraCuts.Cut.Default.blendSeconds, "새 컷 기본 = 툭 끊김");
    }

    [Test]
    public void 블렌드_구간과_곡선()
    {
        BossVictoryTimeline tl = BlendTimeline(0f, 0.4f);
        Assert.AreEqual(-1, tl.BlendingCameraCut(1.59f, out _), "구간 전");
        Assert.AreEqual(1, tl.BlendingCameraCut(1.6f, out float w0));
        Assert.AreEqual(0f, w0, 1e-5f, "시작 = 앞 화면");
        Assert.AreEqual(1, tl.BlendingCameraCut(1.8f, out float wMid));
        Assert.AreEqual(0.5f, wMid, 1e-4f);
        Assert.AreEqual(0, tl.ActiveCameraCut(1.8f), "블렌드 중 지금 컷 = 앞 컷(계속 진행)");
        Assert.AreEqual(-1, tl.BlendingCameraCut(2f, out _), "컷 시작 = 블렌드 끝");
        Assert.AreEqual(1, tl.ActiveCameraCut(2f));

        BossVictoryTimeline easeIn = BlendTimeline(0f, 0.4f, BossVictoryKeyCurve.EaseIn);
        easeIn.BlendingCameraCut(1.8f, out float wIn);
        Assert.AreEqual(0.25f, wIn, 1e-4f);
    }

    [Test]
    public void 블렌드_시간은_앞_컷_길이_이하_첫_컷은_시작까지()
    {
        BossVictoryTimeline tl = BlendTimeline(3f, 5f);
        Assert.AreEqual(-1, tl.PreviousCameraCut(0));
        Assert.AreEqual(0, tl.PreviousCameraCut(1));
        Assert.AreEqual(1f, tl.CameraCutBlendLimit(0), 1e-5f, "첫 컷 = 컷 시작(1초)까지");
        Assert.AreEqual(1f, tl.CameraCutBlendLimit(1), 1e-5f, "앞 컷 길이 1초");
        Assert.AreEqual(1f, tl.CameraCutBlendSeconds(0), 1e-5f);
        Assert.AreEqual(1f, tl.CameraCutBlendSeconds(1), 1e-5f);
        Assert.AreEqual(0, tl.BlendingCameraCut(0f, out _), "첫 컷 블렌드는 처치 순간부터");
        Assert.AreEqual(-1, tl.ActiveCameraCut(0.5f), "첫 컷 블렌드 중 지금 컷 없음 = 게임 카메라에서 출발");
        Assert.AreEqual(0, tl.BlendingCameraCut(0.5f, out _));
    }

    [Test]
    public void 블렌드_보간은_월드_기준()
    {
        Vector3 pa = new Vector3(0f, 2f, -5f), pb = new Vector3(4f, 6f, 3f);
        Quaternion ra = Quaternion.Euler(10f, 0f, 0f), rb = Quaternion.Euler(30f, 90f, 0f);
        BossVictoryCameraCuts.Blend(pa, ra, 30f, pb, rb, 50f, 0f, out Vector3 p0, out Quaternion r0, out float f0);
        Assert.Less(Vector3.Distance(p0, pa), 1e-4f);
        Assert.Less(Quaternion.Angle(r0, ra), 0.01f);
        Assert.AreEqual(30f, f0, 1e-4f);
        BossVictoryCameraCuts.Blend(pa, ra, 30f, pb, rb, 50f, 1f, out Vector3 p1, out Quaternion r1, out float f1);
        Assert.Less(Vector3.Distance(p1, pb), 1e-4f);
        Assert.Less(Quaternion.Angle(r1, rb), 0.01f);
        Assert.AreEqual(50f, f1, 1e-4f);
        BossVictoryCameraCuts.Blend(pa, ra, 30f, pb, rb, 50f, 0.5f, out Vector3 pm, out Quaternion rm, out float fm);
        Assert.Less(Vector3.Distance(pm, (pa + pb) * 0.5f), 1e-4f);
        Assert.AreEqual(Quaternion.Angle(ra, rm), Quaternion.Angle(rm, rb), 0.05f, "회전 = slerp 가운데");
        Assert.AreEqual(40f, fm, 1e-4f);
    }
}
