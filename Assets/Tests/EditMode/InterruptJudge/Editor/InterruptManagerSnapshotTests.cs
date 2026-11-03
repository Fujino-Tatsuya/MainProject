using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>할 일 191 1단계 — InterruptManager 세계 스냅샷의 순수 부품(이력·창 시간표·시각 변환).</summary>
public sealed class InterruptManagerSnapshotTests
{
    const float Tolerance = 1e-4f;

    static InterruptActorSample Actor(float x, Vector3 forward, bool cc = false) =>
        new InterruptActorSample { Position = new Vector3(x, 0f, 0f), Forward = forward, Alive = true, CrowdControlled = cc };

    static InterruptSampleHistory<InterruptActorSample> NewHistory(int capacity = 16, double seconds = 1.0) =>
        new InterruptSampleHistory<InterruptActorSample>(capacity, seconds);

    // ---- 이력 ----

    [Test]
    public void History_InterpolatesPositionAndDirectionBetweenSamples()
    {
        var h = NewHistory();
        h.Add(10.0, Actor(0f, Vector3.forward));
        h.Add(10.1, Actor(1f, Vector3.right));

        Assert.That(h.TryGetAt(10.05, out InterruptActorSample s), Is.True);
        Assert.That(s.Position.x, Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(Vector3.Angle(s.Forward, Vector3.forward), Is.EqualTo(45f).Within(0.01f), "방향은 구면 보간");
        Assert.That(s.Forward.magnitude, Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void History_StateFieldsTakeEarlierSample()
    {
        var h = NewHistory();
        h.Add(1.0, Actor(0f, Vector3.forward, cc: false));
        h.Add(1.1, Actor(1f, Vector3.forward, cc: true));

        Assert.That(h.TryGetAt(1.09, out InterruptActorSample before), Is.True);
        Assert.That(before.CrowdControlled, Is.False, "상태 칸은 사이 값이 없다 — 앞 표본");
        Assert.That(h.TryGetAt(1.1, out InterruptActorSample at), Is.True);
        Assert.That(at.CrowdControlled, Is.True, "표본 시각 정확히 = 그 표본");
    }

    [Test]
    public void History_OutOfRangeReturnsFalse()
    {
        var h = NewHistory();
        Assert.That(h.TryGetAt(0.0, out _), Is.False, "빈 이력");

        h.Add(5.0, Actor(0f, Vector3.forward));
        h.Add(5.5, Actor(1f, Vector3.forward));
        Assert.That(h.TryGetAt(4.99, out _), Is.False, "가장 오래된 것보다 이르다");
        Assert.That(h.TryGetAt(5.51, out _), Is.False, "가장 최신보다 늦다 — 추정하지 않는다");
        Assert.That(h.TryGetAt(5.0, out _), Is.True);
        Assert.That(h.TryGetAt(5.5, out _), Is.True);
    }

    [Test]
    public void History_PrunesOlderThanHistorySecondsButKeepsBoundarySample()
    {
        var h = NewHistory(capacity: 64, seconds: 1.0);
        for (int i = 0; i <= 20; i++)
            h.Add(i * 0.1, Actor(i, Vector3.forward));   // 0.0 ~ 2.0

        Assert.That(h.NewestTime, Is.EqualTo(2.0).Within(1e-9));
        Assert.That(h.OldestTime, Is.LessThanOrEqualTo(1.0), "경계(최신 − 1초)를 보간할 수 있게 그 앞 표본 하나는 남는다");
        Assert.That(h.OldestTime, Is.GreaterThan(0.85));
        Assert.That(h.TryGetAt(1.0, out InterruptActorSample s), Is.True);
        Assert.That(s.Position.x, Is.EqualTo(10f).Within(1e-3f));
        Assert.That(h.TryGetAt(0.5, out _), Is.False, "1초 넘게 지난 시각은 범위 밖");
    }

    [Test]
    public void History_CapacityOverflowDropsOldest()
    {
        var h = NewHistory(capacity: 4, seconds: 100.0);
        for (int i = 0; i < 6; i++)
            h.Add(i, Actor(i, Vector3.forward));

        Assert.That(h.Count, Is.EqualTo(4));
        Assert.That(h.OldestTime, Is.EqualTo(2.0));
        Assert.That(h.TryGetAt(4.5, out InterruptActorSample s), Is.True);
        Assert.That(s.Position.x, Is.EqualTo(4.5f).Within(Tolerance));
    }

    [Test]
    public void History_RejectsEarlierAndOverwritesSameTime()
    {
        var h = NewHistory();
        h.Add(2.0, Actor(0f, Vector3.forward));
        Assert.That(h.Add(1.9, Actor(9f, Vector3.forward)), Is.False, "과거 시각은 버린다");
        Assert.That(h.Add(2.0, Actor(3f, Vector3.forward)), Is.True, "같은 시각은 덮어쓴다");

        Assert.That(h.Count, Is.EqualTo(1));
        Assert.That(h.TryGetLatest(out double t, out InterruptActorSample s), Is.True);
        Assert.That(t, Is.EqualTo(2.0));
        Assert.That(s.Position.x, Is.EqualTo(3f));
    }

    [Test]
    public void History_DashRemainingInterpolatesOnlyWhileDashingInBoth()
    {
        var a = new InterruptActorSample { Dashing = true, DashRemaining = 0.2f, Forward = Vector3.forward };
        var b = new InterruptActorSample { Dashing = true, DashRemaining = 0.1f, Forward = Vector3.forward };
        Assert.That(a.LerpTo(b, 0.5f).DashRemaining, Is.EqualTo(0.15f).Within(Tolerance));

        var notDashing = new InterruptActorSample { Dashing = false, Forward = Vector3.forward };
        Assert.That(a.LerpTo(notDashing, 0.5f).DashRemaining, Is.EqualTo(0.2f).Within(Tolerance), "대시가 끝나는 구간은 앞 표본");
    }

    [Test]
    public void TargetSample_InterpolatesPositionKeepsStateAndDesignee()
    {
        var a = new InterruptTargetSample { Position = Vector3.zero, Forward = Vector3.forward, BlockedByState = false, DesignatedClientId = 7 };
        var b = new InterruptTargetSample { Position = new Vector3(0f, 0f, 2f), Forward = Vector3.forward, BlockedByState = true, DesignatedClientId = 9 };
        InterruptTargetSample s = a.LerpTo(b, 0.25f);

        Assert.That(s.Position.z, Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(s.BlockedByState, Is.False);
        Assert.That(s.DesignatedClientId, Is.EqualTo(7UL));
    }

    // ---- 창 시간표 ----

    [Test]
    public void Schedule_ServerOpenRecordsTimesAndIssuesIncreasingIds()
    {
        var schedule = new InterruptWindowSchedule();
        InterruptWindowRecord w1 = schedule.ServerOpen(42, 9.5, 10.0, 11.5, InterruptWindowRecord.NoDesignee, out bool closedPrev, out _);

        Assert.That(closedPrev, Is.False);
        Assert.That(w1.WindowId, Is.EqualTo(1u));
        Assert.That(w1.TargetId, Is.EqualTo(42UL));
        Assert.That(w1.AttackStartTime, Is.EqualTo(9.5));
        Assert.That(w1.OpenTime, Is.EqualTo(10.0));
        Assert.That(w1.PlannedCloseTime, Is.EqualTo(11.5));
        Assert.That(w1.IsClosed, Is.False);
        Assert.That(w1.IsOpenAt(9.99), Is.False);
        Assert.That(w1.IsOpenAt(10.0), Is.True);
        Assert.That(w1.IsOpenAt(11.49), Is.True);
        Assert.That(w1.IsOpenAt(11.5), Is.False, "예정 시각에 닫힌 것으로 본다 — [열림, 닫힘)");

        InterruptWindowRecord w2 = schedule.ServerOpen(43, -1.0, 10.2, double.NaN, 5, out _, out _);
        Assert.That(w2.WindowId, Is.EqualTo(2u), "모든 대상에 걸친 하나의 증가열");
        Assert.That(double.IsPositiveInfinity(w2.PlannedCloseTime), Is.True, "길이 모름 = +∞");
        Assert.That(w2.AttackStartTime, Is.EqualTo(-1.0), "열림보다 이른 공격 시작은 그대로");
        Assert.That(w2.DesignatedClientId, Is.EqualTo(5UL));
    }

    [Test]
    public void Schedule_AttackStartAfterOpenOrNaNFallsBackToOpenTime()
    {
        var schedule = new InterruptWindowSchedule();
        Assert.That(schedule.ServerOpen(1, double.NaN, 3.0, 4.0, InterruptWindowRecord.NoDesignee, out _, out _).AttackStartTime, Is.EqualTo(3.0));
        Assert.That(schedule.ServerOpen(2, 3.5, 3.0, 4.0, InterruptWindowRecord.NoDesignee, out _, out _).AttackStartTime, Is.EqualTo(3.0));
    }

    [Test]
    public void Schedule_EarlyCloseIsClassifiedAndVisibleAtLookup()
    {
        var schedule = new InterruptWindowSchedule();
        schedule.ServerOpen(42, 10.0, 10.0, 11.5, InterruptWindowRecord.NoDesignee, out _, out _);

        Assert.That(schedule.ServerClose(42, 10.6, out InterruptWindowRecord closed), Is.True);
        Assert.That(closed.CloseKind, Is.EqualTo(InterruptWindowCloseKind.Early));
        Assert.That(closed.CloseTime, Is.EqualTo(10.6));
        Assert.That(schedule.TryGetWindowAt(42, 10.59, out _), Is.True);
        Assert.That(schedule.TryGetWindowAt(42, 10.6, out _), Is.False, "조기 닫힘 뒤에는 닫힘");
        Assert.That(schedule.TryGetWindowAt(42, 11.0, out _), Is.False);
        Assert.That(schedule.ServerClose(42, 10.7, out _), Is.False, "이미 닫힘 — 멱등");
    }

    [Test]
    public void Schedule_LateCloseExtendsWindow()
    {
        var schedule = new InterruptWindowSchedule();
        schedule.ServerOpen(42, 10.0, 10.0, 11.5, InterruptWindowRecord.NoDesignee, out _, out _);

        // 23호 게이트 — 타이머는 11.5 에 끝났지만 애니 Hit 이 11.8 에 와서 그때 닫힘.
        Assert.That(schedule.ServerClose(42, 11.8, out InterruptWindowRecord closed), Is.True);
        Assert.That(closed.CloseKind, Is.EqualTo(InterruptWindowCloseKind.Extended));
        Assert.That(schedule.TryGetWindowAt(42, 11.7, out _), Is.True, "늦은 닫힘 갱신 뒤에는 예정 시각 너머도 열림");
    }

    [Test]
    public void Schedule_ClassifyCloseTolerance()
    {
        double tol = InterruptWindowSchedule.OnTimeTolerance;
        Assert.That(InterruptWindowSchedule.ClassifyClose(5.0, 5.0), Is.EqualTo(InterruptWindowCloseKind.OnTime));
        Assert.That(InterruptWindowSchedule.ClassifyClose(5.0, 5.0 - tol * 0.5), Is.EqualTo(InterruptWindowCloseKind.OnTime));
        Assert.That(InterruptWindowSchedule.ClassifyClose(5.0, 5.0 + tol * 0.5), Is.EqualTo(InterruptWindowCloseKind.OnTime));
        Assert.That(InterruptWindowSchedule.ClassifyClose(5.0, 5.0 - tol * 2), Is.EqualTo(InterruptWindowCloseKind.Early));
        Assert.That(InterruptWindowSchedule.ClassifyClose(5.0, 5.0 + tol * 2), Is.EqualTo(InterruptWindowCloseKind.Extended));
        Assert.That(InterruptWindowSchedule.ClassifyClose(double.PositiveInfinity, 5.0), Is.EqualTo(InterruptWindowCloseKind.Unscheduled));
    }

    [Test]
    public void Schedule_ReopenWhileOpenClosesPreviousFirst()
    {
        var schedule = new InterruptWindowSchedule();
        InterruptWindowRecord first = schedule.ServerOpen(42, 10.0, 10.0, 11.0, InterruptWindowRecord.NoDesignee, out _, out _);
        InterruptWindowRecord second = schedule.ServerOpen(42, 10.4, 10.4, 11.4, InterruptWindowRecord.NoDesignee,
            out bool closedPrev, out InterruptWindowRecord previous);

        Assert.That(closedPrev, Is.True);
        Assert.That(previous.WindowId, Is.EqualTo(first.WindowId));
        Assert.That(previous.CloseTime, Is.EqualTo(10.4));
        Assert.That(previous.CloseKind, Is.EqualTo(InterruptWindowCloseKind.Early));
        Assert.That(schedule.TryGetWindowAt(42, 10.2, out InterruptWindowRecord at), Is.True);
        Assert.That(at.WindowId, Is.EqualTo(first.WindowId), "이력 조회 — 그 시각의 창");
        Assert.That(schedule.TryGetWindowAt(42, 10.5, out at), Is.True);
        Assert.That(at.WindowId, Is.EqualTo(second.WindowId));
    }

    [Test]
    public void Schedule_ClientApplyMirrorsServerAndUpdatesSameWindow()
    {
        var server = new InterruptWindowSchedule();
        var client = new InterruptWindowSchedule();

        InterruptWindowRecord opened = server.ServerOpen(42, 10.0, 10.0, 11.5, 3, out _, out _);
        client.Apply(opened);
        Assert.That(client.TryGetWindowAt(42, 11.0, out InterruptWindowRecord c), Is.True);
        Assert.That(c.DesignatedClientId, Is.EqualTo(3UL));

        server.ServerClose(42, 10.3, out InterruptWindowRecord closed);
        client.Apply(closed);   // 조기 닫힘 배포
        Assert.That(client.TryGetWindowAt(42, 11.0, out _), Is.False);
        Assert.That(client.TryGetLatest(42, out c), Is.True);
        Assert.That(c.CloseKind, Is.EqualTo(InterruptWindowCloseKind.Early));
        Assert.That(c.WindowId, Is.EqualTo(opened.WindowId), "같은 창 번호를 덮어쓴다 — 기록이 늘지 않는다");
    }

    [Test]
    public void Schedule_ClientApplyOutOfOrderKeepsWindowOrder()
    {
        var server = new InterruptWindowSchedule();
        var client = new InterruptWindowSchedule();
        InterruptWindowRecord w1 = server.ServerOpen(42, 1.0, 1.0, 2.0, InterruptWindowRecord.NoDesignee, out _, out _);
        server.ServerClose(42, 2.0, out InterruptWindowRecord w1Closed);
        InterruptWindowRecord w2 = server.ServerOpen(42, 3.0, 3.0, 4.0, InterruptWindowRecord.NoDesignee, out _, out _);

        client.Apply(w2);
        client.Apply(w1Closed);
        Assert.That(client.TryGetLatest(42, out InterruptWindowRecord latest), Is.True);
        Assert.That(latest.WindowId, Is.EqualTo(w2.WindowId), "늦게 온 옛 창이 최신이 되지 않는다");
        Assert.That(client.TryGetById(42, w1.WindowId, out InterruptWindowRecord old), Is.True);
        Assert.That(old.CloseKind, Is.EqualTo(InterruptWindowCloseKind.OnTime));
        client.Apply(default);   // 창 번호 0 = 무시
        Assert.That(client.TargetCount, Is.EqualTo(1));
    }

    [Test]
    public void Schedule_KeepsOnlyRecentRecordsPerTargetAndForgets()
    {
        var schedule = new InterruptWindowSchedule();
        for (int i = 0; i < InterruptWindowSchedule.MaxRecordsPerTarget + 3; i++)
        {
            schedule.ServerOpen(42, i, i, i + 0.5, InterruptWindowRecord.NoDesignee, out _, out _);
            schedule.ServerClose(42, i + 0.5, out _);
        }

        Assert.That(schedule.TryGetWindowAt(42, 0.25, out _), Is.False, "오래된 기록은 밀려난다");
        Assert.That(schedule.TryGetWindowAt(42, InterruptWindowSchedule.MaxRecordsPerTarget + 2.25, out _), Is.True);

        schedule.Forget(42);
        Assert.That(schedule.TryGetLatest(42, out _), Is.False);
    }

    [Test]
    public void Record_SerializationRoundTrip()
    {
        var record = new InterruptWindowRecord
        {
            TargetId = 1234567890123UL,
            WindowId = 77,
            AttackStartTime = 100.25,
            OpenTime = 100.5,
            PlannedCloseTime = double.PositiveInfinity,
            CloseTime = 101.75,
            DesignatedClientId = 2,
            CloseKind = InterruptWindowCloseKind.Unscheduled,
        };

        using (var writer = new FastBufferWriter(InterruptWindowRecord.SerializedSize, Allocator.Temp))
        {
            record.Write(writer);
            Assert.That(writer.Length, Is.EqualTo(InterruptWindowRecord.SerializedSize));
            using (var reader = new FastBufferReader(writer, Allocator.Temp))
            {
                InterruptWindowRecord read = InterruptWindowRecord.Read(reader);
                Assert.That(read.TargetId, Is.EqualTo(record.TargetId));
                Assert.That(read.WindowId, Is.EqualTo(record.WindowId));
                Assert.That(read.AttackStartTime, Is.EqualTo(record.AttackStartTime));
                Assert.That(read.OpenTime, Is.EqualTo(record.OpenTime));
                Assert.That(double.IsPositiveInfinity(read.PlannedCloseTime), Is.True);
                Assert.That(read.CloseTime, Is.EqualTo(record.CloseTime));
                Assert.That(read.DesignatedClientId, Is.EqualTo(record.DesignatedClientId));
                Assert.That(read.CloseKind, Is.EqualTo(record.CloseKind));
            }
        }
    }

    // ---- 서버 ↔ 로컬 시각 ----

    [Test]
    public void Clock_FirstSampleSetsOffsetAndConvertsBothWays()
    {
        var clock = new InterruptServerClock();
        Assert.That(clock.HasSample, Is.False);
        Assert.That(clock.ToServer(5.0), Is.EqualTo(5.0), "표본 전 = 오프셋 0");

        clock.Sample(localTime: 20.0, serverTime: 1020.5);
        Assert.That(clock.HasSample, Is.True);
        Assert.That(clock.Offset, Is.EqualTo(1000.5).Within(1e-9));
        Assert.That(clock.ToServer(21.0), Is.EqualTo(1021.5).Within(1e-9));
        Assert.That(clock.ToLocal(1021.5), Is.EqualTo(21.0).Within(1e-9));
        Assert.That(clock.ToLocal(clock.ToServer(33.3)), Is.EqualTo(33.3).Within(1e-9), "왕복 변환은 원래 값");
    }

    [Test]
    public void Clock_SmallDriftIsSmoothedLargeJumpSnaps()
    {
        var clock = new InterruptServerClock();
        clock.Sample(0.0, 100.0);

        clock.Sample(1.0, 101.01);   // 오프셋 100 → 100.01 (작은 흔들림)
        Assert.That(clock.Offset, Is.EqualTo(100.0 + 0.01 * InterruptServerClock.Smoothing).Within(1e-9));

        clock.Sample(2.0, 105.0);    // 오프셋 103 — 재동기급 점프
        Assert.That(clock.Offset, Is.EqualTo(103.0).Within(1e-9));
    }

    [Test]
    public void Clock_IgnoresInvalidSamplesAndResets()
    {
        var clock = new InterruptServerClock();
        clock.Sample(double.NaN, 1.0);
        clock.Sample(1.0, double.PositiveInfinity);
        Assert.That(clock.HasSample, Is.False);

        clock.Sample(1.0, 3.0);
        clock.Reset();
        Assert.That(clock.HasSample, Is.False);
        Assert.That(clock.Offset, Is.EqualTo(0.0));
    }
}
