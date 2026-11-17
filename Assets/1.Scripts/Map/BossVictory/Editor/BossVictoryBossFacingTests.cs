using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 297 — 어쌔신 처치 연출이 매번 같게: 카메라 컷 기준 '보스 정면'(<see cref="BossVictoryCameraCuts.Anchor.BossFacing"/>) 좌표계 ·
/// 타임라인상 예상 처치자 위치(<see cref="BossVictoryAssassinDash.ExpectedLocal"/>) · 컷 기준 이관(<see cref="BossVictoryCameraCuts.Rebase"/>,
/// <see cref="BossVictoryAssassinDash.RebaseCutsToBossFacing"/>) · 순간이동 Q 클립 시작 프레임 보정(<see cref="BossVictoryKillerMoves.SegmentStartClipTime"/>).
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictory).
/// </summary>
public sealed class BossVictoryBossFacingTests
{
    const float Tol = 1e-3f;
    const BossVictoryCameraCuts.Anchor Facing = BossVictoryCameraCuts.Anchor.BossFacing;

    static void Near(Vector3 expected, Vector3 actual, string message) =>
        Assert.Less(Vector3.Distance(expected, actual), Tol, $"{message} — 기대 {expected} · 실제 {actual}");

    static void Near(Quaternion expected, Quaternion actual, string message) =>
        Assert.Greater(Mathf.Abs(Quaternion.Dot(expected, actual)), 0.99999f, $"{message} — 기대 {expected.eulerAngles} · 실제 {actual.eulerAngles}");

    static BossVictoryClip Dash(float start, float length, Vector3 from, Vector3 to)
    {
        BossVictoryClip c = BossVictoryClip.Make(start, length);
        c.dashFrom = from;
        c.dashTo = to;
        return c;
    }

    static BossVictoryCameraCuts.Cut KeyCut(BossVictoryCameraCuts.Anchor anchor, params BossVictoryCameraCuts.CutKey[] keys) =>
        new BossVictoryCameraCuts.Cut { anchor = anchor, keys = new List<BossVictoryCameraCuts.CutKey>(keys), hideWalls = true };

    // ── 기준 '보스 정면' 좌표계 ──

    [Test]
    public void 보스_정면_기준은_보스_발밑_원점_보스_수평_정면이_Z_처치자_위치와_무관()
    {
        var s = new BossVictoryCameraCuts.Subjects { Killer = new Vector3(3f, 0f, -8f), BossBase = new Vector3(10f, 1f, 5f), BossForward = new Vector3(1f, 0.5f, 0f) };
        BossVictoryCameraCuts.ToWorld(Facing, s, new Vector3(0f, 2f, 4f), Quaternion.identity, out Vector3 p, out Quaternion r);
        Near(new Vector3(14f, 3f, 5f), p, "보스 정면(+X) 4m · 위 2m");
        Near(Quaternion.LookRotation(Vector3.right), r, "회전 = 보스 수평 정면을 본다(기울기 무시)");

        s.Killer = new Vector3(-30f, 0f, 40f);   // 처치자가 어디 있든
        BossVictoryCameraCuts.ToWorld(Facing, s, new Vector3(0f, 2f, 4f), Quaternion.identity, out Vector3 p2, out _);
        Near(p, p2, "처치자 위치가 바뀌어도 같은 화면");
    }

    [Test]
    public void 보스_정면이_없으면_월드_Z()
    {
        var s = new BossVictoryCameraCuts.Subjects { BossBase = Vector3.zero };
        Assert.AreEqual(Vector3.forward, BossVictoryCameraCuts.FrameForward(Facing, s));
        Assert.AreEqual(Vector3.zero, BossVictoryCameraCuts.AnchorPoint(Facing, s));
    }

