using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 191 3단계 — 클라 판정 신고의 서버 검증(<see cref="InterruptClaimValidator"/>)·동시 성공 장부(<see cref="InterruptClaimLedger"/>)·처리 순서(<see cref="InterruptClaimServer"/>).
/// 세계는 가짜: 대상 = 수직 원기둥(반지름 0.5), 간파 상자 = 중심 (0, 1.3, 0.9) · 반 (0.7, 1.2, 0.7) → 정면 도달 1.6m(+반지름 = 대상 중심 2.1m 까지 닿음).
/// 허용 오차 기본값 = 시각 지연 절반 + 0.033초 · 거리 0.2m · 각도 5° · 동시 0.05초.
/// </summary>
public sealed class InterruptClaimServerTests
{
    const ulong CasterA = 100;
    const ulong CasterB = 101;
    const ulong CasterC = 102;
    const ulong Boss = 200;
    const ulong HostClient = 0;
    const ulong ClientA = 7;
    const float TargetRadius = 0.5f;

    // ---- 가짜 세계 ----

    sealed class FakeWorld : IInterruptJudgeWorld
    {
        // 시전자별 (이 시각부터, 표본) — 시각 오름차순으로 넣는다.
        public readonly Dictionary<ulong, List<(double from, InterruptActorSample sample)>> Actors =
            new Dictionary<ulong, List<(double, InterruptActorSample)>>();
        public readonly Dictionary<ulong, InterruptTargetSample> Targets = new Dictionary<ulong, InterruptTargetSample>();
        public readonly List<ulong> TargetIds = new List<ulong>();
        public readonly InterruptWindowSchedule Schedule = new InterruptWindowSchedule();

        public void SetActor(ulong id, InterruptActorSample sample, double from = double.NegativeInfinity)
        {
            if (!Actors.TryGetValue(id, out var list))
                Actors[id] = list = new List<(double, InterruptActorSample)>();
            list.Add((from, sample));
        }

        public void AddTarget(ulong id, InterruptTargetSample sample)
        {
            Targets[id] = sample;
            if (!TargetIds.Contains(id))
                TargetIds.Add(id);
        }

        public bool TryGetActorAt(ulong actorId, double time, out InterruptActorSample sample)
        {
            sample = default;
            if (!Actors.TryGetValue(actorId, out var list))
                return false;
            bool found = false;
            foreach ((double from, InterruptActorSample s) in list)
            {
                if (from > time)
                    break;
                sample = s;
                found = true;
            }
            return found;
        }

        public int TargetCount => TargetIds.Count;
        public ulong GetTargetId(int index) => TargetIds[index];
        public bool TryGetTargetAt(ulong targetId, double time, out InterruptTargetSample sample) => Targets.TryGetValue(targetId, out sample);

        public bool TryGetLatestWindowOpenedBy(ulong targetId, double time, out InterruptWindowRecord record) =>
            Schedule.TryGetLatestOpenedBy(targetId, time, out record);

        public bool ShapeHitsTarget(ulong targetId, in HitboxAnchorShape shape, Vector3 targetPosition, int layers)
        {
            Vector3 local = Quaternion.Inverse(shape.Box.orientation) * (targetPosition - shape.Box.center);
            float dx = Mathf.Max(0f, Mathf.Abs(local.x) - shape.Box.halfExtents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(local.z) - shape.Box.halfExtents.z);
            return dx * dx + dz * dz <= TargetRadius * TargetRadius + 1e-6f;
        }

        public PlayerDashPathResult SimulateDash(ulong actorId, Vector3 start, Vector3 direction, float speed, float remaining) =>
            new PlayerDashPathResult { End = start, Duration = 0f };

        public uint OpenWindow(double openAt, double plannedClose) =>
            Schedule.ServerOpen(Boss, openAt, openAt, plannedClose, InterruptWindowRecord.NoDesignee, out _, out _).WindowId;

        public void CloseWindow(double at) => Schedule.ServerClose(Boss, at, out _);
    }

