using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 간파 연출 단계(할 일 118 C3) — 가짜 시간 포트로 서버 사건 4개가 지금과 같은 슬로우 호출을 내는지 본다.
/// 회귀 핵심: 시도(예측) → 판정(빗나감 실패 복귀) → 성공(성공 프로필 재시작·대상 위치·A1) 순서와 발동 번호.
/// </summary>
public sealed class InterruptProductionStageTests
{
    // ─── 가짜 ───

    internal sealed class FakeSettings : IInterruptProductionSettings
    {
        public SlowMotionProfile Attempt = new SlowMotionProfile { scale = 0.15f };
        public SlowMotionProfile Weak = new SlowMotionProfile { scale = 0.5f };
        public SlowMotionProfile Success = new SlowMotionProfile { scale = 0.25f, hitStopDuration = 0.05f };
        public readonly HashSet<InterruptProductionCue> On = new HashSet<InterruptProductionCue>();

        public SlowMotionProfile InterruptProfile => Attempt;
        public SlowMotionProfile InterruptWeakProfile => Weak;
        public SlowMotionProfile InterruptSuccessProfile => Success;
        public bool Get(InterruptProductionCue cue) => On.Contains(cue);
    }

    internal sealed class FakeTimeScale : ITimeScalePort
    {
        public readonly List<string> Calls = new List<string>();
        public readonly List<(SlowMotionProfile profile, ulong clientId, SlowMotionPurpose purpose, Vector3 position, bool hitStopOn)> Starts =
            new List<(SlowMotionProfile, ulong, SlowMotionPurpose, Vector3, bool)>();
        public readonly List<uint> Fails = new List<uint>();
        public uint NextId = 7;

        public uint ServerStart(SlowMotionProfile profile, ulong triggerClientId, SlowMotionPurpose purpose, Vector3 targetPosition, bool hitStopOn)
        {
            Calls.Add("Start:" + purpose);
            Starts.Add((profile, triggerClientId, purpose, targetPosition, hitStopOn));
            return NextId;
        }

        public void ServerFail(uint triggerId)
        {
            Calls.Add("Fail:" + triggerId);
            Fails.Add(triggerId);
        }
    }

    private sealed class FakeRelay : IInterruptSuccessRelay
    {
        public int Count;
        public InterruptProductionEvent Last;

        public void ServerRelaySuccess(in InterruptProductionEvent e)
        {
            Count++;
            Last = e;
        }
    }

    private FakeSettings _settings;
    private FakeTimeScale _port;
    private InterruptAttemptStage _attempt;
    private InterruptResolveStage _resolve;
    private InterruptSuccessStage _success;
    private InterruptFailStage _fail;

    [SetUp]
    public void SetUp()
    {
        _settings = new FakeSettings();
        _port = new FakeTimeScale();
        _attempt = new InterruptAttemptStage(_settings, _port);
        _resolve = new InterruptResolveStage(_port);
        _success = new InterruptSuccessStage(_settings, _port);
        _fail = new InterruptFailStage(_port);
    }

    private const ulong Caster = 3;

    private static InterruptProductionEvent Success(bool hasTarget, Vector3 target, Vector3 caster) =>
        InterruptProductionEvent.Success(Caster, null, null, hasTarget, target, caster);

    // ① 실패 예측 → 약 프로필·AttemptWeak 로 1회(할 일 132 — 시도 슬로우는 늘 건다)
    [Test]
    public void 시도_실패_예측이면_약_프로필로_1회()
    {
        uint id = _attempt.Server(InterruptProductionEvent.Attempt(Caster, null, null, predictsHit: false));
        Assert.AreEqual(7u, id);
        Assert.AreEqual(1, _port.Starts.Count);
        Assert.AreSame(_settings.Weak, _port.Starts[0].profile);
        Assert.AreEqual(Caster, _port.Starts[0].clientId);
        Assert.AreEqual(SlowMotionPurpose.AttemptWeak, _port.Starts[0].purpose);
        Assert.AreEqual(Vector3.zero, _port.Starts[0].position);
    }

