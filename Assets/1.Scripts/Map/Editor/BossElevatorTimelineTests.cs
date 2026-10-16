using NUnit.Framework;

/// <summary>
/// 보스 엘리베이터 순수 시간표(<see cref="BossElevatorTimeline"/>) — 단계 시각, 이징 끝점·중간값, 경과 시간 → 단계·높이·상승 속도·문 포즈·페이드 알파·
/// 슬롯별 보행(늦은 합류 포함).
/// 기본 수치: 높이 12(테스트 고정 — 설정 기본은 0 = 자동), 문 앞 페이드 아웃 0.4·암전 유지 0.2·페이드 인 0.5, 하강 지연 0(문 앞 이동 순간 시작), 하강 2.5,
/// 문 열림 1.76·닫힘 1.68(클립 길이), 보행 시작 지연 0.2·간격 0.6·속도 3, 보행 뒤 닫힘 0.5, 상승 가속 2.6·속도 14, 페이드 지연 1·길이 1.6.
/// 보행 거리는 테스트 고정 6m(= 2초), 3인.
/// </summary>
public sealed class BossElevatorTimelineTests
{
    const float Eps = 1e-4f;
    const float ClipOpen = 1.76f;
    const float ClipClose = 1.68f;
    const float Speed = 14f;
    const float Accel = 2.6f;
    const int Walkers = 3;
    const float WalkDistance = 6f; // 속도 3 → 2초
    // ∫₀¹ InCirc = 1 - π/4
    static readonly float InCircArea = 1f - (float)System.Math.PI / 4f;

    // 기본 설정의 높이는 0(자동 — 리그가 칸 대기 높이로 채운다)이라 시간표 테스트는 12 로 고정한다.
    static BossElevatorTimingSettings Settings()
    {
        BossElevatorTimingSettings s = BossElevatorTimingSettings.Default;
        s.height = 12f;
        return s;
    }

    static BossElevatorTimeline Default() =>
        new BossElevatorTimeline(Settings(), ClipOpen, ClipClose, Walkers, WalkDistance);

    // ─── 기본값(할 일 46 — 하강·상승 느리게) ─────────────────────────────

    [Test]
    public void Defaults_SlowerThanBefore()
    {
        BossElevatorTimingSettings s = BossElevatorTimingSettings.Default;
        Assert.AreEqual(2.5f, s.descendDuration, Eps, "하강 1.2 → 2.1 → 2.5초(할 일 46 후속 7)");
        Assert.AreEqual(2.6f, s.ascendDuration, Eps, "상승 가속 1.5 → 2.6초");
        Assert.AreEqual(14f, s.ascendSpeed, Eps, "상승 속도 24 → 14m/s");
        // 상승 시작 → 암전까지 보이는 시간: 이전 0 + 1.5 = 1.5초 → 1 + 1.6 = 2.6초(약 1.7배).
        Assert.AreEqual(2.6f, s.fadeOutDelay + s.fadeOutDuration, Eps);
        Assert.AreEqual(0f, s.descendDelay, Eps, "하강은 문 앞 이동 암전 순간 시작(할 일 46 후속 5)");
    }

    // ─── 높이 자동 ─────────────────────────────────────────────────────────

    [Test]
    public void Height_ZeroOrLess_UsesIdleAboveLanded()
    {
        BossElevatorTimingSettings s = BossElevatorTimingSettings.Default;
        Assert.AreEqual(0f, s.height, Eps, "기본 = 자동");
        Assert.AreEqual(20.14f, s.WithResolvedHeight(20.69f - 0.55f).height, Eps, "대기 y 20.69(ZoneS_typeBossEnter 배치) − 착지 0.55");
        s.height = -1f;
        Assert.AreEqual(3f, s.WithResolvedHeight(3f).height, Eps, "음수도 자동");
        Assert.AreEqual(0f, s.WithResolvedHeight(-2f).height, Eps, "대기가 착지보다 낮으면 0");

        var t = new BossElevatorTimeline(BossElevatorTimingSettings.Default.WithResolvedHeight(10.44f), ClipOpen, ClipClose);
        Assert.AreEqual(10.44f, t.HeightAt(0.0), Eps, "재생 시작 = 칸 대기 자리(순간이동 없음)");
    }

