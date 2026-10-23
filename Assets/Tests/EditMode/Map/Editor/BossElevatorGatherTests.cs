using System.Collections.Generic;
using NUnit.Framework;

/// <summary>보스 엘리베이터 파티 집결 — 할 일 89(전원 발판 5초, 사망·이탈·인원 변화 취소, Soul 대기, Forced·발판 잠김·연출·도착 대기 중 비활성, Busy 보류).</summary>
public sealed class BossElevatorGatherTests
{
    const double Hold = 5d;

    static readonly BossElevatorGatherConditions Open = new BossElevatorGatherConditions(true, false, false, false);

    static BossElevatorGatherMember On(ulong id) => new BossElevatorGatherMember(id, true, PlayerLifeState.Alive, 100f, true, false);
    static BossElevatorGatherMember Off(ulong id) => new BossElevatorGatherMember(id, true, PlayerLifeState.Alive, 100f, false, false);
    static BossElevatorGatherMember OnBusy(ulong id) => new BossElevatorGatherMember(id, true, PlayerLifeState.Alive, 100f, true, true);
    static BossElevatorGatherMember OnIn(ulong id, PlayerLifeState state) => new BossElevatorGatherMember(id, true, state, state == PlayerLifeState.Alive ? 100f : 0f, true, false);
    static BossElevatorGatherMember Gone(ulong id) => new BossElevatorGatherMember(id, false, PlayerLifeState.Alive, 100f, false, false);

    static List<BossElevatorGatherMember> Members(params BossElevatorGatherMember[] members) => new List<BossElevatorGatherMember>(members);

    [Test]
    public void 전원_집결_전에는_카운트가_없다()
    {
        var gather = new BossElevatorGather(Hold);

        Assert.That(gather.Tick(0d, Open, Members(On(0), Off(1))), Is.False);
        Assert.That(gather.IsCountdownActive, Is.False);
        Assert.That(gather.GatheredCount, Is.EqualTo(1));
        Assert.That(gather.ParticipantCount, Is.EqualTo(2));
        Assert.That(gather.Tick(100d, Open, Members(On(0), Off(1))), Is.False);
    }

    [Test]
    public void 전원_집결하면_5초_뒤_확정_1회()
    {
        var gather = new BossElevatorGather(Hold);
        var all = Members(On(0), On(1));

        Assert.That(gather.Tick(10d, Open, all), Is.False);
        Assert.That(gather.CountdownEndTime, Is.EqualTo(15d));
        Assert.That(gather.Tick(14.9d, Open, all), Is.False);
        Assert.That(gather.Tick(15d, Open, all), Is.True);
        Assert.That(gather.IsConfirmed, Is.True);
        Assert.That(gather.Tick(30d, Open, all), Is.False);
    }

    [Test]
    public void 기본_유지_시간은_PartyGatherRules_기본값()
    {
        Assert.That(new BossElevatorGather().HoldSeconds, Is.EqualTo(PartyGatherRules.DefaultHoldSeconds));
    }

    [Test]
    public void 카운트_중_이탈하면_취소되고_복귀하면_5초부터_다시()
    {
        var gather = new BossElevatorGather(Hold);
        gather.Tick(0d, Open, Members(On(0), On(1)));

        gather.Tick(4.9d, Open, Members(On(0), Off(1)));
        Assert.That(gather.IsCountdownActive, Is.False);

        gather.Tick(6d, Open, Members(On(0), On(1)));
        Assert.That(gather.CountdownEndTime, Is.EqualTo(11d));
        Assert.That(gather.Tick(10.9d, Open, Members(On(0), On(1))), Is.False);
        Assert.That(gather.Tick(11d, Open, Members(On(0), On(1))), Is.True);
    }

    [Test]
    public void 카운트_중_사망하면_취소된다([Values(PlayerLifeState.DeadPresentation, PlayerLifeState.Soul)] PlayerLifeState down)
    {
        var gather = new BossElevatorGather(Hold);
        gather.Tick(0d, Open, Members(On(0), On(1)));

        Assert.That(gather.Tick(5d, Open, Members(On(0), OnIn(1, down))), Is.False);
        Assert.That(gather.IsCountdownActive, Is.False);
        Assert.That(gather.GatheredCount, Is.EqualTo(1));
        Assert.That(gather.ParticipantCount, Is.EqualTo(2), "쓰러져도 참가자다(PermanentDead 만 빠진다)");
    }

    [Test]
    public void 체력_0이면_Alive_여도_쓰러짐()
    {
        var gather = new BossElevatorGather(Hold);
        var zeroHp = new BossElevatorGatherMember(1, true, PlayerLifeState.Alive, 0f, true, false);

        gather.Tick(0d, Open, Members(On(0), zeroHp));
        Assert.That(gather.IsCountdownActive, Is.False);
    }

    [Test]
    public void Soul_은_집결로_인정하지_않고_부활하면_그때부터_5초()
    {
        var gather = new BossElevatorGather(Hold);

        gather.Tick(0d, Open, Members(On(0), OnIn(1, PlayerLifeState.Soul)));
        Assert.That(gather.IsCountdownActive, Is.False);
        Assert.That(gather.Tick(20d, Open, Members(On(0), OnIn(1, PlayerLifeState.Soul))), Is.False);

        gather.Tick(30d, Open, Members(On(0), On(1)));
        Assert.That(gather.CountdownEndTime, Is.EqualTo(35d));
    }

