using System.Collections.Generic;
using EuniTween;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 포드 강하 순수 시간표(<see cref="PodDropLobbyTimeline"/>·<see cref="PodDropMapTimeline"/>, 할 일 100 C1 → 106)와 클립 묶음 문(<see cref="PodClipGate"/>).
/// 할 일 106 — 클립은 1배속으로 끝까지 재생하고 단계는 클립 애니 이벤트(서버 확정 시각)가 넘긴다. 시간표는 이벤트 시각을 입력으로 받는다(아직 = +∞).
/// 기본 수치(로비): 레터박스 인 0.5(같이 순간이동 암전 0.4, 한가운데 0.2 에 순간이동 = 해치 클립 시작), 간격 0.4, 문턱 → 좌석 미끄러짐 0.4, 줌 1.8 → 1.15 / 6초.
/// 보행 시간은 슬롯마다 받는다(C2d — 경로 길이 ÷ 걷는 속도). 여기선 대부분 3인 각 3초 → 탑승 → 다 앉기 0.8 + 3 + 0.4 = 4.2초.
/// 기본 수치(맵, 106 추가 2): 레터박스 처음부터 덮임, 대기 0.3 → 빠른 낙하 0.3 ~ 0.9(30m → 8m, Linear) → 급감속 0.9 ~ 1.7(8m → 5m, OutCubic — 펴기 시작),
/// 착륙 1.5, 열기 끝 → 1초 → 디졸브 1(레터박스 아웃 0.5 같이 시작), 줌 1.8 → 1 / 2.6초.
/// </summary>
public sealed class PodDropTimelineTests
{
    const float Eps = 1e-4f;
    const int Walkers = 3;
    const float Walk = 3f;
    const float Slide = 0.4f;
    const float Seated = 0.8f + Walk + Slide; // 마지막(P3) 출발 +0.4×2, 문턱 +3, 좌석 +0.4
    const double NotYet = PodDropTimeline.NotYet;

    static float[] Same(int walkers, float seconds)
    {
        var result = new float[walkers];
        for (int i = 0; i < walkers; i++) result[i] = seconds;
        return result;
    }

    static PodDropLobbyTimeline Lobby(int walkers = Walkers) => new PodDropLobbyTimeline(PodDropLobbyTimings.Default, Same(walkers, Walk));
    static PodDropLobbyTimeline Lobby(PodDropLobbyTimings s) => new PodDropLobbyTimeline(s, Same(Walkers, Walk));
    static PodDropMapTimeline Map() => new PodDropMapTimeline(PodDropMapTimings.Default);

    // ─── 로비: 해치 이벤트 → 탑승 → 발사 ─────────────────────────────────