    [Test]
    public void Height_Positive_KeepsSetting()
    {
        Assert.AreEqual(12f, Settings().WithResolvedHeight(3f).height, Eps);
    }

    // ─── 단계 시각 ───────────────────────────────────────────────────────

    [Test]
    public void PhaseStarts_DefaultSettings()
    {
        BossElevatorTimeline t = Default();
        const float fadeInEnd = 0.4f + 0.2f + 0.5f;
        const float descend = 0.4f;
        const float doorOpen = descend + 2.5f;
        const float walkIn = doorOpen + ClipOpen;
        const float lastWalkEnd = walkIn + 0.2f + 2 * 0.6f + 2f;
        const float doorClose = lastWalkEnd + 0.5f;
        const float ascend = doorClose + ClipClose;

        Assert.AreEqual(0f, t.StartOf(BossElevatorPhase.FrontFadeOut), Eps, "카운트다운 끝 = 연출 시작·입력 잠금");
        Assert.AreEqual(0.4f, t.StartOf(BossElevatorPhase.WaitDescend), Eps, "페이드 아웃 0.4 끝 = 문 앞 순간이동");
        Assert.AreEqual(0.4f, t.TeleportFrontAt, Eps);
        Assert.AreEqual(0.6f, t.FrontFadeInAt, Eps, "암전 유지 0.2");
        Assert.AreEqual(fadeInEnd, t.FrontFadeInEndAt, Eps, "페이드 인 0.5 끝 = 보행 지시");
        Assert.AreEqual(descend, t.StartOf(BossElevatorPhase.Descending), Eps, "문 앞 이동 + 하강 지연 0 — 암전 중 하강 시작");
        Assert.AreEqual(doorOpen, t.StartOf(BossElevatorPhase.DoorOpening), Eps, "하강 2.5 끝");
        Assert.AreEqual(walkIn, t.StartOf(BossElevatorPhase.WalkingIn), Eps, "Open 클립 길이 뒤");
        Assert.AreEqual(doorClose, t.StartOf(BossElevatorPhase.DoorClosing), Eps, "마지막 사람 도착 + 0.5");
        Assert.AreEqual(ascend, t.StartOf(BossElevatorPhase.Ascending), Eps, "Close 끝 뒤");
        Assert.AreEqual(ascend + 1f, t.StartOf(BossElevatorPhase.FadingOut), Eps, "페이드 지연 1");
        Assert.AreEqual(ascend + 2.6f, t.StartOf(BossElevatorPhase.Blackout), Eps, "페이드 1.6");
        Assert.AreEqual(1.6f, t.FadeOutDuration, Eps);
    }

