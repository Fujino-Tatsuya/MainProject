using NUnit.Framework;

/// <summary>
/// 간파 선착순 장부(할 일 143) — 첫 성공 기록, 두 번째 거절, 같은 사람 재요청, 창 닫힘·새 창 초기화, 대상별 독립.
/// 판정 지점(PlayerInterruptSkillBase.ResolveHit — 표식 빼고 피해만)과 창 전이 연결(InterruptLagMonitor.RecordWindow)은 MPPM Play 확인.
/// </summary>
public sealed class InterruptWindowClaimsTests
{
    private const int Boss = 100;
    private const int OtherBoss = 200;
    private const int A = 1;
    private const int B = 2;

    private InterruptWindowClaims _claims;

    [SetUp]
    public void SetUp() => _claims = new InterruptWindowClaims();

    [Test]
    public void 성공_전엔_누구나_자유()
    {
        _claims.WindowOpened(Boss);
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(Boss, A, 1f));
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(Boss, B, 1f));
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(OtherBoss, A, 1f), "기록 없는 대상");
    }

    [Test]
    public void 첫_성공을_기록하고_두번째_성공은_거절()
    {
        _claims.WindowOpened(Boss);
        Assert.IsTrue(_claims.Claim(Boss, A, 2f));
        Assert.IsFalse(_claims.Claim(Boss, B, 2.1f), "첫 성공자는 바뀌지 않는다");

        Assert.AreEqual(InterruptClaimVerdict.Preempted, _claims.Evaluate(Boss, B, 1.5f));
        Assert.AreEqual(InterruptClaimVerdict.Mine, _claims.Evaluate(Boss, A, 1.5f));
    }

    [Test]
    public void 창이_열려_있는_동안엔_늦게_시작한_시전도_선점()
    {
        _claims.WindowOpened(Boss);
        _claims.Claim(Boss, A, 2f);
        Assert.AreEqual(InterruptClaimVerdict.Preempted, _claims.Evaluate(Boss, B, 3f));
    }

    [Test]
    public void 같은_사람_재요청은_자기_창()
    {
        _claims.WindowOpened(Boss);
        _claims.Claim(Boss, A, 2f);
        Assert.IsFalse(_claims.Claim(Boss, A, 2.5f));
        Assert.AreEqual(InterruptClaimVerdict.Mine, _claims.Evaluate(Boss, A, 3f));
        _claims.WindowClosed(Boss);
        Assert.AreEqual(InterruptClaimVerdict.Mine, _claims.Evaluate(Boss, A, 3f));
    }

    // 보스는 첫 성공 수신 안에서 창을 닫는다 — 닫힘 → 성공 기록 순서도 같은 창으로 남는다.
    [Test]
    public void 닫힌_뒤_기록돼도_그_창에_이미_시작한_시전은_선점()
    {
        _claims.WindowOpened(Boss);
        _claims.WindowClosed(Boss);
        _claims.Claim(Boss, A, 2f);

        Assert.AreEqual(InterruptClaimVerdict.Preempted, _claims.Evaluate(Boss, B, 1.9f), "첫 성공 전에 시작");
        Assert.AreEqual(InterruptClaimVerdict.Preempted, _claims.Evaluate(Boss, B, 2f), "같은 프레임 시작");
    }

    [Test]
    public void 창이_닫힌_뒤_새로_시작한_시전은_초기화()
    {
        _claims.WindowOpened(Boss);
        _claims.Claim(Boss, A, 2f);
        _claims.WindowClosed(Boss);
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(Boss, B, 2.5f));
    }

    [Test]
    public void 새_창이_열리면_첫_성공자_초기화()
    {
        _claims.WindowOpened(Boss);
        _claims.Claim(Boss, A, 2f);
        _claims.WindowClosed(Boss);

        _claims.RecordWindow(Boss, true);
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(Boss, B, 1f), "앞 창 시전도 새 창에선 자유");
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(Boss, A, 5f), "첫 성공자도 새 창에선 자유");
        Assert.IsTrue(_claims.Claim(Boss, B, 5f));
        Assert.AreEqual(InterruptClaimVerdict.Preempted, _claims.Evaluate(Boss, A, 4.9f));
    }

    [Test]
    public void 대상별로_독립()
    {
        _claims.WindowOpened(Boss);
        _claims.WindowOpened(OtherBoss);
        _claims.Claim(Boss, A, 2f);

        Assert.AreEqual(InterruptClaimVerdict.Preempted, _claims.Evaluate(Boss, B, 1f));
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(OtherBoss, B, 1f));
        Assert.IsTrue(_claims.Claim(OtherBoss, B, 2f));
        Assert.AreEqual(InterruptClaimVerdict.Preempted, _claims.Evaluate(OtherBoss, A, 1f));
    }

    [Test]
    public void 닫힘만_오고_열림_기록이_없으면_아무것도_안_한다()
    {
        _claims.WindowClosed(Boss);
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(Boss, A, 1f));
    }

    [Test]
    public void 비우면_자유()
    {
        _claims.WindowOpened(Boss);
        _claims.Claim(Boss, A, 2f);
        _claims.Clear();
        Assert.AreEqual(InterruptClaimVerdict.Free, _claims.Evaluate(Boss, B, 1f));
    }
}
