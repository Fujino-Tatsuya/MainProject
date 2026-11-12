using NUnit.Framework;
using UnityEngine;
using Step = BossVictoryKillerSequence.Step;

/// <summary>
/// 보스 처치 연출 처치자 순서 노드(PLAN-boss-victory-glitch "255 후속 4") — 단계 판정 순수 함수(<see cref="BossVictoryKillerSequence"/>),
/// 기본값(평타 끝 = 백대쉬 시작 = 0.13 · 스킬 시작 = 0.13 + 0.35 + 0.35 · 느려짐 꺼짐), 옛 '처치자 동작 끝 → 백대쉬' 마커 이관(값 손실 0).
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryKillerSequence).
/// </summary>
public sealed class BossVictoryKillerSequenceTests
{
    const float Tolerance = 1e-5f;

    static BossVictoryKillerSequence.Times Times(bool hasClip, float actionEnd, float dash, float skill) =>
        new BossVictoryKillerSequence.Times { HasActionClip = hasClip, ActionEnd = actionEnd, BackDashStart = dash, FinisherStart = skill };

    [Test]
    public void 단계는_시각_순서대로()
    {
        var tm = Times(true, 0.13f, 0.3f, 0.9f);
        Assert.AreEqual(Step.CurrentAction, BossVictoryKillerSequence.Target(tm, 0f));
        Assert.AreEqual(Step.CurrentAction, BossVictoryKillerSequence.Target(tm, 0.129f));
        Assert.AreEqual(Step.Wait, BossVictoryKillerSequence.Target(tm, 0.13f), "평타 클립 끝 = 동작 끊고 대기");
        Assert.AreEqual(Step.BackDash, BossVictoryKillerSequence.Target(tm, 0.3f));
        Assert.AreEqual(Step.Finisher, BossVictoryKillerSequence.Target(tm, 0.9f));
        Assert.AreEqual(Step.Finisher, BossVictoryKillerSequence.Target(tm, 50f));
    }

    [Test]
    public void 평타_클립이_없으면_백대쉬_시작까지_하던_동작()
    {
        var tm = Times(false, 0f, 0.5f, 1f);
        Assert.AreEqual(Step.CurrentAction, BossVictoryKillerSequence.Target(tm, 0.49f), "클립 없음 = 자기 클립 끝까지(런타임이 클립 끝을 본다)");
        Assert.AreEqual(Step.BackDash, BossVictoryKillerSequence.Target(tm, 0.5f));
    }

    [Test]
    public void 늦은_단계가_이긴다()
    {
        // 백대쉬 시작이 평타 끝보다 앞 — 백대쉬가 동작을 끊는다.
        var early = Times(true, 0.5f, 0.2f, 1f);
        Assert.AreEqual(Step.BackDash, BossVictoryKillerSequence.Target(early, 0.3f));
        // 스킬 시작이 백대쉬 시작보다 앞 — 백대쉬는 건너뛴다.
        var skip = Times(true, 0.13f, 0.6f, 0.4f);
        Assert.AreEqual(Step.Finisher, BossVictoryKillerSequence.Target(skip, 0.4f));
        Assert.AreEqual(Step.Finisher, BossVictoryKillerSequence.Target(skip, 0.7f));
        // 처치 순간 0 이면 바로 스킬(늦게 받은 피어도 시각 그대로).
        Assert.AreEqual(Step.Finisher, BossVictoryKillerSequence.Target(Times(true, 0.13f, 0f, 0f), 0f));
    }

    [Test]
    public void 단계_순서_값은_진행_방향()
    {
        Assert.Less((int)Step.None, (int)Step.CurrentAction);
        Assert.Less((int)Step.CurrentAction, (int)Step.Wait);
        Assert.Less((int)Step.Wait, (int)Step.BackDash);
        Assert.Less((int)Step.BackDash, (int)Step.Finisher);
    }

    [Test]
    public void 느려짐_시작_마커는_0이면_끔()
    {
        Assert.IsFalse(BossVictoryKillerSequence.SlowByMarker(0f, 0f));
        Assert.IsFalse(BossVictoryKillerSequence.SlowByMarker(0f, 10f), "끔 = 스킬 End 이벤트");
        Assert.IsFalse(BossVictoryKillerSequence.SlowByMarker(1.2f, 1.19f));
        Assert.IsTrue(BossVictoryKillerSequence.SlowByMarker(1.2f, 1.2f));
    }