    [Test]
    public void 보스_정면_기준은_순간이동_두_점과_같은_좌표계()
    {
        // 순간이동 두 점 = BossPoint(보스 위치, 보스 회전, 오프셋) — 같은 오프셋을 '보스 정면' 기준 위치로 쓰면 같은 세계 점(높이 0).
        Vector3 boss = new Vector3(-6f, 0.4f, 12f);
        Quaternion rot = Quaternion.Euler(5f, 137f, 0f);
        var s = new BossVictoryCameraCuts.Subjects { BossBase = boss, BossForward = rot * Vector3.forward };
        Vector3 offset = new Vector3(-7.6f, 0f, -0.5f);
        BossVictoryCameraCuts.ToWorld(Facing, s, offset, Quaternion.identity, out Vector3 p, out _);
        Near(BossVictoryKillerMoves.BossPoint(boss, rot, offset), p, "같은 점");
    }

    // ── 타임라인상 예상 처치자 위치 ──

    static List<BossVictoryClip> TwoDashes() => new List<BossVictoryClip>
    {
        Dash(1.0f, 0.2f, new Vector3(-3f, 0f, 2f), new Vector3(3f, 0f, 2f)),   // 목록 순서 = 실행 순서와 다르게
        Dash(0.2f, 0.4f, new Vector3(8f, 0f, -6f), new Vector3(-4f, 0f, 2f)),
    };

    [Test]
    public void 예상_위치_첫_클립_전이면_없음()
    {
        Assert.IsFalse(BossVictoryAssassinDash.ExpectedLocal(TwoDashes(), 0.1f, null, out _));
        Assert.IsFalse(BossVictoryAssassinDash.ExpectedLocal(TwoDashes(), 0.2f, null, out _), "첫 클립과 같은 시각 = 아직 안 들어감");
        Assert.IsFalse(BossVictoryAssassinDash.ExpectedLocal(null, 1f, null, out _));
    }

    [Test]
    public void 예상_위치_클립_도중은_진행만큼_끝난_뒤는_도착점()
    {
        Assert.IsTrue(BossVictoryAssassinDash.ExpectedLocal(TwoDashes(), 0.3f, null, out Vector3 mid));
        Near(new Vector3(5f, 0f, -4f), mid, "0.2→0.6 의 1/4");
        Assert.IsTrue(BossVictoryAssassinDash.ExpectedLocal(TwoDashes(), 0.8f, null, out Vector3 after));
        Near(new Vector3(-4f, 0f, 2f), after, "끝난 뒤 빈 시간 = 도착점");
    }

    [Test]
    public void 예상_위치_같은_시각에_시작하는_클립은_아직_안_들어감()
    {
        Assert.IsTrue(BossVictoryAssassinDash.ExpectedLocal(TwoDashes(), 1.0f, null, out Vector3 p));
        Near(new Vector3(-4f, 0f, 2f), p, "1.0 시작 클립 전 = 앞 클립 도착점(런타임 순서)");
        Assert.IsTrue(BossVictoryAssassinDash.ExpectedLocal(TwoDashes(), 1.1f, null, out Vector3 q));
        Near(new Vector3(0f, 0f, 2f), q, "들어간 뒤 = 그 클립 진행");
    }

    [Test]
    public void 예상_위치_높이는_0()
    {
        var clips = new List<BossVictoryClip> { Dash(0f, 1f, new Vector3(0f, 3f, 0f), new Vector3(2f, 3f, 0f)) };
        Assert.IsTrue(BossVictoryAssassinDash.ExpectedLocal(clips, 0.5f, null, out Vector3 p));
        Assert.AreEqual(0f, p.y);
    }

    // ── 컷 기준 이관 ──

