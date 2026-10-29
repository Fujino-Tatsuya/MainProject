using NUnit.Framework;

/// <summary>
/// 간파 로컬 선착순 장부(할 일 143-2) — 남의 강 시도·성공은 닫고, 남의 약 시도·내 것은 안 닫고, 새 창은 초기화, 대상별 독립.
/// 신호 연결(디렉터 HandleStarted·창 복제 기록·오너 예측 FindPredictTarget)은 MPPM Play 확인.
/// </summary>
public sealed class InterruptLocalWindowClaimsTests
{
    private const ulong Boss = 100;
    private const ulong OtherBoss = 200;
    private const ulong Me = 1;
    private const ulong Other = 2;

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
        Assert.IsTrue(_claims.Observe(Boss, Other, SlowMotionPurpose.Attempt, Me));
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [Test]
    public void 남의_약_시도는_닫지_않는다()
    {
        Assert.IsFalse(_claims.Observe(Boss, Other, SlowMotionPurpose.AttemptWeak, Me));
        Assert.IsFalse(_claims.IsClosed(Boss), "그 사람이 실패하면 내가 첫 성공일 수 있다");
    }

    [Test]
    public void 남의_성공을_받으면_닫힌다()
    {
        Assert.IsTrue(_claims.Observe(Boss, Other, SlowMotionPurpose.Success, Me));
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [TestCase(SlowMotionPurpose.Attempt)]
    [TestCase(SlowMotionPurpose.Success)]
    public void 내_것은_닫지_않는다(SlowMotionPurpose purpose)
    {
        Assert.IsFalse(_claims.Observe(Boss, Me, purpose, Me));
        Assert.IsFalse(_claims.IsClosed(Boss));
    }

    [Test]
    public void 내_강_시도_뒤에도_남의_성공은_닫는다()
    {
        _claims.Observe(Boss, Me, SlowMotionPurpose.Attempt, Me);
        _claims.Observe(Boss, Other, SlowMotionPurpose.Success, Me);
        Assert.IsTrue(_claims.IsClosed(Boss));
    }

    [Test]
    public void 새_창이_열리면_초기화()
    {
        _claims.Observe(Boss, Other, SlowMotionPurpose.Success, Me);
        _claims.WindowOpened(Boss);
        Assert.IsFalse(_claims.IsClosed(Boss));
    }

    [Test]
    public void 대상별로_독립()
    {
        _claims.Observe(Boss, Other, SlowMotionPurpose.Attempt, Me);
        Assert.IsTrue(_claims.IsClosed(Boss));
        Assert.IsFalse(_claims.IsClosed(OtherBoss));

        _claims.WindowOpened(OtherBoss);
        Assert.IsTrue(_claims.IsClosed(Boss), "다른 대상의 새 창은 이 대상을 초기화하지 않는다");
    }

    [Test]
    public void 대상_없음_신호는_무시()
    {
        Assert.IsFalse(_claims.Observe(0, Other, SlowMotionPurpose.Success, Me));
        Assert.IsFalse(_claims.IsClosed(0));
    }

    [Test]
    public void 디스폰되면_기록을_지운다()
    {
        _claims.Observe(Boss, Other, SlowMotionPurpose.Success, Me);
        _claims.Forget(Boss);
        Assert.IsFalse(_claims.IsClosed(Boss));
    }
}
