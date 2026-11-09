using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 4단계 카메라 컷(PLAN-boss-victory-glitch) — 컷 고르기·구도 계산·기본 목록.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryCameraCut).
/// </summary>
public sealed class BossVictoryCameraCutTests
{
    static BossVictoryCameraCuts.Cut C(float start, BossVictoryCameraCuts.Kind kind = BossVictoryCameraCuts.Kind.Wide) =>
        new BossVictoryCameraCuts.Cut { startTime = start, kind = kind, distance = 10f, fov = 40f };

    static BossVictoryCameraCuts.Subjects S() => new BossVictoryCameraCuts.Subjects
    {
        Killer = Vector3.zero,
        BossBase = new Vector3(0f, 0f, 10f),
        BossCenter = new Vector3(0f, 3f, 10f),
    };

    [Test]
    public void 첫_컷_전은_게임_카메라()
    {
        var cuts = new[] { C(0.33f), C(1.67f) };
        Assert.AreEqual(-1, BossVictoryCameraCuts.ActiveIndex(cuts, 0.2f));
        Assert.AreEqual(-1, BossVictoryCameraCuts.ActiveIndex(cuts, -1f));
        Assert.AreEqual(-1, BossVictoryCameraCuts.ActiveIndex(null, 5f));
    }

    [Test]
    public void 시작이_지난_컷_중_가장_늦은_것_순서_무관()
    {
        var cuts = new[] { C(2.6f), C(0.33f), C(1.67f) };
        Assert.AreEqual(1, BossVictoryCameraCuts.ActiveIndex(cuts, 0.33f));
        Assert.AreEqual(2, BossVictoryCameraCuts.ActiveIndex(cuts, 2f));
        Assert.AreEqual(0, BossVictoryCameraCuts.ActiveIndex(cuts, 9f));
    }

    [Test]
    public void 방위_0_은_처치자_쪽에서_보스를_본다()
    {
        var cut = C(0f);
        BossVictoryCameraCuts.Pose(cut, S(), out Vector3 pos, out Quaternion rot);
        Assert.Less(pos.z, 5f, "카메라는 가운데(z 5)보다 처치자 쪽");
        Assert.Greater(Vector3.Dot(rot * Vector3.forward, Vector3.forward), 0.99f);
    }

    [Test]
    public void 처치자_정면_방위_180_은_처치자를_마주본다()
    {
        var cut = C(0f, BossVictoryCameraCuts.Kind.KillerFront);
        cut.yaw = 180f;
        cut.distance = 3f;
        BossVictoryCameraCuts.Pose(cut, S(), out Vector3 pos, out Quaternion rot);
        Assert.AreEqual(3f, pos.z, 0.01f);
        Assert.Less(Vector3.Dot(rot * Vector3.forward, Vector3.forward), -0.99f);
    }

    [Test]
    public void 보스_클로즈업_초점은_보스_몸_중심()
    {
        var cut = C(0f, BossVictoryCameraCuts.Kind.BossCloseup);
        cut.lookHeight = 0.5f;
        Vector3 focus = BossVictoryCameraCuts.Focus(cut, S());
        Assert.AreEqual(new Vector3(0f, 3.5f, 10f), focus);
        BossVictoryCameraCuts.Pose(cut, S(), out Vector3 pos, out _);
        Assert.AreEqual(cut.distance, Vector3.Distance(pos, focus), 0.01f);
    }

    [Test]
    public void 기본_목록은_와이드_보스클로즈업_와이드()
    {
        var settings = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Assert.AreEqual(3, settings.cuts.Length);
            Assert.AreEqual(BossVictoryCameraCuts.Kind.Wide, settings.cuts[0].kind);
            Assert.AreEqual(BossVictoryCameraCuts.Kind.BossCloseup, settings.cuts[1].kind);
            Assert.AreEqual(BossVictoryCameraCuts.Kind.Wide, settings.cuts[2].kind);
            Assert.Less(settings.cuts[0].startTime, settings.cuts[1].startTime);
            Assert.Less(settings.cuts[1].startTime, settings.cuts[2].startTime);
        }
        finally { Object.DestroyImmediate(settings); }
    }
}
