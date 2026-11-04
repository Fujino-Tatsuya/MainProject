using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 191 4단계 — 오너 연출 cue(PLAN §3.3·D4·D5·D10). 매니저 쪽 판단(<see cref="InterruptOwnerCueRouter"/>)과
/// 디렉터 쪽 재생(<see cref="InterruptOwnerJudgeCuePlayer"/> + 오너 상태 기계 + 적중 섬광 진행기 + 마스크 암전 출력)을 디렉터 partial 과 같은 연결로 묶어 본다.
/// </summary>
public sealed class InterruptOwnerJudgeCueTests
{
    const ulong Me = 11;
    const ulong Mate = 12;
    const ulong Boss = 50;
    const uint Window = 7;

    // 이진 소수로 딱 떨어지는 길이 — 암전 들어옴 1/8·걷기 1/4초, 섬광 ②③④ 2·2·2 프레임.
    sealed class FakeSettings : IInterruptHitFlashSettings, IInterruptOwnerPresentationSettings
    {
        public int HitFlashStarburstFrames => 2;
        public int HitFlashWhiteoutFrames => 2;
        public int HitFlashSmearFrames => 2;
        public float HitFlashAberrationDuration => 0.125f;
        public float HitFlashSettleDuration => 0.25f;
        public InterruptEffectTimeBase HitFlashTimeBase => InterruptEffectTimeBase.Unscaled;
        public float OwnerDimInDuration => 0.125f;
        public float OwnerDimOutDuration => 0.25f;
    }

    sealed class Look : IInterruptOwnerDimLook
    {
        public Color OwnerDimColor => Color.black;
        public float OwnerDimOpacity => 1f;
    }

    // InterruptProductionDirector.OwnerJudgeCues.cs 와 같은 연결.
    sealed class DirectorLikeSink : IInterruptOwnerCueSink
    {
        public InterruptOwnerPresentation Presentation;
        public InterruptHitFlashSequencer Flash;
        public InterruptOwnerJudgeCuePlayer Cues;
        public InterruptOwnerDimOutput Dim;
        public bool KeepCoSucceeded = true;
        public readonly Dictionary<ulong, GameObject> Actors = new Dictionary<ulong, GameObject>();
        public readonly List<InterruptOwnerEndReason> Ends = new List<InterruptOwnerEndReason>();
        public readonly List<ulong> Kept = new List<ulong>();
        public int Attempts;
        public int Successes;
        public float Now;
        public int Frame;

        public bool OwnerAttempt(in InterruptOwnerCue cue)
        {
            Attempts++;
            Cues.Attempted();
            if (!Presentation.Accepts(cue.Forecast))
                return false;
            Dim.SetSubjects(cue.Owner, cue.Target);
            return Presentation.Predict(cue.Forecast, null, Now, cue.AttackDirection);
        }

        public void OwnerJudgeSucceeded(in InterruptOwnerCue cue)
        {
            Successes++;
            Cues.JudgeSucceeded(0, cue.FlashPosition, cue.FlashPosition, Now, Now, Frame);
        }

        public void OwnerEnded(InterruptOwnerEndReason reason)
        {
            Ends.Add(reason);
            Cues.Ended(reason, Now);
        }

        public void OwnerApproved(IReadOnlyList<ulong> approvedActorIds, ulong localActorId)
        {
            Cues.Approved(approvedActorIds, localActorId, KeepCoSucceeded, Kept);
            foreach (ulong id in Kept)
                if (Actors.TryGetValue(id, out GameObject actor))
                    Dim.KeepWhileDimmed(actor);
        }
    }

    DirectorLikeSink _sink;
    InterruptOwnerCueRouter _router;
    readonly List<InterruptHitFlashMoment> _moments = new List<InterruptHitFlashMoment>();
    readonly List<GameObject> _objects = new List<GameObject>();
    GameObject _me;
    GameObject _mate;
    GameObject _boss;

