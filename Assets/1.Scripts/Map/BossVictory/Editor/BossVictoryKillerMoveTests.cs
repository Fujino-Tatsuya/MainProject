using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 처치자 이동(할 일 272) — 순수 함수(<see cref="BossVictoryKillerMoves"/>): 보스 기준 두 점·가까움 판정·이동 구간·정지 프레임·단계 시각·차징 구체 크기,
/// 그리고 정본 프로필 연결(어쌔신 = 순간이동 Q·71프레임, 거너 = 후퇴 차징, 팔라딘 = 백대쉬).
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictory).
/// </summary>
public sealed class BossVictoryKillerMoveTests
{
    const float Tolerance = 1e-4f;
    const string ProfileFolder = "Assets/Resources/BossVictory/Profiles";

    static void AreEqual(Vector3 expected, Vector3 actual, string message)
    {
        Assert.AreEqual(expected.x, actual.x, Tolerance, message + " x");
        Assert.AreEqual(expected.y, actual.y, Tolerance, message + " y");
        Assert.AreEqual(expected.z, actual.z, Tolerance, message + " z");
    }

    // ── 보스 기준 점 ──

    [Test]
    public void 보스_기준_점은_보스_위치_더하기_수평_회전된_오프셋()
    {
        Vector3 boss = new Vector3(10f, 2f, -4f);
        Quaternion rot = Quaternion.Euler(0f, 90f, 0f);   // 보스 정면 = +X
        AreEqual(new Vector3(13f, 2f, -4f), BossVictoryKillerMoves.BossPoint(boss, rot, new Vector3(0f, 0f, 3f)), "정면 3m");
        AreEqual(new Vector3(10f, 2f, -2f), BossVictoryKillerMoves.BossPoint(boss, rot, new Vector3(-2f, 0f, 0f)), "보스 왼쪽(-X) 2m = 월드 +Z");
    }

    [Test]
    public void 보스가_기울어도_수평_회전만_쓴다()
    {
        Quaternion tilted = Quaternion.Euler(30f, 90f, 15f);
        AreEqual(new Vector3(3f, 0f, 0f), BossVictoryKillerMoves.BossPoint(Vector3.zero, tilted, new Vector3(0f, 0f, 3f)), "기울기 무시");
    }

    [Test]
    public void 로컬_오프셋은_보스_기준_점의_역()
    {
        Vector3 boss = new Vector3(1f, 0f, 2f);
        Quaternion rot = Quaternion.Euler(0f, 37f, 0f);
        Vector3 offset = new Vector3(-3f, 0f, 2.5f);
        AreEqual(offset, BossVictoryKillerMoves.BossLocalOffset(boss, rot, BossVictoryKillerMoves.BossPoint(boss, rot, offset)), "왕복");
    }

    // ── 가까움 판정(거너) ──

    [Test]
    public void 가까움_판정은_수평_거리로_하고_같으면_멂()
    {
        Assert.IsTrue(BossVictoryKillerMoves.IsTooClose(new Vector3(0f, 0f, 4.9f), Vector3.zero, 5f), "4.9 < 5");
        Assert.IsFalse(BossVictoryKillerMoves.IsTooClose(new Vector3(0f, 0f, 5f), Vector3.zero, 5f), "같으면 멂");
        Assert.IsTrue(BossVictoryKillerMoves.IsTooClose(new Vector3(3f, 100f, 0f), Vector3.zero, 5f), "높이 차이 무시");
        Assert.IsFalse(BossVictoryKillerMoves.IsTooClose(Vector3.zero, Vector3.zero, 0f), "거리 0 칸 = 늘 멂");
    }

    // ── 이동 구간(어쌔신) ──

    [Test]
    public void 이동_구간_A는_Custom0에서_End()
    {
        BossVictoryKillerMoves.MoveInterval(0.15f, 0.425f, 1.1833f, BossVictoryMoveInterval.Custom0ToEnd, out float start, out float stop);
        Assert.AreEqual(0.15f, start, Tolerance);
        Assert.AreEqual(0.425f, stop, Tolerance);
        Assert.AreEqual(0.275f, stop - start, Tolerance, "결정 A = 0.275초(클립 시간)");
    }

    [Test]
    public void 이동_구간_B는_Custom0에서_정지_프레임()
    {
        BossVictoryKillerMoves.MoveInterval(0.15f, 0.425f, 1.1833f, BossVictoryMoveInterval.Custom0ToFreeze, out float start, out float stop);
        Assert.AreEqual(0.15f, start, Tolerance);
        Assert.AreEqual(1.1833f, stop, Tolerance);
    }

    [Test]
    public void 이벤트가_없으면_구간_대체값()
    {
        BossVictoryKillerMoves.MoveInterval(-1f, -1f, 1f, BossVictoryMoveInterval.Custom0ToEnd, out float start, out float stop);
        Assert.AreEqual(0f, start, Tolerance, "Custom0 없음 = 0");
        Assert.AreEqual(1f, stop, Tolerance, "End 없음 = 정지 프레임");
        BossVictoryKillerMoves.MoveInterval(0.5f, 0.2f, 0f, BossVictoryMoveInterval.Custom0ToFreeze, out start, out stop);
        Assert.AreEqual(0.5f, stop, Tolerance, "끝이 시작보다 앞 = 시작(바로 도착)");
    }