    [Test]
    public void PermanentDead_와_끊긴_사람은_참가자에서_빠진다()
    {
        var gather = new BossElevatorGather(Hold);

        gather.Tick(0d, Open, Members(On(0), OnIn(1, PlayerLifeState.PermanentDead), Gone(2)));
        Assert.That(gather.ParticipantCount, Is.EqualTo(1));
        Assert.That(gather.GatheredCount, Is.EqualTo(1));
        Assert.That(gather.IsCountdownActive, Is.True);
        Assert.That(gather.Tick(5d, Open, Members(On(0), OnIn(1, PlayerLifeState.PermanentDead), Gone(2))), Is.True);
    }

    [Test]
    public void 인원_변화는_카운트를_0으로_돌린다()
    {
        var gather = new BossElevatorGather(Hold);
        gather.Tick(0d, Open, Members(On(0), On(1), Off(2)));
        Assert.That(gather.IsCountdownActive, Is.False);

        // 밖에 있던 사람이 끊기면 남은 전원이 안 — 그 순간부터 5초.
        gather.Tick(3d, Open, Members(On(0), On(1), Gone(2)));
        Assert.That(gather.CountdownEndTime, Is.EqualTo(8d));

        // 카운트 중 한 명이 영구 사망 → 인원 변화 → 0, 남은 전원이 안이면 같은 판정에서 처음부터.
        gather.Tick(5d, Open, Members(On(0), OnIn(1, PlayerLifeState.PermanentDead), Gone(2)));
        Assert.That(gather.CountdownEndTime, Is.EqualTo(10d));

        // 재접속(참가자 추가)도 인원 변화 — 밖이면 시작하지 않는다.
        gather.Tick(6d, Open, Members(On(0), OnIn(1, PlayerLifeState.PermanentDead), Off(2)));
        Assert.That(gather.IsCountdownActive, Is.False);
    }

    [Test]
    public void 참가자가_없으면_시작하지_않는다()
    {
        var gather = new BossElevatorGather(Hold);

        Assert.That(gather.Tick(0d, Open, Members()), Is.False);
        Assert.That(gather.Tick(0d, Open, Members(Gone(0))), Is.False);
        Assert.That(gather.IsCountdownActive, Is.False);
    }

    [Test]
    public void 발판_잠김_Forced_연출_도착_대기_중에는_판정하지_않는다()
    {
        var blocked = new[]
        {
            new BossElevatorGatherConditions(false, false, false, false), // 튜토리얼 발판 잠김
            new BossElevatorGatherConditions(true, true, false, false),   // Forced 카운트 진행 중
            new BossElevatorGatherConditions(true, false, true, false),   // 엘리베이터 연출 중
            new BossElevatorGatherConditions(true, false, false, true),   // 도착 대기 중
        };

        foreach (var condition in blocked)
        {
            Assert.That(condition.IsJudgingActive, Is.False);
            var gather = new BossElevatorGather(Hold);
            Assert.That(gather.Tick(0d, condition, Members(On(0), On(1))), Is.False);
            Assert.That(gather.Tick(10d, condition, Members(On(0), On(1))), Is.False);
            Assert.That(gather.IsCountdownActive, Is.False);
        }

        Assert.That(Open.IsJudgingActive, Is.True);
    }

    [Test]
    public void 카운트_중_비활성이_되면_취소되고_풀리면_처음부터()
    {
        var gather = new BossElevatorGather(Hold);
        var all = Members(On(0), On(1));
        gather.Tick(0d, Open, all);

        gather.Tick(2d, new BossElevatorGatherConditions(false, false, false, false), all);
        Assert.That(gather.IsCountdownActive, Is.False);

        gather.Tick(3d, Open, all);
        Assert.That(gather.CountdownEndTime, Is.EqualTo(8d));
    }

    [Test]
    public void 진행_중_행동이_있으면_시작만_미룬다()
    {
        var gather = new BossElevatorGather(Hold);

        gather.Tick(0d, Open, Members(On(0), OnBusy(1)));
        Assert.That(gather.IsCountdownActive, Is.False);
        Assert.That(gather.GatheredCount, Is.EqualTo(2), "Busy 여도 집결 인원에는 든다");

        gather.Tick(1d, Open, Members(On(0), On(1)));
        Assert.That(gather.CountdownEndTime, Is.EqualTo(6d));

        // 시작 뒤 행동은 카운트를 끊지 않는다.
        Assert.That(gather.Tick(3d, Open, Members(OnBusy(0), On(1))), Is.False);
        Assert.That(gather.IsCountdownActive, Is.True);
        Assert.That(gather.Tick(6d, Open, Members(OnBusy(0), On(1))), Is.True);
    }

    [Test]
    public void Busy_는_Idle_Move_가_아닌_상태()
    {
        Assert.That(BossElevatorGather.IsBusy(PlayerActionState.Idle), Is.False);
        Assert.That(BossElevatorGather.IsBusy(PlayerActionState.Move), Is.False);
        Assert.That(BossElevatorGather.IsBusy(PlayerActionState.Dash), Is.True);
    }

    [Test]
    public void 재도전_리셋은_래치를_풀고_다시_판정한다()
    {
        var gather = new BossElevatorGather(Hold);
        var all = Members(On(0));
        gather.Tick(0d, Open, all);
        Assert.That(gather.Tick(5d, Open, all), Is.True);

        gather.ResetConfirmation();
        Assert.That(gather.IsConfirmed, Is.False);
        gather.Tick(20d, Open, all);
        Assert.That(gather.Tick(25d, Open, all), Is.True);
    }
}