    [SetUp]
    public void SetUp()
    {
        var settings = new FakeSettings();
        _sink = new DirectorLikeSink { Dim = new InterruptOwnerDimOutput(new Look(), useHudCover: false) };
        _sink.Presentation = new InterruptOwnerPresentation(settings, _sink.Dim);
        _sink.Flash = new InterruptHitFlashSequencer(settings, null, moment =>
        {
            _moments.Add(moment);
            _sink.Presentation.SuccessFlashMoment(moment.Event.TriggerId, _sink.Now);
        });
        _sink.Cues = new InterruptOwnerJudgeCuePlayer(() => _sink.Presentation, () => _sink.Flash, () => _sink.Flash.ForcedReset());
        _router = new InterruptOwnerCueRouter(() => _sink);
        _moments.Clear();

        _me = MakeRenderer("Me");
        _mate = MakeRenderer("Mate");
        _boss = MakeRenderer("Boss");
        _sink.Actors[Me] = _me;
        _sink.Actors[Mate] = _mate;
    }

    [TearDown]
    public void TearDown()
    {
        _sink.Dim.Dispose();
        foreach (GameObject go in _objects)
            Object.DestroyImmediate(go);
        _objects.Clear();
    }

    GameObject MakeRenderer(string name)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshRenderer>().renderingLayerMask = 1u;
        _objects.Add(go);
        return go;
    }

    static bool Kept(GameObject go) => InterruptOwnerMaskLayers.Has(go.GetComponent<MeshRenderer>().renderingLayerMask);

    InterruptOwnerCue Cue(InterruptJudgeOutcome outcome) => new InterruptOwnerCue
    {
        ActorId = Me,
        Result = new InterruptJudgeResult
        {
            Outcome = outcome,
            FailReason = outcome == InterruptJudgeOutcome.Failure ? InterruptJudgeFailReason.OutOfRange : InterruptJudgeFailReason.None,
            TargetId = outcome == InterruptJudgeOutcome.NoTarget ? 0 : Boss,
            WindowId = outcome == InterruptJudgeOutcome.Success ? Window : 0,
        },
        Owner = _me,
        Target = outcome == InterruptJudgeOutcome.NoTarget ? null : _boss,
        AttackDirection = Vector3.forward,
        FlashPosition = Vector3.zero,
    };

    void Tick(float now, int frame)
    {
        _sink.Now = now;
        _sink.Frame = frame;
        _sink.Presentation.Tick(now);
        _sink.Flash.Tick(now, frame);
    }

    void PredictStrong()
    {
        _router.Predicted(Cue(InterruptJudgeOutcome.Success));
        Tick(10.125f, 101);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sink.Presentation.State);
        Assert.AreEqual(1f, _sink.Presentation.DimIntensity, "강 예측 = 오너 암전");
    }

    void JudgeSuccessAt(float now, int frame)
    {
        _sink.Now = now;
        _sink.Frame = frame;
        _router.Judged(Cue(InterruptJudgeOutcome.Success));
    }

    // ---- D4 ----

    [Test]
    public void 강_예측_후_판정_실패면_그_순간_걷는다()
    {
        _sink.Now = 10f;
        PredictStrong();

        _sink.Now = 10.25f;
        _router.Judged(Cue(InterruptJudgeOutcome.Failure));

        CollectionAssert.AreEqual(new[] { InterruptOwnerEndReason.JudgeFailed }, _sink.Ends);
        Assert.AreEqual(InterruptOwnerPresentationState.Failed, _sink.Presentation.State);
        Assert.IsFalse(_sink.Presentation.LightGatherActive);
        Tick(10.5f, 120);
        Assert.AreEqual(0f, _sink.Presentation.DimIntensity, "걷기 1/4초 뒤 0");
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sink.Presentation.State);
        Assert.AreEqual(0, _sink.Successes);
    }

    [Test]
    public void 약_예측_후_판정_성공이면_성공_섬광은_그대로_나온다()
    {
        _sink.Now = 10f;
        _router.Predicted(Cue(InterruptJudgeOutcome.Failure));
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptWeak, _sink.Presentation.State);

        JudgeSuccessAt(10.25f, 110);

        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sink.Presentation.State);
        Assert.IsFalse(_sink.Presentation.SuccessDimmed, "약 시도 성공 = 끌 암전 없음");
        Assert.IsFalse(_sink.Flash.OverlayActive, "섬광 오버레이 없음(약 시도 성공 규칙 그대로)");
        Tick(10.3f, 116);
        Assert.AreEqual(1, _moments.Count, "섬광 순간(월드 섬광·self 암순응)은 그대로");
        Assert.IsTrue(_moments[0].ViewerIsCaster);
        Assert.IsFalse(_moments[0].OwnerOverlay);
        Assert.IsEmpty(_sink.Ends);
    }

    [Test]
    public void 강_예측_판정_성공은_서버를_기다리지_않고_섬광_오버레이를_튼다()
    {
        _sink.Now = 10f;
        PredictStrong();

        JudgeSuccessAt(10.25f, 110);

        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sink.Presentation.State);
        Assert.IsTrue(_sink.Presentation.SuccessDimmed);
        Assert.IsTrue(_sink.Flash.OverlayActive, "판정 순간 오버레이 ②");
        Assert.IsTrue(_sink.Cues.SuccessPending);
        Tick(10.26f, 116);
        Assert.AreEqual(1, _moments.Count);
        Assert.AreEqual(0f, _sink.Presentation.DimIntensity, "섬광 순간에 오너 암전 끔");

        // 뒤이어 오는 내 서버 성공 슬로우는 한 번만 삼킨다(두 번 터지지 않게).
        Assert.IsTrue(_sink.Cues.ConsumeServerStarted(SlowMotionPurpose.Success));
        Assert.IsFalse(_sink.Cues.ConsumeServerStarted(SlowMotionPurpose.Success));
    }

    [Test]
    public void cue_경로가_몬_시도는_서버_시도_슬로우_목적으로_걷지_않는다()
    {
        _sink.Now = 10f;
        PredictStrong();

        Assert.IsTrue(_sink.Cues.ConsumeServerStarted(SlowMotionPurpose.AttemptWeak), "서버 약(옛 예측기)은 오너 연출에 반영 안 함");
        Assert.IsTrue(_sink.Cues.ConsumeServerStarted(SlowMotionPurpose.Attempt));
        Assert.IsFalse(_sink.Cues.ConsumeServerStarted(SlowMotionPurpose.Success), "로컬 성공 전 서버 성공은 기존 경로");
    }

    // ---- 서버 거부 ----

    [Test]
    public void 서버_거부면_이미_튼_성공_섬광을_즉시_끈다()
    {
        _sink.Now = 10f;
        PredictStrong();
        JudgeSuccessAt(10.25f, 110);
        Tick(10.25f, 111);
        Assert.IsTrue(_sink.Flash.OverlayActive);

        _router.Rejected(new InterruptClaimRejection(Me, Boss, Window, InterruptClaimRejectReason.WindowClosed, InterruptJudgeFailReason.None));

        CollectionAssert.AreEqual(new[] { InterruptOwnerEndReason.ServerRejected }, _sink.Ends);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sink.Presentation.State);
        Assert.AreEqual(0f, _sink.Presentation.DimIntensity, "즉시 0(램프 없음)");
        Assert.IsFalse(_sink.Flash.OverlayActive, "오버레이 즉시 끔");
        Assert.IsFalse(_sink.Cues.SuccessPending);
        Tick(10.5f, 130);
        Assert.AreEqual(0, _moments.Count, "남은 섬광 순간도 취소");
    }

    [Test]
    public void 다른_창이나_판정_전_거부는_무시한다()
    {
        _sink.Now = 10f;
        PredictStrong();
        _router.Rejected(new InterruptClaimRejection(Me, Boss, Window, InterruptClaimRejectReason.FirstComeLost, InterruptJudgeFailReason.None));
        Assert.IsEmpty(_sink.Ends, "판정 전(신고 없음)");

        JudgeSuccessAt(10.25f, 110);
        _router.Rejected(new InterruptClaimRejection(Me, Boss, Window + 1, InterruptClaimRejectReason.FirstComeLost, InterruptJudgeFailReason.None));
        _router.Rejected(new InterruptClaimRejection(Mate, Boss, Window, InterruptClaimRejectReason.FirstComeLost, InterruptJudgeFailReason.None));
        Assert.IsEmpty(_sink.Ends);
        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sink.Presentation.State);
    }

    // ---- D5 ----

    [Test]
    public void 판정_전_CC로_스킬이_끊기면_즉시_걷는다()
    {
        _sink.Now = 10f;
        PredictStrong();

        _sink.Now = 10.2f;
        Assert.IsTrue(_router.SkillEnded(Me, SkillEndReason.Cancelled), "cue 경로가 맡은 시전");

        CollectionAssert.AreEqual(new[] { InterruptOwnerEndReason.CrowdControl }, _sink.Ends);
        Assert.AreEqual(InterruptOwnerPresentationState.Failed, _sink.Presentation.State);
        Assert.IsFalse(_router.AwaitsJudge(Me));

        _router.Judged(Cue(InterruptJudgeOutcome.Success));
        Assert.AreEqual(0, _sink.Successes, "끊긴 뒤 판정은 무시");
    }

    [TestCase(SkillEndReason.Cancelled)]
    [TestCase(SkillEndReason.CasterDied)]
    [TestCase(SkillEndReason.DashCancelled)]
    [TestCase(SkillEndReason.MaxDurationReached)]
    public void 정상_완료가_아닌_끊김은_판정_전_종료다(SkillEndReason reason)
    {
        Assert.IsTrue(InterruptOwnerCueRouter.EndsBeforeJudge(reason));
    }

    [Test]
    public void 일반_피해는_종료하지_않는다()
    {
        // 일반 피해는 경직이 없어 스킬이 끊기지 않는다 — 라우터에 끊김이 오지 않으면 시도는 그대로, 판정이 결론을 낸다.
        _sink.Now = 10f;
        PredictStrong();
        Assert.IsFalse(InterruptOwnerCueRouter.EndsBeforeJudge(SkillEndReason.Completed));

        // 판정 전에 정상 완료(End)가 먼저 와도 끝내지 않고 판정을 기다린다.
        Assert.IsTrue(_router.SkillEnded(Me, SkillEndReason.Completed));
        Assert.IsEmpty(_sink.Ends);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, _sink.Presentation.State);
        Assert.IsTrue(_router.AwaitsJudge(Me));

        JudgeSuccessAt(10.25f, 110);
        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sink.Presentation.State);
    }

    [Test]
    public void 판정_뒤_끊김은_성공_섬광을_건드리지_않는다()
    {
        _sink.Now = 10f;
        PredictStrong();
        JudgeSuccessAt(10.25f, 110);

        Assert.IsTrue(_router.SkillEnded(Me, SkillEndReason.Cancelled));
        Assert.IsEmpty(_sink.Ends);
        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sink.Presentation.State);
    }

    [Test]
    public void cue_경로가_아닌_시전은_스킬_끝을_넘긴다()
    {
        Assert.IsFalse(_router.SkillEnded(Me, SkillEndReason.Cancelled), "예측 없음 → 호출측 기존 걷기");
        _router.Predicted(Cue(InterruptJudgeOutcome.Success));
        _router.Forget(Me);
        Assert.IsFalse(_router.SkillEnded(Me, SkillEndReason.Cancelled), "시전 거부로 잊은 시전");
        Assert.IsEmpty(_sink.Ends);
    }

    [Test]
    public void 판정_시각_표본을_못_얻으면_버리고_걷는다()
    {
        _sink.Now = 10f;
        PredictStrong();
        _router.Dropped(Me);
        CollectionAssert.AreEqual(new[] { InterruptOwnerEndReason.JudgeDropped }, _sink.Ends);
        Assert.AreEqual(InterruptOwnerPresentationState.Failed, _sink.Presentation.State);
    }

    // ---- 동시 성공 + D10 ----

    [Test]
    public void 동시_성공_D10_켬이면_함께_성공한_캐릭터도_암전에_남는다()
    {
        _sink.Now = 10f;
        PredictStrong();
        JudgeSuccessAt(10.25f, 110);
        Assert.IsTrue(Kept(_me) && Kept(_boss), "오너·보스는 시도 때부터 남김");
        Assert.IsFalse(Kept(_mate));

        _router.Approved(new InterruptClaimApproval(Boss, Window, Me, Mate, new List<ulong> { Me, Mate }));

        CollectionAssert.AreEqual(new[] { Mate }, _sink.Kept);
        Assert.IsTrue(Kept(_mate), "함께 성공한 캐릭터 = 마스크 제외 대상(밝게 남음)");
        Assert.IsTrue(_sink.Cues.SuccessPending, "내가 첫 승인 — 내 서버 성공 슬로우를 기다린다");
    }

    [Test]
    public void 동시_성공_D10_끔이면_오너와_보스만_남는다()
    {
        _sink.KeepCoSucceeded = false;
        _sink.Now = 10f;
        PredictStrong();
        JudgeSuccessAt(10.25f, 110);

        _router.Approved(new InterruptClaimApproval(Boss, Window, Me, Mate, new List<ulong> { Me, Mate }));

        Assert.IsEmpty(_sink.Kept);
        Assert.IsFalse(Kept(_mate));
        Assert.IsTrue(Kept(_me) && Kept(_boss));
    }

    [Test]
    public void 내가_첫_승인자가_아니면_서버_성공_대기를_푼다()
    {
        _sink.Now = 10f;
        PredictStrong();
        JudgeSuccessAt(10.25f, 110);

        _router.Approved(new InterruptClaimApproval(Boss, Window, Mate, Me, new List<ulong> { Mate, Me }));

        Assert.IsFalse(_sink.Cues.SuccessPending, "전역 성공 슬로우는 첫 승인자 것 — 내 서버 Success 는 오지 않는다");
        CollectionAssert.AreEqual(new[] { Mate }, _sink.Kept);
    }

    [Test]
    public void 승인_목록에_내가_없거나_다른_창이면_cue_없음()
    {
        _sink.Now = 10f;
        PredictStrong();
        JudgeSuccessAt(10.25f, 110);

        _router.Approved(new InterruptClaimApproval(Boss, Window, Mate, Mate, new List<ulong> { Mate }));
        _router.Approved(new InterruptClaimApproval(Boss, Window + 1, Me, Mate, new List<ulong> { Me, Mate }));

        Assert.IsEmpty(_sink.Kept);
        Assert.IsFalse(Kept(_mate));
    }

    [Test]
    public void D10_선택은_나를_빼고_중복_없이()
    {
        var result = new List<ulong>();
        InterruptOwnerJudgeCuePlayer.SelectCoSucceeded(new List<ulong> { Me, Mate, Mate, 13 }, Me, true, result);
        CollectionAssert.AreEqual(new ulong[] { Mate, 13 }, result);
        InterruptOwnerJudgeCuePlayer.SelectCoSucceeded(new List<ulong> { Me, Mate }, Me, false, result);
        Assert.IsEmpty(result);
    }

    [Test]
    public void 대상_없음_예측도_cue_경로라_판정_성공이면_섬광()
    {
        _sink.Now = 10f;
        _router.Predicted(Cue(InterruptJudgeOutcome.NoTarget));
        Assert.AreEqual(1, _sink.Attempts);
        Assert.AreEqual(InterruptOwnerPresentationState.None, _sink.Presentation.State, "대상 없음 = 아무것도 안 켬");
        Assert.IsTrue(_sink.Cues.DrivesAttempt);

        JudgeSuccessAt(10.25f, 110);
        Assert.AreEqual(InterruptOwnerPresentationState.Success, _sink.Presentation.State);
        Assert.IsFalse(_sink.Flash.OverlayActive);
    }
}
