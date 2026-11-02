using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 191 2단계 — InterruptManager 예측·판정 순수 계산(<see cref="InterruptJudgeCalculator"/>)·대시 경로(<see cref="PlayerDashPath"/>)·Hit 지연(<see cref="InterruptHitDelay"/>).
/// 세계는 가짜: 대상 = 수직 원기둥(반지름), 간파 상자 = 어쌔신·거너 저작값(중심 (0, 1.3, 0.9) · 반 (0.7, 1.2, 0.7) → 정면 도달 1.6m), 대시 충돌 = 가짜 벽.
/// </summary>
public sealed class InterruptJudgeCalculatorTests
{
    const ulong Caster = 100;
    const ulong Boss = 200;
    const ulong CasterClient = 7;
    const float TargetRadius = 0.5f;
    const float FixedDt = 0.02f;

    // ---- 가짜 세계 ----

    sealed class FakeSweep : IPlayerDashSweep
    {
        public float WallZ = float.PositiveInfinity;   // +z 로 가는 대시를 이 면 앞(캡슐 반지름)에서 막는다
        public float CapsuleRadius = 0.3f;
        public bool WallIsUnit;

        public Vector3 Resolve(Vector3 position, Vector3 desiredDelta)
        {
            float limit = WallZ - CapsuleRadius;
            if (desiredDelta.z > 0f && position.z + desiredDelta.z > limit)
                desiredDelta.z = Mathf.Max(0f, limit - position.z);
            return desiredDelta;
        }

        public bool IsUnitBlockingAhead(Vector3 position, Vector3 direction, float distance) => WallIsUnit;
    }

    sealed class FakeWorld : IInterruptJudgeWorld
    {
        public readonly List<(double time, InterruptActorSample sample)> Actor = new List<(double, InterruptActorSample)>();
        public readonly Dictionary<ulong, InterruptTargetSample> Targets = new Dictionary<ulong, InterruptTargetSample>();
        public readonly List<ulong> TargetIds = new List<ulong>();
        public readonly InterruptWindowSchedule Schedule = new InterruptWindowSchedule();
        public readonly FakeSweep Sweep = new FakeSweep();

        public void SetActor(InterruptActorSample sample, double from = double.NegativeInfinity) => Actor.Add((from, sample));

        public void AddTarget(ulong id, InterruptTargetSample sample)
        {
            Targets[id] = sample;
            if (!TargetIds.Contains(id))
                TargetIds.Add(id);
        }

