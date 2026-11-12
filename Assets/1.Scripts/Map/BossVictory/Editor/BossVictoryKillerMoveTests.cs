using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 처치자 이동(할 일 272) — 순수 함수(<see cref="BossVictoryKillerMoves"/>): 보스 기준 두 점·가까움 판정·이동 진행·어쌔신 순간이동·Q 이동 트랙(순서·겹침·진행·애니 배율)·정지 프레임·단계 시각·차징 구체 크기,
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

    // ── 이동 진행(어쌔신) ──

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

    // ── 어쌔신 순간이동·Q 이동 트랙(272 후속) ──

    static List<BossVictoryClip> Dash(params (float start, float length)[] spans)
    {
        var clips = new List<BossVictoryClip>();
        foreach (var s in spans) clips.Add(new BossVictoryClip { start = s.start, length = s.length, weight = 1f });
        return clips;
    }

    static List<int> OrderOf(List<BossVictoryClip> clips)
    {
        var order = new List<int>();
        BossVictoryAssassinDash.Order(clips, order);
        return order;
    }

    [Test]
    public void 순서는_시작_순이고_같으면_목록_순()
    {
        var clips = Dash((0.8f, 0.2f), (0.2f, 0.275f), (0.8f, 0.1f));
        CollectionAssert.AreEqual(new[] { 1, 0, 2 }, OrderOf(clips));
        Assert.AreEqual(2, BossVictoryAssassinDash.Number(clips, 0), "#2");
        Assert.AreEqual(1, BossVictoryAssassinDash.Number(clips, 1), "#1");
        Assert.AreEqual(3, BossVictoryAssassinDash.Number(clips, 2), "#3");
        Assert.AreEqual(0.2f, BossVictoryAssassinDash.FirstStart(clips), Tolerance);
        Assert.IsTrue(float.IsPositiveInfinity(BossVictoryAssassinDash.FirstStart(new List<BossVictoryClip>())), "없으면 무한대");
    }

    [Test]
    public void 겹치면_앞_클립은_다음_클립_시작에서_끝()
    {
        var clips = Dash((0.2f, 0.5f), (0.5f, 0.275f), (1.0f, 0.2f));
        List<int> order = OrderOf(clips);
        Assert.AreEqual(0.5f, BossVictoryAssassinDash.SegmentEnd(clips, order, 0), Tolerance, "#1 = #2 시작에서 잘림");
        Assert.AreEqual(0.775f, BossVictoryAssassinDash.SegmentEnd(clips, order, 1), Tolerance, "#2 = 자기 끝(#3 보다 앞)");
        Assert.AreEqual(1.2f, BossVictoryAssassinDash.SegmentEnd(clips, order, 2), Tolerance, "마지막 = 자기 끝");
    }

    [Test]
    public void 지금_이동과_모두_끝남()
    {
        var clips = Dash((0.2f, 0.275f), (0.8f, 0.275f));
        List<int> order = OrderOf(clips);
        Assert.AreEqual(-1, BossVictoryAssassinDash.Current(clips, order, 0.19f), "아직");
        Assert.AreEqual(0, BossVictoryAssassinDash.Current(clips, order, 0.2f), "#1 시작");
        Assert.AreEqual(0, BossVictoryAssassinDash.Current(clips, order, 0.6f), "#1 끝난 뒤 빈 시간도 #1");
        Assert.AreEqual(1, BossVictoryAssassinDash.Current(clips, order, 0.8f), "#2");
        Assert.IsFalse(BossVictoryAssassinDash.AllDone(clips, order, 0.6f), "빈 시간 = 아직(정지 프레임 안 함)");
        Assert.IsFalse(BossVictoryAssassinDash.AllDone(clips, order, 1.0f));
        Assert.IsTrue(BossVictoryAssassinDash.AllDone(clips, order, 1.075f), "마지막 끝");
        Assert.IsTrue(BossVictoryAssassinDash.AllDone(clips, new List<int>(), 0f), "클립 없음 = 끝");
    }

    [Test]
    public void 이동_진행은_클립_길이_기준이고_잘리면_그_자리에서_멈춤()
    {
        AnimationCurve linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var clips = Dash((0f, 1f), (0.4f, 1f));
        List<int> order = OrderOf(clips);
        Assert.AreEqual(0.25f, BossVictoryAssassinDash.Progress(clips, order, 0, 0.25f, linear), Tolerance, "진행 중");
        Assert.AreEqual(0.4f, BossVictoryAssassinDash.Progress(clips, order, 0, 0.9f, linear), Tolerance, "#2 시작(0.4)에서 멈춤 — 도착점까지 안 감");
        Assert.AreEqual(0.5f, BossVictoryAssassinDash.Progress(clips, order, 1, 0.9f, linear), Tolerance, "#2");
        Assert.AreEqual(1f, BossVictoryAssassinDash.Progress(clips, order, 1, 5f, linear), Tolerance, "끝 = 도착");
    }

    [Test]
    public void 이동_중_애니_배율은_Custom0_End_구간을_클립_길이에_맞춤()
    {
        Assert.AreEqual(0.275f / 0.275f / 1.5f, BossVictoryKillerMoves.MoveAnimScale(0.275f, 0.275f, 1.5f), Tolerance, "기본 클립 0.275초 · 상태 속도 1.5");
        Assert.AreEqual(1f / 1.5f * 2f, BossVictoryKillerMoves.MoveAnimScale(0.275f, 0.1375f, 1.5f), Tolerance, "절반 길이 = 두 배");
        Assert.AreEqual(1f, BossVictoryKillerMoves.MoveAnimScale(0f, 0.275f, 1.5f), Tolerance, "구간 없음 = 1");
        Assert.AreEqual(1f, BossVictoryKillerMoves.MoveAnimScale(0.275f, 0f, 1.5f), Tolerance, "길이 0 = 1");
        Assert.AreEqual(0.5f, BossVictoryKillerMoves.MoveAnimScale(0.5f, 1f, 0f), Tolerance, "상태 속도 0 = 1 로 봄");
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
    public void 백대쉬는_마커_그대로()
    {
        var tl = BossVictoryTimeline.CreateDefault();
        tl.backDashStartTime = 0.2f;
        tl.finisherStartTime = 0.65f;
        BossVictoryKillerSequence.Times t = BossVictoryKillerMoves.Times(tl, BossVictoryKillerMove.BackDash, tooClose: true);
        Assert.AreEqual(0.2f, t.BackDashStart, Tolerance);
        Assert.AreEqual(0.65f, t.FinisherStart, Tolerance);
    }

    [Test]
    public void 순간이동은_백대쉬_없이_트랙_첫_클립_시작에_처치_연출_단계()
    {
        var tl = BossVictoryTimeline.CreateDefault();
        tl.backDashStartTime = 0.2f;
        tl.finisherStartTime = 0.65f;
        tl.assassinDash = Dash((0.9f, 0.275f), (0.3f, 0.275f));
        BossVictoryKillerSequence.Times t = BossVictoryKillerMoves.Times(tl, BossVictoryKillerMove.TeleportDash, tooClose: true);
        Assert.IsTrue(float.IsPositiveInfinity(t.BackDashStart), "백대쉬 시작 마커 안 씀");
        Assert.AreEqual(0.3f, t.FinisherStart, Tolerance, "첫 클립 시작(마커 0.65 아님)");
        tl.assassinDash.Clear();
        Assert.IsTrue(float.IsPositiveInfinity(BossVictoryKillerMoves.Times(tl, BossVictoryKillerMove.TeleportDash, true).FinisherStart), "클립 없음 = 안 함");
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
        // 값은 사용자 튜닝 대상 — 고정값 대신 관계만(정지 프레임은 이동 끝 End 뒤 · 클립 안, 트랙 클립 1개 이상·길이 > 0).
        float end = clip.events[System.Array.FindIndex(clip.events, ev => ev.functionName == nameof(PlayerAnimationEventRelay.HandleSkillEvent) && ev.intParameter == (int)SkillAnimationEventType.End)].time;
        Assert.Greater(assassin.timeline.finisherTrimSeconds, end, "정지 프레임 = 처치 연출 스킬 클립 끝 자르기 — 이동 끝(End) 뒤");
        Assert.LessOrEqual(assassin.timeline.finisherTrimSeconds, clip.length, "클립 안");
        Assert.IsNotNull(assassin.timeline.assassinDash);
        Assert.GreaterOrEqual(assassin.timeline.assassinDash.Count, 1, "순간이동·Q 이동 클립 1개 이상(옛 값 이관)");
        foreach (BossVictoryClip c in assassin.timeline.assassinDash) Assert.Greater(c.length, 0f, "클립 길이");
    }
}
