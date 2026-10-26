using EuniTween;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 포드 강하 순수 시간표(<see cref="PodDropLobbyTimeline"/>·<see cref="PodDropMapTimeline"/>, 할 일 100 C1) — 단계 경계·합계 길이·0 길이 단계·보간 끝값·해제 ≥ 레터박스 아웃.
/// 기본 수치(로비): 레터박스 인 0.5, 보행 3·간격 0.4(3인), 해치 0, 강하 2·깊이 10, 페이드 0.6, 줌 1.8 → 1.15 / 6초. 합계 6.9초.
/// 기본 수치(맵): 대기 0.3, 낙하 0.6·높이 12, 여운 0.4, 디졸브 1, 레터박스 아웃 0.5, 줌 1.8 → 1 / 2.6초. 합계 2.8초.
/// </summary>
public sealed class PodDropTimelineTests
{
    const float Eps = 1e-4f;
    const int Walkers = 3;

    static PodDropLobbyTimeline Lobby(int walkers = Walkers) => new PodDropLobbyTimeline(PodDropLobbyTimings.Default, walkers);
    static PodDropMapTimeline Map() => new PodDropMapTimeline(PodDropMapTimings.Default);

    // ─── 로비: 경계 · 합계 ───────────────────────────────────────────────

    [Test]
    public void Lobby_Boundaries_Default()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0.5f, t.WalkAt, Eps, "레터박스 인 끝 = 첫 사람 출발");
        Assert.AreEqual(0.5f + 0.8f + 3f, t.HatchCloseAt, Eps, "마지막(P3) 출발 0.5+0.4×2, 도착 +3");
        Assert.AreEqual(t.HatchCloseAt, t.DescendAt, Eps, "해치 기본 0 → 강하가 바로");
        Assert.AreEqual(t.DescendAt + 2f, t.FadeOutAt, Eps);
        Assert.AreEqual(6.9f, t.LoadAt, Eps, "로비 합계 6.9초");
    }

    [Test]
    public void Lobby_DefaultTotal_ShortWithWalk()
    {
        // D4 '전체를 짧게' — 보행 포함 로비 8~10초 이내.
        Assert.LessOrEqual(Lobby().LoadAt, 8f);
        Assert.Greater(Lobby().LoadAt, Lobby().WalkSeconds, "보행이 시간표 안에 들어 있다");
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
        var t = new PodDropLobbyTimeline(s, Walkers);

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
        var t = new PodDropLobbyTimeline(default, Walkers);
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
        var t = new PodDropLobbyTimeline(s, Walkers);
        Assert.AreEqual(t.HatchCloseAt, t.DescendAt, Eps);
        Assert.AreEqual(t.WalkAt + 3f, t.HatchCloseAt, Eps, "간격 0 → 전원 동시 출발");
    }

    // ─── 로비: 보행 ──────────────────────────────────────────────────────

    [Test]
    public void Lobby_Walk_SlotsStaggeredAndProgressLinear()
    {
        PodDropLobbyTimeline t = Lobby();
        Assert.AreEqual(0.5f, t.WalkStartAt(0), Eps);
        Assert.AreEqual(0.9f, t.WalkStartAt(1), Eps);
        Assert.AreEqual(1.3f, t.WalkStartAt(2), Eps);
        Assert.AreEqual(4.3f, t.WalkEndAt(2), Eps, "마지막 도착 = 보행 단계 끝");
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
        Assert.AreEqual(one.WalkAt + 3f, one.HatchCloseAt, Eps, "1인 = 간격 없음");
        Assert.AreEqual(Lobby().LoadAt - 0.8f, one.LoadAt, Eps);

        PodDropLobbyTimeline none = Lobby(0);
        Assert.AreEqual(0f, none.DurationOf(PodDropLobbyPhase.Walk), Eps, "0인 = 보행 단계 길이 0");
        Assert.AreEqual(PodDropLobbyPhase.Descend, none.PhaseAt(none.WalkAt));
        Assert.AreEqual(0, Lobby(-2).WalkerCount);
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
            Assert.AreEqual(Lobby().LoadAt, settings.CreateLobbyTimeline(Walkers).LoadAt, Eps);
            Assert.AreEqual(Map().ReleaseAt, settings.CreateMapTimeline().ReleaseAt, Eps);
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }
}
