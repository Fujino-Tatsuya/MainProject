using EuniTween;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 포드 강하 순수 시간표(<see cref="PodDropLobbyTimeline"/>·<see cref="PodDropMapTimeline"/>, 할 일 100 C1) — 단계 경계·합계 길이·0 길이 단계·보간 끝값·해제 ≥ 레터박스 아웃.
/// 기본 수치(로비): 레터박스 인 0.5(같이 순간이동 암전 0.4, 한가운데 0.2 에 순간이동), 간격 0.4, 문턱 → 좌석 미끄러짐 0.4, 해치 0, 강하 2·깊이 10, 페이드 0.6, 줌 1.8 → 1.15 / 6초.
/// 보행 시간은 슬롯마다 받는다(C2d — 경로 길이 ÷ 걷는 속도). 여기선 대부분 3인 각 3초 → 합계 7.3초.
/// 기본 수치(맵): 대기 0.3, 낙하 0.6·높이 12, 여운 0.4, 디졸브 1, 레터박스 아웃 0.5, 줌 1.8 → 1 / 2.6초. 합계 2.8초.
/// </summary>
public sealed class PodDropTimelineTests
{
    const float Eps = 1e-4f;
    const int Walkers = 3;
    const float Walk = 3f;
    const float Slide = 0.4f;

    static float[] Same(int walkers, float seconds)
    {
        var result = new float[walkers];
        for (int i = 0; i < walkers; i++) result[i] = seconds;
        return result;
    }

    static PodDropLobbyTimeline Lobby(int walkers = Walkers) => new PodDropLobbyTimeline(PodDropLobbyTimings.Default, Same(walkers, Walk));
    static PodDropLobbyTimeline Lobby(PodDropLobbyTimings s) => new PodDropLobbyTimeline(s, Same(Walkers, Walk));
    static PodDropMapTimeline Map() => new PodDropMapTimeline(PodDropMapTimings.Default);

    // ─── 로비: 경계 · 합계 ───────────────────────────────────────────────

