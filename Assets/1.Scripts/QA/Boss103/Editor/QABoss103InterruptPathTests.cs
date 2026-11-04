#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 193 — QA 봇 간파 = 191 경로. 누를 타이밍(창 시간표 관측 <see cref="QABoss103WindowWatch"/>) · 누를지(<see cref="InterruptJudgeCalculator.Predict"/> →
/// <see cref="QABoss103InterruptPress.Decide"/>, 봇 전용 형상 없음) · 리포트 집계(<see cref="QABoss103ClaimTally"/> — 191 QA 기록 → 판 파일 <c>claim</c> → 캐릭터 CSV).
/// 예측 세계는 가짜(대상 = 수직 원기둥 r 0.5, 간파 상자 = 저작값 중심 (0, 1.3, 0.9) · 반 (0.7, 1.2, 0.7) → 정면 도달 1.6m).
/// </summary>
public sealed class QABoss103InterruptPathTests
{
    private const ulong Caster = 100;
    private const ulong Boss = 200;
    private const float TargetRadius = 0.5f;

    // ── 창 시간표 관측(누를 타이밍) ─────────────────────────────

    private static InterruptWindowRecord Window(uint id, double open, double planned, double close = double.PositiveInfinity,
        InterruptWindowCloseKind kind = InterruptWindowCloseKind.Open) => new InterruptWindowRecord
    {
        TargetId = Boss, WindowId = id, AttackStartTime = open, OpenTime = open, PlannedCloseTime = planned, CloseTime = close,
        DesignatedClientId = InterruptWindowRecord.NoDesignee, CloseKind = kind,
    };

    [Test]
    public void 창_열림은_새_창_id_한_번만()
    {
        var w = new QABoss103WindowWatch();
        Assert.AreEqual(QABoss103WindowEdge.None, w.Step(false, default, 9.0, out _), "기록 없음");
        Assert.AreEqual(QABoss103WindowEdge.None, w.Step(true, Window(1, 10.0, 11.0), 9.9, out _), "열림 전");
        Assert.AreEqual(QABoss103WindowEdge.Opened, w.Step(true, Window(1, 10.0, 11.0), 10.0, out _));
        Assert.IsTrue(w.IsOpen);
        Assert.AreEqual(1u, w.Current.WindowId);
        Assert.AreEqual(11.0, w.Current.PlannedCloseTime, 1e-9, "닫힘 예정 = 시간표 값");
        Assert.AreEqual(QABoss103WindowEdge.None, w.Step(true, Window(1, 10.0, 11.0), 10.5, out _), "같은 창은 다시 열지 않는다");
    }

    [Test]
    public void 조기_닫힘은_배포를_받은_틱에_닫힘()
    {
        var w = new QABoss103WindowWatch();
        w.Step(true, Window(1, 10.0, 11.0), 10.1, out _);
        InterruptWindowRecord early = Window(1, 10.0, 11.0, 10.4, InterruptWindowCloseKind.Early);
        Assert.AreEqual(QABoss103WindowEdge.Closed, w.Step(true, early, 10.45, out InterruptWindowRecord closed));
        Assert.AreEqual(InterruptWindowCloseKind.Early, closed.CloseKind);
        Assert.IsFalse(w.IsOpen);
        Assert.AreEqual(QABoss103WindowEdge.None, w.Step(true, early, 10.5, out _), "닫힌 창은 다시 열지 않는다");
    }

    [Test]
    public void 닫힘_예정이_지나면_배포가_없어도_닫힘_길이_모름은_계속_열림()
    {
        var w = new QABoss103WindowWatch();
        w.Step(true, Window(1, 10.0, 11.0), 10.1, out _);
        Assert.AreEqual(QABoss103WindowEdge.Closed, w.Step(true, Window(1, 10.0, 11.0), 11.0, out _), "[열림, 닫힘) — 예정 시각에 닫힘");

        var u = new QABoss103WindowWatch();
        u.Step(true, Window(2, 10.0, double.PositiveInfinity), 10.1, out _);
        Assert.AreEqual(QABoss103WindowEdge.None, u.Step(true, Window(2, 10.0, double.PositiveInfinity), 60.0, out _), "길이 모름(잡기·허수아비)");
        Assert.IsTrue(u.IsOpen);
    }

