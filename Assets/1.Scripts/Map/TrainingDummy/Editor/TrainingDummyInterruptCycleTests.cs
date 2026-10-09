using NUnit.Framework;

/// <summary>
/// 허수아비 인터럽트 순환 규칙.
///
/// 🔴 여기서 고정하는 것은 값이 아니라 관계다 — 취약 3초·idle 1초 같은 저작값은 튜닝 대상이라
///    잠그지 않는다(CounterWindowTests 와 같은 원칙). 테스트 안의 길이는 임의값이다.
/// </summary>
public sealed class TrainingDummyInterruptCycleTests
{
    const float Vulnerable = 2f;
    const float Idle = 0.5f;

    static TrainingDummyInterruptCycle NewCycle(float vulnerable = Vulnerable, float idle = Idle) =>
        new TrainingDummyInterruptCycle(vulnerable, idle);

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
    public void 인터럽트_성공하면_취약으로()
    {
        var c = NewCycle();
        Assert.IsTrue(c.TryInterrupt());
        Assert.AreEqual(TrainingDummyInterruptState.Vulnerable, c.State);
        Assert.IsFalse(c.IsInterruptible);
        Assert.AreEqual(Vulnerable, c.Remaining, 1e-5f);
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
    public void 취약_중_인터럽트는_무효이고_타이머도_건드리지_않는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Vulnerable * 0.5f);
        float before = c.Remaining;

        Assert.IsFalse(c.TryInterrupt());
        Assert.AreEqual(TrainingDummyInterruptState.Vulnerable, c.State);
        Assert.AreEqual(before, c.Remaining, 1e-5f, "창 밖 인터럽트가 취약을 연장하면 안 된다");
    }

    [Test]
    public void idle_중_인터럽트는_무효()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Vulnerable + Idle * 0.5f);
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);

        Assert.IsFalse(c.TryInterrupt());
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
    }

    // ─── 순환 ──────────────────────────────────────────────────────────

    [Test]
    public void 취약_끝나면_idle_그다음_인터럽트_가능()
    {
        var c = NewCycle();
        c.TryInterrupt();

        Assert.IsFalse(c.Tick(Vulnerable * 0.9f), "취약 도중엔 상태가 그대로");
        Assert.IsTrue(c.Tick(Vulnerable * 0.2f), "취약 만료 틱에서 상태 변화");
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);

        Assert.IsTrue(c.Tick(Idle), "idle 만료 틱에서 상태 변화");
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 다시_열린_뒤에도_인터럽트가_성공한다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Vulnerable + Idle + 0.01f);

        Assert.IsTrue(c.IsInterruptible);
        Assert.IsTrue(c.TryInterrupt(), "새 창은 소비 이력을 물려받지 않는다");
        Assert.AreEqual(TrainingDummyInterruptState.Vulnerable, c.State);
    }

    // 큰 틱 하나가 여러 단계를 넘기면 남는 시간이 다음 단계로 넘어가 끝 상태로 수렴한다.
    [Test]
    public void 큰_틱_하나로_취약과_idle을_모두_넘긴다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        Assert.IsTrue(c.Tick(Vulnerable + Idle));
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 남는_시간은_idle로_이월된다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Vulnerable + Idle * 0.25f);
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
        Assert.AreEqual(Idle * 0.75f, c.Remaining, 1e-4f);
    }

    [Test]
    public void idle_0이면_취약이_끝나자마자_인터럽트_가능()
    {
        var c = NewCycle(idle: 0f);
        c.TryInterrupt();
        Assert.IsTrue(c.Tick(Vulnerable));
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 음수_틱은_시간을_되감지_않는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(-5f);
        Assert.AreEqual(Vulnerable, c.Remaining, 1e-5f);
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

    // ─── CC 허용 — 취약일 때만 ─────────────────────────────────────────

    [Test]
    public void 인터럽트_가능_상태는_CC를_막는다()
    {
        var c = NewCycle();
        Assert.IsFalse(c.AllowsCrowdControl);
    }

    [Test]
    public void 취약_상태만_CC를_받는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        Assert.AreEqual(TrainingDummyInterruptState.Vulnerable, c.State);
        Assert.IsTrue(c.AllowsCrowdControl);

        c.Tick(Vulnerable * 0.5f);
        Assert.IsTrue(c.AllowsCrowdControl, "취약 도중에도 계속 받는다");
    }

    [Test]
    public void idle_상태는_CC를_막는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Vulnerable + Idle * 0.5f);
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
        Assert.IsFalse(c.AllowsCrowdControl);
    }

    [Test]
    public void 한_바퀴_돌면_다시_CC를_막는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Vulnerable + Idle + 0.01f);
        Assert.IsTrue(c.IsInterruptible);
        Assert.IsFalse(c.AllowsCrowdControl);
    }

    // idle 0 이면 취약 → 인터럽트 가능으로 바로 넘어간다 — 그 사이 CC 가 열린 채 남으면 안 된다.
    [Test]
    public void idle_0이어도_취약이_끝나면_CC를_막는다()
    {
        var c = NewCycle(idle: 0f);
        c.TryInterrupt();
        c.Tick(Vulnerable);
        Assert.IsFalse(c.AllowsCrowdControl);
    }

    // 취약 0 이면 성공 순간 취약이지만 첫 틱에 바로 닫힌다.
    [Test]
    public void 취약_0이면_첫_틱에_CC가_닫힌다()
    {
        var c = NewCycle(vulnerable: 0f);
        c.TryInterrupt();
        Assert.IsTrue(c.AllowsCrowdControl);
        Assert.IsTrue(c.Tick(0f));
        Assert.IsFalse(c.AllowsCrowdControl);
    }

    [Test]
    public void Reset하면_CC를_막는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Reset();
        Assert.IsFalse(c.AllowsCrowdControl);
    }

    // ─── 성공 수 — 슬로우 모션이 한 타격의 전후 비교로 성공을 판별한다 ───

    [Test]
    public void 성공_수는_성공에만_오른다()
    {
        var c = NewCycle();
        Assert.AreEqual(0, c.SuccessCount);

        c.TryInterrupt();
        Assert.AreEqual(1, c.SuccessCount);

        Assert.IsFalse(c.TryInterrupt(), "취약 중 인터럽트는 무효");
        c.Tick(Vulnerable + Idle * 0.5f);
        Assert.IsFalse(c.TryInterrupt(), "idle 중 인터럽트는 무효");
        Assert.AreEqual(1, c.SuccessCount, "무효 타격이 성공으로 잡히면 빗나간 슬로우가 유지된다");
    }

    [Test]
    public void 성공_수는_순환과_Reset에도_줄지_않는다()
    {
        var c = NewCycle();
        c.TryInterrupt();
        c.Tick(Vulnerable + Idle + 0.01f);
        c.TryInterrupt();
        Assert.AreEqual(2, c.SuccessCount);

        c.Reset();
        Assert.AreEqual(2, c.SuccessCount);
    }
}
