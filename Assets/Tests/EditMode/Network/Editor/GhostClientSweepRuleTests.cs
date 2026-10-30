using NUnit.Framework;

/// <summary>끊긴 뒤 NGO 에 남은 유령 클라 판정 — 할 일 188(끊긴 id 가 남아 있으면 치움, 서버·정상 접속은 안 건드림, 사라진 id 는 기록 정리).</summary>
public sealed class GhostClientSweepRuleTests
{
    const ulong ServerId = 0;
    const int DisconnectFrame = 100;
    const float DisconnectTime = 10f;

    static GhostClientAction Decide(
        ulong clientId = 2,
        bool stillConnected = true,
        int frame = DisconnectFrame + 1,
        float now = DisconnectTime + 0.1f)
        => GhostClientSweepRule.Decide(
            new DisconnectedClientRecord(clientId, DisconnectFrame, DisconnectTime),
            stillConnected,
            ServerId,
            frame,
            now);

    [Test]
    public void 끊긴_id_가_다음_프레임에도_접속_목록에_있으면_치운다()
    {
        Assert.That(Decide(), Is.EqualTo(GhostClientAction.Disconnect));
    }

    [Test]
    public void 끊긴_그_프레임에는_기다린다()
    {
        Assert.That(Decide(frame: DisconnectFrame), Is.EqualTo(GhostClientAction.Wait));
    }

    [Test]
    public void 서버_id_는_치우지_않고_기록만_뺀다()
    {
        Assert.That(Decide(clientId: ServerId), Is.EqualTo(GhostClientAction.Forget));
    }

    [Test]
    public void 접속_목록에_없으면_늦은_승인을_기다린다()
    {
        Assert.That(Decide(stillConnected: false), Is.EqualTo(GhostClientAction.Wait));
    }

    [Test]
    public void 접속_목록에_없고_지켜보는_시간이_지나면_기록만_뺀다()
    {
        var now = DisconnectTime + GhostClientSweepRule.RetentionSeconds;
        Assert.That(Decide(stillConnected: false, now: now), Is.EqualTo(GhostClientAction.Forget));
    }

    [Test]
    public void 지켜보는_시간_안에_늦게_다시_들어오면_치운다()
    {
        var now = DisconnectTime + GhostClientSweepRule.RetentionSeconds - 0.01f;
        Assert.That(Decide(frame: DisconnectFrame + 30, now: now), Is.EqualTo(GhostClientAction.Disconnect));
    }
}
