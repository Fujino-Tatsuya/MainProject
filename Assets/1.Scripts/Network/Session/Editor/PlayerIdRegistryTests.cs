using NUnit.Framework;

/// <summary>서버 clientId ↔ playerId 레지스트리 — 퇴장 때 clientId 매핑만 지우고 playerId 기록은 남긴다(Q4).</summary>
public sealed class PlayerIdRegistryTests
{
    [Test]
    public void 등록하면_양방향으로_조회된다()
    {
        var registry = new PlayerIdRegistry();
        registry.Register(0, "host-pc");
        registry.Register(3, "booth-2");

        Assert.That(registry.ConnectedCount, Is.EqualTo(2));
        Assert.That(registry.TryGetPlayerId(3, out var playerId), Is.True);
        Assert.That(playerId, Is.EqualTo("booth-2"));
        Assert.That(registry.TryGetClientId("host-pc", out var clientId), Is.True);
        Assert.That(clientId, Is.EqualTo(0UL));
        Assert.That(registry.IsPlayerConnected("booth-2"), Is.True);
    }

    [Test]
    public void 퇴장하면_매핑만_지우고_playerId_기록은_남긴다()
    {
        var registry = new PlayerIdRegistry();
        registry.Register(3, "booth-2");

        Assert.That(registry.RemoveClient(3), Is.True);
        Assert.That(registry.ConnectedCount, Is.Zero);
        Assert.That(registry.IsPlayerConnected("booth-2"), Is.False);
        Assert.That(registry.TryGetPlayerId(3, out _), Is.False);
        Assert.That(registry.IsKnownPlayer("booth-2"), Is.True);
        Assert.That(registry.KnownPlayerIds, Does.Contain("booth-2"));
    }

    [Test]
    public void 재접속하면_새_clientId_로_같은_playerId_를_묶는다()
    {
        var registry = new PlayerIdRegistry();
        registry.Register(3, "booth-2");
        registry.RemoveClient(3);
        registry.Register(7, "booth-2");

        Assert.That(registry.TryGetClientId("booth-2", out var clientId), Is.True);
        Assert.That(clientId, Is.EqualTo(7UL));
        Assert.That(registry.KnownPlayerIds.Count, Is.EqualTo(1));
    }

    [Test]
    public void 모르는_clientId_퇴장은_무시한다()
    {
        var registry = new PlayerIdRegistry();
        Assert.That(registry.RemoveClient(9), Is.False);
        Assert.That(registry.IsPlayerConnected(null), Is.False);
    }

    [Test]
    public void Clear_는_기록까지_지운다()
    {
        var registry = new PlayerIdRegistry();
        registry.Register(3, "booth-2");
        registry.Clear();

        Assert.That(registry.ConnectedCount, Is.Zero);
        Assert.That(registry.IsKnownPlayer("booth-2"), Is.False);
    }
}
