using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 간파 연출 4단계(할 일 192) — 단계 선택표, 슬로우 Start 이벤트 표식 바이트(제압·대쉬), 서버 제압 표식(같은 프레임만), 서버 성공 사건의 대쉬 칸,
/// 오너 암전 = 단계 설정이 켬일 때만(기본 전부 끔 — 11-04 할 일 211 로 제압 암전도 뺐다)·실패 = 약한 줌만(Q3), 제압 = 일반·대쉬와 같은 V3(211), 피해 판정 선착순(5a).
/// 옛 제압 섬광 시퀀스·제압 암순응 컷 테스트는 Editor/Legacy/SuppressFlashTests.
/// </summary>
public sealed class InterruptPresentationTierTests
{
    private readonly List<Object> _objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in _objects)
            if (o != null)
                Object.DestroyImmediate(o);
        _objects.Clear();
    }

    // ---- 1. 단계 선택표 ----

    [TestCase(false, InterruptVariant.Normal, false, ExpectedResult = InterruptPresentationTier.Fail)]
    [TestCase(false, InterruptVariant.Dash, true, ExpectedResult = InterruptPresentationTier.Fail)]
    [TestCase(true, InterruptVariant.Normal, false, ExpectedResult = InterruptPresentationTier.Normal)]
    [TestCase(true, InterruptVariant.Dash, false, ExpectedResult = InterruptPresentationTier.Dash)]
    [TestCase(true, InterruptVariant.Normal, true, ExpectedResult = InterruptPresentationTier.Suppress)]
    [TestCase(true, InterruptVariant.Dash, true, ExpectedResult = InterruptPresentationTier.Suppress)]
    public InterruptPresentationTier 단계_선택표(bool succeeded, InterruptVariant variant, bool suppress) =>
        InterruptPresentationTierRules.Select(succeeded, variant, suppress);

    [Test]
    public void 사건의_단계는_성공_사건만_성공_단계()
    {
        Assert.AreEqual(InterruptPresentationTier.Dash,
            InterruptProductionEvent.LocalSuccess(1, 0, Vector3.zero, false, InterruptVariant.Dash).Tier);
        Assert.AreEqual(InterruptPresentationTier.Suppress,
            InterruptProductionEvent.LocalSuccess(1, 0, Vector3.zero, true, InterruptVariant.Dash).Tier);
        Assert.AreEqual(InterruptPresentationTier.Fail, InterruptProductionEvent.LocalFail(3).Tier);
        Assert.AreEqual(InterruptVariant.Dash,
            InterruptProductionEvent.Success(5, null, null, true, Vector3.one, Vector3.zero, InterruptVariant.Dash)
                .WithSuppressInterrupt(true).Variant, "제압 칸을 바꿔도 종류는 그대로");
    }

    [Test]
    public void 서버_표식은_같은_프레임에_한_번만()
    {
        var mark = new SuppressInterruptMark();
        Assert.IsFalse(mark.Consume(10));

        mark.Mark(10);
        Assert.IsTrue(mark.Consume(10));
        Assert.IsFalse(mark.Consume(10), "읽으면 비운다");

        mark.Mark(10);
        Assert.IsFalse(mark.Consume(11), "다른 프레임에 남은 표식은 버린다");
        Assert.IsFalse(mark.Consume(10));
    }

    [Test]
    public void 성공_사건_사본은_제압_간파_칸만_바꾼다()
    {
        var e = InterruptProductionEvent.Success(3, null, null, true, new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        InterruptProductionEvent marked = e.WithSuppressInterrupt(true);

        Assert.IsFalse(e.SuppressInterrupt);
        Assert.IsTrue(marked.SuppressInterrupt);
        Assert.AreEqual(InterruptProductionPhase.Success, marked.Phase);
        Assert.AreEqual(3ul, marked.CasterClientId);
        Assert.IsTrue(marked.HasTarget);
        Assert.AreEqual(new Vector3(1, 2, 3), marked.TargetPosition);
        Assert.AreEqual(new Vector3(4, 5, 6), marked.CasterPosition);
        Assert.IsTrue(InterruptProductionEvent.LocalSuccess(1, 2, Vector3.zero, true).SuppressInterrupt);
    }

    // ---- 전송(새 RPC 없음 — 슬로우 Start 이벤트 1바이트) ----

    [TestCase(false)]
    [TestCase(true)]
    public void 슬로우_Start_이벤트는_제압_간파_칸을_싣고_세션이_기억한다(bool suppress)
    {
        var profile = new SlowMotionProfile { scale = 0.25f, holdDuration = 1f, enterDuration = 0.05f, exitDuration = 0.4f };
        SlowMotionEvent e = GlobalTimeScale.CreateStartEvent(5, 10.0, 2, profile, SlowMotionPurpose.Success, Vector3.one, true, suppress);

        SlowMotionEvent read;
        using (var writer = new FastBufferWriter(SlowMotionEvent.SerializedSize, Allocator.Temp))
        {
            e.Write(writer);
            Assert.AreEqual(SlowMotionEvent.SerializedSize, writer.Length, "직렬화 크기 상수 = 실제 쓴 바이트");
            using (var reader = new FastBufferReader(writer, Allocator.Temp))
                read = SlowMotionEvent.Read(reader);
        }

        Assert.AreEqual(suppress, read.SuppressInterrupt);
        Assert.AreEqual(SlowMotionPurpose.Success, read.Purpose);
        Assert.AreEqual(Vector3.one, read.TargetPosition);

        var session = new SlowMotionSession();
        Assert.AreEqual(SlowMotionApplyResult.Started, session.Apply(read));
        Assert.AreEqual(suppress, session.CurrentSuppressInterrupt);
        session.Clear();
        Assert.IsFalse(session.CurrentSuppressInterrupt);
    }

    // ---- 1. 표식 바이트(새 RPC 없음 — 179 바이트에 비트 하나) ----

    [TestCase(false, false, ExpectedResult = (byte)0)]
    [TestCase(true, false, ExpectedResult = (byte)1)]
    [TestCase(false, true, ExpectedResult = (byte)2)]
    [TestCase(true, true, ExpectedResult = (byte)3)]
    public byte 표식_바이트_비트(bool suppress, bool dash) => SlowMotionEvent.PackInterruptFlags(suppress, dash);

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void 슬로우_Start_이벤트는_제압과_대쉬를_한_바이트로_왕복(bool suppress, bool dash)
    {
        SlowMotionEvent sent = SlowMotionEvent.CreateStart(9, 1.5, 2, new SlowMotionProfile { scale = 0.25f },
            SlowMotionPurpose.Success, new Vector3(1f, 2f, 3f), suppress, dash);

        SlowMotionEvent received;
        using (var writer = new FastBufferWriter(SlowMotionEvent.SerializedSize, Allocator.Temp))
        {
            sent.Write(writer);
            Assert.AreEqual(SlowMotionEvent.SerializedSize, writer.Length, "직렬화 크기 그대로");
            using (var reader = new FastBufferReader(writer, Allocator.Temp))
                received = SlowMotionEvent.Read(reader);
        }

        Assert.AreEqual(suppress, received.SuppressInterrupt);
        Assert.AreEqual(dash, received.DashInterrupt);
        Assert.AreEqual(SlowMotionPurpose.Success, received.Purpose);
    }

    [Test]
    public void 세션은_마지막_Start_의_대쉬_칸을_남기고_Clear_로_지운다()
    {
        var session = new SlowMotionSession();
        session.Apply(SlowMotionEvent.CreateStart(1, 1.0, 0, new SlowMotionProfile { scale = 0.5f, holdDuration = 1f },
            SlowMotionPurpose.Success, Vector3.zero, false, true));
        Assert.IsTrue(session.CurrentDashInterrupt);
        Assert.IsFalse(session.CurrentSuppressInterrupt);

        session.Clear();
        Assert.IsFalse(session.CurrentDashInterrupt);
    }

    [Test]
    public void 서버_성공_단계는_사건의_대쉬_칸을_슬로우에_싣는다()
    {
        var settings = new InterruptProductionStageTests.FakeSettings();
        var port = new InterruptProductionStageTests.FakeTimeScale();
        var stage = new InterruptSuccessStage(settings, port);

        stage.Server(InterruptProductionEvent.Success(3, null, null, true, Vector3.one, Vector3.zero, InterruptVariant.Dash), null);
        stage.Server(InterruptProductionEvent.Success(3, null, null, true, Vector3.one, Vector3.zero), null);

        CollectionAssert.AreEqual(new[] { true, false }, port.DashFlags);
        CollectionAssert.AreEqual(new[] { false, false }, port.SuppressFlags);
    }

    // ---- 1-b. 팔라딘 성공 충격파 = 제압 간파 성공만(할 일 241) ----

    [TestCase(false, InterruptVariant.Normal, false, ExpectedResult = false)]
    [TestCase(false, InterruptVariant.Dash, true, ExpectedResult = false)]
    [TestCase(true, InterruptVariant.Normal, false, ExpectedResult = false)] // 일반 간파 성공
    [TestCase(true, InterruptVariant.Dash, false, ExpectedResult = false)]   // 대쉬 간파 성공
    [TestCase(true, InterruptVariant.Normal, true, ExpectedResult = true)]   // 제압 간파 성공
    [TestCase(true, InterruptVariant.Dash, true, ExpectedResult = true)]     // 대쉬로 제압해도 제압
    public bool 팔라딘_성공_충격파는_제압_간파_성공만(bool succeeded, InterruptVariant variant, bool suppress) =>
        InterruptPresentationTierRules.PlaysPaladinWave(InterruptPresentationTierRules.Select(succeeded, variant, suppress));

    // ---- 2. 오너 암전 = 단계 설정이 켬일 때만(Q1, 기본 전부 끔) · 실패 = 약한 줌만(Q3) ----

    private sealed class Timing : IInterruptOwnerPresentationSettings
    {
        public float OwnerDimInDuration => 0.1f;
        public float OwnerDimOutDuration => 0.2f;
    }

    [TestCase(InterruptAttemptForecast.Strong, true, ExpectedResult = true)]
    [TestCase(InterruptAttemptForecast.Strong, false, ExpectedResult = false)]
    [TestCase(InterruptAttemptForecast.Weak, true, ExpectedResult = false)]
    [TestCase(InterruptAttemptForecast.NoTarget, true, ExpectedResult = false)]
    public bool 시도_암전은_강_예측이고_단계가_암전_켬일_때만(InterruptAttemptForecast forecast, bool tierDims) =>
        InterruptPresentationTierRules.AttemptDims(forecast, new InterruptTierProfile(tierDims, InterruptTierFlash.HitFlash));

    [Test]
    public void 실패_단계는_설정이_없어_암전하지_않는다()
    {
        InterruptProductionDirector director = CreateDirector();
        Assert.IsNull(director.TierProfile(InterruptPresentationTier.Fail));
        Assert.IsFalse(InterruptPresentationTierRules.AttemptDims(InterruptAttemptForecast.Strong,
            director.TierProfile(InterruptPresentationTier.Fail)));
    }

    [Test]
    public void 디렉터_기본_단계_설정은_암전_없이_모두_V3()
    {
        InterruptProductionDirector director = CreateDirector();
        foreach (InterruptPresentationTier tier in new[]
                     { InterruptPresentationTier.Normal, InterruptPresentationTier.Dash, InterruptPresentationTier.Suppress })
        {
            InterruptTierProfile profile = director.TierProfile(tier);
            Assert.IsFalse(profile.ownerDim, $"{tier} = 암전 없음(제압도 11-04 할 일 211)");
            Assert.AreEqual(InterruptTierFlash.HitLightReference, profile.flash, $"{tier} = V3");
            Assert.AreEqual(InterruptDarkAdaptMode.SceneLight, profile.darkAdapt, $"{tier} = V3 암순응 나");
        }
    }

    [Test]
    public void 제압_단계도_일반과_같은_적중_라이트_선택()
    {
        // 211 — 제압 간파 = 시퀀스 대신 일반·대쉬와 같은 버전 표(근접·대쉬 무관, Q6).
        InterruptProductionDirector director = CreateDirector();
        Assert.AreEqual(InterruptPresentationTier.Suppress, InterruptPresentationTierRules.Select(true, InterruptVariant.Dash, true));
        InterruptSuccessLook suppress = InterruptPresentationTierRules.SelectSuccessLook(
            director.TierProfile(InterruptPresentationTier.Suppress), true, true);
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, suppress);
        Assert.AreEqual(InterruptPresentationTierRules.SelectSuccessLook(director.TierProfile(InterruptPresentationTier.Normal), true, true),
            suppress, "제압 = 일반과 같은 섬광");
        Assert.IsTrue(InterruptPresentationTierRules.UsesHitLight(suppress), "성공 순간 바로 — ②~⑥ 예약 없음");
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SuccessLook(InterruptPresentationTier.Suppress),
            "V3 데이터·프리팹 없는 디렉터 = V0 로 돌아간다(일반과 같은 규칙)");
    }

    [Test]
    public void 오너는_제압을_예측하지_않는다()
    {
        // 211 — 시도 순간 예측 단계는 일반·대쉬만(제압 예측·암전 제거). 제압은 서버 성공 슬로우 표식으로만.
        InterruptProductionDirector director = CreateDirector();
        Assert.IsFalse(InterruptPresentationTierRules.AttemptDims(InterruptAttemptForecast.Strong,
            director.TierProfile(InterruptPresentationTier.Suppress)), "제압 단계 설정도 암전 끔");
    }

    // ---- 194. 단계 × 버전 선택표 ----

    [TestCase(InterruptTierFlash.HitFlash, true, true, ExpectedResult = InterruptSuccessLook.HitFlash)]
    [TestCase(InterruptTierFlash.HitLight, true, false, ExpectedResult = InterruptSuccessLook.HitLight)]
    [TestCase(InterruptTierFlash.HitLight, false, true, ExpectedResult = InterruptSuccessLook.HitFlash)]   // 연출 없음 → V0
    [TestCase(InterruptTierFlash.HitLightWhiteout, true, false, ExpectedResult = InterruptSuccessLook.HitLightWhiteout)]
    [TestCase(InterruptTierFlash.HitLightWhiteout, false, true, ExpectedResult = InterruptSuccessLook.HitFlash)]
    [TestCase(InterruptTierFlash.HitLightReference, false, true, ExpectedResult = InterruptSuccessLook.HitLightReference)]
    [TestCase(InterruptTierFlash.HitLightReference, true, false, ExpectedResult = InterruptSuccessLook.HitFlash)] // V3 데이터 없음 → V0
    [TestCase((InterruptTierFlash)1, true, true, ExpectedResult = InterruptSuccessLook.HitFlash)]                // 옛 시퀀스 값(Legacy) → V0
    public InterruptSuccessLook 버전_선택표(InterruptTierFlash flash, bool hasHitLight, bool hasReference) =>
        InterruptPresentationTierRules.SelectSuccessLook(new InterruptTierProfile(false, flash), hasHitLight, hasReference);

    [Test]
    public void 설정이_없으면_V0()
    {
        Assert.AreEqual(InterruptSuccessLook.HitFlash, InterruptPresentationTierRules.SelectSuccessLook(null, true, true));
    }

    [TestCase(InterruptSuccessLook.HitLightWhiteout, true, 3, ExpectedResult = 3)]
    [TestCase(InterruptSuccessLook.HitLightWhiteout, false, 3, ExpectedResult = 0)] // 팀원 화면 = 화이트아웃 없음
    [TestCase(InterruptSuccessLook.HitLight, true, 3, ExpectedResult = 0)]
    [TestCase(InterruptSuccessLook.HitLightWhiteout, true, -1, ExpectedResult = 0)]
    public int V2_화이트아웃은_본인_화면만(InterruptSuccessLook look, bool viewerIsCaster, int frames) =>
        InterruptPresentationTierRules.WhiteoutFrames(look, viewerIsCaster, frames);

    [TestCase(InterruptSuccessLook.HitLight, ExpectedResult = true)]
    [TestCase(InterruptSuccessLook.HitLightWhiteout, ExpectedResult = true)]
    [TestCase(InterruptSuccessLook.HitLightReference, ExpectedResult = true)]
    [TestCase(InterruptSuccessLook.HitFlash, ExpectedResult = false)]
    public bool 적중_라이트_버전(InterruptSuccessLook look) => InterruptPresentationTierRules.UsesHitLight(look);

    [Test]
    public void 판정_성공_V0_섬광_순간_사건은_대쉬_종류를_싣는다()
    {
        // 194 — 오너 판정 cue 경로가 LocalSuccess 에 종류를 안 실어 대쉬가 일반 단계로 계산되던 것.
        var sm = new InterruptOwnerPresentation(new Timing());
        sm.Predict(InterruptAttemptForecast.Strong, null, 0f, Vector3.forward, dims: false);
        var moments = new List<InterruptHitFlashMoment>();
        var flash = new InterruptHitFlashSequencer(null, null, moments.Add);
        var cues = new InterruptOwnerJudgeCuePlayer(() => sm, () => flash, null);
        cues.Attempted();

        cues.JudgeSucceeded(1, Vector3.up, Vector3.zero, 0.1f, 0.1f, 1, InterruptSuccessLook.HitFlash, InterruptVariant.Dash);
        flash.Tick(1f, 100);

        Assert.AreEqual(1, moments.Count);
        Assert.AreEqual(InterruptVariant.Dash, moments[0].Event.Variant);
        Assert.AreEqual(InterruptPresentationTier.Dash, moments[0].Event.Tier);
    }

    [Test]
    public void 판정_성공이_적중_라이트면_섬광_예약_없이_로컬_발동_번호를_돌려준다()
    {
        var sm = new InterruptOwnerPresentation(new Timing());
        sm.Predict(InterruptAttemptForecast.Strong, null, 0f, Vector3.forward, dims: false);
        int flashAsked = 0;
        var cues = new InterruptOwnerJudgeCuePlayer(() => sm, () =>
        {
            flashAsked++;
            return null;
        }, null);
        cues.Attempted();

        uint id = cues.JudgeSucceeded(1, Vector3.up, Vector3.zero, 0.1f, 0.1f, 1, InterruptSuccessLook.HitLight, InterruptVariant.Normal);

        Assert.AreNotEqual(0u, id);
        Assert.AreEqual(0, flashAsked, "②~⑥ 진행기를 건드리지 않는다");
        Assert.AreEqual(InterruptSuccessLook.HitLight, cues.LocalSuccessLook);
        Assert.IsTrue(cues.ConsumeServerStarted(SlowMotionPurpose.Success), "뒤이은 내 서버 성공은 한 번 삼킨다");
    }

    [Test]
    public void 강_예측이어도_암전_끔이면_암전_빛모임_없이_강_상태만()
    {
        var sm = new InterruptOwnerPresentation(new Timing());
        Assert.IsTrue(sm.Predict(InterruptAttemptForecast.Strong, null, 10f, Vector3.forward, dims: false));
        sm.Tick(10.5f);

        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, sm.State, "강 상태 — 줌 붙잡기");
        Assert.IsTrue(sm.AwaitsStrongResult);
        Assert.IsFalse(sm.AttemptDims);
        Assert.AreEqual(0f, sm.DimIntensity, "암전 없음");
        Assert.IsFalse(sm.LightGatherActive, "빛 모임 없음");

        sm.ServerStarted(5, SlowMotionPurpose.Success, true, 10.6f);
        Assert.IsTrue(sm.SuccessDimmed, "강으로 남은 시도의 성공 = ②~⑥ 오버레이 조건 그대로");
        sm.Tick(10.8f);
        Assert.AreEqual(0f, sm.DimIntensity, "성공에서도 암전을 켜지 않는다");
        Assert.AreEqual(InterruptOwnerPresentationState.None, sm.State, "섬광 순간을 기다리지 않고 끝");
    }

    [Test]
    public void 암전_켬_단계로_예측한_강_시도는_암전과_빛모임을_켜고_섬광_순간에_끈다()
    {
        var sm = new InterruptOwnerPresentation(new Timing());
        sm.Predict(InterruptAttemptForecast.Strong, null, 10f, Vector3.forward, dims: true);
        sm.Tick(10.2f);
        Assert.AreEqual(1f, sm.DimIntensity);
        Assert.IsTrue(sm.LightGatherActive);

        sm.ServerStarted(7, SlowMotionPurpose.Success, true, 10.3f);
        sm.Tick(10.5f);
        Assert.AreEqual(1f, sm.DimIntensity, "섬광 순간까지 유지");
        sm.SuccessFlashMoment(sm.SuccessTriggerId, 10.6f);
        Assert.AreEqual(0f, sm.DimIntensity);
    }

    [Test]
    public void 약_예측은_아무것도_켜지_않고_판정_실패면_바로_끝()
    {
        var sm = new InterruptOwnerPresentation(new Timing());
        sm.Predict(InterruptAttemptForecast.Weak, null, 10f);
        sm.Tick(10.2f);
        Assert.AreEqual(0f, sm.DimIntensity, "Q3 약 = 약한 줌만(줌은 서버 약 시도 슬로우)");
        Assert.IsFalse(sm.LightGatherActive);

        sm.LocalEnded(10.3f);
        sm.Tick(10.31f);
        Assert.AreEqual(InterruptOwnerPresentationState.None, sm.State);
    }

    [Test]
    public void 암전_없는_강_시도는_전투_표시를_숨기지_않는다()
    {
        // 디렉터 A5 는 '암전 없는 강 시도'를 None 으로 넘긴다(OwnerStateForCombatUi) — 규칙 자체는 그대로.
        Assert.IsFalse(InterruptProductionRules.ShouldHideCombatUi(true, InterruptOwnerPresentationState.None, 0f, false));
        Assert.IsTrue(InterruptProductionRules.ShouldHideCombatUi(true, InterruptOwnerPresentationState.AttemptStrong, 0f, false));
    }

    // ---- 5a. 피해 판정 선착순(매니저 승인 장부) ----

    [Test]
    public void 남이_승인받은_창에_늦게_들어간_시전은_표식을_뗀다()
    {
        var ledger = new InterruptClaimLedger();
        const ulong target = 50, a = 1, b = 2;
        Assert.AreEqual(InterruptClaimOrder.First, ledger.Register(target, 7, a, 10.0, 0.05));

        Assert.IsTrue(ledger.PreemptsCast(target, b, 9.8, 10.2), "첫 승인 이전에 시작한 남의 시전 = 피해만");
        Assert.IsTrue(ledger.PreemptsCast(target, b, 10.0, 10.2), "같은 시각 포함");
        Assert.IsFalse(ledger.PreemptsCast(target, a, 9.8, 10.2), "승인받은 본인");
        Assert.IsFalse(ledger.PreemptsCast(target, b, 10.1, 10.2), "첫 승인 뒤 새로 시작한 시전 = 그대로(취약 중 간파 넉백 규칙)");
        Assert.IsFalse(ledger.PreemptsCast(99, b, 9.8, 10.2), "다른 대상");

        Assert.AreEqual(InterruptClaimOrder.Simultaneous, ledger.Register(target, 7, b, 10.04, 0.05));
        Assert.IsFalse(ledger.PreemptsCast(target, b, 9.8, 10.2), "동시 성공자는 승인 목록에 있다 — 표식은 승인 경로가 뗀다");

        Assert.IsFalse(ledger.PreemptsCast(target, 3, 9.8, 11.5), "보관 1초 밖은 정리");
    }

    // ---- 도우미 ----

    private InterruptProductionDirector CreateDirector()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        return go.AddComponent<InterruptProductionDirector>();
    }
}