        public bool TryGetActorAt(ulong actorId, double time, out InterruptActorSample sample)
        {
            sample = default;
            bool found = false;
            if (actorId != Caster)
                return false;
            foreach ((double t, InterruptActorSample s) in Actor)
            {
                if (t > time && found)
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

        // 상자(수평 회전만) vs 수직 원기둥 — 상자 지역 좌표 수평 최근접 거리 ≤ 반지름.
        public bool ShapeHitsTarget(ulong targetId, in HitboxAnchorShape shape, Vector3 targetPosition, int layers)
        {
            Assert.That(shape.Kind, Is.EqualTo(OverlapCollider.Box));
            Vector3 local = Quaternion.Inverse(shape.Box.orientation) * (targetPosition - shape.Box.center);
            float dx = Mathf.Max(0f, Mathf.Abs(local.x) - shape.Box.halfExtents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(local.z) - shape.Box.halfExtents.z);
            return dx * dx + dz * dz <= TargetRadius * TargetRadius + 1e-6f;
        }

        public PlayerDashPathResult SimulateDash(ulong actorId, Vector3 start, Vector3 direction, float speed, float remaining) =>
            PlayerDashPath.Simulate(Sweep, start, direction, speed, remaining, FixedDt);

        public uint OpenWindow(double openAt, double plannedClose, ulong designee = InterruptWindowRecord.NoDesignee) =>
            Schedule.ServerOpen(Boss, openAt, openAt, plannedClose, designee, out _, out _).WindowId;
    }

    static InterruptActorSample Actor(Vector3 position, bool cc = false, bool alive = true) =>
        new InterruptActorSample
        {
            Position = position,
            Forward = Vector3.forward,
            Alive = alive,
            CrowdControlled = cc,
            ClientId = CasterClient,
            HasHitbox = true,
            Hitbox = HitboxAnchorShape.FromBox(new Vector3(0f, 1.3f, 0.9f), new Vector3(0.7f, 1.2f, 0.7f), Quaternion.identity),
            HitboxForward = Vector3.forward,
            HitLayers = ~0,
        };

    static InterruptActorSample DashingActor(Vector3 position, float speed, float remaining)
    {
        InterruptActorSample a = Actor(position);
        a.Dashing = true;
        a.DashDirection = Vector3.forward;
        a.DashSpeed = speed;
        a.DashRemaining = remaining;
        return a;
    }

    // 대상이 시전자(원점)를 정면으로 본다 — frontAngle 기본 = 정면 판정 없음.
    static InterruptTargetSample Target(Vector3 position, float frontAngle = InterruptTargetGeometry.NoFrontRule, bool blocked = false) =>
        new InterruptTargetSample
        {
            Position = position,
            Forward = InterruptSampleMath.Planar(-position),
            FrontAngle = frontAngle,
            BlockedByState = blocked,
            DesignatedClientId = InterruptWindowRecord.NoDesignee,
        };

    static InterruptCastRequest Melee(double press, double hitDelay) =>
        new InterruptCastRequest { ActorId = Caster, PressTime = press, AttackDirection = Vector3.forward, HitDelay = (float)hitDelay };

    static InterruptCastRequest Dash(double press, float hitDelay) =>
        new InterruptCastRequest { ActorId = Caster, ViaDash = true, PressTime = press, AttackDirection = Vector3.right, HitDelay = hitDelay };

    static FakeWorld MeleeWorld(float targetZ, double open = 10.0, double close = 11.0)
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, targetZ)));
        w.OpenWindow(open, close);
        return w;
    }

    static InterruptJudgeAttempt AttemptAt(double judgeTime, ulong target = Boss) =>
        new InterruptJudgeAttempt { ActorId = Caster, JudgeTime = judgeTime, AttackDirection = Vector3.forward, TargetId = target };

    // ---- 범위 경계 ----

    [Test]
    public void Range_InsideFrontReach_Success_Strong()
    {
        // 정면 도달 1.6m + 원기둥 반지름 0.5 → 중심 2.1m 까지 닿는다.
        InterruptJudgeResult r = InterruptJudgeCalculator.Predict(MeleeWorld(2.09f), Melee(10.2, 0.3), InterruptJudgeTolerance.Zero);

        Assert.That(r.Outcome, Is.EqualTo(InterruptJudgeOutcome.Success));
        Assert.That(r.TargetId, Is.EqualTo(Boss));
        Assert.That(r.WindowId, Is.Not.Zero);
        Assert.That(r.Forecast, Is.EqualTo(InterruptAttemptForecast.Strong));
        Assert.That(r.JudgeTime, Is.EqualTo(10.5).Within(1e-6));
    }

    [Test]
    public void Range_JustOutside_PredictIsNoTarget_JudgeWithFocusIsOutOfRange()
    {
        FakeWorld w = MeleeWorld(2.11f);

        InterruptJudgeResult predicted = InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero);
        Assert.That(predicted.Outcome, Is.EqualTo(InterruptJudgeOutcome.NoTarget), "기존 3상태 — 범위 밖 = 대상 없음");
        Assert.That(predicted.Forecast, Is.EqualTo(InterruptAttemptForecast.NoTarget));

        InterruptJudgeResult judged = InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), InterruptJudgeTolerance.Zero);
        Assert.That(judged.Outcome, Is.EqualTo(InterruptJudgeOutcome.Failure));
        Assert.That(judged.FailReason, Is.EqualTo(InterruptJudgeFailReason.OutOfRange));
    }

    [Test]
    public void Range_DistanceSlackWidensReach()
    {
        FakeWorld w = MeleeWorld(2.25f);
        var slack = new InterruptJudgeTolerance { DistanceSlack = 0.2f };

        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), InterruptJudgeTolerance.Zero).FailReason,
            Is.EqualTo(InterruptJudgeFailReason.OutOfRange));
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), slack).Outcome, Is.EqualTo(InterruptJudgeOutcome.Success));
    }

    [Test]
    public void Range_HitboxIsAimedAtAttackDirection()
    {
        // 대상이 오른쪽 2m — 앵커 정면(+z)이 아니라 공격 방향(+x)으로 돌린 상자가 닿는다.
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero));
        w.AddTarget(Boss, Target(new Vector3(2f, 0f, 0f)));
        w.OpenWindow(10.0, 11.0);

        InterruptCastRequest toRight = Melee(10.2, 0.3);
        toRight.AttackDirection = Vector3.right;
        Assert.That(InterruptJudgeCalculator.Predict(w, toRight, InterruptJudgeTolerance.Zero).Outcome, Is.EqualTo(InterruptJudgeOutcome.Success));
        Assert.That(InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.NoTarget), "정면(+z)으로 누르면 옆 2m 는 상자 밖");
    }

    // ---- 각도 경계 ----

    static FakeWorld AngleWorld(float attackerAngleFromTargetFront, float frontAngle)
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero));
        Vector3 targetPos = new Vector3(0f, 0f, 1.8f);
        InterruptTargetSample t = Target(targetPos, frontAngle);
        t.Forward = Quaternion.AngleAxis(attackerAngleFromTargetFront, Vector3.up) * InterruptSampleMath.Planar(-targetPos);
        w.AddTarget(Boss, t);
        w.OpenWindow(10.0, 11.0);
        return w;
    }

    [Test]
    public void Angle_InsideFrontArc_Success()
    {
        Assert.That(InterruptJudgeCalculator.Judge(AngleWorld(59f, 60f), AttemptAt(10.5), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success));
    }

    [Test]
    public void Angle_OutsideFrontArc_FailsAngle_PredictNoTarget()
    {
        FakeWorld w = AngleWorld(61f, 60f);
        InterruptJudgeResult judged = InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), InterruptJudgeTolerance.Zero);
        Assert.That(judged.Outcome, Is.EqualTo(InterruptJudgeOutcome.Failure));
        Assert.That(judged.FailReason, Is.EqualTo(InterruptJudgeFailReason.Angle));

        Assert.That(InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero).Forecast,
            Is.EqualTo(InterruptAttemptForecast.NoTarget), "기존 3상태 — 정면각 밖 = 대상 없음");
    }

    [Test]
    public void Angle_SlackWidensArc()
    {
        var slack = new InterruptJudgeTolerance { AngleSlack = 5f };
        Assert.That(InterruptJudgeCalculator.Judge(AngleWorld(64f, 60f), AttemptAt(10.5), slack).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success));
        Assert.That(InterruptJudgeCalculator.Judge(AngleWorld(66f, 60f), AttemptAt(10.5), slack).FailReason,
            Is.EqualTo(InterruptJudgeFailReason.Angle));
    }

    // ---- 창 열림/닫힘 경계 ----

    [Test]
    public void Window_OpenAtJudgeTime_Success()
    {
        Assert.That(InterruptJudgeCalculator.Predict(MeleeWorld(1.8f), Melee(10.5, 0.49), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success));
    }

    [Test]
    public void Window_ClosesAtOrBeforeJudgeTime_WindowClosed_Weak()
    {
        // 창 [10, 11) — 판정 11.0 은 닫힘(끝 미포함).
        InterruptJudgeResult r = InterruptJudgeCalculator.Predict(MeleeWorld(1.8f), Melee(10.5, 0.5), InterruptJudgeTolerance.Zero);
        Assert.That(r.Outcome, Is.EqualTo(InterruptJudgeOutcome.Failure));
        Assert.That(r.FailReason, Is.EqualTo(InterruptJudgeFailReason.WindowClosed));
        Assert.That(r.Forecast, Is.EqualTo(InterruptAttemptForecast.Weak), "대상은 있는데 창이 닫힘 = 약");
    }

    [Test]
    public void Window_EarlyClose_IsReflected()
    {
        FakeWorld w = MeleeWorld(1.8f);
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.9), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success), "닫힘 예정 11.0 전");

        w.Schedule.ServerClose(Boss, 10.7, out InterruptWindowRecord closed);
        Assert.That(closed.CloseKind, Is.EqualTo(InterruptWindowCloseKind.Early));

        InterruptJudgeResult r = InterruptJudgeCalculator.Judge(w, AttemptAt(10.9), InterruptJudgeTolerance.Zero);
        Assert.That(r.FailReason, Is.EqualTo(InterruptJudgeFailReason.WindowClosed), "조기 닫힘 10.7 이후 판정");
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.69), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success), "조기 닫힘 직전은 열림");
    }

    [Test]
    public void Window_NotYetOpen_NoWindow_TimeSlackAcceptsBothEdges()
    {
        FakeWorld w = MeleeWorld(1.8f, open: 10.95, close: 11.5);
        InterruptJudgeResult early = InterruptJudgeCalculator.Judge(w, AttemptAt(10.9), InterruptJudgeTolerance.Zero);
        Assert.That(early.FailReason, Is.EqualTo(InterruptJudgeFailReason.NoWindow));

        var slack = new InterruptJudgeTolerance { TimeSlack = 0.06 };
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.9), slack).Outcome, Is.EqualTo(InterruptJudgeOutcome.Success), "열림 여유");
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(11.55), slack).Outcome, Is.EqualTo(InterruptJudgeOutcome.Success), "닫힘 여유");
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(11.57), slack).FailReason, Is.EqualTo(InterruptJudgeFailReason.WindowClosed));
    }

    [Test]
    public void Window_UnknownLength_StaysOpenUntilClosed()
    {
        FakeWorld w = MeleeWorld(1.8f, open: 10.0, close: double.PositiveInfinity);
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(30.0), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success));
    }

    // ---- 근접 Hit 지연 · 공격속도 변경 직후 ----

    [Test]
    public void HitDelay_IsClipHitTimeOverPlaybackSpeed()
    {
        Assert.That(InterruptHitDelay.FromClip(0.4f, 1f), Is.EqualTo(0.4f).Within(1e-6f));
        Assert.That(InterruptHitDelay.FromClip(0.4f, 2f), Is.EqualTo(0.2f).Within(1e-6f));
        Assert.That(InterruptHitDelay.FromClip(0.4f, float.NaN), Is.EqualTo(0.4f).Within(1e-6f), "비정상 속도 = 1");
        Assert.That(InterruptHitDelay.FromClip(0.4f, 0f), Is.EqualTo(4f).Within(1e-5f), "하한 0.1");
    }

    [Test]
    public void HitDelay_AttackSpeedChangeTakesEffectImmediately()
    {
        // 창 [10, 10.75). 클립 Hit 0.4초 — 공속 1배면 판정 10.9(닫힘), 막 2배가 된 순간 누르면 판정 10.7(열림).
        FakeWorld w = MeleeWorld(1.8f, open: 10.0, close: 10.75);
        float slow = InterruptHitDelay.FromClip(0.4f, PlayerAttackSpeedRules.AnimatorSpeed(true, 1f));
        float hasted = InterruptHitDelay.FromClip(0.4f, PlayerAttackSpeedRules.AnimatorSpeed(true, 2f));

        InterruptJudgeResult before = InterruptJudgeCalculator.Predict(w, Melee(10.5, slow), InterruptJudgeTolerance.Zero);
        InterruptJudgeResult after = InterruptJudgeCalculator.Predict(w, Melee(10.5, hasted), InterruptJudgeTolerance.Zero);

        Assert.That(before.JudgeTime, Is.EqualTo(10.9).Within(1e-6));
        Assert.That(before.FailReason, Is.EqualTo(InterruptJudgeFailReason.WindowClosed));
        Assert.That(after.JudgeTime, Is.EqualTo(10.7).Within(1e-6));
        Assert.That(after.Outcome, Is.EqualTo(InterruptJudgeOutcome.Success));
    }

    [Test]
    public void ClipHitTime_FindsEarliestSkillHitEvent()
    {
        var clip = new AnimationClip();
        try
        {
            // 에디터에서 clip.events 세터는 에러 로그를 남긴다 — 영구 이벤트는 AnimationUtility 로 넣는다.
            UnityEditor.AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { functionName = nameof(PlayerAnimationEventRelay.HandleSkillEvent), intParameter = (int)SkillAnimationEventType.End, time = 0.1f },
                new AnimationEvent { functionName = nameof(PlayerAnimationEventRelay.HandleSkillEvent), intParameter = (int)SkillAnimationEventType.Hit, time = 0.45f },
                new AnimationEvent { functionName = nameof(PlayerAnimationEventRelay.HandleSkillEvent), intParameter = (int)SkillAnimationEventType.Hit, time = 0.35f },
                new AnimationEvent { functionName = "Other", intParameter = (int)SkillAnimationEventType.Hit, time = 0.05f },
            });

            Assert.That(InterruptHitDelay.TryFindClipHitTime(clip, out float seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(InterruptHitDelay.TryFindClipHitTime(null, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(clip);
        }
    }

    // ---- 대쉬: 실제 대시 끝 · max(대시 끝, Hit) · 벽 ----

    [Test]
    public void Dash_JudgeTimeIsMaxOfDashEndAndHit()
    {
        var w = new FakeWorld();
        w.SetActor(DashingActor(Vector3.zero, 10f, 0.3f));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 4.5f)));
        w.OpenWindow(9.0, 12.0);

        InterruptJudgeResult hitFirst = InterruptJudgeCalculator.Predict(w, Dash(10.0, 0.2f), InterruptJudgeTolerance.Zero);
        Assert.That(hitFirst.DashEndTime, Is.EqualTo(10.3).Within(1e-5));
        Assert.That(hitFirst.JudgeTime, Is.EqualTo(10.3).Within(1e-5), "대시가 더 길다 → 대시 끝");

        InterruptJudgeResult hitLater = InterruptJudgeCalculator.Predict(w, Dash(10.0, 0.5f), InterruptJudgeTolerance.Zero);
        Assert.That(hitLater.JudgeTime, Is.EqualTo(10.5).Within(1e-5), "Hit 이 더 늦다 → Hit");

        Assert.That(hitFirst.ResolvePosition.z, Is.EqualTo(3f).Within(1e-4f), "판정 위치 = 대시 끝(10m/s × 0.3s)");
        Assert.That(hitFirst.AttackDirection, Is.EqualTo(Vector3.forward), "대쉬 간파 공격 방향 = 대시 방향(조준 방향 무시)");
        Assert.That(hitFirst.Outcome, Is.EqualTo(InterruptJudgeOutcome.Success));
    }

    [Test]
    public void Dash_BlockedByWall_ResolvesAtWall_NoFalseStrong()
    {
        var w = new FakeWorld();
        w.SetActor(DashingActor(Vector3.zero, 10f, 0.3f));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 4.5f)));
        w.OpenWindow(9.0, 12.0);
        w.Sweep.WallZ = 1.0f;

        InterruptJudgeResult r = InterruptJudgeCalculator.Predict(w, Dash(10.0, 0.2f), InterruptJudgeTolerance.Zero);

        Assert.That(r.ResolvePosition.z, Is.EqualTo(0.7f).Within(1e-4f), "벽(1.0) − 캡슐 반지름(0.3)에서 멈춤");
        Assert.That(r.DashBlockedByWall, Is.True);
        Assert.That(r.DashEndTime, Is.EqualTo(10.3).Within(1e-5), "벽은 대시 상태를 끝내지 않는다 — 남은 시간까지");
        Assert.That(r.Forecast, Is.EqualTo(InterruptAttemptForecast.NoTarget), "직선 예측이면 강이 나왔을 대상 — 벽에 막혀 대상 없음");
    }

    [Test]
    public void DashPath_UnitBlockEndsDashAtThatTick()
    {
        var sweep = new FakeSweep { WallZ = 1.0f, WallIsUnit = true };
        PlayerDashPathResult r = PlayerDashPath.Simulate(sweep, Vector3.zero, Vector3.forward, 10f, 0.3f, FixedDt);

        Assert.That(r.EndedByUnit, Is.True);
        Assert.That(r.End.z, Is.EqualTo(0.7f).Within(1e-4f));
        // 0.2m/틱 — 4번째 틱(0.6 → 0.7)에서 깎여 끝.
        Assert.That(r.Duration, Is.EqualTo(0.08f).Within(1e-5f));
    }

    [Test]
    public void DashPath_Unblocked_RunsWholeTicks()
    {
        PlayerDashPathResult r = PlayerDashPath.Simulate(new FakeSweep(), Vector3.zero, Vector3.forward, 10f, 0.25f, FixedDt);

        Assert.That(r.EndedByUnit, Is.False);
        Assert.That(r.BlockedByWall, Is.False);
        Assert.That(r.Duration, Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(r.End.z, Is.EqualTo(2.6f).Within(1e-4f), "남은 0.25s → 13틱 × 0.2m(틱마다 dt 전체를 요청하는 대시 상태와 같다)");
    }

    // ---- 시전자 CC · 생존 ----

    [Test]
    public void CasterCrowdControlled_Fails()
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero, cc: true));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 1.8f)));
        w.OpenWindow(10.0, 11.0);

        InterruptJudgeResult r = InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero);
        Assert.That(r.FailReason, Is.EqualTo(InterruptJudgeFailReason.CrowdControlled));
        Assert.That(r.Forecast, Is.EqualTo(InterruptAttemptForecast.Weak));
    }

    [Test]
    public void CasterCrowdControlledAtJudgeTime_JudgeFails()
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero));
        w.SetActor(Actor(Vector3.zero, cc: true), from: 10.4);   // 누른 뒤 넉백
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 1.8f)));
        w.OpenWindow(10.0, 11.0);

        Assert.That(InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success), "누를 때는 멀쩡");
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), InterruptJudgeTolerance.Zero).FailReason,
            Is.EqualTo(InterruptJudgeFailReason.CrowdControlled));
    }

    [Test]
    public void CasterDown_Fails()
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero, alive: false));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 1.8f)));
        w.OpenWindow(10.0, 11.0);

        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), InterruptJudgeTolerance.Zero).FailReason,
            Is.EqualTo(InterruptJudgeFailReason.CasterDown));
    }

    // ---- 지목자 · 대상 간파 불가 · 창 없음 ----

    [Test]
    public void Designee_OnlyDesignatedClientSucceeds()
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 1.8f)));
        w.OpenWindow(10.0, 11.0, designee: 5);

        InterruptJudgeResult notMe = InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), InterruptJudgeTolerance.Zero);
        Assert.That(notMe.FailReason, Is.EqualTo(InterruptJudgeFailReason.NotDesignated));

        w.Actor.Clear();
        InterruptActorSample designated = Actor(Vector3.zero);
        designated.ClientId = 5;
        w.SetActor(designated);
        Assert.That(InterruptJudgeCalculator.Judge(w, AttemptAt(10.5), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.Success));
    }

    [Test]
    public void TargetBlockedByState_Fails()
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 1.8f), blocked: true));
        w.OpenWindow(10.0, 11.0);

        InterruptJudgeResult r = InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero);
        Assert.That(r.FailReason, Is.EqualTo(InterruptJudgeFailReason.TargetBlocked));
        Assert.That(r.Forecast, Is.EqualTo(InterruptAttemptForecast.Weak));
    }

    [Test]
    public void NoWindowRecord_NoWindow_Weak()
    {
        var w = new FakeWorld();
        w.SetActor(Actor(Vector3.zero));
        w.AddTarget(Boss, Target(new Vector3(0f, 0f, 1.8f)));

        InterruptJudgeResult r = InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero);
        Assert.That(r.FailReason, Is.EqualTo(InterruptJudgeFailReason.NoWindow));
        Assert.That(r.Forecast, Is.EqualTo(InterruptAttemptForecast.Weak));
    }

    // ---- 기타 ----

    [Test]
    public void NoHitbox_IsNoTarget()
    {
        var w = MeleeWorld(1.8f);
        w.Actor.Clear();
        InterruptActorSample a = Actor(Vector3.zero);
        a.HasHitbox = false;
        w.SetActor(a);

        Assert.That(InterruptJudgeCalculator.Predict(w, Melee(10.2, 0.3), InterruptJudgeTolerance.Zero).Outcome,
            Is.EqualTo(InterruptJudgeOutcome.NoTarget));
    }

    [Test]
    public void AttemptFromPrediction_CarriesJudgeTimeDirectionTarget()
    {
        var p = new InterruptJudgeResult { JudgeTime = 12.5, AttackDirection = Vector3.right, TargetId = Boss };
        InterruptJudgeAttempt a = InterruptJudgeAttempt.FromPrediction(Caster, p);

        Assert.That(a.ActorId, Is.EqualTo(Caster));
        Assert.That(a.JudgeTime, Is.EqualTo(12.5));
        Assert.That(a.AttackDirection, Is.EqualTo(Vector3.right));
        Assert.That(a.TargetId, Is.EqualTo(Boss));
    }

    [Test]
    public void Schedule_LatestOpenedBy_IgnoresLaterWindows()
    {
        var s = new InterruptWindowSchedule();
        uint first = s.ServerOpen(Boss, 1.0, 1.0, 1.5, InterruptWindowRecord.NoDesignee, out _, out _).WindowId;
        s.ServerOpen(Boss, 2.0, 2.0, 2.5, InterruptWindowRecord.NoDesignee, out _, out _);

        Assert.That(s.TryGetLatestOpenedBy(Boss, 1.9, out InterruptWindowRecord r), Is.True);
        Assert.That(r.WindowId, Is.EqualTo(first));
        Assert.That(s.TryGetLatestOpenedBy(Boss, 0.9, out _), Is.False);
    }
}