    [Test]
    public void 기본값은_지금_체감과_같다()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        Assert.IsTrue(tl.TryGetKillerAction(out BossVictoryClip clip));
        Assert.AreEqual(0f, clip.start, Tolerance);
        Assert.AreEqual(0.13f, clip.End, Tolerance, "평타 끝 = 사용자 값 0.13");
        Assert.AreEqual(0.13f, tl.backDashStartTime, Tolerance, "백대쉬 시작 = 평타 끝 직후");
        Assert.AreEqual(0f, tl.slowStartTime, "느려짐 = 꺼짐(스킬 End)");

        var s = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            Assert.AreEqual(BossEncounterDirector.SkidIdleDelay, s.backDashIdleDelay, Tolerance, "백대쉬 후 대기 = 등장 밀림 Idle 복귀 지연");
            Assert.AreEqual(BossVictoryKillerSequence.DefaultFinisherStart(tl.backDashStartTime, s.backDashDuration, s.backDashIdleDelay),
                tl.finisherStartTime, Tolerance, "스킬 시작 = 백대쉬 시작 + 시간 + 후 대기");
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 스킬과_같이_가는_프로필은_스킬_단계에서도_백대쉬를_한_번_시작()
    {
        // 267 후속 — 팔라딘: 백대쉬 시작 0 · 스킬 연출 시작 0 → 단계는 바로 스킬, 백대쉬는 같은 순간 이동만으로.
        var tm = new BossVictoryKillerSequence.Times { HasActionClip = false, BackDashStart = 0f, FinisherStart = 0f };
        Assert.AreEqual(Step.Finisher, BossVictoryKillerSequence.Target(tm, 0f), "같은 시각이면 스킬 단계(평타는 스킬 재생이 끊는다)");
        Assert.IsTrue(BossVictoryKillerSequence.StartDashDuringFinisher(true, Step.Finisher, false, 0f, 0f));
        Assert.IsFalse(BossVictoryKillerSequence.StartDashDuringFinisher(true, Step.Finisher, true, 0f, 0.1f), "한 회차에 한 번");
        Assert.IsFalse(BossVictoryKillerSequence.StartDashDuringFinisher(true, Step.Finisher, false, 0.3f, 0.2f), "자기 마커 전");
        Assert.IsTrue(BossVictoryKillerSequence.StartDashDuringFinisher(true, Step.Finisher, false, 0.3f, 0.3f), "스킬 뒤 늦은 백대쉬");
        Assert.IsFalse(BossVictoryKillerSequence.StartDashDuringFinisher(true, Step.BackDash, false, 0f, 0.1f), "스킬 단계에서만");
        Assert.IsFalse(BossVictoryKillerSequence.StartDashDuringFinisher(false, Step.Finisher, false, 0f, 0.1f), "끈 프로필 = 옛 흐름(건너뜀)");
    }

    [Test]
    public void 팔라딘만_스킬과_백대쉬가_동시()
    {
        BossVictoryGlitchSettings s = Resources.Load<BossVictoryGlitchSettings>(BossVictoryGlitchSettings.ResourcePath);
        Assert.IsNotNull(s, "설정 에셋");
        BossVictoryProfile paladin = s.ProfileFor(TutorialCharacter.Paladin);
        Assert.IsTrue(paladin.finisherWithBackDash, "팔라딘 = 같이 감");
        Assert.AreEqual(0f, paladin.timeline.backDashStartTime, Tolerance);
        Assert.AreEqual(0f, paladin.timeline.finisherStartTime, Tolerance);
        Assert.IsFalse(paladin.timeline.KillerTimes().HasActionClip, "처치자 평타 클립 없음");
        Assert.IsFalse(s.ProfileFor(TutorialCharacter.None).finisherWithBackDash, "코드 기본값 = 옛 흐름");
        Assert.IsFalse(s.ProfileFor(TutorialCharacter.Gunner).finisherWithBackDash, "거너 = 옛 흐름");
        Assert.IsFalse(s.ProfileFor(TutorialCharacter.Assassin).finisherWithBackDash, "어쌔신 = 옛 흐름");
    }

    [Test]
    public void 새_타임라인_단계_시각()
    {
        var tl = new BossVictoryTimeline();
        tl.killerAction.Add(BossVictoryClip.Make(0f, 0.25f));
        tl.backDashStartTime = 0.3f;
        tl.finisherStartTime = 1f;
        BossVictoryKillerSequence.Times tm = tl.KillerTimes();
        Assert.IsTrue(tm.HasActionClip);
        Assert.AreEqual(0.25f, tm.ActionEnd, Tolerance);
        Assert.AreEqual(0.3f, tm.BackDashStart, Tolerance);
        Assert.AreEqual(1f, tm.FinisherStart, Tolerance);
        tl.killerAction.Clear();
        Assert.IsFalse(tl.KillerTimes().HasActionClip);
    }

