using NUnit.Framework;

/// <summary>파티 집결 규칙 — 할 일 86 PLAN §3.1·§7(유지 5초, 이탈·인원 변화 즉시 0, 사망·쓰러짐 미인정, 확정 1회 래치).</summary>
public sealed class PartyGatherRulesTests
{
    const double Hold = 5d;

    static readonly PartyGatherMemberState In = new PartyGatherMemberState(true, false, false);
    static readonly PartyGatherMemberState Out = new PartyGatherMemberState(false, false, false);
    static readonly PartyGatherMemberState InDown = new PartyGatherMemberState(true, true, false);
    static readonly PartyGatherMemberState InBusy = new PartyGatherMemberState(true, false, true);

    static PartyGatherRules CreateActive(params ulong[] clientIds)
    {
        var rules = new PartyGatherRules(Hold);
        rules.SetParticipants(clientIds);
        rules.SetActive(true);
        return rules;
    }

    static void SetAll(PartyGatherRules rules, PartyGatherMemberState state)
    {
        foreach (var clientId in rules.Participants)
            rules.SetMemberState(clientId, state);
    }

    [Test]
    public void 기본_유지_시간은_5초다()
    {
        Assert.That(new PartyGatherRules().HoldSeconds, Is.EqualTo(5d));
    }

    [Test]
    public void 전원_입장하면_5초_뒤_확정_1회()
    {
        var rules = CreateActive(0, 1, 2);
        SetAll(rules, In);

        Assert.That(rules.Tick(10d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.True);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(15d));
        Assert.That(rules.Tick(14.99d), Is.False);

        Assert.That(rules.Tick(15d), Is.True);
        Assert.That(rules.IsConfirmed, Is.True);
        Assert.That(rules.IsCountdownActive, Is.False);
    }

    [Test]
    public void 혼자여도_집결로_확정된다()
    {
        var rules = CreateActive(0);
        rules.SetMemberState(0, In);

        rules.Tick(0d);
        Assert.That(rules.Tick(5d), Is.True);
    }

    [Test]
    public void 확정_뒤에는_중복_확정이_없다()
    {
        var rules = CreateActive(0, 1);
        SetAll(rules, In);
        rules.Tick(0d);
        Assert.That(rules.Tick(5d), Is.True);

        Assert.That(rules.Tick(6d), Is.False);
        Assert.That(rules.Tick(100d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
    }

    [Test]
    public void 확정_래치는_ResetConfirmation_으로만_풀린다()
    {
        var rules = CreateActive(0);
        rules.SetMemberState(0, In);
        rules.Tick(0d);
        rules.Tick(5d);

        rules.SetActive(false);
        rules.SetActive(true);
        Assert.That(rules.IsConfirmed, Is.True);

        rules.ResetConfirmation();
        Assert.That(rules.IsConfirmed, Is.False);
        rules.Tick(10d);
        Assert.That(rules.Tick(15d), Is.True);
    }

    [Test]
    public void 확정_4_9초에_한_명이_나가면_0으로_초기화된다()
    {
        var rules = CreateActive(0, 1);
        SetAll(rules, In);
        rules.Tick(0d);

        rules.SetMemberState(1, Out);
        Assert.That(rules.Tick(4.9d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);

        // 다시 들어오면 처음부터 5초.
        rules.SetMemberState(1, In);
        Assert.That(rules.Tick(5d), Is.False);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(10d));
        Assert.That(rules.Tick(9.9d), Is.False);
        Assert.That(rules.Tick(10d), Is.True);
    }

    [Test]
    public void 참여자가_추가되면_0으로_초기화된다()
    {
        var rules = CreateActive(0, 1);
        SetAll(rules, In);
        rules.Tick(0d);

        Assert.That(rules.AddParticipant(2), Is.True);
        Assert.That(rules.IsCountdownActive, Is.False);

        // 새 참여자가 밖이면 시작하지 않는다.
        Assert.That(rules.Tick(5d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.GatheredCount, Is.EqualTo(2));
        Assert.That(rules.ParticipantCount, Is.EqualTo(3));
    }

    [Test]
    public void 참여자가_제거되면_0으로_초기화되고_남은_전원이_안이면_다시_시작해_확정된다()
    {
        var rules = CreateActive(0, 1, 2);
        rules.SetMemberState(0, In);
        rules.SetMemberState(1, In);
        rules.SetMemberState(2, Out);
        rules.Tick(0d);
        Assert.That(rules.IsCountdownActive, Is.False);

        // 연결 종료 확정 → 명단에서 제거.
        Assert.That(rules.RemoveParticipant(2), Is.True);
        Assert.That(rules.ParticipantCount, Is.EqualTo(2));

        rules.Tick(1d);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(6d));
        Assert.That(rules.Tick(6d), Is.True);
    }

    [Test]
    public void 카운트다운_중_참여자_제거도_0으로_초기화한다()
    {
        var rules = CreateActive(0, 1);
        SetAll(rules, In);
        rules.Tick(0d);

        rules.RemoveParticipant(1);
        Assert.That(rules.IsCountdownActive, Is.False);
        rules.Tick(3d);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(8d));
    }

    [Test]
    public void 명단_교체는_구성이_바뀔_때만_초기화한다()
    {
        var rules = CreateActive(0, 1);
        SetAll(rules, In);
        rules.Tick(0d);

        Assert.That(rules.SetParticipants(new ulong[] { 1, 0 }), Is.False);
        Assert.That(rules.IsCountdownActive, Is.True);

        Assert.That(rules.SetParticipants(new ulong[] { 0 }), Is.True);
        Assert.That(rules.IsCountdownActive, Is.False);
    }

    [Test]
    public void 쓰러짐_사망_참여자가_있으면_확정할_수_없다()
    {
        var rules = CreateActive(0, 1);
        rules.SetMemberState(0, In);
        rules.SetMemberState(1, InDown);

        rules.Tick(0d);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.Tick(100d), Is.False);
        Assert.That(rules.IsConfirmed, Is.False);
        Assert.That(rules.GatheredCount, Is.EqualTo(1));
    }

