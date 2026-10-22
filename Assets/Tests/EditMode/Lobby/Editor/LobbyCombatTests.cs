using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 86 T4 — TRAIN_02 일반 전투 장부(<see cref="LobbyCombatLedger"/>)·끼임 규칙(<see cref="LobbyCombatStuckTracker"/>)·단계 범위(<see cref="LobbyCombatRules"/>).
/// </summary>
public sealed class LobbyCombatTests
{
    // 훈련실 대략(사전 베이크 볼륨과 같은 모양): x -20.3~-12, z -8.3~8.3, y -0.5~2.
    static readonly Bounds Room = new Bounds(new Vector3(-16.15f, 0.75f, 0f), new Vector3(8.3f, 2.5f, 16.6f));
    static readonly Vector3 Home = new Vector3(-16f, 0f, 3f);
    const float FarPlayer = 10f;

    static LobbyCombatLedger Started(params ulong[] ids)
    {
        var ledger = new LobbyCombatLedger();
        ledger.Begin(ids);
        return ledger;
    }

    static LobbyCombatStuckTracker Tracked(ulong id = 1, double now = 0d)
    {
        var tracker = new LobbyCombatStuckTracker();
        tracker.Track(id, Home, now);
        return tracker;
    }

    // ─── 장부 ──────────────────────────────────────────────────────────

    [Test]
    public void Ledger_BeforeBegin_DeathsIgnored()
    {
        var ledger = new LobbyCombatLedger();
        Assert.That(ledger.MarkDead(1), Is.False);
        Assert.That(ledger.TryConsumeAllDead(), Is.False);
    }

    [Test]
    public void Ledger_CountsAgainstSpawnList()
    {
        var ledger = Started(10, 11, 12);
        Assert.That(ledger.Total, Is.EqualTo(3));
        Assert.That(ledger.RemainingCount, Is.EqualTo(3));

        Assert.That(ledger.MarkDead(11), Is.True);
        Assert.That(ledger.RemainingCount, Is.EqualTo(2));
        Assert.That(ledger.KilledCount, Is.EqualTo(1));
    }

    [Test]
    public void Ledger_DeathOutsideSpawnList_Ignored()
    {
        var ledger = Started(10, 11);
        Assert.That(ledger.MarkDead(99), Is.False);
        Assert.That(ledger.RemainingCount, Is.EqualTo(2));
    }

    [Test]
    public void Ledger_DuplicateDeath_CountsOnce()
    {
        var ledger = Started(10, 11);
        Assert.That(ledger.MarkDead(10), Is.True);
        Assert.That(ledger.MarkDead(10), Is.False);
        Assert.That(ledger.RemainingCount, Is.EqualTo(1));
    }

    [Test]
    public void Ledger_DuplicateSpawnId_CountedOnce()
    {
        var ledger = Started(10, 10, 11);
        Assert.That(ledger.Total, Is.EqualTo(2));
    }

    [Test]
    public void Ledger_RecoveryIsNotKill()
    {
        var ledger = Started(10, 11);
        Assert.That(ledger.RecordRecovery(10), Is.True);
        Assert.That(ledger.RecordRecovery(10), Is.True);
        Assert.That(ledger.RemainingCount, Is.EqualTo(2));
        Assert.That(ledger.KilledCount, Is.EqualTo(0));
        Assert.That(ledger.RecoveriesOf(10), Is.EqualTo(2));
        Assert.That(ledger.RecoveryCount, Is.EqualTo(2));
        Assert.That(ledger.TryConsumeAllDead(), Is.False);
    }

    [Test]
    public void Ledger_RecoveryOfDeadOrUnknown_Ignored()
    {
        var ledger = Started(10);
        ledger.MarkDead(10);
        Assert.That(ledger.RecordRecovery(10), Is.False);
        Assert.That(ledger.RecordRecovery(77), Is.False);
    }

    [Test]
    public void Ledger_AllDead_ReportedExactlyOnce()
    {
        var ledger = Started(10, 11);
        ledger.MarkDead(10);
        Assert.That(ledger.TryConsumeAllDead(), Is.False);

        ledger.MarkDead(11);
        Assert.That(ledger.TryConsumeAllDead(), Is.True);
        Assert.That(ledger.TryConsumeAllDead(), Is.False);
    }