    [Test]
    public void 이동_진행은_구간_밖에서_0과_끝()
    {
        AnimationCurve linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        Assert.AreEqual(0f, BossVictoryKillerMoves.MoveProgress(0.1f, 0.15f, 0.425f, linear), Tolerance, "앞");
        Assert.AreEqual(0.5f, BossVictoryKillerMoves.MoveProgress(0.2875f, 0.15f, 0.425f, linear), Tolerance, "가운데");
        Assert.AreEqual(1f, BossVictoryKillerMoves.MoveProgress(0.9f, 0.15f, 0.425f, linear), Tolerance, "뒤");
        Assert.AreEqual(0.5f, BossVictoryKillerMoves.MoveProgress(0.2875f, 0.15f, 0.425f, null), Tolerance, "곡선 없음 = 선형");
        AnimationCurve easeIn = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
        Assert.Less(BossVictoryKillerMoves.MoveProgress(0.2875f, 0.15f, 0.425f, easeIn), 0.5f, "곡선 적용");
    }

    // ── 정지 프레임 ──

    [Test]
    public void 정지_프레임_71은_60fps_1_1833초()
    {
        float seconds = BossVictoryKillerMoves.FreezeSeconds(BossVictoryKillerMoves.DefaultFreezeFrame, 60f);
        Assert.AreEqual(71f / 60f, seconds, Tolerance);
        Assert.AreEqual(71, BossVictoryAnimTrim.Frames(seconds, 60f), "끝 자르기 프레임으로 다시 읽어도 71");
        Assert.AreEqual(seconds, BossVictoryAnimTrim.ClampTrim(seconds, 1.9166667f, 60f), Tolerance, "끝 자르기로 저장 가능(클립 1.917초 안)");
        Assert.IsTrue(BossVictoryAnimTrim.ShouldFreeze(seconds, 1.19f), "그 프레임 지나면 멈춤");
    }

    // ── 단계 시각 ──

    [Test]
    public void 백대쉬와_순간이동은_마커_그대로()
    {
        var tl = BossVictoryTimeline.CreateDefault();
        tl.backDashStartTime = 0.2f;
        tl.finisherStartTime = 0.65f;
        foreach (BossVictoryKillerMove move in new[] { BossVictoryKillerMove.BackDash, BossVictoryKillerMove.TeleportDash })
        {
            BossVictoryKillerSequence.Times t = BossVictoryKillerMoves.Times(tl, move, tooClose: true);
            Assert.AreEqual(0.2f, t.BackDashStart, Tolerance, move.ToString());
            Assert.AreEqual(0.65f, t.FinisherStart, Tolerance, move.ToString());
        }
    }

    [Test]
    public void 후퇴_차징_가까우면_백대쉬_뒤_Q_차징_시작_마커()
    {
        var tl = BossVictoryTimeline.CreateDefault();
        tl.backDashStartTime = 0.2f;
        tl.finisherStartTime = 0.65f;
        BossVictoryKillerSequence.Times t = BossVictoryKillerMoves.Times(tl, BossVictoryKillerMove.RetreatCharge, tooClose: true);
        Assert.AreEqual(0.2f, t.BackDashStart, Tolerance);
        Assert.AreEqual(0.4f, t.FinisherStart, Tolerance, "Q 차징 시작 기본 = 백대쉬 시작 + 0.2");
        Assert.AreEqual(BossVictoryKillerSequence.Step.BackDash, BossVictoryKillerSequence.Target(t, 0.3f));
        Assert.AreEqual(BossVictoryKillerSequence.Step.Finisher, BossVictoryKillerSequence.Target(t, 0.4f));
        tl.SetMarkerTime(BossVictoryMarker.ChargeStart, 0.9f);
        Assert.AreEqual(0.9f, BossVictoryKillerMoves.Times(tl, BossVictoryKillerMove.RetreatCharge, true).FinisherStart, Tolerance, "끌어 고정");
    }

    [Test]
    public void 후퇴_차징_멀면_백대쉬_없이_백대쉬_시작_시각에_Q()
    {
        var tl = BossVictoryTimeline.CreateDefault();
        tl.backDashStartTime = 0.2f;
        BossVictoryKillerSequence.Times t = BossVictoryKillerMoves.Times(tl, BossVictoryKillerMove.RetreatCharge, tooClose: false);
        Assert.AreEqual(0.2f, t.FinisherStart, Tolerance);
        Assert.IsTrue(float.IsPositiveInfinity(t.BackDashStart), "백대쉬 없음");
        Assert.AreNotEqual(BossVictoryKillerSequence.Step.BackDash, BossVictoryKillerSequence.Target(t, 0.19f));
        Assert.AreEqual(BossVictoryKillerSequence.Step.Finisher, BossVictoryKillerSequence.Target(t, 0.2f));
        Assert.IsFalse(BossVictoryKillerSequence.StartDashDuringFinisher(true, BossVictoryKillerSequence.Step.Finisher, false, t.BackDashStart, 10f),
            "Q 중에도 백대쉬가 늦게 시작하지 않는다");
    }