    [Test]
    public void 카운트다운_중_누가_쓰러지면_0으로_초기화된다()
    {
        var rules = CreateActive(0, 1);
        SetAll(rules, In);
        rules.Tick(0d);

        rules.SetMemberState(0, InDown);
        Assert.That(rules.Tick(2d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
    }

    [Test]
    public void 진행_중_행동이_있으면_카운트다운을_시작하지_않는다()
    {
        var rules = CreateActive(0, 1);
        rules.SetMemberState(0, In);
        rules.SetMemberState(1, InBusy);

        Assert.That(rules.Tick(0d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.GatheredCount, Is.EqualTo(2));

        // 행동이 끝나면 그때부터 5초.
        rules.SetMemberState(1, In);
        rules.Tick(2d);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(7d));
    }

    [Test]
    public void 카운트다운_중_행동을_시작해도_카운트다운은_이어진다()
    {
        // 카운트다운 중 입력 제한은 없다(사용자 결정 10-22) — 시작 뒤의 행동은 카운트다운을 끊지 않는다.
        var rules = CreateActive(0, 1);
        SetAll(rules, In);
        rules.Tick(0d);

        rules.SetMemberState(1, InBusy);
        Assert.That(rules.Tick(3d), Is.False);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(5d));
        Assert.That(rules.Tick(5d), Is.True);
    }

    [Test]
    public void 비활성이면_판정도_카운트도_없다()
    {
        var rules = new PartyGatherRules(Hold);
        rules.SetParticipants(new ulong[] { 0 });
        rules.SetMemberState(0, In);

        Assert.That(rules.Tick(0d), Is.False);
        Assert.That(rules.Tick(100d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.IsConfirmed, Is.False);
    }

    [Test]
    public void 카운트다운_중_비활성으로_바꾸면_0으로_초기화된다()
    {
        var rules = CreateActive(0);
        rules.SetMemberState(0, In);
        rules.Tick(0d);

        rules.SetActive(false);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.Tick(5d), Is.False);

        rules.SetActive(true);
        rules.Tick(6d);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(11d));
    }

    [Test]
    public void 참여자가_없으면_시작하지_않는다()
    {
        var rules = CreateActive();

        Assert.That(rules.Tick(0d), Is.False);
        Assert.That(rules.Tick(10d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
    }

    [Test]
    public void 상태를_받지_못한_참여자는_밖으로_본다()
    {
        var rules = CreateActive(0, 1);
        rules.SetMemberState(0, In);

        rules.Tick(0d);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.GatheredCount, Is.EqualTo(1));
    }

    [Test]
    public void 참여자가_아닌_상태는_무시한다()
    {
        var rules = CreateActive(0);

        Assert.That(rules.SetMemberState(9, In), Is.False);
        Assert.That(rules.GatheredCount, Is.EqualTo(0));
    }

    [Test]
    public void 집결_n_m_은_안에_있고_유효한_인원_대_참여자_수다()
    {
        var rules = CreateActive(0, 1, 2);
        rules.SetMemberState(0, In);
        rules.SetMemberState(1, InDown);
        rules.SetMemberState(2, Out);

        Assert.That(rules.GatheredCount, Is.EqualTo(1));
        Assert.That(rules.ParticipantCount, Is.EqualTo(3));

        rules.SetMemberState(1, InBusy);
        rules.SetMemberState(2, In);
        Assert.That(rules.GatheredCount, Is.EqualTo(3));
        Assert.That(rules.AreAllGathered, Is.True);
    }

    [Test]
    public void 남은_초는_0_이상이다()
    {
        Assert.That(PartyGatherRules.RemainingSeconds(15d, 12d), Is.EqualTo(3d));
        Assert.That(PartyGatherRules.RemainingSeconds(15d, 20d), Is.EqualTo(0d));
        Assert.That(PartyGatherRules.RemainingSeconds(PartyGatherRules.NoCountdown, 5d), Is.EqualTo(0d));
    }
}
