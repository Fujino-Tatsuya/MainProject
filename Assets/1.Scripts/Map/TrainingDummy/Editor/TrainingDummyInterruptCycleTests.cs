using NUnit.Framework;

/// <summary>
/// 허수아비 인터럽트 순환 규칙.
///
/// 🔴 여기서 고정하는 것은 값이 아니라 관계다 — 그로기 3초·idle 1초 같은 저작값은 튜닝 대상이라
///    잠그지 않는다(CounterWindowTests 와 같은 원칙). 테스트 안의 길이는 임의값이다.
/// </summary>
public sealed class TrainingDummyInterruptCycleTests
{
    const float Groggy = 2f;
    const float Idle = 0.5f;

    static TrainingDummyInterruptCycle NewCycle(float groggy = Groggy, float idle = Idle) =>
        new TrainingDummyInterruptCycle(groggy, idle);

    // ─── 기본 상태 ─────────────────────────────────────────────────────

    [Test]
    public void 스폰_직후는_인터럽트_가능()
    {
        var c = NewCycle();
        Assert.AreEqual(TrainingDummyInterruptState.Interruptible, c.State);
        Assert.IsTrue(c.IsInterruptible);
    }

    // 상호작용이 없으면 무기한 열려 있어야 연습이 된다.
    [Test]
    public void 인터럽트_가능_상태는_시간이_지나도_만료되지_않는다()
    {
        var c = NewCycle();
        for (int i = 0; i < 1000; i++)
            Assert.IsFalse(c.Tick(60f), "상태 변화가 없어야 한다");

        Assert.IsTrue(c.IsInterruptible);
        Assert.IsTrue(c.TryInterrupt(), "오래 지나도 인터럽트가 먹는다");
    }

    // ─── 성공 ──────────────────────────────────────────────────────────

    [Test]
    public void 인터럽트_성공하면_그로기로()
    {
        var c = NewCycle();
        Assert.IsTrue(c.TryInterrupt());
        Assert.AreEqual(TrainingDummyInterruptState.Groggy, c.State);
        Assert.IsFalse(c.IsInterruptible);
        Assert.AreEqual(Groggy, c.Remaining, 1e-5f);
    }

    // 몬스터 카운터 창과 같은 1회 소비 — 두 명이 같은 프레임에 넣어도 성공은 하나다.
    [Test]
    public void 인터럽트는_한_번만_소비된다()
    {
        var c = NewCycle();
        Assert.IsTrue(c.TryInterrupt());
        Assert.IsFalse(c.TryInterrupt());
    }

    // ─── 창 밖 ─────────────────────────────────────────────────────────

    [Test]
    public void 그로기_중_인터럽트는_무효이고_타이머도_건드리지_않는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Groggy * 0.5f);
        float before = c.Remaining;

        Assert.IsFalse(c.TryInterrupt());
        Assert.AreEqual(TrainingDummyInterruptState.Groggy, c.State);
        Assert.AreEqual(before, c.Remaining, 1e-5f, "창 밖 인터럽트가 그로기를 연장하면 안 된다");
    }

    [Test]
    public void idle_중_인터럽트는_무효()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Groggy + Idle * 0.5f);
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);

        Assert.IsFalse(c.TryInterrupt());
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
    }

    // ─── 순환 ──────────────────────────────────────────────────────────

    [Test]
    public void 그로기_끝나면_idle_그다음_인터럽트_가능()
    {
        var c = NewCycle();
        c.TryInterrupt();

        Assert.IsFalse(c.Tick(Groggy * 0.9f), "그로기 도중엔 상태가 그대로");
        Assert.IsTrue(c.Tick(Groggy * 0.2f), "그로기 만료 틱에서 상태 변화");
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);

        Assert.IsTrue(c.Tick(Idle), "idle 만료 틱에서 상태 변화");
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 다시_열린_뒤에도_인터럽트가_성공한다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Groggy + Idle + 0.01f);

        Assert.IsTrue(c.IsInterruptible);
        Assert.IsTrue(c.TryInterrupt(), "새 창은 소비 이력을 물려받지 않는다");
        Assert.AreEqual(TrainingDummyInterruptState.Groggy, c.State);
    }

    // 큰 틱 하나가 여러 단계를 넘기면 남는 시간이 다음 단계로 넘어가 끝 상태로 수렴한다.
    [Test]
    public void 큰_틱_하나로_그로기와_idle을_모두_넘긴다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        Assert.IsTrue(c.Tick(Groggy + Idle));
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 남는_시간은_idle로_이월된다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Groggy + Idle * 0.25f);
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
        Assert.AreEqual(Idle * 0.75f, c.Remaining, 1e-4f);
    }

    [Test]
    public void idle_0이면_그로기가_끝나자마자_인터럽트_가능()
    {
        var c = NewCycle(idle: 0f);
        c.TryInterrupt();
        Assert.IsTrue(c.Tick(Groggy));
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 음수_틱은_시간을_되감지_않는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(-5f);
        Assert.AreEqual(Groggy, c.Remaining, 1e-5f);
    }

    [Test]
    public void Reset하면_인터럽트_가능으로_돌아온다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Reset();
        Assert.IsTrue(c.IsInterruptible);
        Assert.IsTrue(c.TryInterrupt());
    }
}
