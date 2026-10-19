using NUnit.Framework;

/// <summary>로비 준비·카운트다운 규칙 — PLAN-48 §4.6(호스트 항상 Ready 예외 없음, 새 접속 = Ready 초기화, 시작 1회 래치).</summary>
public sealed class LobbyReadyRulesTests
{
    const double Countdown = 5d;
    const double Cooldown = 0.5d;

    static LobbyReadyRules CreateWith(params ulong[] clientIds)
    {
        var rules = new LobbyReadyRules(3);
        foreach (var clientId in clientIds)
            rules.AddPlayer(clientId, 0);
        return rules;
    }

    [Test]
    public void 혼자_준비하면_카운트다운이_시작된다()
    {
        var rules = CreateWith(0);

        Assert.That(rules.SetReady(0, true, 10d, Countdown), Is.True);
        Assert.That(rules.IsCountdownActive, Is.True);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(15d));
    }

    [Test]
    public void 호스트도_준비해야_한다_자동_Ready_없음()
    {
        var rules = CreateWith(0, 1);

        Assert.That(rules.TryGetSeat(0, out var host), Is.True);
        Assert.That(host.Ready, Is.False);

        rules.SetReady(1, true, 0d, Countdown);
        Assert.That(rules.IsCountdownActive, Is.False);

        rules.SetReady(0, true, 1d, Countdown);
        Assert.That(rules.IsCountdownActive, Is.True);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(6d));
    }

    [Test]
    public void 접속자가_0명이면_시작하지_않는다()
    {
        var rules = new LobbyReadyRules(3);

        Assert.That(rules.AreAllReady, Is.False);
        Assert.That(rules.SetReady(0, true, 0d, Countdown), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.TryConsumeStart(100d), Is.False);
    }

    [Test]
    public void 정원을_넘으면_전원_Ready_여도_시작하지_않는다()
    {
        var rules = new LobbyReadyRules(1);
        rules.AddPlayer(0, 0);
        rules.AddPlayer(1, 0);

        rules.SetReady(0, true, 0d, Countdown);
        rules.SetReady(1, true, 0d, Countdown);

        Assert.That(rules.IsCountdownActive, Is.False);
    }

    [Test]
    public void 한_명이_준비를_풀면_카운트다운이_취소된다()
    {
        var rules = CreateWith(0, 1);
        rules.SetReady(0, true, 0d, Countdown);
        rules.SetReady(1, true, 0d, Countdown);

        Assert.That(rules.ToggleReady(1, 2d, Countdown), Is.True);

        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(LobbyReadyRules.NoCountdown));
        Assert.That(rules.TryConsumeStart(100d), Is.False);
    }

    [Test]
    public void 다시_전원_준비하면_새_시각으로_다시_시작한다()
    {
        var rules = CreateWith(0);
        rules.SetReady(0, true, 0d, Countdown);
        rules.SetReady(0, false, 1d, Countdown);
        rules.SetReady(0, true, 3d, Countdown);

        Assert.That(rules.CountdownEndTime, Is.EqualTo(8d));
    }

    [Test]
    public void 퇴장하면_카운트다운_취소와_Ready_초기화()
    {
        var rules = CreateWith(0, 1);
        rules.SetReady(0, true, 0d, Countdown);
        rules.SetReady(1, true, 0d, Countdown);

        Assert.That(rules.RemovePlayer(1), Is.True);

        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.ReadyCount, Is.Zero);
        Assert.That(rules.Seats.Count, Is.EqualTo(1));
    }

    [Test]
    public void 새_접속이_오면_전원_Ready_초기화와_카운트다운_취소()
    {
        var rules = CreateWith(0);
        rules.SetReady(0, true, 0d, Countdown);

        Assert.That(rules.AddPlayer(2, 1), Is.True);

        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.ReadyCount, Is.Zero);
        Assert.That(rules.TryGetSeat(2, out var seat), Is.True);
        Assert.That(seat.CharacterId, Is.EqualTo(1));
    }

    [Test]
    public void 이미_있는_clientId_는_다시_더하지_않고_Ready_도_유지한다()
    {
        var rules = CreateWith(0, 1);
        rules.SetReady(1, true, 0d, Countdown);

        Assert.That(rules.AddPlayer(1, 2), Is.False);
        Assert.That(rules.TryGetSeat(1, out var seat), Is.True);
        Assert.That(seat.Ready, Is.True);
        Assert.That(seat.CharacterId, Is.Zero);
    }

    [Test]
    public void 카운트다운_종료는_서버_시각이_종료_시각_이상일_때_한_번만()
    {
        var rules = CreateWith(0);
        rules.SetReady(0, true, 10d, Countdown);

        Assert.That(rules.TryConsumeStart(14.99d), Is.False);
        Assert.That(rules.TryConsumeStart(15d), Is.True);
        Assert.That(rules.HasStarted, Is.True);
        Assert.That(rules.TryConsumeStart(16d), Is.False);
    }

    [Test]
    public void 시작_래치_뒤에는_준비와_인원_변경이_무시된다()
    {
        var rules = CreateWith(0);
        rules.SetReady(0, true, 0d, Countdown);
        rules.TryConsumeStart(Countdown);

        Assert.That(rules.SetReady(0, false, 6d, Countdown), Is.False);
        rules.AddPlayer(1, 0);
        Assert.That(rules.IsCountdownActive, Is.True, "래치 뒤 접속 승인은 계속 진행 중으로 봐야 한다");
        Assert.That(rules.TryGetSeat(0, out var seat) && seat.Ready, Is.True);
    }

    [Test]
    public void 카운트다운_0초도_진행_중으로_보이고_바로_끝난다()
    {
        var rules = CreateWith(0);
        rules.SetReady(0, true, 0d, 0d);

        Assert.That(rules.IsCountdownActive, Is.True);
        Assert.That(rules.TryConsumeStart(0.001d), Is.True);
    }

    [Test]
    public void Ready_상태면_캐릭터_변경_거부()
    {
        var rules = CreateWith(0, 1);
        rules.SetReady(1, true, 0d, Countdown);

        Assert.That(rules.TryChangeCharacter(1, 2, true, 1d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.Ready));
        Assert.That(rules.TryGetSeat(1, out var seat) && seat.CharacterId == 0, Is.True);
    }

    [Test]
    public void 카운트다운_중이면_캐릭터_변경_거부()
    {
        var rules = CreateWith(0);
        rules.SetReady(0, true, 0d, Countdown);

        Assert.That(rules.CanChangeCharacter(0, 1d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.CountdownActive));
    }

    [Test]
    public void 시작_래치_뒤에는_GameStarting()
    {
        var rules = CreateWith(0);
        rules.SetReady(0, true, 0d, Countdown);
        rules.TryConsumeStart(Countdown);

        Assert.That(rules.CanChangeCharacter(0, 6d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.GameStarting));
    }

    [Test]
    public void 모르는_사람과_못_쓰는_캐릭터는_거부()
    {
        var rules = CreateWith(0);

        Assert.That(rules.TryChangeCharacter(9, 1, true, 0d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.UnknownPlayer));
        Assert.That(rules.TryChangeCharacter(0, 7, false, 0d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.UnavailableCharacter));
    }

    [Test]
    public void 교체_쿨다운_안에서는_거부하고_지나면_허용()
    {
        var rules = CreateWith(0);

        Assert.That(rules.TryChangeCharacter(0, 1, true, 10d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None));
        Assert.That(rules.TryChangeCharacter(0, 2, true, 10.3d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.Cooldown));
        Assert.That(rules.TryChangeCharacter(0, 2, true, 10.5d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None));
        Assert.That(rules.TryGetSeat(0, out var seat) && seat.CharacterId == 2, Is.True);
    }

    [Test]
    public void 같은_캐릭터_요청은_쿨다운을_걸지_않는다()
    {
        var rules = CreateWith(0);

        Assert.That(rules.TryChangeCharacter(0, 0, true, 10d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None));
        Assert.That(rules.TryChangeCharacter(0, 1, true, 10.1d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None));
    }

    [Test]
    public void 남은_초는_0_이상이고_카운트다운이_없으면_0()
    {
        Assert.That(LobbyReadyRules.RemainingSeconds(15d, 12d), Is.EqualTo(3d));
        Assert.That(LobbyReadyRules.RemainingSeconds(15d, 20d), Is.Zero);
        Assert.That(LobbyReadyRules.RemainingSeconds(0d, 3d), Is.Zero);
        Assert.That(LobbyReadyRules.RemainingSeconds(-1d, 3d), Is.Zero);
    }

    [Test]
    public void 교체_진행_중이면_SwapInProgress_로_거부하고_끝나면_허용()
    {
        var rules = CreateWith(0);

        Assert.That(rules.BeginSwap(0), Is.True);
        Assert.That(rules.IsSwapping(0), Is.True);
        Assert.That(rules.CanChangeCharacter(0, 100d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.SwapInProgress));
        Assert.That(rules.BeginSwap(0), Is.False, "같은 사람의 교체는 하나만");

        rules.EndSwap(0);
        Assert.That(rules.CanChangeCharacter(0, 100d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None));
    }

    [Test]
    public void 교체_진행은_사람별이고_자리에_없으면_시작하지_않는다()
    {
        var rules = CreateWith(0, 1);

        Assert.That(rules.BeginSwap(9), Is.False);
        rules.BeginSwap(0);
        Assert.That(rules.CanChangeCharacter(1, 0d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None));
    }

    [Test]
    public void 준비가_교체_진행보다_먼저_보인다()
    {
        var rules = CreateWith(0, 1);
        rules.BeginSwap(0);
        rules.SetReady(0, true, 0d, Countdown);

        Assert.That(rules.CanChangeCharacter(0, 0d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.Ready));
    }

    [Test]
    public void 퇴장하면_교체_진행_표시도_지운다()
    {
        var rules = CreateWith(0);
        rules.BeginSwap(0);
        rules.RemovePlayer(0);
        rules.AddPlayer(0, 0);

        Assert.That(rules.IsSwapping(0), Is.False);
    }

    [Test]
    public void 교체_확정은_진행_중에도_자리를_바꾸고_쿨다운을_건다()
    {
        var rules = CreateWith(0);
        rules.BeginSwap(0);

        Assert.That(rules.CommitCharacter(0, 2, 10d), Is.True);
        Assert.That(rules.TryGetSeat(0, out var seat) && seat.CharacterId == 2, Is.True);
        Assert.That(rules.CommitCharacter(0, 2, 10d), Is.False, "같은 캐릭터");

        rules.EndSwap(0);
        Assert.That(rules.CanChangeCharacter(0, 10.3d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.Cooldown));
        Assert.That(rules.CanChangeCharacter(0, 10.5d, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None));
    }

    // 서버 흐름(LobbySession.RequestCharacterServerRpc)과 같은 순서: 검사 → 진행 표시 → 확정 → 다음 프레임 해제.
    [Test]
    public void 교체_10회_연속은_쿨다운_간격이면_모두_허용()
    {
        var rules = CreateWith(0);
        for (var i = 0; i < 10; i++)
        {
            var now = i * Cooldown;
            var next = (i + 1) % 3;
            Assert.That(rules.CanChangeCharacter(0, now, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.None), $"{i}회째 검사");
            Assert.That(rules.BeginSwap(0), Is.True, $"{i}회째 시작");
            Assert.That(rules.CommitCharacter(0, next, now), Is.True, $"{i}회째 확정");
            Assert.That(rules.CanChangeCharacter(0, now, Cooldown), Is.EqualTo(LobbyCharacterChangeReject.SwapInProgress), $"{i}회째 연타");
            rules.EndSwap(0);
        }

        Assert.That(rules.TryGetSeat(0, out var seat) && seat.CharacterId == 10 % 3, Is.True);
    }
}
