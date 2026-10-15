using NUnit.Framework;

/// <summary>
/// 인터럽트 스킬 한 번의 슬로우 발동 기록 규칙 — 예측 통과 → 판정 성공이면 그대로, 빗나감·판정 전 종료면 실패 복귀.
/// </summary>
public sealed class InterruptSlowMotionTrackerTests
{
    const uint Id = 7;

    static InterruptSlowMotionTracker Started(uint id = Id)
    {
        var t = new InterruptSlowMotionTracker();
        t.Begin();
        t.Started(id);
        return t;
    }

    // ─── 시작 ──────────────────────────────────────────────────────────

    [Test]
    public void 새_기록은_대기_중이_아니다()
    {
        var t = new InterruptSlowMotionTracker();
        Assert.IsFalse(t.IsPending);
        Assert.AreEqual(0u, t.Abort());
        Assert.AreEqual(0u, t.Resolve(false));
    }

    [Test]
    public void 발동하면_대기_중()
    {
        var t = Started();
        Assert.IsTrue(t.IsPending);
        Assert.AreEqual(Id, t.TriggerId);
    }

    // 예측 실패로 발동하지 않은 스킬은 빗나가도·끝나도 실패 복귀를 보내지 않는다.
    [Test]
    public void 발동하지_않았으면_결론은_항상_없음()
    {
        var t = new InterruptSlowMotionTracker();
        t.Begin();
        Assert.IsFalse(t.IsPending);
        Assert.AreEqual(0u, t.Resolve(false));

        t.Begin();
        Assert.AreEqual(0u, t.Abort());
    }

    // 레이어가 거부(컷신·씬 전환)하면 0 이 온다 — 시작하지 않은 것과 같다.
    [Test]
    public void 레이어가_거부하면_시작하지_않은_것()
    {
        var t = Started(0);
        Assert.IsFalse(t.IsPending);
        Assert.AreEqual(0u, t.Resolve(false));
    }

    // ─── 판정 ──────────────────────────────────────────────────────────

    [Test]
    public void 판정_성공이면_실패_복귀_없음()
    {
        var t = Started();
        Assert.AreEqual(0u, t.Resolve(true));
        Assert.IsFalse(t.IsPending);
    }

    [Test]
    public void 판정_빗나가면_발동_번호로_실패_복귀()
    {
        var t = Started();
        Assert.AreEqual(Id, t.Resolve(false));
        Assert.IsFalse(t.IsPending);
    }

    // ─── 조기 종료 ─────────────────────────────────────────────────────

    [Test]
    public void 판정_전에_끝나면_실패_복귀()
    {
        var t = Started();
        Assert.AreEqual(Id, t.Abort());
        Assert.IsFalse(t.IsPending);
    }

    // OnTick 은 같은 프레임에 판정 → 종료 순으로 부른다. 성공 판정 뒤의 종료가 슬로우를 끊으면 안 된다.
    [Test]
    public void 판정_성공_뒤_종료는_아무것도_안_한다()
    {
        var t = Started();
        t.Resolve(true);
        Assert.AreEqual(0u, t.Abort());
    }

    [Test]
    public void 빗나감은_한_번만_보낸다()
    {
        var t = Started();
        Assert.AreEqual(Id, t.Resolve(false));
        Assert.AreEqual(0u, t.Abort());
        Assert.AreEqual(0u, t.Resolve(false));
    }

    [Test]
    public void 종료_뒤_늦은_판정은_무시()
    {
        var t = Started();
        Assert.AreEqual(Id, t.Abort());
        Assert.AreEqual(0u, t.Resolve(false));
    }

    // 결론이 난 뒤 Started 가 와도 다시 열리지 않는다.
    [Test]
    public void 결론_뒤의_발동_통지는_무시()
    {
        var t = Started();
        t.Abort();
        t.Started(Id + 1);
        Assert.IsFalse(t.IsPending);
        Assert.AreEqual(0u, t.Resolve(false));
    }

    // ─── 재사용 ────────────────────────────────────────────────────────

    [Test]
    public void 다음_승인은_이전_기록을_버린다()
    {
        var t = Started();
        t.Resolve(true);

        t.Begin();
        Assert.IsFalse(t.IsPending, "이전 발동 번호가 새 스킬로 넘어오면 안 된다");
        Assert.AreEqual(0u, t.Abort());

        t.Begin();
        t.Started(Id + 1);
        Assert.AreEqual(Id + 1, t.Resolve(false));
    }
}