    [Test]
    public void 이관하면_같은_기준_위치에서_화면이_그대로()
    {
        var cut = KeyCut(BossVictoryCameraCuts.Anchor.Middle,
            BossVictoryCameraCuts.CutKey.Make(0f, new Vector3(3.7f, 16.6f, 6.6f), Quaternion.Euler(50f, 120f, 10f), 42f),
            BossVictoryCameraCuts.CutKey.Make(1f, new Vector3(-9.9f, 0.03f, -4f), Quaternion.Euler(-10f, 250f, 0f), 49f));
        var s = BossVictoryAssassinDash.LocalSubjects(new Vector3(5.9f, 0f, -4.5f));
        BossVictoryCameraCuts.Cut moved = BossVictoryCameraCuts.Rebase(cut, Facing, s);
        Assert.AreEqual(Facing, moved.anchor);
        Assert.AreEqual(BossVictoryCameraCuts.Anchor.Middle, cut.anchor, "원본 그대로");
        Assert.AreNotSame(cut.keys, moved.keys, "새 키 목록");
        foreach (float u in new[] { 0f, 0.3f, 1f })
        {
            BossVictoryCameraCuts.Pose(cut, s, u, out Vector3 p0, out Quaternion r0, out float f0);
            BossVictoryCameraCuts.Pose(moved, s, u, out Vector3 p1, out Quaternion r1, out float f1);
            Near(p0, p1, $"u {u} 위치");
            Near(r0, r1, $"u {u} 회전");
            Assert.AreEqual(f0, f1, 1e-4f, $"u {u} FOV");
        }
    }

    [Test]
    public void 이관한_컷은_보스가_어디서_어느_쪽을_보든_예상_위치_기준_화면과_같다()
    {
        var cut = KeyCut(BossVictoryCameraCuts.Anchor.Middle,
            BossVictoryCameraCuts.CutKey.Make(0f, new Vector3(-9.9f, 0.03f, -4f), new Quaternion(0.0895f, -0.546f, -0.0589f, -0.8308f), 49f));
        Vector3 killerLocal = new Vector3(4.5f, 0f, 3.8f);
        BossVictoryCameraCuts.Cut moved = BossVictoryCameraCuts.Rebase(cut, Facing, BossVictoryAssassinDash.LocalSubjects(killerLocal));

        Vector3 boss = new Vector3(20f, 1f, -7f);
        Quaternion bossRot = Quaternion.Euler(0f, -63f, 0f);
        // 옛 길 = 처치자가 예상 위치(보스 기준 점)에 정확히 있을 때의 화면 · 새 길 = 처치자 위치와 무관하게 보스만으로.
        var exact = new BossVictoryCameraCuts.Subjects
        {
            Killer = BossVictoryKillerMoves.BossPoint(boss, bossRot, killerLocal),
            BossBase = boss,
            BossForward = bossRot * Vector3.forward,
        };
        exact.Killer.y = boss.y;
        var wrongKiller = exact;
        wrongKiller.Killer = new Vector3(-40f, 0f, 3f);   // 순간이동 전·복제 지연 등으로 처치자가 엉뚱한 곳
        BossVictoryCameraCuts.Pose(cut, exact, 0f, out Vector3 p0, out Quaternion r0, out _);
        BossVictoryCameraCuts.Pose(moved, wrongKiller, 0f, out Vector3 p1, out Quaternion r1, out _);
        Near(p0, p1, "위치");
        Near(r0, r1, "회전");
    }

    [Test]
    public void 옛_2점_컷도_키로_옮겨_이관()
    {
        var cut = new BossVictoryCameraCuts.Cut
        {
            anchor = BossVictoryCameraCuts.Anchor.Killer, startPosition = new Vector3(0f, 2f, -5f), startRotation = Quaternion.identity, startFov = 40f,
        };
        var s = BossVictoryAssassinDash.LocalSubjects(new Vector3(0f, 0f, -6f));
        BossVictoryCameraCuts.Cut moved = BossVictoryCameraCuts.Rebase(cut, Facing, s);
        Assert.IsTrue(moved.HasKeys);
        BossVictoryCameraCuts.Pose(cut, s, 0f, out Vector3 p0, out _, out _);
        BossVictoryCameraCuts.Pose(moved, s, 0f, out Vector3 p1, out _, out _);
        Near(p0, p1, "위치");
    }