    sealed class FakeTarget : IInterruptClaimTarget
    {
        public bool Accept = true;
        public bool RefuseAfterEffect;   // 첫 효과가 상태를 바꿔(취약·기믹 진행) 이후 대상 규칙이 거절하는 경우
        public int Effects;
        public readonly List<ulong> EffectCasters = new List<ulong>();
        public readonly List<ulong> Records = new List<ulong>();

        public bool ServerAcceptsInterruptClaim(ulong casterClientId) => Accept && !(RefuseAfterEffect && Effects > 0);

        public void ServerApplyInterruptSuccessEffect(ulong casterClientId, Unit caster)
        {
            Effects++;
            EffectCasters.Add(casterClientId);
        }

        public void ServerRecordInterruptSuccess(ulong casterClientId) => Records.Add(casterClientId);
    }

    static InterruptActorSample Actor(Vector3 position, ulong clientId = ClientA, bool cc = false) =>
        new InterruptActorSample
        {
            Position = position,
            Forward = Vector3.forward,
            Alive = true,
            CrowdControlled = cc,
            ClientId = clientId,
            HasHitbox = true,
            Hitbox = HitboxAnchorShape.FromBox(new Vector3(0f, 1.3f, 0.9f), new Vector3(0.7f, 1.2f, 0.7f), Quaternion.identity),
            HitboxForward = Vector3.forward,
            HitLayers = ~0,
        };

    // 대상이 원점(시전자)을 정면으로 본다. yawFromAttacker = 대상 정면을 시전자 방향에서 그만큼 돌린 각도.
    static InterruptTargetSample Target(Vector3 position, float frontAngle = InterruptTargetGeometry.NoFrontRule, float yawFromAttacker = 0f) =>
        new InterruptTargetSample
        {
            Position = position,
            Forward = Quaternion.Euler(0f, yawFromAttacker, 0f) * InterruptSampleMath.Planar(-position),
            FrontAngle = frontAngle,
            DesignatedClientId = InterruptWindowRecord.NoDesignee,
        };