    [Test]
    public void Lobby_Default_StaticTimes()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0.2f, t.TeleportAt, Eps, "순간이동 = 암전 한가운데");
        Assert.AreEqual(t.TeleportAt, t.HatchAt, Eps, "해치 클립은 순간이동 순간 시작");
        Assert.AreEqual(0.5f, t.WalkReadyAt, Eps, "레터박스 인 끝 = 탑승 가능");
        Assert.AreEqual(Seated, t.SeatedSeconds, Eps, "탑승 → 가장 늦게 앉기");
    }

    [Test]
    public void Lobby_BoardAt_WaitsForHatchEvent()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.IsTrue(double.IsPositiveInfinity(t.BoardAt(NotYet)), "해치 이벤트 전 = 탑승 없음");
        Assert.AreEqual(1.92d, t.BoardAt(1.92d), Eps, "해치 이벤트(0.2 + 103/60) 가 늦으면 그 순간 탑승");
        Assert.AreEqual(0.5d, t.BoardAt(0.3d), Eps, "해치가 레터박스보다 일찍 열려도 탑승은 레터박스·암전 끝 뒤");
    }

    [Test]
    public void Lobby_WalkAndLaunch_RelativeToBoard()
    {
        PodDropLobbyTimeline t = Lobby();
        const double board = 2d;
        Assert.AreEqual(2.0d, t.WalkStartAt(0, board), Eps);
        Assert.AreEqual(2.4d, t.WalkStartAt(1, board), Eps);
        Assert.AreEqual(2.8d, t.WalkStartAt(2, board), Eps);
        Assert.AreEqual(5.8d, t.WalkArriveAt(2, board), Eps, "문턱 도착 = 출발 + 보행 시간");
        Assert.AreEqual(6.2d, t.WalkEndAt(2, board), Eps, "좌석 도착 = 문턱 + 미끄러짐");
        Assert.AreEqual(t.WalkEndAt(2, board), t.LaunchAt(board), Eps, "발사 클립 = 가장 늦게 앉은 순간");

        Assert.IsTrue(double.IsPositiveInfinity(t.LaunchAt(NotYet)), "탑승 전 = 발사 없음");
        Assert.IsTrue(double.IsPositiveInfinity(t.WalkEndAt(0, NotYet)), "탑승 전 = 숨김 없음");
        Assert.AreEqual(0f, t.WalkProgressAt(0, 100d, NotYet), Eps, "탑승 전 = 걷기 시작 지점에 서 있다");
    }

    [Test]
    public void Lobby_WalkProgress_Linear()
    {
        PodDropLobbyTimeline t = Lobby();
        const double board = 1d;
        Assert.AreEqual(0f, t.WalkProgressAt(1, 0d, board), Eps, "출발 전 0");
        Assert.AreEqual(0f, t.WalkProgressAt(1, t.WalkStartAt(1, board), board), Eps);
        Assert.AreEqual(0.5f, t.WalkProgressAt(1, t.WalkStartAt(1, board) + 1.5d, board), Eps);
        Assert.AreEqual(1f, t.WalkProgressAt(1, t.WalkEndAt(1, board), board), Eps);
        Assert.AreEqual(1f, t.WalkProgressAt(1, 100d, board), Eps, "도착 뒤 1 유지");
    }

    [Test]
    public void Lobby_WalkerCount_ChangesOnlySeated()
    {
        Assert.AreEqual(Walk + Slide, Lobby(1).SeatedSeconds, Eps, "1인 = 간격 없음");
        PodDropLobbyTimeline none = Lobby(0);
        Assert.AreEqual(0f, none.SeatedSeconds, Eps, "0인 = 탑승 즉시 발사");
        Assert.AreEqual(3d, none.LaunchAt(3d), Eps);
        Assert.AreEqual(0, new PodDropLobbyTimeline(PodDropLobbyTimings.Default, null).WalkerCount, "목록 없음 = 0인");
    }

    [Test]
    public void Lobby_DifferentPathLengths_SeatedFollowsLatestArrival()
    {
        // 경로 9m · 4.5m · 13.5m, 4.5m/s → 2 · 1 · 3초. 출발 0 · 0.4 · 0.8 → 좌석 2.4 · 1.8 · 4.2.
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        var t = new PodDropLobbyTimeline(s, new[] { 9f / s.walkSpeed, 4.5f / s.walkSpeed, 13.5f / s.walkSpeed });
        Assert.AreEqual(2.4d, t.WalkEndAt(0, 0d), Eps);
        Assert.AreEqual(1.8d, t.WalkEndAt(1, 0d), Eps, "P2 가 P1 보다 먼저 앉는다");
        Assert.AreEqual(4.2f, t.SeatedSeconds, Eps);

        var first = new PodDropLobbyTimeline(s, new[] { 4f, 1f, 1f });
        Assert.AreEqual(4f + Slide, first.SeatedSeconds, Eps, "가장 늦게 앉는 사람(P1) 기준");
    }

    [Test]
    public void Lobby_ZeroOrInvalidWalk_ClampedToZero()
    {
        var zero = new PodDropLobbyTimeline(PodDropLobbyTimings.Default, Same(Walkers, 0f));
        Assert.AreEqual(zero.WalkStartAt(0, 1d), zero.WalkArriveAt(0, 1d), Eps, "길이 0 = 걷지 않고 곧바로 미끄러짐");
        Assert.AreEqual(0.8f + Slide, zero.SeatedSeconds, Eps, "마지막 출발 + 미끄러짐");

        var bad = new PodDropLobbyTimeline(PodDropLobbyTimings.Default, new[] { -2f, float.NaN, 3f });
        Assert.AreEqual(0f, bad.WalkSecondsOf(0), Eps, "음수 → 0");
        Assert.AreEqual(0f, bad.WalkSecondsOf(1), Eps, "NaN → 0");
        Assert.AreEqual(0f, bad.WalkSecondsOf(7), Eps, "명단 밖 슬롯 → 0");
        Assert.AreEqual(0.8f + 3f + Slide, bad.SeatedSeconds, Eps);

        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.walkInterval = -1f;
        s.seatSlideSeconds = -1f;
        var clamped = Lobby(s);
        Assert.AreEqual(Walk, clamped.SeatedSeconds, Eps, "간격·미끄러짐 음수 → 0 → 전원 동시 출발");
    }

    [Test]
    public void WalkSecondsFor_LengthOverSpeed()
    {
        Assert.AreEqual(2f, PodDropLobbyTimeline.WalkSecondsFor(9f, 4.5f), Eps, "9m ÷ 4.5m/s");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(0f, 4.5f), Eps, "길이 0 → 0");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(-3f, 4.5f), Eps, "음수 길이 → 0");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(9f, 0f), Eps, "속도 0 → 무한 대신 0");
        Assert.AreEqual(0f, PodDropLobbyTimeline.WalkSecondsFor(float.NaN, 4.5f), Eps, "NaN → 0");
    }

    // ─── 로비: 화면 (레터박스 · 암전 · 카메라) ─────────────────────────────

    [Test]
    public void Lobby_LetterboxAlpha_InThenHeld()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0f, t.LetterboxAlphaAt(-0.1d), Eps, "시작 전 없음");
        Assert.AreEqual(0f, t.LetterboxAlphaAt(0d), Eps);
        Assert.AreEqual(0.5f, t.LetterboxAlphaAt(0.25d), Eps, "선형");
        Assert.AreEqual(1f, t.LetterboxAlphaAt(0.5d), Eps);
        Assert.AreEqual(1f, t.LetterboxAlphaAt(100d), Eps, "로딩 화면으로 넘어갈 때까지 덮임(끝 페이드 아래)");

        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.letterboxInEase = Ease.OutQuad;
        Assert.AreEqual(Easing.Evaluate(Ease.OutQuad, 0.5f), Lobby(s).LetterboxAlphaAt(0.25d), Eps, "이징");
    }

    [Test]
    public void Lobby_BlackoutAlpha_InHoldOut()
    {
        PodDropLobbyTimeline t = Lobby();
        float third = 0.4f / 3f;
        Assert.AreEqual(0.4f, t.BlackoutSeconds, Eps, "기본 = 짧은 암전 0.4초");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(-0.1d), Eps, "시작 전 없음");
        Assert.AreEqual(0.5f, t.BlackoutAlphaAt(third * 0.5f), Eps, "검게 — 선형");
        Assert.AreEqual(1f, t.BlackoutAlphaAt(t.TeleportAt), Eps, "순간이동 순간 = 완전 암전");
        Assert.AreEqual(0.5f, t.BlackoutAlphaAt(third * 2.5f), Eps, "밝게 — 선형");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(0.4d), Eps, "다 밝아짐");
        Assert.AreEqual(0f, t.BlackoutAlphaAt(100d), Eps, "시작 암전은 다시 검어지지 않는다(끝 페이드는 따로)");
    }

    // ─── 로비: 발사 끝 → 대기 → 끝 페이드 아웃 → 로딩 (할 일 121) ─────────────

    [Test]
    public void Lobby_EndFade_WaitThenFadeThenLoad()
    {
        PodDropLobbyTimeline t = Lobby();
        const double end = 20d; // 서버가 확정·복제한 발사 클립 끝(경과 초)
        Assert.AreEqual(1f, t.LaunchEndWaitSeconds, Eps, "기본 대기 1초");
        Assert.AreEqual(1f, t.LaunchEndFadeSeconds, Eps, "기본 페이드 1초");
        Assert.AreEqual(2f, t.LoadAfterLaunchEndSeconds, Eps, "끝 → 로딩 2초");
        Assert.AreEqual(21d, t.EndFadeAt(end), Eps, "페이드 시작 = 끝 + 대기");
        Assert.AreEqual(22d, t.LoadAt(end), Eps, "로딩 = 끝 + 대기 + 페이드");

        Assert.AreEqual(0f, t.EndFadeAlphaAt(end, end), Eps, "끝 순간 = 아직 밝다");
        Assert.AreEqual(0f, t.EndFadeAlphaAt(20.99d, end), Eps, "대기 동안 0");
        Assert.AreEqual(0.5f, t.EndFadeAlphaAt(21.5d, end), Eps, "페이드 — 선형");
        Assert.AreEqual(1f, t.EndFadeAlphaAt(t.LoadAt(end), end), Eps, "로딩 순간 = 완전히 검다(깜빡임 없음)");
        Assert.AreEqual(1f, t.EndFadeAlphaAt(100d, end), Eps, "로딩 화면이 뜰 때까지 검은 채 유지");

        Assert.IsTrue(double.IsPositiveInfinity(t.LoadAt(NotYet)), "끝 이벤트 전 = 로딩 없음");
        Assert.AreEqual(0f, t.EndFadeAlphaAt(100d, NotYet), Eps, "끝 이벤트 전 = 끝 페이드 없음");
    }

    [Test]
    public void Lobby_ScreenFade_MaxOfStartBlackoutAndEndFade()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(1f, t.ScreenFadeAlphaAt(t.TeleportAt, NotYet), Eps, "시작 암전 그대로");
        Assert.AreEqual(0f, t.ScreenFadeAlphaAt(10d, NotYet), Eps, "중간은 밝다");
        Assert.AreEqual(0.5f, t.ScreenFadeAlphaAt(21.5d, 20d), Eps, "끝 페이드");

        // 끝 이벤트가 시작 암전 중에 와도(발사대·보행 없음 극단) 큰 쪽.
        Assert.AreEqual(1f, t.ScreenFadeAlphaAt(t.TeleportAt, 0d), Eps);
    }

    [Test]
    public void Lobby_EndFade_ZeroOrNegative()
    {
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.launchEndWaitSeconds = 0f;
        s.launchEndFadeSeconds = 0f;
        var none = Lobby(s);
        Assert.AreEqual(5d, none.LoadAt(5d), Eps, "둘 다 0 = 끝 이벤트 순간 곧바로 로딩");
        Assert.AreEqual(1f, none.EndFadeAlphaAt(5d, 5d), Eps, "페이드 0 = 그 순간 검다");
        Assert.AreEqual(0f, none.EndFadeAlphaAt(4.99d, 5d), Eps);

        s.launchEndWaitSeconds = -1f;
        s.launchEndFadeSeconds = -1f;
        Assert.AreEqual(0f, Lobby(s).LoadAfterLaunchEndSeconds, Eps, "음수 → 0");

        s.launchEndWaitSeconds = 0.5f;
        s.launchEndFadeSeconds = 2f;
        var tuned = Lobby(s);
        Assert.AreEqual(12.5d, tuned.LoadAt(10d), Eps);
        Assert.AreEqual(0.25f, tuned.EndFadeAlphaAt(11d, 10d), Eps, "0.5초 대기 뒤 2초 중 0.5초");
    }

    [Test]
    public void Lobby_BlackoutLongerThanLetterbox_WalkReadyWaitsForLight()
    {
        var t = Lobby(Blackout(0.4f, 0.4f, 0.4f));
        Assert.AreEqual(0.6f, t.TeleportAt, Eps);
        Assert.AreEqual(1.2f, t.WalkReadyAt, Eps, "탑승 가능 = 암전 끝(레터박스 0.5 보다 늦음)");
        Assert.AreEqual(1.2d, t.BoardAt(0.9d), Eps, "해치가 먼저 열려도 밝아진 뒤 탑승");

        var none = Lobby(Blackout(0f, 0f, 0f));
        Assert.AreEqual(0f, none.TeleportAt, Eps, "암전 없음 → t=0 에 순간이동");
        Assert.AreEqual(0.5f, none.WalkReadyAt, Eps);
        for (double e = -0.5d; e <= 5d; e += 0.01d)
            Assert.AreEqual(0f, none.BlackoutAlphaAt(e), Eps, $"t={e:F2} 암전이 없어야 한다");

        var negative = Lobby(Blackout(-1f, -1f, -1f));
        Assert.AreEqual(0f, negative.BlackoutSeconds, Eps, "음수 → 0");
    }

    static PodDropLobbyTimings Blackout(float fadeIn, float hold, float fadeOut)
    {
        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.blackoutFadeInSeconds = fadeIn;
        s.blackoutHoldSeconds = hold;
        s.blackoutFadeOutSeconds = fadeOut;
        return s;
    }

    [Test]
    public void Lobby_CameraDistance_ZoomDelayAndEnd()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(1.8f, t.CameraDistanceAt(-1d), Eps, "시작 전 = 시작 거리");
        Assert.AreEqual((1.8f + 1.15f) * 0.5f, t.CameraDistanceAt(3d), Eps, "InOutSine 중간 = 절반");
        Assert.AreEqual(1.15f, t.CameraDistanceAt(100d), Eps, "끝 거리 유지");

        PodDropLobbyTimings s = PodDropLobbyTimings.Default;
        s.cameraZoomDelaySeconds = 1f;
        var delayed = Lobby(s);
        Assert.AreEqual(1.8f, delayed.CameraDistanceAt(1d), Eps, "지연 동안 시작 거리");
        Assert.AreEqual((1.8f + 1.15f) * 0.5f, delayed.CameraDistanceAt(4d), Eps);
    }

    [Test]
    public void Lobby_Defaults_ClipStatesOnly()
    {
        PodDropLobbyTimings d = PodDropLobbyTimings.Default;
        Assert.AreEqual("DP_HatchLobbyOpenClose", d.hatchState);
        Assert.AreEqual("DP_LaunchCycle", d.launchState);
        Assert.AreEqual(Ease.Linear, d.letterboxInEase);
        Assert.AreEqual(0f, d.cameraZoomDelaySeconds, Eps);
        Assert.AreEqual(0.4f / 3f, d.blackoutFadeInSeconds, Eps, "검게·유지·밝게 1/3 씩");
    }

    // ─── 로비: 발사 클립 가속 (106 추가 2) ─────────────────────────────────

    [Test]
    public void Lobby_LaunchClipSeconds_FastAfterSpeedUpEvent()
    {
        const double launch = 10d;
        Assert.AreEqual(0f, PodDropLobbyTimeline.LaunchClipSecondsAt(9d, launch, NotYet, 6f), Eps, "발사 전 0");
        Assert.AreEqual(2f, PodDropLobbyTimeline.LaunchClipSecondsAt(12d, launch, NotYet, 6f), Eps, "가속 이벤트 전 = 1배속");
        const double fast = 10d + 213d / 60d; // 213프레임(60fps)
        Assert.AreEqual(213f / 60f, PodDropLobbyTimeline.LaunchClipSecondsAt(fast, launch, fast, 6f), Eps, "가속 순간 = 1배속 구간 끝");
        Assert.AreEqual(213f / 60f + 6f, PodDropLobbyTimeline.LaunchClipSecondsAt(fast + 1d, launch, fast, 6f), Eps, "가속 뒤 1초 = 클립 6초");
        Assert.AreEqual(213f / 60f + 1f, PodDropLobbyTimeline.LaunchClipSecondsAt(fast + 1d, launch, fast, 0f), Eps, "배율 0 이하 → 1배속");
        Assert.AreEqual(12f, PodDropLobbyTimeline.LaunchClipSecondsAt(12d, launch, 5d, 6f), Eps, "가속 시각이 발사보다 앞이면 발사부터 배율");
    }

    [Test]
    public void Lobby_LaunchClipSeconds_TwoStages()
    {
        // 할 일 121 — 213프레임부터 6배, 553프레임부터 3배(60fps).
        const double launch = 10d;
        const double fast = launch + 213d / 60d;                    // 1배속으로 213프레임
        const double fast2 = fast + (553d - 213d) / 60d / 6d;       // 6배속으로 340프레임
        Assert.AreEqual(213f / 60f + 6f, PodDropLobbyTimeline.LaunchClipSecondsAt(fast + 1d, launch, fast, 6f, NotYet, 3f), Eps, "2단계 이벤트 전 = 1단계 배율");
        Assert.AreEqual(553f / 60f, PodDropLobbyTimeline.LaunchClipSecondsAt(fast2, launch, fast, 6f, fast2, 3f), Eps, "2단계 순간 = 553프레임");
        Assert.AreEqual(553f / 60f + 3f, PodDropLobbyTimeline.LaunchClipSecondsAt(fast2 + 1d, launch, fast, 6f, fast2, 3f), Eps, "2단계 뒤 1초 = 클립 3초");
        Assert.AreEqual(553f / 60f + 1f, PodDropLobbyTimeline.LaunchClipSecondsAt(fast2 + 1d, launch, fast, 6f, fast2, 0f), Eps, "2단계 배율 0 이하 → 1배속");
        Assert.AreEqual(2f + 3f, PodDropLobbyTimeline.LaunchClipSecondsAt(13d, launch, NotYet, 6f, 12d, 3f), Eps, "1단계 없이 2단계만 = 1배속 → 3배");
        Assert.AreEqual(2f + 3f, PodDropLobbyTimeline.LaunchClipSecondsAt(13d, launch, 12.5d, 6f, 12d, 3f), Eps, "2단계가 1단계보다 앞이면 1단계 구간 없음");
        Assert.AreEqual(2f * 6f + 3f, PodDropLobbyTimeline.LaunchClipSecondsAt(13d, launch, 5d, 6f, 12d, 3f), Eps, "1단계가 발사보다 앞 = 발사부터 6배 → 2단계 3배");
    }

    [Test]
    public void Lobby_LaunchRealSeconds_TwoStages()
    {
        // 클립 719프레임 끝: 213/60 + 340/60/6 + 166/60/3 = 3.55 + 0.9444 + 0.9222 = 5.4167초.
        float real = PodDropScheduleView.LaunchRealSecondsTo(719f / 60f, 213f / 60f, true, 6f, 553f / 60f, true, 3f);
        Assert.AreEqual(213f / 60f + 340f / 360f + 166f / 180f, real, Eps);
        Assert.AreEqual(213f / 60f + 506f / 360f, PodDropScheduleView.LaunchRealSecondsTo(719f / 60f, 213f / 60f, true, 6f, 0f, false, 3f), Eps, "2단계 없음 = 끝까지 6배");
        Assert.AreEqual(553f / 60f + 166f / 180f, PodDropScheduleView.LaunchRealSecondsTo(719f / 60f, 0f, false, 6f, 553f / 60f, true, 3f), Eps, "1단계 없음 = 553까지 1배");

        // 시간표 역과 맞는다 — 실제 초를 넣으면 클립 끝.
        const double fast = 213d / 60d;
        const double fast2 = fast + 340d / 360d;
        Assert.AreEqual(719f / 60f, PodDropLobbyTimeline.LaunchClipSecondsAt(real, 0d, fast, 6f, fast2, 3f), 1e-3f);
    }

    [Test]
    public void Lobby_Defaults_LaunchFastSpeed()
    {
        Assert.AreEqual(6f, PodDropLobbyTimings.Default.launchFastSpeed, Eps, "213프레임부터 6배");
        Assert.AreEqual(3f, PodDropLobbyTimings.Default.launchFastSpeed2, Eps, "553프레임부터 3배");
        Assert.AreEqual(213, PodAnimEventAuthoring.LobbyLaunchFastFrame);
        Assert.AreEqual(553, PodAnimEventAuthoring.LobbyLaunchFast2Frame);
    }

    // ─── 맵: 빠른 낙하 → 급감속(펴기 시작) → (펴기 이벤트) → 착륙 → (열기 이벤트) → 지연 → 디졸브 → 해제 ───

    [Test]
    public void Map_Default_StaticTimes()
    {
        PodDropMapTimeline t = Map();
        PodDropMapTimings d = PodDropMapTimings.Default;
        Assert.AreEqual(0.3f, t.FallAt, Eps, "낙하 = 대기 끝(레터박스 인 없음)");
        Assert.AreEqual(0.9f, t.BrakeAt, Eps, "급감속 = 낙하 + 0.6");
        Assert.AreEqual(1.7f, t.HoverAt, Eps, "연출 높이 도착 = 급감속 + 0.8");
        Assert.AreEqual(30f, t.FallHeight, Eps, "Y30 에서 시작");
        Assert.AreEqual(8f, t.BrakeHeight, Eps, "급감속 = 연출 높이 5 + 3");
        Assert.AreEqual(5f, t.HoverHeight, Eps);
        Assert.AreEqual(2f, t.ReleaseAfterOpenSeconds, Eps, "열기 끝 → 지연 1 + 디졸브 1 = 조작 해제");
        Assert.AreEqual(2f, t.EndAfterOpenSeconds, Eps, "레터박스 아웃 0.5 는 디졸브 안에 끝난다");
        Assert.AreEqual(0.25f, d.podGroundClearance, Eps, "기어 최저점 −0.248m 실측");
        CollectionAssert.AreEqual(new[] { "DP_WingDeploy", "DP_GearDeploy" }, d.DeployStates);
        CollectionAssert.AreEqual(new[] { "DP_HatchStageOpen", "DP_WingFold" }, d.OpenStates);
    }

    [Test]
    public void Map_PodHeight_FallBrakeHoverLand()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(30f, t.PodHeightAt(-1d, NotYet), Eps, "시작 전 위");
        Assert.AreEqual(30f, t.PodHeightAt(t.FallAt, NotYet), Eps);
        Assert.AreEqual(19f, t.PodHeightAt(t.FallAt + 0.3d, NotYet), Eps, "Linear 중간(30 → 8)");
        Assert.AreEqual(8f, t.PodHeightAt(t.BrakeAt, NotYet), Eps, "급감속 시작 높이");
        Assert.AreEqual(Mathf.Lerp(8f, 5f, Easing.Evaluate(Ease.OutCubic, 0.5f)), t.PodHeightAt(t.BrakeAt + 0.4d, NotYet), Eps, "OutCubic 급감속");
        Assert.AreEqual(5f, t.PodHeightAt(t.HoverAt, NotYet), Eps, "연출 높이");
        Assert.AreEqual(5f, t.PodHeightAt(100d, NotYet), Eps, "펴기 이벤트 전엔 계속 떠 있다");

        const double deployed = 5d;
        Assert.AreEqual(5f, t.PodHeightAt(deployed, deployed), Eps, "착륙 시작");
        Assert.AreEqual(2.5f, t.PodHeightAt(deployed + 0.75d, deployed), Eps, "InOutSine 중간 = 절반");
        Assert.AreEqual(0f, t.PodHeightAt(t.TouchdownAt(deployed), deployed), Eps, "지면");
        Assert.AreEqual(0f, t.PodHeightAt(100d, deployed), Eps);
    }

    [Test]
    public void Map_EarlyDeployEvent_LandsAfterBrakeEnds()
    {
        // 펴기 끝 이벤트가 급감속 도중에 와도 연출 높이에 닿은 뒤 착륙한다(높이가 튀지 않는다).
        PodDropMapTimeline t = Map();
        double early = t.BrakeAt + 0.2d;
        Assert.AreEqual(t.HoverAt, t.LandStartAt(early), Eps);
        Assert.AreEqual(t.HoverAt + 1.5d, t.TouchdownAt(early), Eps);
        Assert.AreEqual(t.PodHeightAt(t.BrakeAt + 0.4d, NotYet), t.PodHeightAt(t.BrakeAt + 0.4d, early), Eps, "급감속은 그대로");
        Assert.AreEqual(5f, t.PodHeightAt(t.HoverAt, early), Eps);
    }

    [Test]
    public void Map_HoverAndBrake_Clamped()
    {
        PodDropMapTimings s = PodDropMapTimings.Default;
        s.hoverHeight = 40f;
        var high = new PodDropMapTimeline(s);
        Assert.AreEqual(30f, high.HoverHeight, Eps, "연출 높이는 낙하 시작보다 높지 않다");
        Assert.AreEqual(30f, high.BrakeHeight, Eps, "급감속 높이도 낙하 시작까지");

        s = PodDropMapTimings.Default;
        s.brakeDistance = 100f;
        Assert.AreEqual(30f, new PodDropMapTimeline(s).BrakeHeight, Eps);
        s.brakeDistance = -1f;
        Assert.AreEqual(5f, new PodDropMapTimeline(s).BrakeHeight, Eps, "급감속 거리 음수 → 0 → 연출 높이에서 바로");

        s = PodDropMapTimings.Default;
        s.hoverHeight = -1f;
        var ground = new PodDropMapTimeline(s);
        Assert.AreEqual(0f, ground.HoverHeight, Eps);
        Assert.AreEqual(0f, ground.PodHeightAt(ground.HoverAt, NotYet), Eps, "연출 높이 0 = 지면까지 떨어진다");
    }

    [Test]
    public void Map_CameraFocus_FixedThenFollowsPod()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(7f, t.CameraFocusHeightAt(-1d, NotYet), Eps, "시작 전 = 연출 높이 5 + 2");
        Assert.AreEqual(7f, t.CameraFocusHeightAt(t.FallAt + 0.3d, NotYet), Eps, "빠른 낙하 동안 고정");
        Assert.AreEqual(t.PodHeightAt(t.BrakeAt, NotYet), t.CameraFocusHeightAt(t.BrakeAt, NotYet), Eps, "급감속부터 포드");
        Assert.AreEqual(0f, t.CameraFocusHeightAt(100d, 5d), Eps, "착지 뒤 포드(지면)");
    }

    [Test]
    public void Map_EventTimes_DriveTouchdownDissolveRelease()
    {
        PodDropMapTimeline t = Map();
        Assert.IsTrue(double.IsPositiveInfinity(t.TouchdownAt(NotYet)), "펴기 이벤트 전 = 착륙 없음");
        Assert.AreEqual(6.5d, t.TouchdownAt(5d), Eps, "착륙 = 펴기 끝 + 1.5");

        const double opened = 8d;
        Assert.AreEqual(9d, t.DissolveAt(opened), Eps, "디졸브 = 열기 끝 + 1초");
        Assert.AreEqual(9d, t.LetterboxOutAt(opened), Eps, "레터박스 아웃 = 디졸브 시작");
        Assert.AreEqual(10d, t.ReleaseAt(opened), Eps, "조작 해제 = 디졸브 끝");
        Assert.IsFalse(t.IsReleasedAt(9.99d, opened));
        Assert.IsTrue(t.IsReleasedAt(10d, opened));
        Assert.IsFalse(t.IsReleasedAt(1e6d, NotYet), "열기 이벤트 전엔 해제 없음");
        Assert.AreEqual(10d, t.EndAt(opened), Eps);

        Assert.AreEqual(0f, t.DissolveProgressAt(8.9d, opened), Eps, "지연 동안 온전");
        Assert.AreEqual(0.5f, t.DissolveProgressAt(9.5d, opened), Eps);
        Assert.AreEqual(1f, t.DissolveProgressAt(10d, opened), Eps, "해제 순간 다 사라짐");
        Assert.AreEqual(0f, t.DissolveProgressAt(1e6d, NotYet), Eps);
    }

    [Test]
    public void Map_LetterboxAlpha_CoveredFromEntryThenOutAtDissolve()
    {
        PodDropMapTimeline t = Map();
        Assert.AreEqual(1f, t.LetterboxAlphaAt(-1d, NotYet), Eps, "로딩 화면 아래부터 덮임");
        Assert.AreEqual(1f, t.LetterboxAlphaAt(0d, NotYet), Eps, "로딩이 걷힌 순간 이미 덮임");
        Assert.AreEqual(1f, t.LetterboxAlphaAt(1e6d, NotYet), Eps, "열기 이벤트 전엔 덮인 채");

        const double opened = 8d;
        Assert.AreEqual(1f, t.LetterboxAlphaAt(t.DissolveAt(opened) - 0.01d, opened), Eps, "디졸브 지연 동안 덮임");
        Assert.AreEqual(1f, t.LetterboxAlphaAt(t.LetterboxOutAt(opened), opened), Eps);
        Assert.AreEqual(0.5f, t.LetterboxAlphaAt(t.LetterboxOutAt(opened) + 0.25d, opened), Eps, "아웃 — 선형");
        Assert.AreEqual(0f, t.LetterboxAlphaAt(t.EndAt(opened), opened), Eps, "연출 끝 = 레터박스 다 걷힘");
    }

    [Test]
    public void Map_End_AfterDissolveAndLetterbox()
    {
        PodDropMapTimeline t = Map();
        const double opened = 4d;
        for (double e = 0d; e <= t.EndAt(opened) + 0.5d; e += 0.01d)
        {
            if (t.IsEndedAt(e, opened))
            {
                Assert.AreEqual(0f, t.LetterboxAlphaAt(e, opened), Eps, $"t={e:F2} 끝났는데 레터박스가 남아 있다");
                Assert.IsTrue(t.IsReleasedAt(e, opened), $"t={e:F2} 끝났는데 조작이 잠겨 있다");
            }
        }

        PodDropMapTimings s = PodDropMapTimings.Default;
        s.letterboxOutSeconds = 3f;
        var longOut = new PodDropMapTimeline(s);
        Assert.AreEqual(opened + 2d, longOut.ReleaseAt(opened), Eps, "조작은 디졸브 끝");
        Assert.AreEqual(opened + 4d, longOut.EndAt(opened), Eps, "연출 끝은 레터박스가 더 길면 그 끝");

        s = PodDropMapTimings.Default;
        s.letterboxOutSeconds = 0f;
        s.dissolveSeconds = 0f;
        s.dissolveDelaySeconds = 0f;
        var z = new PodDropMapTimeline(s);
        Assert.AreEqual(opened, z.ReleaseAt(opened), Eps, "지연·디졸브·아웃 0 → 열기 끝 순간 해제");
        Assert.AreEqual(0f, z.LetterboxAlphaAt(z.EndAt(opened), opened), Eps);
    }

    [Test]
    public void Map_FadeIn_And_ZoomDelay()
    {
        PodDropMapTimings s = PodDropMapTimings.Default;
        Assert.AreEqual(0f, new PodDropMapTimeline(s).FadeAlphaAt(0d), Eps, "도착 페이드인 기본 없음");

        s.fadeInSeconds = 0.8f;
        s.cameraZoomDelaySeconds = 0.5f;
        var t = new PodDropMapTimeline(s);
        Assert.AreEqual(1f, t.FadeAlphaAt(-1d), Eps, "시작 전(로딩 화면 아래) 검다");
        Assert.AreEqual(0.5f, t.FadeAlphaAt(0.4d), Eps, "페이드인 — 선형");
        Assert.AreEqual(0f, t.FadeAlphaAt(0.8d), Eps);
        Assert.AreEqual(1.8f, t.CameraDistanceAt(0.5d), Eps, "지연 동안 시작 거리");
        Assert.AreEqual(1f, t.CameraDistanceAt(0.5d + 2.6d), Eps);
        Assert.AreEqual(Map().HoverAt, t.HoverAt, Eps, "페이드·줌은 시각을 밀지 않는다");
    }

    [Test]
    public void Map_NegativeValues_ClampedToZero()
    {
        PodDropMapTimings s = PodDropMapTimings.Default;
        s.holdSeconds = -1f;
        s.fallSeconds = -1f;
        s.brakeSeconds = -1f;
        s.landSeconds = -1f;
        s.dissolveDelaySeconds = -1f;
        var t = new PodDropMapTimeline(s);
        Assert.AreEqual(0f, t.FallAt, Eps);
        Assert.AreEqual(0f, t.HoverAt, Eps, "낙하·급감속 0 → 곧바로 연출 높이");
        Assert.AreEqual(3d, t.DissolveAt(3d), Eps, "지연 음수 → 0");
        Assert.AreEqual(t.HoverHeight, t.PodHeightAt(0d, NotYet), Eps);
        Assert.AreEqual(2d, t.TouchdownAt(2d), Eps, "착륙 0 → 펴기 끝 순간 지면");
        Assert.AreEqual(0f, t.PodHeightAt(2d, 2d), Eps);
    }

    // ─── 클립 묶음 문 ────────────────────────────────────────────────────

    [Test]
    public void ClipGate_OpensWhenEveryClipHasEvent()
    {
        var gate = new PodClipGate(new[] { "DP_WingDeploy", "DP_GearDeploy" });
        Assert.IsFalse(gate.IsOpen(_ => false, out _), "아무 이벤트도 없다");
        Assert.IsTrue(gate.Notify("DP_WingDeploy"));
        Assert.IsFalse(gate.IsOpen(_ => false, out _), "둘 다 와야 열린다");
        Assert.IsFalse(gate.Notify("DP_HatchStageOpen"), "묶음 밖 클립은 무시");
        Assert.IsTrue(gate.Notify("DP_GearDeploy"));
        Assert.IsTrue(gate.IsOpen(_ => false, out List<string> fallbacks));
        Assert.IsNull(fallbacks, "이벤트로 열림 — 예비 판정 없음");
    }

    [Test]
    public void ClipGate_MissingEvent_FallsBackToClipEnd()
    {
        var gate = new PodClipGate(new[] { "DP_HatchStageOpen", "DP_WingFold" });
        gate.Notify("DP_HatchStageOpen");
        Assert.IsFalse(gate.IsOpen(s => false, out _), "날개 접기가 아직 돈다");
        Assert.IsTrue(gate.IsOpen(s => s == "DP_WingFold", out List<string> fallbacks), "이벤트가 빠진 클립은 끝까지 돌면 연다");
        CollectionAssert.AreEqual(new[] { "DP_WingFold" }, fallbacks, "경고할 클립");
    }

    [Test]
    public void ClipGate_EmptyStatesSkipped()
    {
        var gate = new PodClipGate(new[] { "", null, "DP_LaunchCycle", "DP_LaunchCycle" });
        Assert.AreEqual(1, gate.States.Count, "비운 칸·중복 뺀다");

        var none = new PodClipGate(new[] { "", null });
        Assert.IsTrue(none.IsOpen(null, out _), "전부 비면 처음부터 열림(애니 없이 곧바로)");
    }

    // ─── 저작 도구 · SO ─────────────────────────────────────────────────

    [Test]
    public void EventAuthoring_FrameToNormalized()
    {
        Assert.AreEqual(103f / 188f, PodAnimEventAuthoring.ToNormalized(103, 0f, 188f), Eps, "로비 해치 103프레임(클립 0~188)");
        Assert.AreEqual(1f, PodAnimEventAuthoring.ToNormalized(-1, 0f, 719f), Eps, "음수 = 끝 프레임");
        Assert.AreEqual(1f, PodAnimEventAuthoring.ToNormalized(500, 0f, 188f), Eps, "범위 밖은 끝");
        Assert.AreEqual(1f, PodAnimEventAuthoring.ToNormalized(3, 0f, 0f), Eps, "길이 0 = 끝");
    }

    [Test]
    public void Settings_NewInstance_UsesDefaults()
    {
        var settings = ScriptableObject.CreateInstance<PodDropSettings>();
        try
        {
            Assert.AreEqual(Lobby().SeatedSeconds, settings.CreateLobbyTimeline(Same(Walkers, Walk)).SeatedSeconds, Eps);
            Assert.AreEqual(Map().HoverAt, settings.CreateMapTimeline().HoverAt, Eps);
            Assert.AreEqual(2f, settings.WalkSecondsFor(9f), Eps, "기본 걷는 속도 4.5m/s");
            Assert.AreEqual(0.04f, settings.LetterboxHeight, Eps);
            Assert.AreEqual(120f, settings.Map.startTimeoutSeconds, Eps);
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void Settings_Asset_NewFieldsTakeDefaults()
    {
        // 106 — 에셋에 아직 없는 새 칸은 코드 기본값(SO 필드 초기값)으로 읽힌다(에셋을 다시 쓰지 않는다 — 사용자 튜닝 값 보존).
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PodDropSettings>("Assets/9.ScriptableObject/Lobby/PodDropSettings.asset");
        Assert.IsNotNull(asset, "PodDropSettings.asset 이 없다");
        PodDropLobbyTimings d = PodDropLobbyTimings.Default;
        Assert.AreEqual(d.hatchState, asset.Lobby.hatchState);
        Assert.AreEqual(d.launchState, asset.Lobby.launchState);

        PodDropMapTimings map = asset.Map;
        PodDropMapTimings m = PodDropMapTimings.Default;
        Assert.AreEqual(m.hoverHeight, map.hoverHeight, Eps);
        Assert.AreEqual(m.landSeconds, map.landSeconds, Eps);
        // 106 추가 2 — 새 칸은 기본값, 시작 높이는 에셋에서 30 으로 고침.
        Assert.AreEqual(d.launchFastSpeed, asset.Lobby.launchFastSpeed, Eps);
        Assert.AreEqual(d.launchFastSpeed2, asset.Lobby.launchFastSpeed2, Eps, "할 일 121 — 가속 2단계 배율(에셋에도 3)");
        // 할 일 121 — 끝 대기·끝 페이드(에셋에도 1·1).
        Assert.AreEqual(1f, asset.Lobby.launchEndWaitSeconds, Eps);
        Assert.AreEqual(1f, asset.Lobby.launchEndFadeSeconds, Eps);
        Assert.AreEqual(m.brakeDistance, map.brakeDistance, Eps);
        Assert.AreEqual(m.brakeSeconds, map.brakeSeconds, Eps);
        Assert.AreEqual(m.dissolveDelaySeconds, map.dissolveDelaySeconds, Eps);
        Assert.AreEqual(m.cameraStartHeightOffset, map.cameraStartHeightOffset, Eps);
        Assert.AreEqual(m.podGroundClearance, map.podGroundClearance, Eps);
        Assert.AreEqual(30f, map.fallHeight, Eps);
        CollectionAssert.AreEqual(m.DeployStates, map.DeployStates);
        CollectionAssert.AreEqual(m.OpenStates, map.OpenStates);
    }
}