    [Test]
    public void 마커_이름과_시각_설정()
    {
        var tl = new BossVictoryTimeline();
        Assert.IsFalse(System.Enum.IsDefined(typeof(BossVictoryMarker), "KillerActionEnd"), "옛 마커는 처치자 평타 클립 끝으로 흡수");
        Assert.AreEqual("백대쉬 시작", BossVictoryTimeline.MarkerLabel(BossVictoryMarker.BackDashStart));
        Assert.AreEqual("스킬 연출 시작", BossVictoryTimeline.MarkerLabel(BossVictoryMarker.FinisherStart));
        Assert.AreEqual("느려짐 시작", BossVictoryTimeline.MarkerLabel(BossVictoryMarker.SlowStart));
        Assert.AreEqual("처치자 평타", BossVictoryTimeline.TrackLabel(BossVictoryTrack.KillerAction));
        tl.SetMarkerTime(BossVictoryMarker.BackDashStart, 0.4f);
        tl.SetMarkerTime(BossVictoryMarker.FinisherStart, 1.1f);
        tl.SetMarkerTime(BossVictoryMarker.SlowStart, -1f);
        Assert.AreEqual(0.4f, tl.backDashStartTime, Tolerance);
        Assert.AreEqual(1.1f, tl.MarkerTime(BossVictoryMarker.FinisherStart), Tolerance);
        Assert.AreEqual(0f, tl.slowStartTime, "음수 = 0(끔)");
        Assert.AreEqual(BossVictoryTimeline.TrackKind.Single, BossVictoryTimeline.KindOf(BossVictoryTrack.KillerAction));
        Assert.IsFalse(BossVictoryTimeline.HasFades(BossVictoryTrack.KillerAction));
        Assert.IsTrue(BossVictoryTimeline.StartFixed(BossVictoryTrack.KillerAction));
        Assert.IsFalse(BossVictoryTimeline.StartFixed(BossVictoryTrack.CrtOff));
    }

    // 옛 형식 에셋(255 — 'killerActionEndTime' 마커, 처치자 평타·새 마커 칸 없음)을 읽으면 설정 SO 역직렬화가 옮긴다.
    [Test]
    public void 옛_처치자_동작_끝_마커는_평타_클립_끝으로_옮긴다()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            string json = "{\"backDashDuration\":0.5,\"backDashIdleDelay\":0.35,\"timeline\":{\"lobbyTransitionTime\":5.6,\"killerActionEndTime\":0.2}}";
            JsonUtility.FromJsonOverwrite(json, s);
            BossVictoryTimeline tl = s.timeline;
            Assert.AreEqual(1, tl.killerAction.Count, "평타 클립 1개");
            Assert.AreEqual(0f, tl.killerAction[0].start, Tolerance);
            Assert.AreEqual(0.2f, tl.killerAction[0].End, Tolerance, "옛 마커 값 = 평타 끝(손실 0)");
            Assert.AreEqual(0.2f, tl.backDashStartTime, Tolerance, "백대쉬 시작 = 평타 끝 직후");
            Assert.AreEqual(0.2f + 0.5f + 0.35f, tl.finisherStartTime, Tolerance, "스킬 시작 = 그때 체감(백대쉬 시간 + 후 대기)");

            // 저장 → 다시 읽어도 그대로(옛 칸은 0 이 돼 두 번 옮기지 않는다).
            tl.killerAction[0] = BossVictoryClip.Make(0f, 0.4f);
            string saved = JsonUtility.ToJson(s);
            JsonUtility.FromJsonOverwrite(saved, s);
            Assert.AreEqual(0.4f, s.timeline.killerAction[0].End, Tolerance, "새 형식은 사용자가 고친 값 그대로");
            Assert.AreEqual(1, s.timeline.killerAction.Count);
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 옛_마커가_0이면_아무것도_안_옮긴다()
    {
        var tl = new BossVictoryTimeline();
        Assert.IsFalse(tl.MigrateLegacyKillerActionEnd(0.35f, 0.35f));
        Assert.AreEqual(0, tl.killerAction.Count);
    }
}