    static FakeWorld World(float targetZ = 1.5f, double open = 10.0, double planned = 11.0, float frontAngle = InterruptTargetGeometry.NoFrontRule,
        float yaw = 0f)
    {
        var w = new FakeWorld();
        w.SetActor(CasterA, Actor(Vector3.zero));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, targetZ), frontAngle, yaw));
        w.OpenWindow(open, planned);
        return w;
    }

    static InterruptClaim Claim(double judgeTime, ulong actor = CasterA, uint window = 1) =>
        new InterruptClaim
        {
            ActorId = actor,
            TargetId = Boss,
            WindowId = window,
            JudgeTime = judgeTime,
            AttackDirection = Vector3.forward,
        };

    static InterruptClaimCheck Check(FakeWorld w, InterruptClaim claim, double halfLag = 0.0) =>
        InterruptClaimValidator.Validate(w, claim, InterruptVerifySettings.Default.ToleranceFor(halfLag));

    static InterruptClaimOutcome Process(InterruptClaimServer server, FakeWorld w, InterruptClaim claim, FakeTarget target, double now,
        ulong sender = ClientA, bool owns = true, double halfLag = 0.0) =>
        server.Process(w, claim, sender, owns, halfLag, now, InterruptVerifySettings.Default, target);

    // ---- 기본값 ----

    [Test]
    public void 기본_허용_오차는_시각_0_033초_거리_0_2m_각도_5도_동시_0_05초()
    {
        InterruptVerifySettings s = InterruptVerifySettings.Default;
        Assert.That(s.TimeSlack, Is.EqualTo(0.033).Within(1e-9));
        Assert.That(s.DistanceSlack, Is.EqualTo(0.2f));
        Assert.That(s.AngleSlack, Is.EqualTo(5f));
        Assert.That(s.SimultaneousSeconds, Is.EqualTo(0.05).Within(1e-9));
        Assert.That(s.ToleranceFor(0.04).TimeSlack, Is.EqualTo(0.073).Within(1e-9), "시각 = 지연 절반 + 여유");
    }

    [Test]
    public void 측정_지연_절반은_RTT_ms_의_절반_초_호스트는_0()
    {
        Assert.That(InterruptClaimServer.HalfLagSeconds(100.0), Is.EqualTo(0.05).Within(1e-9));
        Assert.That(InterruptClaimServer.HalfLagSeconds(0.0), Is.EqualTo(0.0));
        Assert.That(InterruptClaimServer.HalfLagSeconds(-5.0), Is.EqualTo(0.0));
    }

    // ---- 시각 허용 오차(창 닫힘) ----

    [Test]
    public void 예정_닫힘_뒤_시각_여유_안이면_통과()
    {
        FakeWorld w = World(planned: 11.0);
        Assert.That(Check(w, Claim(11.03)).Passed, Is.True);
    }

    [Test]
    public void 예정_닫힘_뒤_시각_여유_밖이면_창_닫힘()
    {
        FakeWorld w = World(planned: 11.0);
        InterruptClaimCheck c = Check(w, Claim(11.04));
        Assert.That(c.Passed, Is.False);
        Assert.That(InterruptClaimValidator.ToRejectReason(c.FailReason), Is.EqualTo(InterruptClaimRejectReason.WindowClosed));
    }

    [Test]
    public void 열림_전_시각_여유_안이면_통과_밖이면_창_없음()
    {
        FakeWorld w = World(open: 10.0, planned: 11.0);
        Assert.That(Check(w, Claim(9.97)).Passed, Is.True, "열림 0.03초 전");
        InterruptClaimCheck c = Check(w, Claim(9.96));
        Assert.That(c.Passed, Is.False);
        Assert.That(c.FailReason, Is.EqualTo(InterruptJudgeFailReason.NoWindow));
        Assert.That(InterruptClaimValidator.ToRejectReason(c.FailReason), Is.EqualTo(InterruptClaimRejectReason.WindowClosed));
    }

    [Test]
    public void 지연이_크면_시각_여유가_지연_절반만큼_넓어진다()
    {
        FakeWorld w = World(planned: 11.0);
        Assert.That(Check(w, Claim(11.08), halfLag: 0.05).Passed, Is.True, "0.05 + 0.033 = 0.083 안");
        Assert.That(Check(w, Claim(11.09), halfLag: 0.05).Passed, Is.False, "0.083 밖");
    }

    // ---- 조기 닫힘 + 허용 오차(D7) ----

    [Test]
    public void 조기_닫힘_뒤라도_클라_판정_시각이_닫힘_더하기_여유_안이면_인정()
    {
        FakeWorld w = World(open: 10.0, planned: 12.0);
        w.CloseWindow(10.5);
        Assert.That(Check(w, Claim(10.52)).Passed, Is.True);
    }

    [Test]
    public void 조기_닫힘_더하기_여유_밖이면_창_닫힘()
    {
        FakeWorld w = World(open: 10.0, planned: 12.0);
        w.CloseWindow(10.5);
        InterruptClaimCheck c = Check(w, Claim(10.54));
        Assert.That(c.Passed, Is.False);
        Assert.That(c.FailReason, Is.EqualTo(InterruptJudgeFailReason.WindowClosed));
    }

    // ---- 거리 ----

    [Test]
    public void 거리_여유_안이면_통과()
    {
        // 허용 오차 0 이면 2.1m 까지 — 2.25m 는 거리 여유 0.2m 안.
        FakeWorld w = World(targetZ: 2.25f);
        Assert.That(InterruptJudgeCalculator.Judge(w, Claim(10.5).ToAttempt(), InterruptJudgeTolerance.Zero).IsSuccess, Is.False, "클라 기준으론 밖");
        Assert.That(Check(w, Claim(10.5)).Passed, Is.True);
    }

    [Test]
    public void 거리_여유_밖이면_범위_거부()
    {
        FakeWorld w = World(targetZ: 2.35f);
        InterruptClaimCheck c = Check(w, Claim(10.5));
        Assert.That(c.Passed, Is.False);
        Assert.That(InterruptClaimValidator.ToRejectReason(c.FailReason), Is.EqualTo(InterruptClaimRejectReason.OutOfRange));
    }

    // ---- 각도 ----

    [Test]
    public void 정면_각도_여유_안이면_통과()
    {
        FakeWorld w = World(frontAngle: 60f, yaw: 63f);
        Assert.That(Check(w, Claim(10.5)).Passed, Is.True, "60 + 5 ≥ 63");
    }

    [Test]
    public void 정면_각도_여유_밖이면_각도_거부()
    {
        FakeWorld w = World(frontAngle: 60f, yaw: 67f);
        InterruptClaimCheck c = Check(w, Claim(10.5));
        Assert.That(c.Passed, Is.False);
        Assert.That(InterruptClaimValidator.ToRejectReason(c.FailReason), Is.EqualTo(InterruptClaimRejectReason.Angle));
    }

    // ---- 위치 표본 시각(오너 권한 이동의 지연) ----

    [Test]
    public void 서버_이력이_시각_여유_안에서_따라오면_통과()
    {
        // 서버가 본 시전자는 10.02 에야 사거리에 들어온다(오너 이동이 늦게 보임). 판정 시각 10.0 + 여유 0.033 안.
        var w = new FakeWorld();
        w.SetActor(CasterA, Actor(new Vector3(0f, 0f, -1f)));
        w.SetActor(CasterA, Actor(Vector3.zero), 10.02);
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 2f)));
        w.OpenWindow(9.5, 11.0);

        InterruptClaimCheck c = Check(w, Claim(10.0));
        Assert.That(c.Passed, Is.True);
        Assert.That(c.SampleOffset, Is.GreaterThan(0.0));
    }

    [Test]
    public void 서버_이력이_시각_여유_밖에서야_따라오면_범위_거부()
    {
        var w = new FakeWorld();
        w.SetActor(CasterA, Actor(new Vector3(0f, 0f, -1f)));
        w.SetActor(CasterA, Actor(Vector3.zero), 10.05);
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 2f)));
        w.OpenWindow(9.5, 11.0);

        InterruptClaimCheck c = Check(w, Claim(10.0));
        Assert.That(c.Passed, Is.False);
        Assert.That(InterruptClaimValidator.ToRejectReason(c.FailReason), Is.EqualTo(InterruptClaimRejectReason.OutOfRange));
    }

    // ---- 동시 성공 장부(D8) ----

    [Test]
    public void 장부_첫_승인_뒤_0_05초_안은_동시_밖은_선착순_패배()
    {
        var ledger = new InterruptClaimLedger();
        Assert.That(ledger.Register(Boss, 1, CasterA, 10.0, 0.05), Is.EqualTo(InterruptClaimOrder.First));
        Assert.That(ledger.Register(Boss, 1, CasterB, 10.05, 0.05), Is.EqualTo(InterruptClaimOrder.Simultaneous), "경계 포함");
        Assert.That(ledger.Register(Boss, 1, CasterC, 10.051, 0.05), Is.EqualTo(InterruptClaimOrder.Lost));
        Assert.That(ledger.ApprovedActors(Boss, 1), Is.EqualTo(new[] { CasterA, CasterB }));
    }

    [Test]
    public void 장부_창이_다르면_따로_첫_승인()
    {
        var ledger = new InterruptClaimLedger();
        Assert.That(ledger.Register(Boss, 1, CasterA, 10.0, 0.05), Is.EqualTo(InterruptClaimOrder.First));
        Assert.That(ledger.Register(Boss, 2, CasterB, 10.5, 0.05), Is.EqualTo(InterruptClaimOrder.First));
    }

    [Test]
    public void 장부_같은_시전자_재신고는_중복()
    {
        var ledger = new InterruptClaimLedger();
        ledger.Register(Boss, 1, CasterA, 10.0, 0.05);
        Assert.That(ledger.Register(Boss, 1, CasterA, 10.01, 0.05), Is.EqualTo(InterruptClaimOrder.Duplicate));
    }

    [Test]
    public void 장부_평가는_기록하지_않는다()
    {
        var ledger = new InterruptClaimLedger();
        Assert.That(ledger.Evaluate(Boss, 1, CasterA, 10.0, 0.05), Is.EqualTo(InterruptClaimOrder.First));
        Assert.That(ledger.Count, Is.EqualTo(0));
    }

    // ---- 처리 순서 · 효과 1회(D9) ----

    static FakeWorld ThreeCasterWorld()
    {
        FakeWorld w = World();
        w.SetActor(CasterB, Actor(Vector3.zero, clientId: 8));
        w.SetActor(CasterC, Actor(Vector3.zero, clientId: 9));
        return w;
    }

    [Test]
    public void 동시_성공_둘이어도_보스_효과는_1회_개인_기록은_2회()
    {
        FakeWorld w = ThreeCasterWorld();
        var server = new InterruptClaimServer();
        var target = new FakeTarget();

        InterruptClaimOutcome a = Process(server, w, Claim(10.5, CasterA), target, now: 10.55, sender: ClientA);
        InterruptClaimOutcome b = Process(server, w, Claim(10.5, CasterB), target, now: 10.58, sender: 8);

        Assert.That(a.Approved && a.IsFirst, Is.True);
        Assert.That(b.Approved, Is.True);
        Assert.That(b.IsFirst, Is.False);
        Assert.That(target.Effects, Is.EqualTo(1));
        Assert.That(target.EffectCasters, Is.EqualTo(new[] { ClientA }));
        Assert.That(target.Records, Is.EqualTo(new ulong[] { ClientA, 8 }));
        Assert.That(server.Ledger.ApprovedActors(Boss, a.WindowId), Is.EqualTo(new[] { CasterA, CasterB }));
    }

    [Test]
    public void 동시_인정_시간_뒤_신고는_선착순_패배_효과_기록_없음()
    {
        FakeWorld w = ThreeCasterWorld();
        var server = new InterruptClaimServer();
        var target = new FakeTarget();

        Process(server, w, Claim(10.5, CasterA), target, now: 10.55, sender: ClientA);
        InterruptClaimOutcome c = Process(server, w, Claim(10.5, CasterC), target, now: 10.61, sender: 9);

        Assert.That(c.Approved, Is.False);
        Assert.That(c.RejectReason, Is.EqualTo(InterruptClaimRejectReason.FirstComeLost));
        Assert.That(target.Effects, Is.EqualTo(1));
        Assert.That(target.Records, Is.EqualTo(new[] { ClientA }));
    }

    [Test]
    public void 동시_성공_후보에는_대상_규칙을_묻지_않는다()
    {
        // 첫 승인 효과(취약·기믹 진행)로 대상 규칙이 닫혀도 동시 성공은 승인된다.
        FakeWorld w = ThreeCasterWorld();
        var server = new InterruptClaimServer();
        var target = new FakeTarget { RefuseAfterEffect = true };

        Process(server, w, Claim(10.5, CasterA), target, now: 10.55, sender: ClientA);
        InterruptClaimOutcome b = Process(server, w, Claim(10.5, CasterB), target, now: 10.56, sender: 8);

        Assert.That(b.Approved, Is.True);
        Assert.That(target.Effects, Is.EqualTo(1));
    }

    // ---- 거부 이유 ----

    [Test]
    public void 시전자_소유가_아니면_서버_거부()
    {
        var target = new FakeTarget();
        InterruptClaimOutcome r = Process(new InterruptClaimServer(), World(), Claim(10.5), target, now: 10.55, owns: false);
        Assert.That(r.Approved, Is.False);
        Assert.That(r.RejectReason, Is.EqualTo(InterruptClaimRejectReason.ServerRejected));
        Assert.That(target.Effects + target.Records.Count, Is.EqualTo(0));
    }

    [Test]
    public void 첫_승인을_대상_규칙이_거절하면_서버_거부_장부에_안_남는다()
    {
        var server = new InterruptClaimServer();
        var target = new FakeTarget { Accept = false };
        InterruptClaimOutcome r = Process(server, World(), Claim(10.5), target, now: 10.55);
        Assert.That(r.RejectReason, Is.EqualTo(InterruptClaimRejectReason.ServerRejected));
        Assert.That(server.Ledger.Count, Is.EqualTo(0));
        Assert.That(target.Effects, Is.EqualTo(0));
    }

    [Test]
    public void 대상이_신고_처리를_구현하지_않으면_서버_거부()
    {
        InterruptClaimOutcome r = Process(new InterruptClaimServer(), World(), Claim(10.5), null, now: 10.55);
        Assert.That(r.RejectReason, Is.EqualTo(InterruptClaimRejectReason.ServerRejected));
    }

    [Test]
    public void 같은_시전자_재신고는_서버_거부()
    {
        var server = new InterruptClaimServer();
        var target = new FakeTarget();
        FakeWorld w = World();
        Process(server, w, Claim(10.5), target, now: 10.55);
        InterruptClaimOutcome r = Process(server, w, Claim(10.5), target, now: 10.56);
        Assert.That(r.RejectReason, Is.EqualTo(InterruptClaimRejectReason.ServerRejected));
        Assert.That(target.Records.Count, Is.EqualTo(1));
    }

    [Test]
    public void 판정_시각에_시전자가_CC_면_서버_거부_판정_이유_CC()
    {
        var w = new FakeWorld();
        w.SetActor(CasterA, Actor(Vector3.zero, cc: true));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 1.5f)));
        w.OpenWindow(10.0, 11.0);

        InterruptClaimOutcome r = Process(new InterruptClaimServer(), w, Claim(10.5), new FakeTarget(), now: 10.55);
        Assert.That(r.RejectReason, Is.EqualTo(InterruptClaimRejectReason.ServerRejected));
        Assert.That(r.JudgeFailReason, Is.EqualTo(InterruptJudgeFailReason.CrowdControlled));
    }

    [Test]
    public void 이력_밖_시전자는_서버_거부()
    {
        InterruptClaimOutcome r = Process(new InterruptClaimServer(), World(), Claim(10.5, actor: 999), new FakeTarget(), now: 10.55);
        Assert.That(r.Approved, Is.False);
        Assert.That(r.RejectReason, Is.EqualTo(InterruptClaimRejectReason.ServerRejected));
    }

    [Test]
    public void 범위_각도_창_실패는_이유를_그대로_싣는다()
    {
        Assert.That(Process(new InterruptClaimServer(), World(targetZ: 2.4f), Claim(10.5), new FakeTarget(), 10.55).RejectReason,
            Is.EqualTo(InterruptClaimRejectReason.OutOfRange));
        Assert.That(Process(new InterruptClaimServer(), World(frontAngle: 60f, yaw: 70f), Claim(10.5), new FakeTarget(), 10.55).RejectReason,
            Is.EqualTo(InterruptClaimRejectReason.Angle));
        Assert.That(Process(new InterruptClaimServer(), World(planned: 11.0), Claim(11.2), new FakeTarget(), 11.25).RejectReason,
            Is.EqualTo(InterruptClaimRejectReason.WindowClosed));
    }

    // ---- 호스트 오너 경로 ----

    [Test]
    public void 호스트_오너도_같은_처리를_탄다_지연_0_이라_시각_여유는_0_033초뿐()
    {
        FakeWorld w = World(planned: 11.0);
        w.SetActor(CasterA, Actor(Vector3.zero, clientId: HostClient));
        double hostHalfLag = InterruptClaimServer.HalfLagSeconds(0.0);

        var target = new FakeTarget();
        InterruptClaimOutcome inside = Process(new InterruptClaimServer(), w, Claim(11.03), target, now: 11.03, sender: HostClient, halfLag: hostHalfLag);
        InterruptClaimOutcome outside = Process(new InterruptClaimServer(), w, Claim(11.04), new FakeTarget(), now: 11.04, sender: HostClient, halfLag: hostHalfLag);

        Assert.That(inside.Approved && inside.IsFirst, Is.True);
        Assert.That(target.Effects, Is.EqualTo(1));
        Assert.That(target.Records, Is.EqualTo(new[] { HostClient }));
        Assert.That(outside.RejectReason, Is.EqualTo(InterruptClaimRejectReason.WindowClosed));
    }

    [Test]
    public void 호스트가_먼저_승인되면_0_05초_안_클라는_동시_성공()
    {
        FakeWorld w = ThreeCasterWorld();
        var server = new InterruptClaimServer();
        var target = new FakeTarget();

        InterruptClaimOutcome host = Process(server, w, Claim(10.5, CasterB), target, now: 10.5, sender: 8, halfLag: 0.0);
        InterruptClaimOutcome client = Process(server, w, Claim(10.49, CasterA), target, now: 10.54, sender: ClientA, halfLag: 0.04);

        Assert.That(host.IsFirst, Is.True);
        Assert.That(client.Approved, Is.True);
        Assert.That(client.IsFirst, Is.False);
        Assert.That(target.Effects, Is.EqualTo(1));
    }

    // ---- 직렬화 ----

    [Test]
    public void 신고_직렬화_왕복()
    {
        var claim = new InterruptClaim
        {
            ActorId = 12345,
            TargetId = 678,
            WindowId = 9,
            JudgeTime = 123.456789,
            AttackDirection = new Vector3(0.6f, 0f, 0.8f),
            Position = new Vector3(1f, 2f, 3f),
            ViaDash = true,
        };

        using (var writer = new FastBufferWriter(InterruptClaim.SerializedSize, Allocator.Temp))
        {
            claim.Write(writer);
            Assert.That(writer.Length, Is.EqualTo(InterruptClaim.SerializedSize));
            using (var reader = new FastBufferReader(writer, Allocator.Temp))
            {
                InterruptClaim back = InterruptClaim.Read(reader);
                Assert.That(back.ActorId, Is.EqualTo(claim.ActorId));
                Assert.That(back.TargetId, Is.EqualTo(claim.TargetId));
                Assert.That(back.WindowId, Is.EqualTo(claim.WindowId));
                Assert.That(back.JudgeTime, Is.EqualTo(claim.JudgeTime));
                Assert.That(back.AttackDirection, Is.EqualTo(claim.AttackDirection));
                Assert.That(back.Position, Is.EqualTo(claim.Position));
                Assert.That(back.ViaDash, Is.True);
            }
        }
    }

    [Test]
    public void 판정_결과로_만든_신고는_같은_판정_입력으로_돌아간다()
    {
        var result = new InterruptJudgeResult
        {
            Outcome = InterruptJudgeOutcome.Success,
            TargetId = Boss,
            WindowId = 3,
            JudgeTime = 10.25,
            AttackDirection = Vector3.right,
            ResolvePosition = new Vector3(1f, 0f, 2f),
        };

        InterruptClaim claim = InterruptClaim.FromJudge(CasterA, false, result);
        InterruptJudgeAttempt attempt = claim.ToAttempt(0.01);
        Assert.That(attempt.ActorId, Is.EqualTo(CasterA));
        Assert.That(attempt.TargetId, Is.EqualTo(Boss));
        Assert.That(attempt.JudgeTime, Is.EqualTo(10.25));
        Assert.That(attempt.AttackDirection, Is.EqualTo(Vector3.right));
        Assert.That(attempt.SampleOffset, Is.EqualTo(0.01));
        Assert.That(claim.Position, Is.EqualTo(result.ResolvePosition));
    }
}