    [Test]
    public void CameraFocus_SwitchesInFrontBlackout_NotAfterFadeIn()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(t.TeleportFrontAt, t.CameraFocusAt, Eps, "카메라 전환 = 문 앞 이동 순간(암전)");
        Assert.AreEqual(1f, t.FadeAlphaAt(t.CameraFocusAt), Eps, "전환 순간은 완전 암전 — 컷이 보이지 않는다");
        Assert.AreEqual(BossElevatorPhase.Descending, t.PhaseAt(t.CameraFocusAt), "하강 지연 0 — 카메라 전환과 같은 순간 하강");
        Assert.Less(t.CameraFocusAt, t.FrontFadeInAt, "페이드 인이 시작될 때 이미 엘리베이터를 보고 있다");
        Assert.AreEqual(t.TeleportFrontAt, t.DescendStartAt, Eps, "하강은 문 앞 이동(암전) 기준 — 페이드 인과 무관");
    }

    [Test]
    public void FrontFadeIn_CabAlreadyDescending()
    {
        BossElevatorTimeline t = Default();
        Assert.LessOrEqual(t.DescendStartAt, t.CameraFocusAt, "하강 시작 ≤ 카메라 전환(+0.4)");
        Assert.Less(t.DescendStartAt, t.FrontFadeInAt, "하강 시작 < 페이드 인 시작(+0.6)");
        Assert.AreEqual(1f, t.FadeAlphaAt(t.DescendStartAt), Eps, "하강 출발은 완전 암전 속 — 보이지 않는다");

        float atFadeIn = t.HeightAt(t.FrontFadeInAt);
        Assert.Greater(atFadeIn, 0f, "페이드 인 시작 = 아직 착지 전");
        Assert.Less(atFadeIn, t.Height, "페이드 인 시작 = 이미 대기 높이에서 내려오는 중");
        Assert.AreEqual(BossElevatorPhase.Descending, t.PhaseAt(t.FrontFadeInAt));

        float atFadeInEnd = t.HeightAt(t.FrontFadeInEndAt);
        Assert.Greater(atFadeInEnd, 0f, "다 밝아진 순간에도 아직 움직이는 중");
        Assert.Less(atFadeInEnd, atFadeIn);
    }

    [Test]
    public void DescendDelay_CountsFromFrontTeleport_AndShiftsLaterSteps()
    {
        BossElevatorTimeline baseline = Default();
        BossElevatorTimingSettings s = Settings();
        s.descendDelay = 0.1f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose, Walkers, WalkDistance);
        Assert.AreEqual(t.TeleportFrontAt + 0.1f, t.DescendStartAt, Eps, "문 앞 이동 + 0.1");
        Assert.AreEqual(BossElevatorPhase.WaitDescend, t.PhaseAt(t.TeleportFrontAt), "지연 동안 = 하강 대기(암전)");
        Assert.AreEqual(t.Height, t.HeightAt(t.DescendStartAt - 0.01), Eps);
        Assert.AreEqual(baseline.FrontFadeInEndAt, t.FrontFadeInEndAt, Eps, "페이드 인은 지연과 무관");
        Assert.AreEqual(t.DescendStartAt + 2.5f, t.DoorOpenAt, Eps, "이후 단계는 하강 끝 기준");
        Assert.AreEqual(0.1f, t.WalkStartAt(0) - baseline.WalkStartAt(0), Eps, "보행·닫힘·상승도 같이 밀린다");
        Assert.AreEqual(0.1f, t.BlackoutAt - baseline.BlackoutAt, Eps);
    }

    [Test]
    public void CameraFollowsCab_FromFocusThroughWholeElevator()
    {
        BossElevatorTimeline t = Default();
        Assert.IsFalse(t.CameraFollowsCabAt(0.0), "연출 시작(페이드 아웃) = 플레이어");
        Assert.IsFalse(t.CameraFollowsCabAt(t.CameraFocusAt - 0.01), "+0.4 직전 = 플레이어");
        Assert.IsTrue(t.CameraFollowsCabAt(t.CameraFocusAt), "+0.4(암전) = 칸");
        Assert.AreEqual(0.4f, t.CameraFocusAt, Eps);
        // 착지 중앙 고정 단계 없음 — 하강·문 열림·보행·닫힘·상승·암전 내내 칸.
        foreach (BossElevatorPhase phase in new[]
                 {
                     BossElevatorPhase.WaitDescend, BossElevatorPhase.Descending,
                     BossElevatorPhase.DoorOpening, BossElevatorPhase.WalkingIn, BossElevatorPhase.DoorClosing,
                     BossElevatorPhase.Ascending, BossElevatorPhase.FadingOut, BossElevatorPhase.Blackout,
                 })
        {
            Assert.IsTrue(t.CameraFollowsCabAt(t.StartOf(phase)), $"{phase} 시작 = 칸");
        }
        Assert.IsTrue(t.CameraFollowsCabAt(t.BlackoutAt + 10.0), "도착 RPC 전까지 칸 — 복귀는 호출자");
    }

    [Test]
    public void PhaseAt_BoundaryBelongsToNextPhase()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(BossElevatorPhase.FrontFadeOut, t.PhaseAt(-1.0), "시작 전 = 첫 단계");
        Assert.AreEqual(BossElevatorPhase.FrontFadeOut, t.PhaseAt(0.0));
        Assert.AreEqual(BossElevatorPhase.FrontFadeOut, t.PhaseAt(0.39));
        Assert.AreEqual(BossElevatorPhase.Descending, t.PhaseAt(t.TeleportFrontAt), "하강 대기 0 길이는 건너뛴다");
        Assert.AreEqual(BossElevatorPhase.Descending, t.PhaseAt(t.FrontFadeInEndAt), "페이드 인 끝도 하강 중");
        Assert.AreEqual(BossElevatorPhase.Descending, t.PhaseAt(t.DescendStartAt));
        Assert.AreEqual(BossElevatorPhase.DoorOpening, t.PhaseAt(t.DoorOpenAt));
        Assert.AreEqual(BossElevatorPhase.WalkingIn, t.PhaseAt(t.WalkInAt));
        Assert.AreEqual(BossElevatorPhase.WalkingIn, t.PhaseAt(t.WalkEndAt(Walkers - 1)), "마지막 도착 뒤 닫힘 대기도 WalkingIn");
        Assert.AreEqual(BossElevatorPhase.DoorClosing, t.PhaseAt(t.DoorCloseAt));
        Assert.AreEqual(BossElevatorPhase.Ascending, t.PhaseAt(t.AscendStartAt));
        Assert.AreEqual(BossElevatorPhase.FadingOut, t.PhaseAt(t.FadeOutStartAt));
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(t.BlackoutAt));
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(t.BlackoutAt + 100.0), "Blackout 은 끝이 없다");
    }

    [Test]
    public void DoorDurations_SettingOverridesClipLength()
    {
        BossElevatorTimingSettings s = Settings();
        s.doorOpenDuration = 1f;
        s.doorCloseDuration = 2f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose, Walkers, WalkDistance);
        Assert.AreEqual(1f, t.DoorOpenDuration, Eps);
        Assert.AreEqual(2f, t.DoorCloseDuration, Eps);
        Assert.AreEqual(t.DoorOpenAt + 1f, t.WalkInAt, Eps);
        Assert.AreEqual(t.DoorCloseAt + 2f, t.AscendStartAt, Eps);
    }

    [Test]
    public void FadeOutDelay_ShiftsFadeAndBlackout()
    {
        BossElevatorTimingSettings s = Settings();
        s.fadeOutDelay = 0.4f;
        s.fadeOutDuration = 2f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose, Walkers, WalkDistance);
        Assert.AreEqual(t.AscendStartAt + 0.4f, t.FadeOutStartAt, Eps);
        Assert.AreEqual(t.AscendStartAt + 2.4f, t.BlackoutAt, Eps);
        Assert.AreEqual(BossElevatorPhase.Ascending, t.PhaseAt(t.AscendStartAt + 0.2));
        Assert.AreEqual(BossElevatorPhase.FadingOut, t.PhaseAt(t.FadeOutStartAt));
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(t.BlackoutAt));
    }

    // ─── 보행(P1 → P2 → P3) ──────────────────────────────────────────────

    [Test]
    public void Walk_SlotsStartInOrderAtInterval()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(3, t.WalkerCount);
        Assert.AreEqual(2f, t.WalkDuration, Eps, "거리 6 ÷ 속도 3");
        Assert.AreEqual(t.WalkInAt + 0.2f, t.WalkStartAt(0), Eps, "문이 다 열리고 0.2초 뒤 P1");
        Assert.AreEqual(t.WalkStartAt(0) + 0.6f, t.WalkStartAt(1), Eps, "P2 = P1 + 간격");
        Assert.AreEqual(t.WalkStartAt(0) + 1.2f, t.WalkStartAt(2), Eps, "P3 = P1 + 간격 × 2");
        Assert.AreEqual(t.WalkStartAt(2) + 2f, t.WalkEndAt(2), Eps);
        Assert.AreEqual(t.WalkEndAt(2) + 0.5f, t.DoorCloseAt, Eps, "마지막(P3) 도착 + 0.5 = 문 닫힘");
        Assert.Less(t.WalkEndAt(2), t.DoorCloseAt, "전원 도착 뒤에 문이 닫힌다");
    }

    [Test]
    public void Walk_ProgressIsLinear()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(0f, t.WalkProgressAt(1, t.WalkStartAt(1) - 0.01), Eps, "시작 전 = 문 앞");
        Assert.AreEqual(0f, t.WalkProgressAt(1, t.WalkStartAt(1)), Eps);
        Assert.AreEqual(0.5f, t.WalkProgressAt(1, t.WalkStartAt(1) + 1.0), Eps, "등속");
        Assert.AreEqual(1f, t.WalkProgressAt(1, t.WalkEndAt(1)), Eps);
        Assert.AreEqual(1f, t.WalkProgressAt(1, t.DoorCloseAt), Eps, "도착 뒤 = 칸 안 고정");
    }

    [Test]
    public void Walk_DoorStaysOpenWhileWalking()
    {
        BossElevatorTimeline t = Default();
        for (int slot = 0; slot < Walkers; slot++)
        {
            Assert.AreEqual(BossElevatorDoorState.Open, t.DoorPoseAt(t.WalkStartAt(slot), out _), $"P{slot + 1} 출발");
            Assert.AreEqual(BossElevatorDoorState.Open, t.DoorPoseAt(t.WalkEndAt(slot), out _), $"P{slot + 1} 도착");
            Assert.AreEqual(0f, t.HeightAt(t.WalkStartAt(slot)), Eps, "칸은 착지해 있다");
        }
    }

    [Test]
    public void Walk_SoloPlayer_ClosesSooner()
    {
        var solo = new BossElevatorTimeline(Settings(), ClipOpen, ClipClose, 1, WalkDistance);
        BossElevatorTimeline trio = Default();
        Assert.AreEqual(solo.WalkEndAt(0) + 0.5f, solo.DoorCloseAt, Eps);
        Assert.AreEqual(1.2f, trio.DoorCloseAt - solo.DoorCloseAt, Eps, "남은 두 사람 몫(간격 × 2)만큼 빠르다");
        Assert.AreEqual(trio.FrontFadeInEndAt, solo.FrontFadeInEndAt, Eps, "보행 앞 단계는 인원과 무관");
    }

    [Test]
    public void Walk_NoWalkers_ClosesAfterDelayOnly()
    {
        var t = new BossElevatorTimeline(Settings(), ClipOpen, ClipClose, 0, WalkDistance);
        Assert.AreEqual(t.WalkInAt + 0.5f, t.DoorCloseAt, Eps, "아무도 안 걸으면 보행 시작 지연·보행 시간 없이 닫힘 대기만");
    }

    // ─── 이징 ─────────────────────────────────────────────────────────────

    [Test]
    public void Easing_Endpoints()
    {
        Assert.AreEqual(0f, BossElevatorTimeline.EaseOutQuint(0f), Eps);
        Assert.AreEqual(1f, BossElevatorTimeline.EaseOutQuint(1f), Eps);
        Assert.AreEqual(0f, BossElevatorTimeline.EaseInCirc(0f), Eps);
        Assert.AreEqual(1f, BossElevatorTimeline.EaseInCirc(1f), Eps);
    }

    [Test]
    public void Easing_Midpoints()
    {
        // OutQuint(0.5) = 1 - 0.5^5 = 0.96875 — 빨리 내려와 천천히 멈춘다.
        Assert.AreEqual(0.96875f, BossElevatorTimeline.EaseOutQuint(0.5f), Eps);
        // InCirc(0.5) = 1 - sqrt(0.75) ≈ 0.133975 — 천천히 출발해 가속한다.
        Assert.AreEqual(1f - (float)System.Math.Sqrt(0.75), BossElevatorTimeline.EaseInCirc(0.5f), Eps);
    }

    // ─── 높이 ─────────────────────────────────────────────────────────────

    [Test]
    public void Height_DescentAndDoors()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(12f, t.HeightAt(0.0), Eps, "시작 = 위에서 대기");
        Assert.AreEqual(12f, t.HeightAt(t.TeleportFrontAt - 0.01), Eps, "문 앞 이동 전(페이드 아웃) 동안도 위");
        Assert.AreEqual(12f, t.HeightAt(t.DescendStartAt), Eps, "하강 시작");
        Assert.AreEqual(12f * (1f - 0.96875f), t.HeightAt(t.DescendStartAt + 2.5 * 0.5), Eps, "하강 중간 = OutQuint");
        Assert.AreEqual(0f, t.HeightAt(t.DoorOpenAt), Eps, "착지");
        Assert.AreEqual(0f, t.HeightAt(t.WalkInAt + 0.1), Eps);
        Assert.AreEqual(0f, t.HeightAt(t.AscendStartAt), Eps, "상승 시작");
    }

    [Test]
    public void Ascent_AcceleratesThenKeepsSpeed_NoCeiling()
    {
        BossElevatorTimeline t = Default();
        double a = t.AscendStartAt;
        float accelDistance = Speed * Accel * InCircArea;
        Assert.AreEqual(accelDistance, t.HeightAt(a + Accel), 1e-3f, "가속 끝 = 속도 × 가속 시간 × (1 - π/4)");
        Assert.AreEqual(accelDistance + Speed * 1f, t.HeightAt(a + Accel + 1.0), 1e-3f, "가속 뒤 등속");
        Assert.AreEqual(accelDistance + Speed * 100f, t.HeightAt(a + Accel + 100.0), 1e-1f, "Blackout 뒤에도 상한 없음");
        Assert.Less(t.HeightAt(a + Accel * 0.5), accelDistance * 0.5f, "가속 전반은 느리다(InCirc)");
    }

    [Test]
    public void Ascent_SpeedFollowsInCircAndIsContinuous()
    {
        BossElevatorTimeline t = Default();
        double a = t.AscendStartAt;
        Assert.AreEqual(0f, t.AscendSpeedAt(a - 0.1), Eps, "상승 전 0");
        Assert.AreEqual(0f, t.AscendSpeedAt(a), Eps, "정지에서 출발");
        Assert.AreEqual(Speed * BossElevatorTimeline.EaseInCirc(0.5f), t.AscendSpeedAt(a + Accel * 0.5), Eps);
        Assert.AreEqual(Speed, t.AscendSpeedAt(a + Accel + 10.0), Eps, "가속 뒤 유지");

        // 높이의 수치 미분이 속도와 맞는다(가속 중간·가속 끝 직후).
        const double h = 1e-3;
        foreach (double s in new[] { Accel * 0.5, Accel * 0.9, Accel + 0.5 })
        {
            float numeric = (float)((t.HeightAt(a + s + h) - t.HeightAt(a + s - h)) / (2 * h));
            Assert.AreEqual(t.AscendSpeedAt(a + s), numeric, 0.1f, $"s={s}");
        }
    }

    [Test]
    public void Ascent_ZeroAccel_IsConstantSpeed()
    {
        BossElevatorTimingSettings s = Settings();
        s.ascendDuration = 0f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose, Walkers, WalkDistance);
        Assert.AreEqual(Speed, t.AscendSpeedAt(t.AscendStartAt), Eps);
        Assert.AreEqual(Speed * 2f, t.HeightAt(t.AscendStartAt + 2.0), 1e-3f);
    }

    // ─── 페이드 ───────────────────────────────────────────────────────────

    [Test]
    public void FrontFade_OutHoldIn_IsOneContinuousDip()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(0f, t.FadeAlphaAt(-0.5), Eps, "시작 전(카운트다운) 투명");
        Assert.AreEqual(0f, t.FadeAlphaAt(0.0), Eps, "카운트다운 끝 = 투명에서 출발(번쩍임 없음)");
        Assert.AreEqual(0.5f, t.FadeAlphaAt(0.2), Eps, "페이드 아웃 중간");
        Assert.AreEqual(1f, t.FadeAlphaAt(t.TeleportFrontAt), Eps, "순간이동 순간 = 완전 암전");
        Assert.AreEqual(1f, t.FadeAlphaAt(t.FrontFadeInAt - 0.01), Eps, "암전 유지");
        Assert.AreEqual(0.5f, t.FadeAlphaAt(t.FrontFadeInAt + 0.25), Eps, "페이드 인 중간");
        Assert.AreEqual(0f, t.FadeAlphaAt(t.FrontFadeInEndAt), Eps, "페이드 인 끝 = 다 밝아짐");

        // 0 → 1 → 0 이 한 번뿐 — 올라가다 내려가는 지점이 하나다(중간에 다시 어두워지지 않는다).
        float prev = 0f;
        int turns = 0;
        bool rising = true;
        for (double e = 0.0; e <= t.FrontFadeInEndAt + 1e-6; e += 0.01)
        {
            float a = t.FadeAlphaAt(e);
            if (rising && a < prev - 1e-5f) { rising = false; turns++; }
            else if (!rising && a > prev + 1e-5f) turns++;
            prev = a;
        }
        Assert.AreEqual(1, turns, "어두워졌다 밝아지는 꺾임은 한 번");
    }

    [Test]
    public void FadeAlpha_TransparentFromFrontFadeInEndUntilAscentFade()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(0f, t.FadeAlphaAt(t.FrontFadeInEndAt), Eps, "하강 중 다 밝아짐");
        Assert.AreEqual(0f, t.FadeAlphaAt(t.DoorOpenAt), Eps);
        Assert.AreEqual(0f, t.FadeAlphaAt(t.WalkStartAt(1)), Eps, "걷는 동안 투명");
        Assert.AreEqual(0f, t.FadeAlphaAt(t.DoorCloseAt), Eps, "상승 전 투명");
        Assert.AreEqual(0f, t.FadeAlphaAt(t.FadeOutStartAt), Eps);
        Assert.AreEqual(0.5f, t.FadeAlphaAt(t.FadeOutStartAt + 0.8), Eps);
        Assert.AreEqual(1f, t.FadeAlphaAt(t.BlackoutAt), Eps, "Blackout = 완전 암전");
        Assert.AreEqual(1f, t.FadeAlphaAt(t.BlackoutAt + 5.0), Eps);
    }

    [Test]
    public void FadeAlpha_ZeroDuration_JumpsAtBlackout()
    {
        BossElevatorTimingSettings s = Settings();
        s.fadeOutDelay = 0.5f;
        s.fadeOutDuration = 0f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose, Walkers, WalkDistance);
        Assert.AreEqual(t.FadeOutStartAt, t.BlackoutAt, Eps);
        Assert.AreEqual(0f, t.FadeAlphaAt(t.BlackoutAt - 0.01), Eps);
        Assert.AreEqual(1f, t.FadeAlphaAt(t.BlackoutAt), Eps);
    }

    [Test]
    public void FrontFade_ZeroDurations_NoBlackFrame()
    {
        BossElevatorTimingSettings s = Settings();
        s.frontFadeOutDuration = 0f;
        s.frontBlackHold = 0f;
        s.frontFadeInDuration = 0f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose, Walkers, WalkDistance);
        Assert.AreEqual(0f, t.TeleportFrontAt, Eps);
        Assert.AreEqual(0f, t.CameraFocusAt, Eps, "곧바로 카메라 전환");
        Assert.AreEqual(0f, t.FrontFadeInEndAt, Eps, "곧바로 다 밝아짐");
        Assert.AreEqual(BossElevatorPhase.Descending, t.PhaseAt(0.0), "0 길이 단계는 건너뛰고 곧바로 하강");
        Assert.AreEqual(0f, t.FadeAlphaAt(0.0), Eps, "암전 없음");
    }

    // ─── 늦은 합류 ────────────────────────────────────────────────────────

    [Test]
    public void LateJoin_MidDoorOpen_ResumesClipFromMiddle()
    {
        BossElevatorTimeline t = Default();
        double elapsed = t.DoorOpenAt + ClipOpen * 0.25;
        Assert.AreEqual(BossElevatorPhase.DoorOpening, t.PhaseAt(elapsed));
        Assert.AreEqual(0f, t.HeightAt(elapsed), Eps);
        Assert.AreEqual(BossElevatorDoorState.Opening, t.DoorPoseAt(elapsed, out float n));
        Assert.AreEqual(0.25f, n, Eps);
    }

    [Test]
    public void LateJoin_MidWalk_SlotsInDifferentProgress()
    {
        BossElevatorTimeline t = Default();
        double elapsed = t.WalkStartAt(1) + 1.0; // P2 절반
        Assert.AreEqual(BossElevatorPhase.WalkingIn, t.PhaseAt(elapsed));
        Assert.AreEqual(0.8f, t.WalkProgressAt(0, elapsed), Eps, "P1 은 1.6초째");
        Assert.AreEqual(0.5f, t.WalkProgressAt(1, elapsed), Eps, "P2 는 1초째");
        Assert.AreEqual(0.2f, t.WalkProgressAt(2, elapsed), Eps, "P3 는 0.4초째");
    }

    [Test]
    public void LateJoin_DoorPoseAcrossPhases()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(BossElevatorDoorState.Closed, t.DoorPoseAt(t.DescendStartAt + 1.0, out _), "하강 중 = 닫힘");
        Assert.AreEqual(BossElevatorDoorState.Open, t.DoorPoseAt(t.WalkInAt + 1.0, out float open));
        Assert.AreEqual(1f, open, Eps);
        Assert.AreEqual(BossElevatorDoorState.Closing, t.DoorPoseAt(t.DoorCloseAt + ClipClose * 0.5, out float closing));
        Assert.AreEqual(0.5f, closing, Eps);
        Assert.AreEqual(BossElevatorDoorState.Closed, t.DoorPoseAt(t.AscendStartAt + 0.1, out _), "상승 중 = 닫힘");
    }

    [Test]
    public void LateJoin_AfterBlackout_IsBlackAndClosed()
    {
        BossElevatorTimeline t = Default();
        double elapsed = t.BlackoutAt + 30.0;
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(elapsed));
        Assert.AreEqual(1f, t.FadeAlphaAt(elapsed), Eps);
        Assert.AreEqual(BossElevatorDoorState.Closed, t.DoorPoseAt(elapsed, out _));
    }

    [Test]
    public void ZeroDurations_DoNotDivideByZero()
    {
        var s = new BossElevatorTimingSettings { height = 5f, doorOpenDuration = 0f, doorCloseDuration = 0f };
        var t = new BossElevatorTimeline(s, 0f, 0f, Walkers, WalkDistance);
        Assert.AreEqual(0f, t.WalkDuration, Eps, "속도 0 = 보행 시간 0(나눗셈 없음)");
        Assert.AreEqual(0f, t.BlackoutAt, Eps);
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(0.0));
        Assert.AreEqual(0f, t.HeightAt(0.0), Eps, "속도 0 = 착지 자리");
        Assert.AreEqual(1f, t.FadeAlphaAt(0.0), Eps);
        Assert.AreEqual(1f, t.WalkProgressAt(2, 0.0), Eps, "0 길이 보행 = 바로 도착");
    }
}