    // ② 성공 예측 → 강(옛 시도) 프로필로 1회
    [Test]
    public void 시도_성공_예측이면_강_프로필로_1회()
    {
        uint id = _attempt.Server(InterruptProductionEvent.Attempt(Caster, null, null, predictsHit: true));
        Assert.AreEqual(7u, id);
        Assert.AreEqual(1, _port.Starts.Count);
        Assert.AreSame(_settings.Attempt, _port.Starts[0].profile);
        Assert.AreEqual(Caster, _port.Starts[0].clientId);
        Assert.AreEqual(SlowMotionPurpose.Attempt, _port.Starts[0].purpose);
        Assert.AreEqual(Vector3.zero, _port.Starts[0].position);
    }

    [Test]
    public void 시도_슬로우도_히트스톱_칸을_넘긴다()
    {
        _attempt.Server(InterruptProductionEvent.Attempt(Caster, null, null, true));
        _settings.On.Add(InterruptProductionCue.HitStop);
        _attempt.Server(InterruptProductionEvent.Attempt(Caster, null, null, true));
        Assert.IsFalse(_port.Starts[0].hitStopOn);
        Assert.IsTrue(_port.Starts[1].hitStopOn);
    }

    // ③ 판정 landed=false, 번호≠0 → ServerFail(번호)
    [Test]
    public void 판정_빗나감이면_시도_슬로우_실패_복귀()
    {
        _resolve.Server(InterruptProductionEvent.Resolve(Caster, null, null, attemptTriggerId: 5, landed: false, succeeded: false, resolvedCount: 0));
        CollectionAssert.AreEqual(new[] { 5u }, _port.Fails);
    }

    // ④ landed=true → Fail 없음
    [Test]
    public void 판정_적중이면_실패_없음()
    {
        _resolve.Server(InterruptProductionEvent.Resolve(Caster, null, null, 5, landed: true, succeeded: true, resolvedCount: 1));
        _resolve.Server(InterruptProductionEvent.Resolve(Caster, null, null, 0, landed: false, succeeded: false, resolvedCount: 0));
        Assert.IsEmpty(_port.Calls);
    }

    // ⑤ 성공 → ServerStart(성공 프로필, Success, 대상 위치, hitStopOn=칸 값)
    [TestCase(false)]
    [TestCase(true)]
    public void 성공은_성공_프로필_대상_위치_히트스톱_칸(bool hitStopOn)
    {
        if (hitStopOn)
            _settings.On.Add(InterruptProductionCue.HitStop);
        var target = new Vector3(1, 2, 3);

        _success.Server(Success(true, target, Vector3.one), null);

        Assert.AreEqual(1, _port.Starts.Count);
        Assert.AreSame(_settings.Success, _port.Starts[0].profile);
        Assert.AreEqual(Caster, _port.Starts[0].clientId);
        Assert.AreEqual(SlowMotionPurpose.Success, _port.Starts[0].purpose);
        Assert.AreEqual(target, _port.Starts[0].position);
        Assert.AreEqual(hitStopOn, _port.Starts[0].hitStopOn);
    }

    // ⑥ 성공인데 landed=false → Fail 다음 Start 순서(스킬이 판정 → 성공 순서로 알린다)
    [Test]
    public void 성공인데_예측_대기에_못_잡혔으면_실패_다음_성공_시작()
    {
        uint id = _attempt.Server(InterruptProductionEvent.Attempt(Caster, null, null, true));
        _resolve.Server(InterruptProductionEvent.Resolve(Caster, null, null, id, landed: false, succeeded: true, resolvedCount: 1));
        _success.Server(Success(true, Vector3.one, Vector3.zero), null);

        CollectionAssert.AreEqual(new[] { "Start:Attempt", "Fail:7", "Start:Success" }, _port.Calls);
    }