    [Test]
    public void Ledger_RecoveredThenKilled_StillCompletesOnce()
    {
        var ledger = Started(10, 11);
        ledger.RecordRecovery(11);
        ledger.MarkDead(10);
        ledger.MarkDead(11);
        Assert.That(ledger.TryConsumeAllDead(), Is.True);
        Assert.That(ledger.TryConsumeAllDead(), Is.False);
    }

    [Test]
    public void Ledger_EmptySpawnList_NeverCompletes()
    {
        var ledger = Started();
        Assert.That(ledger.IsRunning, Is.True);
        Assert.That(ledger.TryConsumeAllDead(), Is.False);
    }

    [Test]
    public void Ledger_Stop_ReturnsAliveLeftoversAndClears()
    {
        var ledger = Started(10, 11, 12);
        ledger.MarkDead(11);
        var leftovers = new List<ulong>();
        ledger.Stop(leftovers);

        Assert.That(leftovers, Is.EquivalentTo(new ulong[] { 10, 12 }));
        Assert.That(ledger.IsRunning, Is.False);
        Assert.That(ledger.Total, Is.EqualTo(0));
        Assert.That(ledger.MarkDead(10), Is.False, "단계 이탈 뒤 사망은 무시");
        Assert.That(ledger.TryConsumeAllDead(), Is.False, "이탈 뒤 완료 보고 없음");
    }

    [Test]
    public void Ledger_Restart_ResetsCompletionLatch()
    {
        var ledger = Started(10);
        ledger.MarkDead(10);
        Assert.That(ledger.TryConsumeAllDead(), Is.True);

        ledger.Begin(new ulong[] { 20 });
        Assert.That(ledger.RemainingCount, Is.EqualTo(1));
        Assert.That(ledger.RecoveryCount, Is.EqualTo(0));
        ledger.MarkDead(20);
        Assert.That(ledger.TryConsumeAllDead(), Is.True);
    }

    // ─── 끼임 규칙 ────────────────────────────────────────────────────

    [Test]
    public void Stuck_Untracked_None()
    {
        var tracker = new LobbyCombatStuckTracker();
        Assert.That(tracker.Evaluate(5, 100d, new Vector3(-30f, 0f, 0f), Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.None));
    }

    [Test]
    public void Stuck_OutOfBounds_Immediate()
    {
        var tracker = Tracked();
        Assert.That(tracker.Evaluate(1, 0.1d, new Vector3(-9f, 0f, 0f), Room, 0f), Is.EqualTo(LobbyCombatStuckVerdict.OutOfBounds), "동쪽 벽 너머");
        Assert.That(tracker.Evaluate(1, 0.1d, new Vector3(-16f, -3f, 0f), Room, 0f), Is.EqualTo(LobbyCombatStuckVerdict.OutOfBounds), "바닥 아래로 낙하");
    }

    [Test]
    public void Stuck_WithinMargin_IsInside()
    {
        Assert.That(LobbyCombatStuckTracker.IsInside(Room, new Vector3(-11.8f, 0f, 0f)), Is.True, "경계 여유 0.5 안");
        Assert.That(LobbyCombatStuckTracker.IsInside(Room, new Vector3(-11.4f, 0f, 0f)), Is.False);
    }

    [Test]
    public void Stuck_StillAwayFromHomeAndNoPlayer_AfterStillSeconds()
    {
        var tracker = Tracked();
        var stuckAt = new Vector3(-19f, 0f, -6f);
        Assert.That(tracker.Evaluate(1, 1d, stuckAt, Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.None), "이동 감지 — 시계 시작");
        Assert.That(tracker.Evaluate(1, 1d + LobbyCombatStuckTracker.StillSeconds - 0.1d, stuckAt, Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.None));
        Assert.That(tracker.Evaluate(1, 1d + LobbyCombatStuckTracker.StillSeconds, stuckAt, Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.NotMoving));
    }

    [Test]
    public void Stuck_MovingResetsClock()
    {
        var tracker = Tracked();
        var a = new Vector3(-19f, 0f, -6f);
        var b = a + new Vector3(LobbyCombatStuckTracker.MoveEpsilon + 0.1f, 0f, 0f);
        tracker.Evaluate(1, 0d, a, Room, FarPlayer);
        tracker.Evaluate(1, 7d, b, Room, FarPlayer);
        Assert.That(tracker.Evaluate(1, 7d + LobbyCombatStuckTracker.StillSeconds - 0.5d, b, Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.None));
    }