    [Test]
    public void 다음_창으로_바뀌면_닫힘_다음_틱_열림_리셋하면_다시_연다()
    {
        var w = new QABoss103WindowWatch();
        w.Step(true, Window(1, 10.0, 11.0), 10.1, out _);
        Assert.AreEqual(QABoss103WindowEdge.Closed, w.Step(true, Window(2, 10.5, 12.0), 10.6, out InterruptWindowRecord closed));
        Assert.AreEqual(1u, closed.WindowId, "앞 창");
        Assert.AreEqual(QABoss103WindowEdge.Opened, w.Step(true, Window(2, 10.5, 12.0), 10.62, out _));
        Assert.AreEqual(2u, w.Current.WindowId);

        w.Reset();
        Assert.AreEqual(QABoss103WindowEdge.Opened, w.Step(true, Window(2, 10.5, 12.0), 10.7, out _), "봇이 쉬었다 돌아오면 지금 창을 새로 본다");
    }

    [Test]
    public void 닫힘_예정을_봇_실시간으로_길이_모름은_무한()
    {
        Assert.AreEqual(100.4, QABoss103InterruptPress.CloseAtReal(100.0, 50.0, 50.4), 1e-9);
        Assert.AreEqual(100.0, QABoss103InterruptPress.CloseAtReal(100.0, 50.0, 49.0), 1e-9, "이미 지남 = 지금");
        Assert.IsTrue(double.IsPositiveInfinity(QABoss103InterruptPress.CloseAtReal(100.0, 50.0, double.PositiveInfinity)));
        Assert.IsTrue(double.IsPositiveInfinity(QABoss103InterruptPress.CloseAtReal(100.0, double.NaN, 50.4)), "매니저 없음");
        Assert.AreEqual(9.9833 - QABoss103InterruptPress.SampleEpsilon, QABoss103InterruptPress.ProbeLocalTime(10.0, 0.0167f), 1e-6, "누른 시각 = 직전 샘플");
    }

    // ── 누를지 = Predict 결과 ───────────────────────────────────

    private static InterruptJudgeResult Result(InterruptJudgeOutcome outcome, InterruptJudgeFailReason reason = InterruptJudgeFailReason.None) =>
        new InterruptJudgeResult { Outcome = outcome, FailReason = reason };

