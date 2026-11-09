using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 4단계 카메라 컷(PLAN-boss-victory-glitch) — 컷 고르기·구도 계산·기본 목록 · 타임라인 '카메라 컷' 트랙(255: 트랙 구조·컷 값 짝·길이 자동·옛 cuts 이관).
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryCameraCut).
/// </summary>
public sealed class BossVictoryCameraCutTests
{
    const string AssetPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";

    static BossVictoryCameraCuts.Cut C(BossVictoryCameraCuts.Kind kind = BossVictoryCameraCuts.Kind.Wide) =>
        new BossVictoryCameraCuts.Cut { kind = kind, distance = 10f, fov = 40f, hideWalls = true };

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
        BossCenter = new Vector3(0f, 3f, 10f),
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

    [Test]
    public void 방위_0_은_처치자_쪽에서_보스를_본다()
    {
        var cut = C();
        BossVictoryCameraCuts.Pose(cut, S(), out Vector3 pos, out Quaternion rot);
        Assert.Less(pos.z, 5f, "카메라는 가운데(z 5)보다 처치자 쪽");
        Assert.Greater(Vector3.Dot(rot * Vector3.forward, Vector3.forward), 0.99f);
    }

    [Test]
    public void 처치자_정면_방위_180_은_처치자를_마주본다()
    {
        var cut = C(BossVictoryCameraCuts.Kind.KillerFront);
        cut.yaw = 180f;
        cut.distance = 3f;
        BossVictoryCameraCuts.Pose(cut, S(), out Vector3 pos, out Quaternion rot);
        Assert.AreEqual(3f, pos.z, 0.01f);
        Assert.Less(Vector3.Dot(rot * Vector3.forward, Vector3.forward), -0.99f);
    }

    [Test]
    public void 보스_클로즈업_초점은_보스_몸_중심()
    {
        var cut = C(BossVictoryCameraCuts.Kind.BossCloseup);
        cut.lookHeight = 0.5f;
        Vector3 focus = BossVictoryCameraCuts.Focus(cut, S());
        Assert.AreEqual(new Vector3(0f, 3.5f, 10f), focus);
        BossVictoryCameraCuts.Pose(cut, S(), out Vector3 pos, out _);
        Assert.AreEqual(cut.distance, Vector3.Distance(pos, focus), 0.01f);
    }

    [Test]
    public void 기본_목록은_비어_있어_게임_카메라만_쓴다()
    {
        var settings = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
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
        var close = C(BossVictoryCameraCuts.Kind.BossCloseup);
        close.hideWalls = false;
        tl.cameraCutShots.Add(close);
        Assert.AreEqual(BossVictoryCameraCuts.Kind.BossCloseup, tl.CameraCutShot(0).kind);
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
        Assert.AreEqual(BossVictoryCameraCuts.Kind.Wide, d.kind);
        Assert.Greater(d.distance, 0.1f);
        Assert.That(d.fov, Is.InRange(10f, 90f));
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
        Assert.AreEqual(settings.timeline.cameraCut.Count, settings.timeline.cameraCutShots.Count, "컷 값 개수 = 컷 클립 개수");
    }
}