    [Test]
    public void Stuck_SmallJitter_DoesNotResetClock()
    {
        var tracker = Tracked();
        var a = new Vector3(-19f, 0f, -6f);
        tracker.Evaluate(1, 0d, a, Room, FarPlayer);
        tracker.Evaluate(1, 4d, a + new Vector3(0.2f, 0f, 0f), Room, FarPlayer);
        Assert.That(tracker.Evaluate(1, LobbyCombatStuckTracker.StillSeconds, a + new Vector3(0.1f, 0f, 0.1f), Room, FarPlayer),
            Is.EqualTo(LobbyCombatStuckVerdict.NotMoving));
    }

    [Test]
    public void Stuck_NearHome_NeverNotMoving()
    {
        var tracker = Tracked();
        Assert.That(tracker.Evaluate(1, 100d, Home + new Vector3(0.3f, 0f, 0f), Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.None), "제자리 대기 몹");
    }

    [Test]
    public void Stuck_PlayerNearby_NeverNotMoving()
    {
        var tracker = Tracked();
        var fighting = new Vector3(-19f, 0f, -6f);
        tracker.Evaluate(1, 0d, fighting, Room, 1f);
        Assert.That(tracker.Evaluate(1, 100d, fighting, Room, LobbyCombatStuckTracker.PlayerReachRadius), Is.EqualTo(LobbyCombatStuckVerdict.None), "플레이어 옆에서 서서 싸우는 몹");
    }

    [Test]
    public void Stuck_AfterRecovery_ClockRestartsAtHome()
    {
        var tracker = Tracked();
        var stuckAt = new Vector3(-19f, 0f, -6f);
        tracker.Evaluate(1, 0d, stuckAt, Room, FarPlayer);
        Assert.That(tracker.Evaluate(1, 10d, stuckAt, Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.NotMoving));

        tracker.OnRecovered(1, 10d);
        Assert.That(tracker.Evaluate(1, 10.5d, Home, Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.None));
        Assert.That(tracker.TryGetHome(1, out var home), Is.True);
        Assert.That(Vector3.Distance(home, Home), Is.LessThan(1e-4f));
    }

    [Test]
    public void Stuck_Forget_StopsTracking()
    {
        var tracker = Tracked();
        tracker.Forget(1);
        Assert.That(tracker.IsTracked(1), Is.False);
        Assert.That(tracker.Evaluate(1, 100d, new Vector3(-30f, 0f, 0f), Room, FarPlayer), Is.EqualTo(LobbyCombatStuckVerdict.None));
    }

    // ─── 단계 범위·배치 ────────────────────────────────────────────────

    [Test]
    public void MonstersActive_OnlyTrain02InProgress()
    {
        Assert.That(LobbyCombatRules.MonstersActive(LobbyTrainingStage.Train02, LobbyTrainingPhase.InProgress), Is.True);
        Assert.That(LobbyCombatRules.MonstersActive(LobbyTrainingStage.Train02, LobbyTrainingPhase.Gathering), Is.False);
        Assert.That(LobbyCombatRules.MonstersActive(LobbyTrainingStage.Train01, LobbyTrainingPhase.InProgress), Is.False);
        Assert.That(LobbyCombatRules.MonstersActive(LobbyTrainingStage.Train03A, LobbyTrainingPhase.Gathering), Is.False);
        Assert.That(LobbyCombatRules.MonstersActive(LobbyTrainingStage.Sortie, LobbyTrainingPhase.InProgress), Is.False);
    }

    [Test]
    public void CandidatePosition_KeepsOffsetWithoutRotationOrScale()
    {
        var origin = new Vector3(-16f, 0f, 0f);
        var a = LobbyCombatRules.CandidatePosition(origin, Vector3.zero, new Vector3(7.2f, 0f, -1.4f));
        var b = LobbyCombatRules.CandidatePosition(origin, new Vector3(100f, 0f, 0f), new Vector3(101.4f, 0f, 7.2f));
        Assert.That(Vector3.Distance(a, new Vector3(-8.8f, 0f, -1.4f)), Is.LessThan(1e-4f));
        Assert.That(Vector3.Distance(b, new Vector3(-14.6f, 0f, 7.2f)), Is.LessThan(1e-4f));
    }

    [Test]
    public void CombatStartReset_NoRageGrant()
    {
        var options = TrainingResetOptions.CombatStart;
        Assert.That(options.FullHealth, Is.True);
        Assert.That(options.ResetCooldowns, Is.True);
        Assert.That(options.RefillDash, Is.True);
        Assert.That(options.GrantAssassinTransformRage, Is.False);
    }
}