    [Test]
    public void Q_차징_시작_마커_기본은_자동_백대쉬_시작_더하기_0_2()
    {
        var tl = new BossVictoryTimeline { backDashStartTime = 0.5f };
        Assert.Less(tl.chargeStartTime, 0f, "새 칸 = 자동(옛 에셋도 칸이 없어 자동)");
        Assert.AreEqual(0.7f, tl.MarkerTime(BossVictoryMarker.ChargeStart), Tolerance);
        Assert.IsFalse(BossVictoryTimeline.ZeroMeansOff(BossVictoryMarker.ChargeStart));
    }

    // ── 차징 구체(거너) ──

    [Test]
    public void 차징_구체는_시작에서_목표_크기로_발사_순간_목표()
    {
        AnimationCurve linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        Assert.AreEqual(0.5f, BossVictoryKillerMoves.ChargeScale(0.5f, 2f, linear, 0f), Tolerance, "시작");
        Assert.AreEqual(1.25f, BossVictoryKillerMoves.ChargeScale(0.5f, 2f, linear, 0.5f), Tolerance, "가운데");
        Assert.AreEqual(2f, BossVictoryKillerMoves.ChargeScale(0.5f, 2f, linear, 1f), Tolerance, "최대 충전 = 목표");
        Assert.AreEqual(2f, BossVictoryKillerMoves.ChargeScale(0.5f, 2f, linear, 3f), Tolerance, "넘쳐도 목표");
        Assert.AreEqual(1.25f, BossVictoryKillerMoves.ChargeScale(0.5f, 2f, null, 0.5f), Tolerance, "곡선 없음 = 선형");
    }

    // ── 프로필 기본값·정본 연결 ──

    [Test]
    public void 새_프로필_기본은_백대쉬_순간이동_칸은_기본_상태()
    {
        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            Assert.AreEqual(BossVictoryKillerMove.BackDash, p.killerMove, "기본 = 지금 흐름");
            Assert.AreEqual(BossVictoryKillerMoves.DefaultTeleportMoveState, p.teleportMoveState);
            Assert.AreEqual(BossVictoryMoveInterval.Custom0ToEnd, p.teleportMoveInterval, "결정 A");
            Assert.AreEqual(PlayerSkillSlot.Sub, p.retreatSkillSlot, "후퇴 스킬 = E");
        }
        finally { Object.DestroyImmediate(p); }
    }

    [Test]
    public void 정본_프로필_처치자_이동()
    {
        var paladin = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>($"{ProfileFolder}/BossVictoryProfile_Paladin.asset");
        var gunner = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>($"{ProfileFolder}/BossVictoryProfile_Gunner.asset");
        var assassin = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>($"{ProfileFolder}/BossVictoryProfile_Assassin.asset");
        Assert.IsNotNull(paladin);
        Assert.IsNotNull(gunner);
        Assert.IsNotNull(assassin);
        Assert.AreEqual(BossVictoryKillerMove.BackDash, paladin.killerMove, "팔라딘 = 백대쉬");
        Assert.AreEqual(BossVictoryKillerMove.RetreatCharge, gunner.killerMove, "거너 = 후퇴 차징");
        Assert.AreEqual(PlayerSkillSlot.Main, gunner.finisherSlot, "거너 처치 연출 스킬 = Q");
        Assert.AreEqual(BossVictoryKillerMove.TeleportDash, assassin.killerMove, "어쌔신 = 순간이동 Q");
    }

    [Test]
    public void 어쌔신_순간이동_Q_상태_클립과_정지_프레임()
    {
        var assassin = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>($"{ProfileFolder}/BossVictoryProfile_Assassin.asset");
        Assert.IsNotNull(assassin);
        AnimationClip clip = BossVictoryProfileAuthoring.TeleportMoveClip(assassin.teleportMoveState);
        Assert.IsNotNull(clip, $"어쌔신 Animator 에 '{assassin.teleportMoveState}' 상태");
        Assert.AreEqual("Combo_Attack_03_04", clip.name);
        Assert.IsTrue(AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)SkillAnimationEventType.Custom0), "이동 시작 Custom0");
        Assert.IsTrue(AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)SkillAnimationEventType.End), "이동 끝 End");
        Assert.AreEqual(BossVictoryKillerMoves.DefaultFreezeFrame, BossVictoryAnimTrim.Frames(assassin.timeline.finisherTrimSeconds, clip.frameRate),
            "정지 프레임 = 처치 연출 스킬 클립 끝 자르기");
        Assert.LessOrEqual(assassin.timeline.finisherStartTime, assassin.timeline.backDashStartTime, "순간이동 직후 Q");
    }
}
