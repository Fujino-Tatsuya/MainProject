using NUnit.Framework;

/// <summary>입력 차단 토큰 — 여러 요청자가 각자 막고 풀며, 마지막 토큰이 풀릴 때만 열린다(할 일 48 C9).</summary>
public sealed class PlayerInputBlockTokensTests
{
    [Test]
    public void 처음에는_열려_있다()
    {
        var tokens = new PlayerInputBlockTokens();

        Assert.That(tokens.IsBlocked, Is.False);
        Assert.That(tokens.Count, Is.EqualTo(0));
    }

    [Test]
    public void 토큰을_걸면_막히고_풀면_열린다()
    {
        var tokens = new PlayerInputBlockTokens();
        var modal = new object();

        Assert.That(tokens.Acquire(modal), Is.True);
        Assert.That(tokens.IsBlocked, Is.True);

        Assert.That(tokens.Release(modal), Is.True);
        Assert.That(tokens.IsBlocked, Is.False);
    }

    [Test]
    public void 마지막_토큰이_풀릴_때만_열린다()
    {
        var tokens = new PlayerInputBlockTokens();
        var resultPanel = new object();
        var characterPanel = new object();

        Assert.That(tokens.Acquire(resultPanel), Is.True);
        Assert.That(tokens.Acquire(characterPanel), Is.False, "이미 막혀 있으면 상태 변화 없음");

        Assert.That(tokens.Release(resultPanel), Is.False);
        Assert.That(tokens.IsBlocked, Is.True);

        Assert.That(tokens.Release(characterPanel), Is.True);
        Assert.That(tokens.IsBlocked, Is.False);
    }

    [Test]
    public void 같은_토큰을_두_번_걸어도_한_번_풀면_열린다()
    {
        var tokens = new PlayerInputBlockTokens();
        var modal = new object();

        tokens.Acquire(modal);
        Assert.That(tokens.Acquire(modal), Is.False);
        Assert.That(tokens.Count, Is.EqualTo(1));

        Assert.That(tokens.Release(modal), Is.True);
        Assert.That(tokens.IsBlocked, Is.False);
    }

    [Test]
    public void 걸지_않은_토큰과_null_은_무시한다()
    {
        var tokens = new PlayerInputBlockTokens();
        var held = new object();
        tokens.Acquire(held);

        Assert.That(tokens.Release(new object()), Is.False);
        Assert.That(tokens.Release(null), Is.False);
        Assert.That(tokens.Acquire(null), Is.False);
        Assert.That(tokens.IsBlocked, Is.True);
        Assert.That(tokens.Contains(held), Is.True);
        Assert.That(tokens.Contains(null), Is.False);
    }
}