    // ⑦ 대상 없음 → 시전자 위치
    [Test]
    public void 성공_대상이_없으면_시전자_위치()
    {
        var caster = new Vector3(4, 0, 5);
        _success.Server(Success(false, new Vector3(9, 9, 9), caster), null);
        Assert.AreEqual(caster, _port.Starts[0].position);
    }

    // ⑧ 판정 전 종료 → Fail(Abort 번호), 번호 0 이면 무호출
    [Test]
    public void 판정_전_종료는_발동_번호가_있을_때만_실패_복귀()
    {
        _fail.Server(InterruptProductionEvent.Fail(Caster, null, null, InterruptFailReason.EndedBeforeResolve, 0));
        Assert.IsEmpty(_port.Calls);

        _fail.Server(InterruptProductionEvent.Fail(Caster, null, null, InterruptFailReason.EndedBeforeResolve, 9));
        _fail.Server(InterruptProductionEvent.Fail(Caster, null, null, InterruptFailReason.NoHitboxAnchor, 11));
        CollectionAssert.AreEqual(new[] { 9u, 11u }, _port.Fails);
    }

    // ⑨ relay 는 성공 1회당 relay 마다 1회, 슬로우가 거부돼도(번호 0) 부른다
    [Test]
    public void 성공_relay_는_성공마다_relay_당_1회()
    {
        var a = new FakeRelay();
        var b = new FakeRelay();
        _port.NextId = 0; // 컷신 잠금 등으로 슬로우 거부

        _success.Server(Success(true, Vector3.one, Vector3.zero), new IInterruptSuccessRelay[] { a, null, b });

        Assert.AreEqual(1, a.Count);
        Assert.AreEqual(1, b.Count);
        Assert.AreEqual(InterruptProductionPhase.Success, a.Last.Phase);
        Assert.AreEqual(Caster, a.Last.CasterClientId);
    }

    [Test]
    public void 실패_판정_시도_단계는_relay_를_부르지_않는다()
    {
        // relay 는 성공 단계에만 넘어간다 — 다른 단계는 받는 창구가 없다(컴파일 수준 보장). 성공 0회면 0.
        var relay = new FakeRelay();
        _attempt.Server(InterruptProductionEvent.Attempt(Caster, null, null, true));
        _resolve.Server(InterruptProductionEvent.Resolve(Caster, null, null, 7, false, false, 0));
        _fail.Server(InterruptProductionEvent.Fail(Caster, null, null, InterruptFailReason.EndedBeforeResolve, 7));
        Assert.AreEqual(0, relay.Count);
    }

    [Test]
    public void 디렉터_인스턴스가_없으면_서버_사건은_무시()
    {
        Assume.That(InterruptProductionDirector.Instance == null, "EditMode 에 인스턴스가 살아 있다");
        Assert.AreEqual(0u, InterruptProductionDirector.ServerAttempt(InterruptProductionEvent.Attempt(Caster, null, null, true)));
        Assert.DoesNotThrow(() =>
        {
            InterruptProductionDirector.ServerResolve(InterruptProductionEvent.Resolve(Caster, null, null, 3, false, false, 0));
            InterruptProductionDirector.ServerSuccess(Success(true, Vector3.one, Vector3.zero));
            InterruptProductionDirector.ServerFail(InterruptProductionEvent.Fail(Caster, null, null, InterruptFailReason.Missed, 3));
        });
    }

    [Test]
    public void 사건_팩토리는_시전자가_없어도_clientId_0()
    {
        var e = InterruptProductionEvent.Fail((Player)null, null, InterruptFailReason.NoHitboxAnchor, 4);
        Assert.AreEqual(0ul, e.CasterClientId);
        Assert.AreEqual(4u, e.AttemptTriggerId);
        Assert.AreEqual(InterruptProductionPhase.Fail, e.Phase);
        Assert.AreEqual(InterruptFailReason.NoHitboxAnchor, e.FailReason);
    }
}