    [Test]
    public void 판단표_강_누름_범위_다가감_창닫힘_포기()
    {
        Assert.AreEqual(QABoss103PressCall.Press, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Success), true));
        Assert.AreEqual(QABoss103PressCall.Wait, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Success), false), "시전 중이면 기다림");
        Assert.AreEqual(QABoss103PressCall.Approach, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.NoTarget), true));
        Assert.AreEqual(QABoss103PressCall.Approach, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.OutOfRange), true));
        Assert.AreEqual(QABoss103PressCall.Approach, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.Angle), true));
        Assert.AreEqual(QABoss103PressCall.Wait, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.CrowdControlled), true));
        Assert.AreEqual(QABoss103PressCall.Wait, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.NoWindow), true));
        Assert.AreEqual(QABoss103PressCall.GiveUp, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.WindowClosed), true));
        Assert.AreEqual(QABoss103PressCall.GiveUp, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.NotDesignated), true));
        Assert.AreEqual(QABoss103PressCall.GiveUp, QABoss103InterruptPress.Decide(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.TargetBlocked), true));

        Assert.IsTrue(QABoss103InterruptPress.InReach(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.WindowClosed)), "범위·정면은 지났다");
        Assert.IsFalse(QABoss103InterruptPress.InReach(Result(InterruptJudgeOutcome.NoTarget)));
        Assert.AreEqual("Weak_WindowClosed", QABoss103InterruptPress.Token(Result(InterruptJudgeOutcome.Failure, InterruptJudgeFailReason.WindowClosed)));
        Assert.AreEqual("Strong", QABoss103InterruptPress.Token(Result(InterruptJudgeOutcome.Success)));
    }

    private sealed class FakeWorld : IInterruptJudgeWorld
    {
        public InterruptActorSample Actor;
        public InterruptTargetSample Target;
        public readonly InterruptWindowSchedule Schedule = new InterruptWindowSchedule();

        public bool TryGetActorAt(ulong actorId, double time, out InterruptActorSample sample)
        {
            sample = Actor;
            return actorId == Caster;
        }

        public int TargetCount => 1;
        public ulong GetTargetId(int index) => Boss;

        public bool TryGetTargetAt(ulong targetId, double time, out InterruptTargetSample sample)
        {
            sample = Target;
            return targetId == Boss;
        }

        public bool TryGetLatestWindowOpenedBy(ulong targetId, double time, out InterruptWindowRecord record) =>
            Schedule.TryGetLatestOpenedBy(targetId, time, out record);

        // 상자(수평 회전만) vs 수직 원기둥.
        public bool ShapeHitsTarget(ulong targetId, in HitboxAnchorShape shape, Vector3 targetPosition, int layers)
        {
            Vector3 local = Quaternion.Inverse(shape.Box.orientation) * (targetPosition - shape.Box.center);
            float dx = Mathf.Max(0f, Mathf.Abs(local.x) - shape.Box.halfExtents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(local.z) - shape.Box.halfExtents.z);
            return dx * dx + dz * dz <= TargetRadius * TargetRadius + 1e-6f;
        }

        public PlayerDashPathResult SimulateDash(ulong actorId, Vector3 start, Vector3 direction, float speed, float remaining) => default;
    }

    private static FakeWorld World(float targetZ, double open, double plannedClose)
    {
        var w = new FakeWorld
        {
            Actor = new InterruptActorSample
            {
                Position = Vector3.zero, Forward = Vector3.forward, Alive = true, ClientId = 7, HasHitbox = true,
                Hitbox = HitboxAnchorShape.FromBox(new Vector3(0f, 1.3f, 0.9f), new Vector3(0.7f, 1.2f, 0.7f), Quaternion.identity),
                HitboxForward = Vector3.forward, HitLayers = ~0,
            },
            Target = new InterruptTargetSample
            {
                Position = new Vector3(0f, 0f, targetZ), Forward = Vector3.back, FrontAngle = InterruptTargetGeometry.NoFrontRule,
                DesignatedClientId = InterruptWindowRecord.NoDesignee,
            },
        };
        w.Schedule.ServerOpen(Boss, open, open, plannedClose, InterruptWindowRecord.NoDesignee, out _, out _);
        return w;
    }

    private static QABoss103PressCall CallFor(FakeWorld w, double press, float hitDelay, bool castable = true) =>
        QABoss103InterruptPress.Decide(InterruptJudgeCalculator.Predict(w,
            new InterruptCastRequest { ActorId = Caster, PressTime = press, AttackDirection = Vector3.forward, HitDelay = hitDelay },
            InterruptJudgeTolerance.Zero), castable);

    [Test]
    public void 예측_강이면_누르고_상자가_안_닿으면_다가간다()
    {
        Assert.AreEqual(QABoss103PressCall.Press, CallFor(World(2.0f, 10.0, 11.0), 10.2, 0.333f), "정면 도달 1.6 + r 0.5 안");
        Assert.AreEqual(QABoss103PressCall.Approach, CallFor(World(2.3f, 10.0, 11.0), 10.2, 0.333f), "2.1m 밖 — 봇 형상 계산 없이 예측이 대상 없음");
    }

    [Test]
    public void Hit_전에_창이_닫히면_포기_조기_닫힘도_같다()
    {
        Assert.AreEqual(QABoss103PressCall.GiveUp, CallFor(World(2.0f, 10.0, 11.0), 10.8, 0.333f), "누름 10.8 + Hit 0.333 > 닫힘 11.0");
        Assert.AreEqual(QABoss103PressCall.Press, CallFor(World(2.0f, 10.0, 11.0), 10.6, 0.333f), "10.933 < 11.0");

        FakeWorld early = World(2.0f, 10.0, 11.0);
        early.Schedule.ServerClose(Boss, 10.3, out _);   // 다른 사람 간파 성공 등 — 조기 닫힘 배포
        Assert.AreEqual(QABoss103PressCall.GiveUp, CallFor(early, 10.2, 0.333f));
    }

    // ── 리포트 집계(시도·승인·거부 사유) ─────────────────────────

    private static InterruptQaRecord Record(InterruptQaJudge judge, InterruptQaServer server,
        InterruptClaimRejectReason reject = InterruptClaimRejectReason.None) =>
        new InterruptQaRecord { ActorId = Caster, Judge = judge, Server = server, RejectReason = reject };

    [Test]
    public void 집계는_시도_판정성공_승인_첫_동시_거부_사유별()
    {
        var t = new QABoss103ClaimTally();
        t.Add(Record(InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst));
        t.Add(Record(InterruptQaJudge.Success, InterruptQaServer.ApprovedSimultaneous));
        t.Add(Record(InterruptQaJudge.Success, InterruptQaServer.Rejected, InterruptClaimRejectReason.FirstComeLost));
        t.Add(Record(InterruptQaJudge.Success, InterruptQaServer.Rejected, InterruptClaimRejectReason.OutOfRange));
        t.Add(Record(InterruptQaJudge.Success, InterruptQaServer.Rejected, InterruptClaimRejectReason.WindowClosed));
        t.Add(Record(InterruptQaJudge.Success, InterruptQaServer.NoResponse));
        t.Add(Record(InterruptQaJudge.Failure, InterruptQaServer.NotClaimed));
        t.Add(Record(InterruptQaJudge.Cut, InterruptQaServer.NotClaimed));

        Assert.AreEqual(8, t.Attempts);
        Assert.AreEqual(6, t.JudgeSuccess);
        Assert.AreEqual(1, t.ApprovedFirst);
        Assert.AreEqual(1, t.ApprovedSimultaneous);
        Assert.AreEqual(2, t.Approved);
        Assert.AreEqual(3, t.Rejected);
        Assert.AreEqual(1, t.NoResponse);
        CollectionAssert.AreEqual(new[] { 1, 0, 1, 1, 0 }, t.RejectByReason, "범위·각도·창 닫힘·선착순·그 밖");
        Assert.AreEqual("out_of_range=1 angle=0 window_closed=1 first_come=1 server=0", t.RejectSummary());
    }

    [Test]
    public void 판_파일_claim_줄이_캐릭터_CSV_열로()
    {
        var s = new QABoss103RunStats();
        s.Begin(100.0);
        s.End(160.0);
        s.AddPart(0, new[]
        {
            QABoss103PartFile.Format("claim", "slot", "0", "mode", "melee", "predict", "Strong", "judge", "Success", "server", "ApprovedFirst",
                "reject", "None", "mismatch", "None", "window", "3", "serverT", "120.000"),
            QABoss103PartFile.Format("claim", "slot", "0", "mode", "dash", "predict", "Strong", "judge", "Success", "server", "Rejected",
                "reject", "Angle", "mismatch", "ServerRejected", "window", "4", "serverT", "125.000"),
            QABoss103PartFile.Format("claim", "slot", "0", "mode", "melee", "predict", "Weak_WindowClosed", "judge", "Failure", "server", "NotClaimed",
                "reject", "None", "mismatch", "None", "window", "5", "serverT", "130.000"),
            "done slot=0 reason=host_end",
        });
        s.Finish();

        string[] h = QABoss103Metrics.CharsHeader;
        List<string[]> rows = QABoss103CsvRows.Chars(new QABoss103RunMeta { Run = 1, Tier = QABoss103BotTier.Skilled }, s);
        string[] pal = rows[0];
        Assert.AreEqual(h.Length, pal.Length, "열 수");
        Assert.AreEqual("3", pal[Array.IndexOf(h, "claim_attempts")]);
        Assert.AreEqual("2", pal[Array.IndexOf(h, "claim_judge_success")]);
        Assert.AreEqual("1", pal[Array.IndexOf(h, "claim_approved_first")]);
        Assert.AreEqual("0", pal[Array.IndexOf(h, "claim_approved_simul")]);
        Assert.AreEqual("1", pal[Array.IndexOf(h, "claim_rejected")]);
        Assert.AreEqual("1", pal[Array.IndexOf(h, "claim_reject_angle")]);
        Assert.AreEqual("0", pal[Array.IndexOf(h, "claim_reject_first_come")]);
        Assert.AreEqual("0", pal[Array.IndexOf(h, "claim_no_response")]);
        Assert.AreEqual("", rows[1][Array.IndexOf(h, "claim_attempts")], "판 파일 없는 캐릭터 = 빈칸");
    }
}
#endif
