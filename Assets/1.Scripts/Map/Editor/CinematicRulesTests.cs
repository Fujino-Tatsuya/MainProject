using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CinematicRulesTests
{
    // 폭 = 보이는 글자 수(태그 제외) — 런타임 TMP 실측 대신.
    private static float Chars(string s) => CinematicLineSplitter.StripTags(s).Length;

    // ── 나누기 ───────────────────────────────────────────────────────────

    [Test]
    public void Split_ShortLine_IsOneChunk()
    {
        List<string> r = CinematicLineSplitter.Split("어서 움직여.", Chars, 20f);
        Assert.AreEqual(new[] { "어서 움직여." }, r.ToArray());
    }

    [Test]
    public void Split_ManualBreak_AlwaysBreaks()
    {
        List<string> r = CinematicLineSplitter.Split("문이 잠겨 있어.|중앙에 있는 저 녀석이 신경 쓰이는데.", Chars, 100f);
        Assert.AreEqual(new[] { "문이 잠겨 있어.", "중앙에 있는 저 녀석이 신경 쓰이는데." }, r.ToArray());
    }

    [Test]
    public void Split_TooLong_BreaksAtSpaces_EachChunkFits()
    {
        const string line = "장치가 네 군데나 있네. 저걸 전부 켜면… 다리를 연결할 수 있을 것 같아.";
        List<string> r = CinematicLineSplitter.Split(line, Chars, 16f);
        Assert.That(r.Count, Is.GreaterThan(1));
        foreach (string chunk in r) Assert.That(Chars(chunk), Is.LessThanOrEqualTo(16f), chunk);
        Assert.AreEqual(line, string.Join(" ", r));   // 글자를 잃거나 바꾸지 않는다
    }

    [Test]
    public void Split_SingleWordLongerThanMax_StaysWhole()
    {
        List<string> r = CinematicLineSplitter.Split("아주아주긴한어절 다음", Chars, 4f);
        Assert.AreEqual(new[] { "아주아주긴한어절", "다음" }, r.ToArray());
    }

    [Test]
    public void Split_RichTag_AcrossChunks_ReopensAndCloses()
    {
        List<string> r = CinematicLineSplitter.Split("<b>세계선을 엉망으로 만든</b> 원흉", Chars, 9f);
        Assert.AreEqual(new[] { "<b>세계선을 엉망으로</b>", "<b>만든</b> 원흉" }, r.ToArray());
    }

    [Test]
    public void Split_ManualBreakInsideTag_KeepsTagBalanced()
    {
        List<string> r = CinematicLineSplitter.Split("<b>가|나</b>", Chars, 100f);
        Assert.AreEqual(new[] { "<b>가</b>", "<b>나</b>" }, r.ToArray());
    }

    [Test]
    public void Split_EmptyOrBlankParts_Skipped()
    {
        Assert.IsEmpty(CinematicLineSplitter.Split("  ", Chars, 10f));
        Assert.AreEqual(new[] { "가", "나" }, CinematicLineSplitter.Split("가|| |나", Chars, 10f).ToArray());
    }

    // ── 조각 스케줄 ───────────────────────────────────────────────────────

    [Test]
    public void Schedule_ShowGapShow_ThenEnd()
    {
        // 조각 2개 · 표시 2초 · 간격 0.5초
        Assert.AreEqual(-1, CinematicLineSchedule.ChunkAt(-0.1f, 2, 2f, 0.5f));
        Assert.AreEqual(0, CinematicLineSchedule.ChunkAt(0f, 2, 2f, 0.5f));
        Assert.AreEqual(0, CinematicLineSchedule.ChunkAt(1.99f, 2, 2f, 0.5f));
        Assert.AreEqual(-1, CinematicLineSchedule.ChunkAt(2.2f, 2, 2f, 0.5f));   // 간격
        Assert.AreEqual(1, CinematicLineSchedule.ChunkAt(2.5f, 2, 2f, 0.5f));
        Assert.AreEqual(-1, CinematicLineSchedule.ChunkAt(4.6f, 2, 2f, 0.5f));   // 끝
        Assert.AreEqual(4.5f, CinematicLineSchedule.TotalSeconds(2, 2f, 0.5f), 1e-4f);
    }

    [Test]
    public void Schedule_NoGap_Contiguous()
    {
        Assert.AreEqual(1, CinematicLineSchedule.ChunkAt(2f, 3, 2f, 0f));
        Assert.AreEqual(6f, CinematicLineSchedule.TotalSeconds(3, 2f, 0f), 1e-4f);
    }

    [Test]
    public void ScheduleHeld_LastChunkStaysUntilHold()
    {
        // 조각 2개 · 표시 2초 · 간격 0.5초 → 원래 끝 4.5초. 마지막 조각을 9초까지 붙잡는다.
        Assert.IsTrue(CinematicLineSchedule.Sample(1f, 2, 2f, 0.5f, 9f, out int i, out _, out _));
        Assert.AreEqual(0, i);
        Assert.IsFalse(CinematicLineSchedule.Sample(2.2f, 2, 2f, 0.5f, 9f, out _, out _, out _));   // 조각 사이 간격은 그대로
        Assert.IsTrue(CinematicLineSchedule.Sample(6f, 2, 2f, 0.5f, 9f, out i, out float within, out float duration));
        Assert.AreEqual(1, i);
        Assert.AreEqual(3.5f, within, 1e-4f);
        Assert.AreEqual(6.5f, duration, 1e-4f);   // 2.5 ~ 9 — 페이드아웃은 9초 직전
        Assert.IsFalse(CinematicLineSchedule.Sample(9f, 2, 2f, 0.5f, 9f, out _, out _, out _));
    }

    [Test]
    public void ScheduleHeld_EarlierOrNoHold_IsPlainSchedule()
    {
        Assert.IsFalse(CinematicLineSchedule.Sample(4.6f, 2, 2f, 0.5f, -1f, out _, out _, out _));
        Assert.IsFalse(CinematicLineSchedule.Sample(4.6f, 2, 2f, 0.5f, 3f, out _, out _, out _));   // 원래 끝보다 이른 붙잡기 = 무시
        Assert.IsTrue(CinematicLineSchedule.Sample(3f, 2, 2f, 0.5f, -1f, out int i, out _, out float d));
        Assert.AreEqual((1, 2f), (i, d));
    }

    // ── 샷 타임라인 ──────────────────────────────────────────────────────

    private static readonly CinematicShotTiming[] Shots =
    {
        new CinematicShotTiming(1f, 2f),   // 0: 0~1 이동 · 1~3 머묾
        new CinematicShotTiming(1f, 2f),   // 1: 3~4 이동 · 4~6 머묾
        new CinematicShotTiming(0f, 1f),   // 2: 컷 · 6~7 머묾
    };

    [Test]
    public void Timeline_TotalAndArrivals()
    {
        Assert.AreEqual(7f, CinematicTimeline.TotalSeconds(Shots), 1e-4f);
        Assert.AreEqual(1f, CinematicTimeline.ArrivalTime(Shots, 0), 1e-4f);
        Assert.AreEqual(4f, CinematicTimeline.ArrivalTime(Shots, 1), 1e-4f);
        Assert.AreEqual(6f, CinematicTimeline.ArrivalTime(Shots, 2), 1e-4f);
    }

    [Test]
    public void Timeline_Sample_MoveHoldAndAfterEnd()
    {
        CinematicTimeline.Sample(Shots, 0.5f, out int f, out int t, out float b);
        Assert.AreEqual((-1, 0), (f, t));   // 게임 카메라 → 첫 샷
        Assert.AreEqual(0.5f, b, 1e-4f);    // smoothstep(0.5) = 0.5

        CinematicTimeline.Sample(Shots, 2f, out f, out t, out b);
        Assert.AreEqual((0, 0, 1f), (f, t, b));

        CinematicTimeline.Sample(Shots, 3.25f, out f, out t, out b);
        Assert.AreEqual((0, 1), (f, t));
        Assert.That(b, Is.GreaterThan(0f).And.LessThan(0.5f));

        CinematicTimeline.Sample(Shots, 6f, out f, out t, out b);   // 컷 = 이동 0 → 바로 머묾
        Assert.AreEqual((2, 2, 1f), (f, t, b));

        CinematicTimeline.Sample(Shots, 99f, out f, out t, out b);   // 끝난 뒤 마지막 샷
        Assert.AreEqual((2, 2, 1f), (f, t, b));
    }

    // ── 캡션 등장 움직임 ───────────────────────────────────────────────────

    [Test]
    public void CaptionRise_StartsBelow_EndsAtZero()
    {
        Assert.AreEqual(-14f, CinematicCaptionMotion.RiseOffset(0f, 14f, 0.25f), 1e-4f);
        float mid = CinematicCaptionMotion.RiseOffset(0.125f, 14f, 0.25f);
        Assert.That(mid, Is.GreaterThan(-7f).And.LessThan(0f));   // 감속 — 절반 시간에 절반보다 많이 올라왔다
        Assert.AreEqual(0f, CinematicCaptionMotion.RiseOffset(0.25f, 14f, 0.25f), 1e-4f);
        Assert.AreEqual(0f, CinematicCaptionMotion.RiseOffset(0.1f, 0f, 0.25f), 1e-4f);   // 거리 0 = 끔
    }

    [Test]
    public void BadgePulse_OnceUpAndBack()
    {
        Assert.AreEqual(1f, CinematicCaptionMotion.PulseScale(0f, 0.18f, 0.35f), 1e-4f);
        Assert.AreEqual(1.18f, CinematicCaptionMotion.PulseScale(0.175f, 0.18f, 0.35f), 1e-4f);   // 가운데에서 최대
        Assert.AreEqual(1f, CinematicCaptionMotion.PulseScale(0.35f, 0.18f, 0.35f), 1e-4f);
        Assert.AreEqual(1f, CinematicCaptionMotion.PulseScale(1f, 0.18f, 0.35f), 1e-4f);
        Assert.AreEqual(1f, CinematicCaptionMotion.PulseScale(0.1f, 0f, 0.35f), 1e-4f);   // 0 = 끔
    }

    // ── 구도 ─────────────────────────────────────────────────────────────

    [Test]
    public void Framing_Pose_LooksAtFocusFromScreenSouth()
    {
        CinematicFraming.Pose(Vector3.zero, Vector3.back, 45f, 10f, out Vector3 pos, out Quaternion rot);
        Assert.That(pos.z, Is.LessThan(0f));       // 화면 아래(−Z)에서
        Assert.That(pos.y, Is.GreaterThan(0f));    // 위에서 내려다본다
        Assert.AreEqual(10f, pos.magnitude, 1e-3f);
        Assert.That(Vector3.Dot(rot * Vector3.forward, -pos.normalized), Is.GreaterThan(0.999f));
    }

    [Test]
    public void Framing_FitDistance_UsesNarrowerHalfAngle()
    {
        // 세로 60° · 16:9 → 세로 반각 30° 가 좁다 → 10 / sin30 = 20.
        Assert.AreEqual(20f, CinematicFraming.FitDistance(10f, 60f, 16f / 9f, 1f), 1e-3f);
        // 세로로 긴 화면(9:16)은 가로가 좁다 → 더 멀어진다.
        Assert.That(CinematicFraming.FitDistance(10f, 60f, 9f / 16f, 1f), Is.GreaterThan(20f));
        Assert.AreEqual(22f, CinematicFraming.FitDistance(10f, 60f, 16f / 9f, 1.1f), 1e-3f);
    }

    [Test]
    public void FirstCovered_AfterVisibleOnly()
    {
        const float top = 2.5f, tol = 0.05f, nan = float.NaN;
        // 섬 쪽 끝이 고정 다리(3.0) 밑 → 보이는 다리(2.5) → 덮은 바닥(2.9) — 가장자리 = 덮은 바닥 첫 칸(4).
        Assert.AreEqual(4, CinematicFraming.FirstCoveredAfterVisible(new[] { 3.0f, 3.0f, 2.5f, 2.5f, 2.9f, 2.9f }, top, tol));
        // 못 맞은 칸(NaN)은 건너뛴다.
        Assert.AreEqual(3, CinematicFraming.FirstCoveredAfterVisible(new[] { 2.5f, nan, 2.5f, 3.2f }, top, tol));
        // 끝까지 안 덮이면 -1, 처음부터 끝까지 덮였으면 -1.
        Assert.AreEqual(-1, CinematicFraming.FirstCoveredAfterVisible(new[] { 2.5f, 2.52f, 2.5f }, top, tol));
        Assert.AreEqual(-1, CinematicFraming.FirstCoveredAfterVisible(new[] { 3f, 3f }, top, tol));
        // 다리보다 한참 낮은 곳(구멍 바닥)에 맞으면 보인 걸로 치지 않는다.
        Assert.AreEqual(-1, CinematicFraming.FirstCoveredAfterVisible(new[] { -5f, 3f }, top, tol));
    }

    [Test]
    public void Framing_ClockwiseFromBottomLeft()
    {
        // 화면 아래 = −Z → 화면 오른쪽 = +X, 위 = +Z. 입력 순서를 섞어 둔다.
        var pts = new[]
        {
            new Vector3(10f, 0f, 10f),    // 0 오른쪽 위
            new Vector3(-10f, 0f, -10f),  // 1 왼쪽 아래
            new Vector3(10f, 0f, -10f),   // 2 오른쪽 아래
            new Vector3(-10f, 0f, 10f),   // 3 왼쪽 위
        };
        Assert.AreEqual(new[] { 1, 3, 0, 2 }, CinematicFraming.ClockwiseFromBottomLeft(pts, Vector3.back).ToArray());
    }
}