    [Test]
    public void Lobby_Boundaries_Default()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0.5f, t.WalkAt, Eps, "레터박스 인 끝 = 첫 사람 출발");
        Assert.AreEqual(0.5f + 0.8f + Walk + Slide, t.HatchCloseAt, Eps, "마지막(P3) 출발 0.5+0.4×2, 문턱 +3, 좌석 +0.4");
        Assert.AreEqual(t.HatchCloseAt, t.DescendAt, Eps, "해치 기본 0 → 강하가 바로");
        Assert.AreEqual(t.DescendAt + 2f, t.FadeOutAt, Eps);
        Assert.AreEqual(7.3f, t.LoadAt, Eps, "로비 합계 7.3초(3인 각 3초)");
    }

    [Test]
    public void Lobby_DefaultTotal_ShortWithWalk()
    {
        // D4 '전체를 짧게' — 보행 포함 로비 8~10초 이내.
        Assert.LessOrEqual(Lobby().LoadAt, 8f);
        Assert.Greater(Lobby().LoadAt, Lobby().WalkSecondsOf(0), "보행이 시간표 안에 들어 있다");
    }

    [Test]
    public void Lobby_PhaseAt_BoundaryBelongsToNextPhase()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(PodDropLobbyPhase.LetterboxIn, t.PhaseAt(-1d), "시작 전 = 첫 단계");
        Assert.AreEqual(PodDropLobbyPhase.LetterboxIn, t.PhaseAt(0d));
        Assert.AreEqual(PodDropLobbyPhase.LetterboxIn, t.PhaseAt(t.WalkAt - 0.01d));
        Assert.AreEqual(PodDropLobbyPhase.Walk, t.PhaseAt(t.WalkAt));
        Assert.AreEqual(PodDropLobbyPhase.Walk, t.PhaseAt(t.HatchCloseAt - 0.01d));
        Assert.AreEqual(PodDropLobbyPhase.Descend, t.PhaseAt(t.DescendAt));
        Assert.AreEqual(PodDropLobbyPhase.FadeOut, t.PhaseAt(t.FadeOutAt));
        Assert.AreEqual(PodDropLobbyPhase.Done, t.PhaseAt(t.LoadAt));
        Assert.AreEqual(PodDropLobbyPhase.Done, t.PhaseAt(t.LoadAt + 100d), "끝이 없다");
    }

    [Test]
    public void Lobby_StartOfAndDurations_SumToTotal()
    {
        PodDropLobbyTimeline t = Lobby();
        float sum = 0f;
        for (var p = PodDropLobbyPhase.LetterboxIn; p < PodDropLobbyPhase.Done; p++)
        {
            Assert.AreEqual(sum, t.StartOf(p), Eps, $"{p} 시작 = 앞 단계 길이 합");
            sum += t.DurationOf(p);
        }
        Assert.AreEqual(t.LoadAt, sum, Eps);
        Assert.AreEqual(t.LoadAt, t.StartOf(PodDropLobbyPhase.Done), Eps);
        Assert.AreEqual(0f, t.DurationOf(PodDropLobbyPhase.Done), Eps);
    }

    // ─── 로비: 순간이동 암전 (C2b) ──────────────────────────────────────

    [Test]
    public void Lobby_Blackout_Default_InsideLetterboxIn()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0.4f, PodDropLobbyTimings.Default.teleportBlackoutSeconds, Eps, "기본 = 짧은 암전 0.4초");
        Assert.AreEqual(0.2f, t.TeleportAt, Eps, "순간이동 = 암전 한가운데");
        Assert.AreEqual(0.5f, t.WalkAt, Eps, "암전(0.4)이 레터박스 인(0.5) 안 → 보행 시작 그대로");
        Assert.AreEqual(7.3f, t.LoadAt, Eps, "합계 그대로");
        Assert.Less(t.TeleportAt, t.WalkStartAt(0), "순간이동이 첫 출발보다 먼저");
    }

    [Test]
    public void Lobby_BlackoutAlpha_InHoldOut()
    {
        PodDropLobbyTimeline t = Lobby();
        float third = 0.4f / 3f;
        Assert.AreEqual(0f, t.BlackoutAlphaAt(-0.1d), Eps, "시작 전 없음");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(0d), Eps);
        Assert.AreEqual(0.5f, t.BlackoutAlphaAt(third * 0.5f), Eps, "검게 — 선형");
        Assert.AreEqual(1f, t.BlackoutAlphaAt(third), Eps, "다 검어짐");
        Assert.AreEqual(1f, t.BlackoutAlphaAt(t.TeleportAt), Eps, "순간이동 순간 = 완전 암전");
        Assert.AreEqual(1f, t.BlackoutAlphaAt(third * 2f), Eps, "유지 끝");
        Assert.AreEqual(0.5f, t.BlackoutAlphaAt(third * 2.5f), Eps, "밝게 — 선형");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(0.4d), Eps, "다 밝아짐");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(t.WalkAt), Eps, "걸을 땐 밝다");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(t.FadeOutAt + 0.3d), Eps, "끝 페이드와 무관");
    }

    [Test]
    public void Lobby_ScreenFadeAlpha_MaxOfBlackoutAndFadeOut()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(1f, t.ScreenFadeAlphaAt(t.TeleportAt), Eps, "앞 = 순간이동 암전");
        Assert.AreEqual(0f, t.ScreenFadeAlphaAt(t.WalkAt + 1d), Eps, "보행 중 밝음");
        Assert.AreEqual(0.5f, t.ScreenFadeAlphaAt(t.FadeOutAt + 0.3d), Eps, "뒤 = 끝 페이드");
        Assert.AreEqual(1f, t.ScreenFadeAlphaAt(t.LoadAt), Eps);
    }

    [Test]
    public void Lobby_BlackoutLongerThanLetterbox_WalkWaitsForLight()
    {
        PodDropLobbyTimeline zero = Lobby();
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.teleportBlackoutSeconds = 1.2f;
        var t = Lobby(s);

        Assert.AreEqual(0.6f, t.TeleportAt, Eps);
        Assert.AreEqual(1.2f, t.WalkAt, Eps, "보행 시작 = 암전 끝(레터박스 0.5 보다 늦음)");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(t.WalkAt), Eps);
        Assert.AreEqual(zero.LoadAt + 0.7f, t.LoadAt, Eps, "뒤 경계는 늘어난 만큼(1.2 − 0.5) 밀린다");
        Assert.AreEqual(PodDropLobbyPhase.LetterboxIn, t.PhaseAt(1.1d), "암전 중은 아직 레터박스 인 단계");
        Assert.AreEqual(PodDropLobbyPhase.Walk, t.PhaseAt(t.WalkAt));
    }

    [Test]
    public void Lobby_BlackoutZero_TeleportAtStartNoBlack()
    {
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.teleportBlackoutSeconds = 0f;
        var t = Lobby(s);

        Assert.AreEqual(0f, t.TeleportAt, Eps, "암전 없음 → t=0 에 순간이동");
        Assert.AreEqual(0.5f, t.WalkAt, Eps, "보행은 레터박스 끝");
        for (double e = -0.5d; e <= t.FadeOutAt - 0.01d; e += 0.01d)
            Assert.AreEqual(0f, t.BlackoutAlphaAt(e), Eps, $"t={e:F2} 암전이 없어야 한다");

        s.teleportBlackoutSeconds = -1f;
        Assert.AreEqual(0f, Lobby(s).TeleportBlackoutSeconds, Eps, "음수 → 0");
    }

    // ─── 로비: 해치 (D13 — 길이 0 이면 건너뜀) ────────────────────────────

    [Test]
    public void Lobby_HatchZero_PhaseNeverAppears()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0f, PodDropLobbyTimings.Default.hatchCloseSeconds, Eps, "기본 = 해치 연출 없음");
        Assert.AreEqual(0f, t.DurationOf(PodDropLobbyPhase.HatchClose), Eps);
        for (double e = -0.5d; e <= t.LoadAt + 0.5d; e += 0.01d)
            Assert.AreNotEqual(PodDropLobbyPhase.HatchClose, t.PhaseAt(e), $"t={e:F2}");
        Assert.AreEqual(PodDropLobbyPhase.Descend, t.PhaseAt(t.HatchCloseAt), "보행 끝 = 바로 강하");
    }

    [Test]
    public void Lobby_HatchNonZero_OnlyShiftsLaterBoundaries()
    {
        PodDropLobbyTimeline zero = Lobby();
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.hatchCloseSeconds = 0.7f;
        var t = Lobby(s);

        Assert.AreEqual(zero.WalkAt, t.WalkAt, Eps, "앞 경계는 그대로");
        Assert.AreEqual(zero.HatchCloseAt, t.HatchCloseAt, Eps);
        Assert.AreEqual(zero.DescendAt + 0.7f, t.DescendAt, Eps, "뒤 경계는 해치 길이만큼 밀린다");
        Assert.AreEqual(zero.FadeOutAt + 0.7f, t.FadeOutAt, Eps);
        Assert.AreEqual(zero.LoadAt + 0.7f, t.LoadAt, Eps);
        Assert.AreEqual(PodDropLobbyPhase.HatchClose, t.PhaseAt(t.HatchCloseAt));
        Assert.AreEqual(0f, t.HatchCloseProgressAt(t.HatchCloseAt), Eps);
        Assert.AreEqual(0.5f, t.HatchCloseProgressAt(t.HatchCloseAt + 0.35d), Eps);
        Assert.AreEqual(1f, t.HatchCloseProgressAt(t.DescendAt), Eps);
    }

    [Test]
    public void Lobby_AllZero_GoesStraightToDone()
    {
        var t = new PodDropLobbyTimeline(default, Same(Walkers, 0f));
        Assert.AreEqual(0f, t.LoadAt, Eps);
        Assert.AreEqual(PodDropLobbyPhase.Done, t.PhaseAt(0d));
        Assert.AreEqual(1f, t.FadeAlphaAt(0d), Eps, "0 길이 페이드 = 바로 암전");
    }

    [Test]
    public void Lobby_NegativeDurations_ClampedToZero()
    {
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.hatchCloseSeconds = -3f;
        s.walkInterval = -1f;
        var t = Lobby(s);
        Assert.AreEqual(t.HatchCloseAt, t.DescendAt, Eps);
        Assert.AreEqual(t.WalkAt + Walk + Slide, t.HatchCloseAt, Eps, "간격 0 → 전원 동시 출발");
    }

    // ─── 로비: 보행 ──────────────────────────────────────────────────────

    [Test]
    public void Lobby_Walk_SlotsStaggeredAndProgressLinear()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0.5f, t.WalkStartAt(0), Eps);
        Assert.AreEqual(0.9f, t.WalkStartAt(1), Eps);
        Assert.AreEqual(1.3f, t.WalkStartAt(2), Eps);
        Assert.AreEqual(4.3f, t.WalkArriveAt(2), Eps, "문턱 도착 = 출발 + 보행 시간");
        Assert.AreEqual(4.7f, t.WalkEndAt(2), Eps, "좌석 도착 = 문턱 + 미끄러짐 = 보행 단계 끝");
        Assert.AreEqual(t.HatchCloseAt, t.WalkEndAt(Walkers - 1), Eps);

        Assert.AreEqual(0f, t.WalkProgressAt(1, 0d), Eps, "출발 전 0");
        Assert.AreEqual(0f, t.WalkProgressAt(1, t.WalkStartAt(1)), Eps);
        Assert.AreEqual(0.5f, t.WalkProgressAt(1, t.WalkStartAt(1) + 1.5d), Eps);
        Assert.AreEqual(1f, t.WalkProgressAt(1, t.WalkEndAt(1)), Eps);
        Assert.AreEqual(1f, t.WalkProgressAt(1, t.LoadAt), Eps, "도착 뒤 1 유지");
    }

    [Test]
    public void Lobby_WalkerCount_ChangesOnlyWalkLength()
    {
        PodDropLobbyTimeline one = Lobby(1);
        Assert.AreEqual(one.WalkAt + Walk + Slide, one.HatchCloseAt, Eps, "1인 = 간격 없음");
        Assert.AreEqual(Lobby().LoadAt - 0.8f, one.LoadAt, Eps);

        PodDropLobbyTimeline none = Lobby(0);
        Assert.AreEqual(0f, none.DurationOf(PodDropLobbyPhase.Walk), Eps, "0인 = 보행 단계 길이 0");
        Assert.AreEqual(PodDropLobbyPhase.Descend, none.PhaseAt(none.WalkAt));
        Assert.AreEqual(0, new PodDropLobbyTimeline(PodDropLobbyTimings.Default, null).WalkerCount, "목록 없음 = 0인");
    }

    // ─── 로비: 걷는 속도 기준 보행 시간 (C2d) ──────────────────────────────

    [Test]
    public void WalkSecondsFor_LengthOverSpeed()
    {
        Assert.AreEqual(2f, PodDropLobbyTimeline.WalkSecondsFor(9f, 4.5f), Eps, "9m ÷ 4.5m/s");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(0f, 4.5f), Eps, "길이 0 → 0");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(-3f, 4.5f), Eps, "음수 길이 → 0");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(9f, 0f), Eps, "속도 0 → 무한 대신 0");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(9f, -1f), Eps, "음수 속도 → 0");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(float.NaN, 4.5f), Eps, "NaN → 0");
    }

    [Test]
    public void Lobby_DifferentPathLengths_TotalFollowsLatestArrival()
    {
        // 경로 9m · 4.5m · 13.5m, 4.5m/s → 2 · 1 · 3초. 출발 0.5 · 0.9 · 1.3 → 좌석 2.9 · 2.3 · 4.7.
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        float[] seconds = { 9f / s.walkSpeed, 4.5f / s.walkSpeed, 13.5f / s.walkSpeed };
        var t = new PodDropLobbyTimeline(s, seconds);
        Assert.AreEqual(2.9f, t.WalkEndAt(0), Eps);
        Assert.AreEqual(2.3f, t.WalkEndAt(1), Eps, "P2 가 P1 보다 먼저 앉는다");
        Assert.AreEqual(4.7f, t.WalkEndAt(2), Eps);
        Assert.AreEqual(4.7f, t.HatchCloseAt, Eps);

        // 첫 사람 경로가 가장 길면 마지막 슬롯이 아니라 그 사람 기준.
        var first = new PodDropLobbyTimeline(s, new[] { 4f, 1f, 1f });
        Assert.AreEqual(0.5f + 4f + Slide, first.WalkEndAt(0), Eps);
        Assert.AreEqual(first.WalkEndAt(0), first.HatchCloseAt, Eps, "가장 늦게 앉는 사람(P1) 기준");
        Assert.Less(first.WalkEndAt(2), first.HatchCloseAt);
        Assert.AreEqual(PodDropLobbyPhase.Walk, first.PhaseAt(first.WalkEndAt(2) + 0.1d), "P3 가 앉아도 P1 이 걷는 동안 보행 단계");
    }

    [Test]
    public void Lobby_ZeroLengthPath_OnlySlide()
    {
        var t = new PodDropLobbyTimeline(PodDropLobbyTimings.Default, Same(Walkers, 0f));
        Assert.AreEqual(t.WalkStartAt(0), t.WalkArriveAt(0), Eps, "길이 0 = 걷지 않고 곧바로 미끄러짐");
        Assert.AreEqual(1.3f + Slide, t.HatchCloseAt, Eps, "마지막 출발 + 미끄러짐");
        Assert.AreEqual(1f, t.WalkProgressAt(0, t.WalkStartAt(0)), Eps, "길이 0 진행률은 출발 순간 1");
    }

    [Test]
    public void Lobby_InvalidWalkSeconds_ClampedToZero()
    {
        var t = new PodDropLobbyTimeline(PodDropLobbyTimings.Default, new[] { -2f, float.NaN, 3f });
        Assert.AreEqual(0f, t.WalkSecondsOf(0), Eps, "음수 → 0");
        Assert.AreEqual(0f, t.WalkSecondsOf(1), Eps, "NaN → 0");
        Assert.AreEqual(0f, t.WalkSecondsOf(7), Eps, "명단 밖 슬롯 → 0");
        Assert.AreEqual(1.3f + 3f + Slide, t.HatchCloseAt, Eps);
        Assert.IsFalse(float.IsNaN(t.LoadAt));
    }

    [Test]
    public void Lobby_SeatSlide_SeparateFromWalk()
    {
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.seatSlideSeconds = 0f;
        var none = Lobby(s);
        Assert.AreEqual(none.WalkArriveAt(2), none.WalkEndAt(2), Eps, "미끄러짐 0 → 문턱 도착 = 좌석 도착");

        s.seatSlideSeconds = 1f;
        var longer = Lobby(s);
        Assert.AreEqual(none.WalkArriveAt(2), longer.WalkArriveAt(2), Eps, "걷기 시간은 그대로");
        Assert.AreEqual(none.LoadAt + 1f, longer.LoadAt, Eps, "뒤 경계만 미끄러짐만큼 밀린다");

        s.seatSlideSeconds = -1f;
        Assert.AreEqual(0f, Lobby(s).SeatSlideSeconds, Eps, "음수 → 0");
    }

    // ─── 로비: 레터박스 · 카메라 · 강하 · 페이드 ───────────────────────────

    [Test]
    public void Lobby_LetterboxAlpha()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0f, t.LetterboxAlphaAt(-0.1d), Eps, "시작 전 없음");
        Assert.AreEqual(0f, t.LetterboxAlphaAt(0d), Eps);
        Assert.AreEqual(0.5f, t.LetterboxAlphaAt(0.25d), Eps, "선형");
        Assert.AreEqual(1f, t.LetterboxAlphaAt(t.WalkAt), Eps);
        Assert.AreEqual(1f, t.LetterboxAlphaAt(t.LoadAt - 0.01d), Eps, "페이드 끝까지 덮임");
        Assert.AreEqual(0f, t.LetterboxAlphaAt(t.LoadAt), Eps, "완전 암전부터 걷힘(로딩 화면을 가리지 않음)");
    }

    [Test]
    public void Lobby_CameraDistance_EndValues()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(1.8f, t.CameraDistanceAt(-1d), Eps, "시작 전 = 시작 거리");
        Assert.AreEqual(1.8f, t.CameraDistanceAt(0d), Eps);
        Assert.AreEqual((1.8f + 1.15f) * 0.5f, t.CameraDistanceAt(3d), Eps, "InOutSine 중간 = 절반");
        Assert.AreEqual(1.15f, t.CameraDistanceAt(6d), Eps);
        Assert.AreEqual(1.15f, t.CameraDistanceAt(t.LoadAt + 5d), Eps, "끝 거리 유지");

        float prev = t.CameraDistanceAt(0d);
        for (double e = 0.1d; e <= 6d; e += 0.1d)
        {
            float d = t.CameraDistanceAt(e);
            Assert.LessOrEqual(d, prev + Eps, "멀리서 줌인 — 거리가 줄기만 한다");
            prev = d;
        }
    }

    [Test]
    public void Lobby_PodOffset_EndValues()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0f, t.PodOffsetAt(0d), Eps, "강하 전 제자리");
        Assert.AreEqual(0f, t.PodOffsetAt(t.DescendAt), Eps);
        Assert.AreEqual(-10f * Easing.Evaluate(Ease.InCubic, 0.5f), t.PodOffsetAt(t.DescendAt + 1d), Eps, "InCubic 중간");
        Assert.AreEqual(-10f, t.PodOffsetAt(t.FadeOutAt), Eps);
        Assert.AreEqual(-10f, t.PodOffsetAt(t.LoadAt + 1d), Eps, "끝 깊이 유지");
    }

    [Test]
    public void Lobby_FadeAlpha()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0f, t.FadeAlphaAt(-1d), Eps);
        Assert.AreEqual(0f, t.FadeAlphaAt(t.FadeOutAt - 0.01d), Eps);
        Assert.AreEqual(0.5f, t.FadeAlphaAt(t.FadeOutAt + 0.3d), Eps);
        Assert.AreEqual(1f, t.FadeAlphaAt(t.LoadAt), Eps);
        Assert.AreEqual(1f, t.FadeAlphaAt(t.LoadAt + 10d), Eps);
    }

    [Test]
    public void Lobby_PhaseProgress()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0f, t.PhaseProgressAt(t.DescendAt), Eps);
        Assert.AreEqual(0.5f, t.PhaseProgressAt(t.DescendAt + 1d), Eps);
        Assert.AreEqual(0.5f, t.PhaseProgressAt(t.FadeOutAt + 0.3d), Eps);
        Assert.AreEqual(1f, t.PhaseProgressAt(t.LoadAt), Eps, "Done = 1");
    }

    // ─── 맵 ──────────────────────────────────────────────────────────────

    [Test]
    public void Map_Boundaries_Default()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(0.3f, t.FallAt, Eps);
        Assert.AreEqual(0.9f, t.ImpactAt, Eps);
        Assert.AreEqual(1.3f, t.DissolveAt, Eps);
        Assert.AreEqual(t.DissolveAt, t.PlayersVisibleAt, Eps, "디졸브 시작 = 플레이어 보임");
        Assert.AreEqual(2.3f, t.LetterboxOutAt, Eps);
        Assert.AreEqual(2.8f, t.ReleaseAt, Eps, "맵 합계 2.8초");
        Assert.LessOrEqual(t.ReleaseAt, 4f, "D4 — 맵 3~4초 이내");
    }

    [Test]
    public void Map_PhaseAt_BoundaryBelongsToNextPhase()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(PodDropMapPhase.Hold, t.PhaseAt(-1d));
        Assert.AreEqual(PodDropMapPhase.Hold, t.PhaseAt(0d));
        Assert.AreEqual(PodDropMapPhase.Fall, t.PhaseAt(t.FallAt));
        Assert.AreEqual(PodDropMapPhase.Fall, t.PhaseAt(t.ImpactAt - 0.01d));
        Assert.AreEqual(PodDropMapPhase.Settle, t.PhaseAt(t.ImpactAt));
        Assert.AreEqual(PodDropMapPhase.Dissolve, t.PhaseAt(t.DissolveAt));
        Assert.AreEqual(PodDropMapPhase.LetterboxOut, t.PhaseAt(t.LetterboxOutAt));
        Assert.AreEqual(PodDropMapPhase.LetterboxOut, t.PhaseAt(t.ReleaseAt - 0.01d));
        Assert.AreEqual(PodDropMapPhase.Released, t.PhaseAt(t.ReleaseAt));
        Assert.AreEqual(PodDropMapPhase.Released, t.PhaseAt(t.ReleaseAt + 100d));
    }

    [Test]
    public void Map_StartOfAndDurations_SumToTotal()
    {
        PodDropMapTimeline t = Map();
        float sum = 0f;
        for (var p = PodDropMapPhase.Hold; p < PodDropMapPhase.Released; p++)
        {
            Assert.AreEqual(sum, t.StartOf(p), Eps, $"{p} 시작 = 앞 단계 길이 합");
            sum += t.DurationOf(p);
        }
        Assert.AreEqual(t.ReleaseAt, sum, Eps);
    }

    [Test]
    public void Map_Release_AfterLetterboxOutEnds()
    {
        // D8 — 해제(UI·조작·S0)는 레터박스가 다 걷힌 뒤.
        PodDropMapTimeline t = Map();
        Assert.GreaterOrEqual(t.ReleaseAt, t.LetterboxOutAt + t.LetterboxOutSeconds - Eps);
        for (double e = -0.5d; e <= t.ReleaseAt + 0.5d; e += 0.01d)
        {
            if (t.IsReleasedAt(e))
                Assert.AreEqual(0f, t.LetterboxAlphaAt(e), Eps, $"t={e:F2} 해제됐는데 레터박스가 남아 있다");
            else
                Assert.Less(e, t.ReleaseAt);
        }

        // 아웃 길이 0 이어도 해제는 디졸브 뒤.
        PodDropMapTimings s = PodDropMapTimings.Default;
        s.letterboxOutSeconds = 0f;
        var z = new PodDropMapTimeline(s);
        Assert.AreEqual(z.LetterboxOutAt, z.ReleaseAt, Eps);
        Assert.AreEqual(0f, z.LetterboxAlphaAt(z.ReleaseAt), Eps);
        Assert.IsTrue(z.IsReleasedAt(z.ReleaseAt));
        Assert.IsFalse(z.IsReleasedAt(z.ReleaseAt - 0.01d));
    }

    [Test]
    public void Map_LetterboxAlpha_StartsCovered()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(1f, t.LetterboxAlphaAt(-1d), Eps, "로딩이 걷힐 때 이미 덮임");
        Assert.AreEqual(1f, t.LetterboxAlphaAt(0d), Eps);
        Assert.AreEqual(1f, t.LetterboxAlphaAt(t.LetterboxOutAt), Eps);
        Assert.AreEqual(0.5f, t.LetterboxAlphaAt(t.LetterboxOutAt + 0.25d), Eps, "선형");
        Assert.AreEqual(0f, t.LetterboxAlphaAt(t.ReleaseAt), Eps);
        Assert.AreEqual(0f, t.LetterboxAlphaAt(t.ReleaseAt + 5d), Eps);
    }

    [Test]
    public void Map_PodHeight_EndValues()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(12f, t.PodHeightAt(-1d), Eps, "시작 전 위");
        Assert.AreEqual(12f, t.PodHeightAt(t.FallAt), Eps);
        Assert.AreEqual(12f * (1f - Easing.Evaluate(Ease.InQuad, 0.5f)), t.PodHeightAt(t.FallAt + 0.3d), Eps, "InQuad 중간");
        Assert.AreEqual(0f, t.PodHeightAt(t.ImpactAt), Eps, "착지 = 0");
        Assert.AreEqual(0f, t.PodHeightAt(t.ReleaseAt + 1d), Eps);
    }

    [Test]
    public void Map_Dissolve_EndValues()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(0f, t.DissolveProgressAt(0d), Eps);
        Assert.AreEqual(0f, t.DissolveProgressAt(t.DissolveAt), Eps);
        Assert.AreEqual(0.5f, t.DissolveProgressAt(t.DissolveAt + 0.5d), Eps);
        Assert.AreEqual(1f, t.DissolveProgressAt(t.LetterboxOutAt), Eps, "레터박스 아웃 전에 다 사라짐");
        Assert.AreEqual(1f, t.DissolveProgressAt(t.ReleaseAt + 1d), Eps);
    }

    [Test]
    public void Map_CameraDistance_EndsAtNormalBeforeRelease()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(1.8f, t.CameraDistanceAt(0d), Eps);
        Assert.AreEqual(1f, t.CameraDistanceAt(t.CameraZoomSeconds), Eps);
        Assert.LessOrEqual(t.CameraZoomSeconds, t.ReleaseAt, "기본값 — 해제 전에 줌이 끝나 평소 카메라와 이어진다");
        Assert.AreEqual(1f, t.CameraDistanceAt(t.ReleaseAt), Eps);
    }

    [Test]
    public void Map_ZeroLengthPhases_Skipped()
    {
        PodDropMapTimings s = PodDropMapTimings.Default;
        s.holdSeconds = 0f;
        s.settleSeconds = 0f;
        var t = new PodDropMapTimeline(s);
        Assert.AreEqual(PodDropMapPhase.Fall, t.PhaseAt(0d), "대기 0 → 바로 낙하");
        Assert.AreEqual(PodDropMapPhase.Dissolve, t.PhaseAt(t.ImpactAt), "여운 0 → 착지 순간 디졸브");
        Assert.AreEqual(t.ImpactAt, t.DissolveAt, Eps);
        Assert.AreEqual(0.6f + 1f + 0.5f, t.ReleaseAt, Eps);
    }

    // ─── SO ──────────────────────────────────────────────────────────────

    [Test]
    public void Settings_NewInstance_UsesDefaults()
    {
        var settings = ScriptableObject.CreateInstance<PodDropSettings>();
        try
        {
            Assert.AreEqual(Lobby().LoadAt, settings.CreateLobbyTimeline(Same(Walkers, Walk)).LoadAt, Eps);
            Assert.AreEqual(Map().ReleaseAt, settings.CreateMapTimeline().ReleaseAt, Eps);
            Assert.AreEqual(2f, settings.WalkSecondsFor(9f), Eps, "기본 걷는 속도 4.5m/s");
            Assert.AreEqual(0.04f, settings.LetterboxHeight, Eps);
            Assert.AreEqual(120f, settings.Map.startTimeoutSeconds, Eps);
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }
}
