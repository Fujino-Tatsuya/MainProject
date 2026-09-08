using NUnit.Framework;

/// <summary>
/// 인터럽트 카운터 창의 수명 규칙.
///
/// 🔴 여기서 고정하는 것은 값이 아니라 관계다 — 창 길이 1.5초·그로기 0.5초 같은 저작값은
///    튜닝 대상이라 잠그지 않는다(팀장 피드백: 튜닝값을 테스트로 잠그지 말 것).
/// </summary>
public sealed class CounterWindowTests
{
    // ─── 열기 ──────────────────────────────────────────────────────────

    [Test]
    public void 열면_열린다()
    {
        var w = new CounterWindow();
        w.Open(1.5f);
        Assert.IsTrue(w.IsOpen);
        Assert.AreEqual(1.5f, w.Remaining, 1e-5f);
    }

    // 저작값 0 은 "카운터 없음"이다 — 열렸다가 즉시 만료되는 모호한 상태를 만들지 않는다.
    [Test]
    public void 길이_0이면_열리지_않는다()
    {
        var w = new CounterWindow();
        w.Open(0f);
        Assert.IsFalse(w.IsOpen);
        Assert.IsFalse(w.TryConsumeInterrupt(), "열리지 않았으면 인터럽트도 안 먹는다");
    }

    [Test]
    public void 음수_길이도_열리지_않는다()
    {
        var w = new CounterWindow();
        w.Open(-1f);
        Assert.IsFalse(w.IsOpen);
    }

    [Test]
    public void 다시_열면_이전_상태가_지워진다()
    {
        var w = new CounterWindow();
        w.Open(1f);
        Assert.IsTrue(w.TryConsumeInterrupt());
        Assert.IsTrue(w.InterruptConsumed);

        w.Open(1f);
        Assert.IsFalse(w.InterruptConsumed, "새 창은 소비 이력을 물려받지 않는다");
        Assert.IsTrue(w.TryConsumeInterrupt(), "새 창에서는 다시 성공할 수 있다");
    }

    // ─── 만료 ──────────────────────────────────────────────────────────

    [Test]
    public void 만료_전에는_false()
    {
        var w = new CounterWindow();
        w.Open(1f);
        Assert.IsFalse(w.TickAndDetectExpiry(0.4f));
        Assert.IsFalse(w.TickAndDetectExpiry(0.4f));
        Assert.IsTrue(w.IsOpen);
    }

    // "실패 확정"을 호출측이 한 번만 처리해야 하므로 만료 틱에서만 true 여야 한다.
    [Test]
    public void 만료되는_틱에만_true()
    {
        var w = new CounterWindow();
        w.Open(1f);
        Assert.IsFalse(w.TickAndDetectExpiry(0.5f));
        Assert.IsTrue(w.TickAndDetectExpiry(0.5f), "여기서 만료");
        Assert.IsFalse(w.TickAndDetectExpiry(0.5f), "이미 닫힌 창은 다시 만료되지 않는다");
        Assert.IsFalse(w.IsOpen);
    }

    [Test]
    public void 한_틱이_창보다_길어도_한_번만_만료()
    {
        var w = new CounterWindow();
        w.Open(0.5f);
        Assert.IsTrue(w.TickAndDetectExpiry(10f));
        Assert.IsFalse(w.TickAndDetectExpiry(10f));
    }

    // 시간이 되감기면 창이 영원히 안 닫힐 수 있다 — 음수는 0 으로 눌러야 한다.
    [Test]
    public void 음수_델타는_시간을_되감지_않는다()
    {
        var w = new CounterWindow();
        w.Open(1f);
        w.TickAndDetectExpiry(-5f);
        Assert.AreEqual(1f, w.Remaining, 1e-5f);
    }

    [Test]
    public void 닫힌_창은_틱해도_조용하다()
    {
        var w = new CounterWindow();
        Assert.IsFalse(w.TickAndDetectExpiry(1f));
        Assert.AreEqual(0f, w.Remaining, 1e-5f);
    }

    // ─── 인터럽트 소비 ─────────────────────────────────────────────────

    [Test]
    public void 창_안_인터럽트는_성공()
    {
        var w = new CounterWindow();
        w.Open(1.5f);
        Assert.IsTrue(w.TryConsumeInterrupt());
    }

    [Test]
    public void 성공하면_창이_닫힌다()
    {
        var w = new CounterWindow();
        w.Open(1.5f);
        w.TryConsumeInterrupt();
        Assert.IsFalse(w.IsOpen, "성공 이후 히트는 카운터로 인정되지 않는다");
    }

    // 2인 협동에서 같은 창에 둘이 인터럽트를 넣는 경우.
    [Test]
    public void 한_창에서_인터럽트는_한_번만()
    {
        var w = new CounterWindow();
        w.Open(1.5f);
        Assert.IsTrue(w.TryConsumeInterrupt());
        Assert.IsFalse(w.TryConsumeInterrupt(), "두 번째는 성공하지 않는다");
    }

    [Test]
    public void 만료된_뒤_인터럽트는_실패()
    {
        var w = new CounterWindow();
        w.Open(0.5f);
        w.TickAndDetectExpiry(0.5f);
        Assert.IsFalse(w.TryConsumeInterrupt(), "창 밖 인터럽트는 데미지만 남는다");
    }

    [Test]
    public void 성공_후_만료_틱은_다시_안_뛴다()
    {
        var w = new CounterWindow();
        w.Open(1f);
        w.TryConsumeInterrupt();
        Assert.IsFalse(w.TickAndDetectExpiry(1f), "성공으로 닫힌 창이 실패로도 처리되면 이중 처리다");
    }

    // ─── 닫기 ──────────────────────────────────────────────────────────

    [Test]
    public void 닫기는_멱등()
    {
        var w = new CounterWindow();
        w.Open(1f);
        w.Close();
        w.Close();
        Assert.IsFalse(w.IsOpen);
        Assert.IsFalse(w.InterruptConsumed);
    }

    [Test]
    public void 강제로_닫으면_인터럽트가_안_먹는다()
    {
        var w = new CounterWindow();
        w.Open(1f);
        w.Close();
        Assert.IsFalse(w.TryConsumeInterrupt());
    }
}
