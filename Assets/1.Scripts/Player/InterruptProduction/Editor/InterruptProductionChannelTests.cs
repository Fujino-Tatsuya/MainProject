using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 간파 연출 피어 단계·개인/공용 채널(할 일 118 C4) — 보는 사람 기준으로 암순응 강도가 갈리고, 칸이 꺼지면 sink 를 안 부르며,
/// 시도 슬로우엔 무반응, 강제 리셋엔 암순응을 끈다.
/// </summary>
public sealed class InterruptProductionChannelTests
{
    private sealed class FakeDarkAdaptation : IInterruptDarkAdaptationOutput
    {
        public readonly List<(bool viewerIsCaster, ulong casterClientId, Vector3 target)> Plays =
            new List<(bool, ulong, Vector3)>();
        public int Stops;

        public void Play(bool viewerIsCaster, ulong casterClientId, Vector3 targetPosition) =>
            Plays.Add((viewerIsCaster, casterClientId, targetPosition));

        public void Stop() => Stops++;
    }

    private sealed class FakeSink : IInterruptCueSink
    {
        public readonly List<InterruptProductionCue> Cues = new List<InterruptProductionCue>();
        public InterruptProductionEvent Last;

        public void PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e)
        {
            Cues.Add(cue);
            Last = e;
        }
    }

    private InterruptProductionStageTests.FakeSettings _settings;
    private InterruptSuccessStage _success;
    private FakeDarkAdaptation _dark;
    private FakeSink _sink;

    private static readonly Vector3 Target = new Vector3(1, 2, 3);
    private static InterruptProductionEvent Event => InterruptProductionEvent.LocalSuccess(4, 2, Target);

    [SetUp]
    public void SetUp()
    {
        _settings = new InterruptProductionStageTests.FakeSettings();
        _success = new InterruptSuccessStage(_settings, new InterruptProductionStageTests.FakeTimeScale());
        _dark = new FakeDarkAdaptation();
        _sink = new FakeSink();
    }

    private void Started(SlowMotionPurpose purpose, bool viewerIsCaster) =>
        _success.LocalStarted(purpose, Event, viewerIsCaster, _dark, new IInterruptCueSink[] { _sink });

    // ─── 개인 / 공용 ───

    [Test]
    public void 본인_화면은_암순응_self_강도()
    {
        _settings.On.Add(InterruptProductionCue.DarkAdaptation);
        Started(SlowMotionPurpose.Success, viewerIsCaster: true);
        Assert.AreEqual(1, _dark.Plays.Count);
        Assert.IsTrue(_dark.Plays[0].viewerIsCaster);
        Assert.AreEqual(2ul, _dark.Plays[0].casterClientId);
        Assert.AreEqual(Target, _dark.Plays[0].target);
    }

    [Test]
    public void 다른_사람_화면은_암순응_others_강도()
    {
        _settings.On.Add(InterruptProductionCue.DarkAdaptation);
        Started(SlowMotionPurpose.Success, viewerIsCaster: false);
        Assert.AreEqual(1, _dark.Plays.Count);
        Assert.IsFalse(_dark.Plays[0].viewerIsCaster);
    }

    [Test]
    public void 오너_줌은_개인_채널_칸이_켜지면_배율_꺼지면_1()
    {
        Assert.AreEqual(1f, _success.OwnerZoomFactor(0.85f));
        _settings.On.Add(InterruptProductionCue.OwnerZoom);
        Assert.AreEqual(0.85f, _success.OwnerZoomFactor(0.85f));
    }

    [Test]
    public void 성공_이펙트_칸은_보는_사람과_무관하게_공용()
    {
        _settings.On.Add(InterruptProductionCue.SuccessFxSlot);
        Started(SlowMotionPurpose.Success, viewerIsCaster: true);
        Started(SlowMotionPurpose.Success, viewerIsCaster: false);
        CollectionAssert.AreEqual(new[] { InterruptProductionCue.SuccessFxSlot, InterruptProductionCue.SuccessFxSlot }, _sink.Cues);
        Assert.AreEqual(Target, _sink.Last.TargetPosition);
        Assert.AreEqual(2ul, _sink.Last.CasterClientId);
    }

    // ─── 스위치 ───

    [Test]
    public void 칸이_꺼지면_sink_와_암순응_호출_0()
    {
        Started(SlowMotionPurpose.Success, viewerIsCaster: true);
        Started(SlowMotionPurpose.Success, viewerIsCaster: false);
        Assert.IsEmpty(_dark.Plays);
        Assert.IsEmpty(_sink.Cues);
    }

    [Test]
    public void 공용_채널_PlayCue_는_칸이_켜진_cue_만_모든_sink_에()
    {
        var shared = new InterruptSharedChannel(_settings);
        var other = new FakeSink();
        var sinks = new IInterruptCueSink[] { _sink, null, other };

        shared.PlayCue(InterruptProductionCue.TargetFlash, Event, sinks);
        Assert.IsEmpty(_sink.Cues);

        _settings.On.Add(InterruptProductionCue.TargetFlash);
        shared.PlayCue(InterruptProductionCue.TargetFlash, Event, sinks);
        Assert.AreEqual(1, _sink.Cues.Count);
        Assert.AreEqual(1, other.Cues.Count);
        Assert.DoesNotThrow(() => shared.PlayCue(InterruptProductionCue.TargetFlash, Event, null));
    }

    // ─── 단계 ───

    [Test]
    public void 시도_슬로우_Started_는_성공_단계_무반응()
    {
        foreach (InterruptProductionCue cue in System.Enum.GetValues(typeof(InterruptProductionCue)))
            _settings.On.Add(cue);

        Started(SlowMotionPurpose.Attempt, viewerIsCaster: true);
        Started(SlowMotionPurpose.Attempt, viewerIsCaster: false);

        Assert.IsEmpty(_dark.Plays);
        Assert.IsEmpty(_sink.Cues);
    }

    [Test]
    public void 강제_리셋은_칸과_무관하게_암순응_Stop()
    {
        _success.LocalForcedReset(_dark);
        Assert.AreEqual(1, _dark.Stops);
        Assert.DoesNotThrow(() => _success.LocalForcedReset(null));
    }

    [Test]
    public void 암순응_창구가_없으면_건너뛴다()
    {
        _settings.On.Add(InterruptProductionCue.DarkAdaptation);
        _settings.On.Add(InterruptProductionCue.SuccessFxSlot);
        Assert.DoesNotThrow(() => _success.LocalStarted(SlowMotionPurpose.Success, Event, true, null, new IInterruptCueSink[] { _sink }));
        Assert.AreEqual(1, _sink.Cues.Count);
    }

    [Test]
    public void 보는_사람_판단()
    {
        Assert.IsTrue(InterruptProductionRules.IsCasterView(3, 3));
        Assert.IsFalse(InterruptProductionRules.IsCasterView(3, 0));
    }

    [Test]
    public void 피어_실패_단계는_연출_없음()
    {
        var port = new InterruptProductionStageTests.FakeTimeScale();
        Assert.DoesNotThrow(() => new InterruptFailStage(port).Local(InterruptProductionEvent.LocalFail(5)));
        Assert.IsEmpty(port.Calls);
    }

    // ─── 성공 이펙트 칸 sink(InterruptSuccessFx) ───

    [Test]
    public void InterruptSuccessFx_는_다른_cue_와_스폰_전에는_재생_안_함()
    {
        var go = new GameObject("Player");
        try
        {
            var fx = go.AddComponent<InterruptSuccessFx>();
            // EditMode 에서는 Awake 가 안 돌아 NetworkObject 를 못 찾는다 — 스폰 전과 같다. 칸이 비어 있어도 예외 없어야 한다.
            Assert.DoesNotThrow(() => fx.PlayCue(InterruptProductionCue.SuccessFxSlot, Event));
            Assert.DoesNotThrow(() => fx.PlayCue(InterruptProductionCue.TargetFlash, Event));
            Assert.IsInstanceOf<IInterruptCueSink>(fx);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
