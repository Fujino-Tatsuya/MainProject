using NUnit.Framework;

/// <summary>
/// 카운터 선딜 게이트의 순수 판정 테스트.
///
/// 게이트가 여는 조건은 <b>두 사건의 논리곱</b>이다 — 카운터 창 타이머 만료 + 애니메이션 준비 지점 도달.
/// 둘의 도착 순서가 데이터(창 길이)와 클립 이벤트 시점에 따라 뒤바뀔 수 있어서,
/// 순서별로 동작을 고정해 둔다.
/// </summary>
public sealed class BossCounterWindupGateTests
{
    /// 정상 순서 — 애니메이션이 먼저 준비되고, 창 타이머가 끝나야 공격이 나간다.
    [Test]
    public void ReadyFirst_ReleasesOnlyAfterTimer()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(1f);
        gate.MarkAnimationReady();
        gate.Tick(0.99f);
        Assert.That(gate.ShouldRelease, Is.False);
        gate.Tick(0.01f);
        Assert.That(gate.ShouldRelease, Is.True);
    }

    /// 비정상 순서 — 창이 클립 이벤트보다 짧으면 타이머가 먼저 끝난다.
    /// 이때 공격을 미리 내보내면 보이지 않는 판정이 생기므로, 이벤트를 기다리되 그 사실을 기록한다.
    [Test]
    public void TimerFirst_WaitsForAnimationAndReportsOrdering()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(1f);
        gate.Tick(1f);
        Assert.That(gate.ShouldRelease, Is.False);
        Assert.That(gate.TimerElapsedBeforeAnimationReady, Is.True);
        gate.MarkAnimationReady();
        Assert.That(gate.ShouldRelease, Is.True);
    }

    /// 강제 중단(카운터 성공·사망·타임아웃·디스폰)에서 예약된 발사가 남지 않아야 한다.
    [Test]
    public void Reset_ClearsPendingRelease()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(1f);
        gate.MarkAnimationReady();
        gate.Tick(1f);
        gate.Reset();
        Assert.That(gate.IsActive, Is.False);
        Assert.That(gate.ShouldRelease, Is.False);
        Assert.That(gate.TimerElapsedBeforeAnimationReady, Is.False);
    }

    /// 🔴 창 길이 0 — 인스펙터 범위가 0~2 라 저작 가능한 값이다.
    /// 이때도 타이머는 애니메이션보다 먼저 끝난 것이므로 위 비정상 순서와 <b>같게</b> 취급해야 한다.
    /// (Tick 을 한 번도 안 거치는 경로라 플래그를 Begin 에서도 세우지 않으면 조용히 false 로 남는다.)
    [Test]
    public void ZeroDuration_ReportsOrderingWithoutTick()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(0f);
        Assert.That(gate.IsTimerElapsed, Is.True);
        Assert.That(gate.TimerElapsedBeforeAnimationReady, Is.True);
        Assert.That(gate.ShouldRelease, Is.False);   // 애니메이션 준비는 아직 안 됐다
        gate.MarkAnimationReady();
        Assert.That(gate.ShouldRelease, Is.True);
    }

    /// 애니 이벤트가 중복 도착해도(같은 클립에서 이벤트가 두 번 실리는 경우) 판정이 흔들리지 않아야 한다.
    [Test]
    public void DuplicateReadySignal_ReleasesOnlyOnceThroughSameDecision()
    {
        var gate = new BossCounterWindupGate();
        gate.Begin(1f);
        gate.MarkAnimationReady();
        gate.MarkAnimationReady();
        gate.Tick(1f);
        Assert.That(gate.ShouldRelease, Is.True);
    }
}
