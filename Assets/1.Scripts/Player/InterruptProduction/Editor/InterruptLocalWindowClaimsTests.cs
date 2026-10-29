using NUnit.Framework;

/// <summary>
/// 간파 로컬 선착순 장부(할 일 143-2·143-3) — 남의 강 시도·성공은 닫고, 남의 약 시도·내 것은 안 닫고, 새 창은 초기화, 대상별 독립,
/// 강 시도가 전부 실패하면 다시 열림(성공으로 닫힌 창은 그대로).
/// 신호 연결(디렉터 HandleStarted/Failed/ForcedReset·피어 대상 재탐색·창 복제 기록·오너 예측 FindPredictTarget)은 MPPM Play 확인.
/// </summary>
public sealed class InterruptLocalWindowClaimsTests
{
    private const ulong Boss = 100;
    private const ulong OtherBoss = 200;
    private const ulong Me = 1;
    private const ulong Other = 2;
    private const ulong Third = 3;

    private InterruptLocalWindowClaims _claims;

    [SetUp]
    public void SetUp()
    {
        _claims = new InterruptLocalWindowClaims();
        _claims.WindowOpened(Boss);
        _claims.WindowOpened(OtherBoss);
    }

    [Test]
    public void 아무_신호도_없으면_열려_있다()
    {
        Assert.IsFalse(_claims.IsClosed(Boss));
    }

    [Test]
    public void 남의_강_시도를_받으면_닫힌다()
    {
        Assert.IsTrue(_claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.Attempt, Me));
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [Test]
    public void 남의_약_시도는_닫지_않는다()
    {
        Assert.IsFalse(_claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.AttemptWeak, Me));
        Assert.IsFalse(_claims.IsClosed(Boss), "그 사람이 실패하면 내가 첫 성공일 수 있다");
    }

    [Test]
    public void 남의_성공을_받으면_닫힌다()
    {
        Assert.IsTrue(_claims.ObserveSuccess(Boss, Other, Me));
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [Test]
    public void 내_강_시도는_닫지_않는다()
    {
        Assert.IsFalse(_claims.ObserveAttempt(Boss, 5, Me, SlowMotionPurpose.Attempt, Me));
        Assert.IsFalse(_claims.IsClosed(Boss));
    }

    [Test]
    public void 내_성공은_닫지_않는다()
    {
        Assert.IsFalse(_claims.ObserveSuccess(Boss, Me, Me));
        Assert.IsFalse(_claims.IsClosed(Boss));
    }

    [Test]
    public void 내_강_시도_뒤에도_남의_성공은_닫는다()
    {
        _claims.ObserveAttempt(Boss, 5, Me, SlowMotionPurpose.Attempt, Me);
        _claims.ObserveSuccess(Boss, Other, Me);
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [Test]
    public void 새_창이_열리면_초기화()
    {
        _claims.ObserveSuccess(Boss, Other, Me);
        _claims.WindowOpened(Boss);
        Assert.IsFalse(_claims.IsClosed(Boss));
    }

    [Test]
    public void 대상별로_독립()
    {
        _claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.Attempt, Me);
        Assert.IsTrue(_claims.IsClosed(Boss));
        Assert.IsFalse(_claims.IsClosed(OtherBoss));

        _claims.WindowOpened(OtherBoss);
        Assert.IsTrue(_claims.IsClosed(Boss), "다른 대상의 새 창은 이 대상을 초기화하지 않는다");
    }

    [Test]
    public void 대상_없음_신호는_무시()
    {
        Assert.IsFalse(_claims.ObserveAttempt(0, 5, Other, SlowMotionPurpose.Attempt, Me));
        Assert.IsFalse(_claims.ObserveSuccess(0, Other, Me));
        Assert.IsFalse(_claims.IsClosed(0));
    }

    [Test]
    public void 디스폰되면_기록을_지운다()
    {
        _claims.ObserveSuccess(Boss, Other, Me);
        _claims.Forget(Boss);
        Assert.IsFalse(_claims.IsClosed(Boss));
    }

    // ── 할 일 143-3 — 강 시도 실패 시 다시 열림 ──

    [Test]
    public void 강_시도_뒤_실패를_받으면_다시_열린다()
    {
        _claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.Attempt, Me);
        Assert.AreEqual(1, _claims.AttemptEnded(5));
        Assert.IsFalse(_claims.IsClosed(Boss), "그 뒤 내 간파는 정상 강 예측");
    }

    [Test]
    public void 강_시도_뒤_성공하면_실패_신호가_와도_닫힘_유지()
    {
        _claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.Attempt, Me);
        _claims.ObserveSuccess(Boss, Other, Me);
        Assert.AreEqual(0, _claims.AttemptEnded(5));
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [Test]
    public void 강_시도_뒤_대상_없는_성공도_그_시전자의_시도_창을_닫힘으로_굳힌다()
    {
        _claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.Attempt, Me);
        Assert.IsTrue(_claims.ObserveSuccess(0, Other, Me));
        _claims.AttemptEnded(5);
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [Test]
    public void 두_명_강_시도_중_한_명만_실패하면_아직_닫힘()
    {
        _claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.Attempt, Me);
        _claims.ObserveAttempt(Boss, 6, Third, SlowMotionPurpose.Attempt, Me);

        Assert.AreEqual(0, _claims.AttemptEnded(5));
        Assert.IsTrue(_claims.IsClosed(Boss), "남은 강 시도가 있다");

        Assert.AreEqual(1, _claims.AttemptEnded(6));
        Assert.IsFalse(_claims.IsClosed(Boss), "모두 실패 — 다시 열림");
    }

    [Test]
    public void 상관없는_발동_번호의_실패는_무시()
    {
        _claims.ObserveAttempt(Boss, 5, Other, SlowMotionPurpose.Attempt, Me);
        Assert.AreEqual(0, _claims.AttemptEnded(9));
        Assert.AreEqual(0, _claims.AttemptEnded(0));
        Assert.IsTrue(_claims.IsClosed(Boss));
    }
}