    [Test]
    public void 타임라인_이관은_보스_정면_무기_첫_클립_전_컷을_건너뛰고_다시_해도_그대로()
    {
        var tl = new BossVictoryTimeline();
        tl.assassinDash.AddRange(TwoDashes());
        var key = BossVictoryCameraCuts.CutKey.Make(0f, new Vector3(1f, 5f, -6f), Quaternion.Euler(30f, 10f, 0f), 40f);
        tl.cameraCut.Add(BossVictoryClip.Make(0.1f, 0.2f));   // 첫 클립 전
        tl.cameraCut.Add(BossVictoryClip.Make(0.3f, 0.2f));   // 바뀜
        tl.cameraCut.Add(BossVictoryClip.Make(0.5f, 0.2f));   // 무기
        tl.cameraCut.Add(BossVictoryClip.Make(0.7f, 0.2f));   // 이미 보스 정면
        tl.cameraCutShots.Add(KeyCut(BossVictoryCameraCuts.Anchor.Middle, key));
        tl.cameraCutShots.Add(KeyCut(BossVictoryCameraCuts.Anchor.Middle, key));
        tl.cameraCutShots.Add(KeyCut(BossVictoryCameraCuts.Anchor.KillerWeapon, key));
        tl.cameraCutShots.Add(KeyCut(Facing, key));

        var skipped = new List<string>();
        Assert.AreEqual(1, BossVictoryAssassinDash.RebaseCutsToBossFacing(tl, null, skipped));
        Assert.AreEqual(2, skipped.Count, string.Join(" / ", skipped));
        Assert.AreEqual(BossVictoryCameraCuts.Anchor.Middle, tl.cameraCutShots[0].anchor);
        Assert.AreEqual(Facing, tl.cameraCutShots[1].anchor);
        Assert.AreEqual(BossVictoryCameraCuts.Anchor.KillerWeapon, tl.cameraCutShots[2].anchor);
        Near(key.position, tl.cameraCutShots[3].keys[0].position, "이미 보스 정면 = 그대로");

        // 바뀐 컷 = 그 컷 시작(0.3)의 예상 위치 기준으로 같은 화면.
        Assert.IsTrue(BossVictoryAssassinDash.ExpectedLocal(tl.assassinDash, 0.3f, null, out Vector3 killer));
        var s = BossVictoryAssassinDash.LocalSubjects(killer);
        BossVictoryCameraCuts.Pose(KeyCut(BossVictoryCameraCuts.Anchor.Middle, key), s, 0f, out Vector3 p0, out _, out _);
        BossVictoryCameraCuts.Pose(tl.cameraCutShots[1], s, 0f, out Vector3 p1, out _, out _);
        Near(p0, p1, "0.3 예상 위치 기준 같은 화면");

        Vector3 once = tl.cameraCutShots[1].keys[0].position;
        Assert.AreEqual(0, BossVictoryAssassinDash.RebaseCutsToBossFacing(tl, null));
        Near(once, tl.cameraCutShots[1].keys[0].position, "다시 해도 그대로");
    }

    // ── 순간이동 Q 클립 시작 프레임 보정 ──

    [Test]
    public void 클립_시작_프레임은_늦게_들어간_만큼_앞으로()
    {
        const float custom0 = 0.15f, span = 0.275f;
        Assert.AreEqual(custom0, BossVictoryKillerMoves.SegmentStartClipTime(custom0, span, 1f, 0.2f, 1f), 1e-6f, "제때");
        Assert.AreEqual(custom0 + span * 0.25f, BossVictoryKillerMoves.SegmentStartClipTime(custom0, span, 1f, 0.2f, 1.05f), 1e-5f, "1/4 늦음");
        Assert.AreEqual(custom0 + span, BossVictoryKillerMoves.SegmentStartClipTime(custom0, span, 1f, 0.2f, 2f), 1e-5f, "끝 넘으면 End 자세");
        Assert.AreEqual(custom0, BossVictoryKillerMoves.SegmentStartClipTime(custom0, span, 1f, 0.2f, 0.9f), 1e-6f, "앞이면 Custom0");
        Assert.AreEqual(custom0, BossVictoryKillerMoves.SegmentStartClipTime(custom0, span, 1f, 0f, 1.5f), 1e-6f, "길이 0 = Custom0");
    }
}
