using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 간파 연출 4단계(할 일 192) — 단계 선택표, 슬로우 Start 이벤트 표식 바이트(제압·대쉬), 서버 성공 사건의 대쉬 칸,
/// 오너 암전 = 제압 간파만(Q1)·실패 = 약한 줌만(Q3), 제압 섬광 시퀀스 시작 규칙, 제압 암순응 컷 진행(Q7), 피해 판정 선착순(5a).
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

    // ---- 2. 오너 암전 = 제압 간파만(Q1) · 실패 = 약한 줌만(Q3) ----

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
        InterruptProductionDirector director = CreateDirector(null);
        Assert.IsNull(director.TierProfile(InterruptPresentationTier.Fail));
        Assert.IsFalse(InterruptPresentationTierRules.AttemptDims(InterruptAttemptForecast.Strong,
            director.TierProfile(InterruptPresentationTier.Fail)));
    }

    [Test]
    public void 디렉터_기본_단계_설정은_제압만_암전과_시퀀스()
    {
        InterruptProductionDirector director = CreateDirector(null);
        Assert.IsFalse(director.TierProfile(InterruptPresentationTier.Normal).ownerDim, "Q1 일반 = 암전 없음");
        Assert.IsFalse(director.TierProfile(InterruptPresentationTier.Dash).ownerDim, "Q1 대쉬 = 암전 없음");
        Assert.IsTrue(director.TierProfile(InterruptPresentationTier.Suppress).ownerDim, "Q1 제압 = 오너 암전");
        Assert.AreEqual(InterruptTierFlash.HitFlash, director.TierProfile(InterruptPresentationTier.Normal).flash, "Q4 보류 = ②~⑥");
        Assert.AreEqual(InterruptTierFlash.HitFlash, director.TierProfile(InterruptPresentationTier.Dash).flash, "Q5 보류 = ②~⑥");
        Assert.AreEqual(InterruptTierFlash.SuppressSequence, director.TierProfile(InterruptPresentationTier.Suppress).flash);
        Assert.AreEqual(SuppressDarkAdaptMode.Cut, director.SuppressDarkAdaptMode, "Q7 기본 = 컷");
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
    public void 제압_예측_강_시도는_암전과_빛모임을_켜고_섬광_순간에_끈다()
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
        Assert.IsFalse(InterruptProductionRules.ShouldHideCombatUi(true, InterruptOwnerPresentationState.None, 0f, false, false));
        Assert.IsTrue(InterruptProductionRules.ShouldHideCombatUi(true, InterruptOwnerPresentationState.AttemptStrong, 0f, false, false));
    }

    // ---- 2. 오너 판정 성공 = 제압 예측이면 ②~⑥ 대신 시퀀스 ----

    [Test]
    public void 판정_성공이_시퀀스면_섬광_예약_없이_로컬_발동_번호를_돌려준다()
    {
        var sm = new InterruptOwnerPresentation(new Timing());
        sm.Predict(InterruptAttemptForecast.Strong, null, 0f, Vector3.forward, dims: true);
        int flashAsked = 0;
        var cues = new InterruptOwnerJudgeCuePlayer(() => sm, () =>
        {
            flashAsked++;
            return null;
        }, null);
        cues.Attempted();

        uint id = cues.JudgeSucceeded(1, Vector3.up, Vector3.zero, 0.1f, 0.1f, 1, suppressSequence: true);

        Assert.AreNotEqual(0u, id);
        Assert.AreEqual(id, sm.SuccessTriggerId);
        Assert.AreEqual(0, flashAsked, "②~⑥ 진행기를 건드리지 않는다");
        Assert.IsTrue(cues.LocalSuccessPlayedSuppress);
        Assert.IsTrue(cues.ConsumeServerStarted(SlowMotionPurpose.Success), "뒤이은 내 서버 성공은 한 번 삼킨다");
    }

    [TestCase(true, false, false, ExpectedResult = true)]   // 팀원 화면·cue 경로 아님
    [TestCase(true, true, false, ExpectedResult = true)]    // 오너: 예측은 제압 아님 → 서버 제압 = 지금 시퀀스로
    [TestCase(true, true, true, ExpectedResult = false)]    // 오너: 판정 순간 이미 시퀀스
    [TestCase(false, true, true, ExpectedResult = false)]   // 서버는 제압 아님 — 이미 튼 시퀀스는 끝까지
    [TestCase(false, false, false, ExpectedResult = false)]
    public bool 서버_성공에서_시퀀스_시작(bool playsSequence, bool ownerCueHandled, bool ownerPlayed) =>
        InterruptPresentationTierRules.StartsSuppressSequenceOnServerSuccess(playsSequence, ownerCueHandled, ownerPlayed);

    // ---- 3. 제압 = 대쉬 세트 포함(Q6) · 시퀀스 선택 ----

    [Test]
    public void 시퀀스는_제압_단계만_근접_대쉬_무관()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        InterruptProductionDirector director = CreateDirector(sequence);

        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, InterruptPresentationTier.Normal));
        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, InterruptPresentationTier.Dash));
        Assert.IsTrue(director.PlaysSuppressFlash(SlowMotionPurpose.Success, InterruptPresentationTier.Suppress));
        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, InterruptPresentationTier.Fail));
        Assert.IsTrue(director.PlaysSuppressFlash(SlowMotionPurpose.Success,
            InterruptPresentationTierRules.Select(true, InterruptVariant.Dash, true)), "대쉬 + 제압 = 제압");

        director.SuppressFlashEnabled = false;
        Assert.IsFalse(director.PlaysSuppressFlash(SlowMotionPurpose.Success, InterruptPresentationTier.Suppress), "A6 끔 = ②~⑥");
    }

    // ---- 3. 제압 암순응 컷(Q7) ----

    private sealed class FakeFlashOutput : ISuppressFlashOutput
    {
        public int Hides;
        public void SetSubjects(IReadOnlyList<GameObject> subjects) { }
        public void Show(in SuppressFlashFrame frame, Vector3 worldCenter, SuppressFlashSequence sequence) { }
        public void Hide() => Hides++;
    }

    private sealed class FakeDarkAdapt : ISuppressDarkAdaptOutput
    {
        public readonly List<float> Cuts = new List<float>();
        public int Smooth;
        public int Clears;
        public void ShowDarkAdaptCut(float darkness, ulong casterClientId, Vector3 targetPosition) => Cuts.Add(darkness);
        public void PlayDarkAdaptSmooth(ulong casterClientId, Vector3 targetPosition) => Smooth++;
        public void ClearDarkAdapt() => Clears++;
    }

    [Test]
    public void 암순응_컷_기본값은_어두움_중간_두_컷()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        Assert.AreEqual(2, sequence.darkAdaptCuts.Count);
        Assert.AreEqual(1f, sequence.darkAdaptCuts[0].darkness);
        Assert.AreEqual(0.5f, sequence.darkAdaptCuts[1].darkness);

        sequence.ResetToReference();
        Assert.AreEqual(2, sequence.darkAdaptCuts.Count, "B 도 같은 암순응 컷(초 단위라 프레임 속도 무관)");
    }

    [Test]
    public void 암순응_컷_시간표()
    {
        var cuts = new List<SuppressDarkAdaptCut> { new SuppressDarkAdaptCut(0.1f, 1f), new SuppressDarkAdaptCut(0.2f, 0.4f) };

        Assert.IsTrue(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, 0f, out int i, out float d));
        Assert.AreEqual(0, i);
        Assert.AreEqual(1f, d);
        Assert.IsTrue(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, 0.15f, out i, out d));
        Assert.AreEqual(1, i);
        Assert.AreEqual(0.4f, d, 1e-5f);
        Assert.IsFalse(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, 0.31f, out _, out _), "끝 = 원래 색");
        Assert.IsFalse(SuppressFlashRules.TryEvaluateDarkAdapt(null, 0f, out _, out _));
        Assert.IsFalse(SuppressFlashRules.TryEvaluateDarkAdapt(cuts, -0.01f, out _, out _));
    }

    [Test]
    public void 컷_방식은_시퀀스_끝_뒤_어두움_중간_원래_색으로_끊어_진행()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        float end = sequence.TotalFrames / sequence.frameRate;
        var flash = new FakeFlashOutput();
        var dark = new FakeDarkAdapt();
        var player = new SuppressFlashPlayer(() => sequence, () => false, flash, dark);

        player.Begin(0f, Vector3.zero, null, SuppressDarkAdaptMode.Cut, 4, Vector3.one);
        player.Tick(end - 0.01f);
        Assert.IsTrue(player.IsPlaying);
        Assert.IsFalse(player.DarkAdaptActive);
        Assert.AreEqual(0, dark.Cuts.Count, "시퀀스 중엔 암순응 없음");

        player.Tick(end + 0.05f);
        Assert.IsFalse(player.IsPlaying, "시퀀스 끝");
        Assert.AreEqual(1, flash.Hides);
        Assert.IsTrue(player.DarkAdaptActive);
        Assert.AreEqual(0, player.DarkAdaptCutIndex);
        CollectionAssert.AreEqual(new[] { 1f }, dark.Cuts, "어두움");

        player.Tick(end + 0.1f);
        Assert.AreEqual(1, dark.Cuts.Count, "같은 컷이면 다시 안 보낸다");

        player.Tick(end + 0.2f);
        Assert.AreEqual(1, player.DarkAdaptCutIndex);
        CollectionAssert.AreEqual(new[] { 1f, 0.5f }, dark.Cuts, "중간");

        player.Tick(end + 0.31f);
        Assert.IsFalse(player.DarkAdaptActive, "원래 색");
        Assert.AreEqual(1, dark.Clears);
        Assert.AreEqual(0, dark.Smooth);
    }

    [Test]
    public void 부드럽게_방식은_시퀀스_끝에_곡선을_한_번_튼다()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        float end = sequence.TotalFrames / sequence.frameRate;
        var dark = new FakeDarkAdapt();
        var player = new SuppressFlashPlayer(() => sequence, () => false, new FakeFlashOutput(), dark);

        player.Begin(0f, Vector3.zero, null, SuppressDarkAdaptMode.Smooth);
        player.Tick(end + 0.05f);
        player.Tick(end + 0.5f);

        Assert.AreEqual(1, dark.Smooth);
        Assert.AreEqual(0, dark.Cuts.Count);
        Assert.IsFalse(player.DarkAdaptActive);
        Assert.AreEqual(SuppressDarkAdaptMode.Smooth, player.DarkAdaptMode);
    }

    [Test]
    public void 암순응_컷_중_멈춤_새_성공은_즉시_걷는다()
    {
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        float end = sequence.TotalFrames / sequence.frameRate;
        var dark = new FakeDarkAdapt();
        var player = new SuppressFlashPlayer(() => sequence, () => false, new FakeFlashOutput(), dark);

        player.Begin(0f, Vector3.zero, null);
        player.Tick(end + 0.05f);
        Assert.IsTrue(player.DarkAdaptActive);
        player.Stop();
        Assert.IsFalse(player.DarkAdaptActive);
        Assert.AreEqual(1, dark.Clears);

        player.Begin(10f, Vector3.zero, null);
        player.Tick(10f + end + 0.05f);
        player.Begin(20f, Vector3.zero, null);
        Assert.AreEqual(2, dark.Clears, "새 성공 = 남은 암순응 컷을 걷고 처음부터");
        Assert.IsTrue(player.IsPlaying);
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

    private InterruptProductionDirector CreateDirector(SuppressFlashSequence sequence)
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        if (sequence != null)
        {
            var so = new SerializedObject(director);
            so.FindProperty("suppressFlashSequence").objectReferenceValue = sequence;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        return director;
    }
}
