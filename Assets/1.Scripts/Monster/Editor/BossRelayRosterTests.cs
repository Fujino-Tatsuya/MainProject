using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 레이드 '간파 릴레이' 명단·잠금(PLAN-boss-raid-relay §1). 번호는 기획 기믹1 §9 의 인수 테스트(T01~).
/// </summary>
public class BossRelayRosterTests
{
    const ulong A = 1, B = 2, C = 3;

    static BossRelayRoster Three()
    {
        var r = new BossRelayRoster();
        r.Begin(new[] { A, B, C });
        return r;
    }

    [Test]
    public void T01_세명이_한번씩_성공하면_전원_해제()
    {
        BossRelayRoster r = Three();
        Assert.That(r.RecordSuccess(A), Is.False);
        Assert.That(r.RecordSuccess(B), Is.False);
        Assert.That(r.RecordSuccess(C), Is.True, "세 번째 성공 = 라운드 넘어감");
        Assert.That(r.IsLocked(A) || r.IsLocked(B) || r.IsLocked(C), Is.False);
        Assert.That(r.Round, Is.EqualTo(2));
    }

    [Test]
    public void T02_성공한_사람은_같은_라운드에_다시_인정되지_않는다()
    {
        BossRelayRoster r = Three();
        r.RecordSuccess(A);
        Assert.That(r.CanAccept(A), Is.EqualTo(BossRelayRoster.Accept.Locked));
        Assert.That(r.RecordSuccess(A), Is.False);
        Assert.That(r.SucceededCount, Is.EqualTo(1));
    }

    [Test]
    public void T03_라운드_경계에서_같은_사람_연속_가능()
    {
        BossRelayRoster r = Three();
        r.RecordSuccess(A); r.RecordSuccess(B); r.RecordSuccess(C);
        Assert.That(r.CanAccept(C), Is.EqualTo(BossRelayRoster.Accept.Accepted), "직전 라운드 마지막 사람이 첫 번째여도 된다");
        r.RecordSuccess(C);
        Assert.That(r.IsLocked(C), Is.True);
    }

    [Test]
    public void 명단에_없는_사람은_인정하지_않는다()
    {
        BossRelayRoster r = Three();
        Assert.That(r.CanAccept(99), Is.EqualTo(BossRelayRoster.Accept.NotParticipant));
    }

    [Test]
    public void T13_두명_성공_뒤_남은_사람이_쓰러지면_해제()
    {
        BossRelayRoster r = Three();
        r.RecordSuccess(A); r.RecordSuccess(B);
        Assert.That(r.Remove(C), Is.True);
        Assert.That(r.IsLocked(A) || r.IsLocked(B), Is.False);
        Assert.That(r.ParticipantCount, Is.EqualTo(2));
    }

    [Test]
    public void T14_한명_성공_뒤_다른_사람_이탈하면_잠금_유지()
    {
        BossRelayRoster r = Three();
        r.RecordSuccess(A);
        Assert.That(r.Remove(B), Is.False);
        Assert.That(r.IsLocked(A), Is.True);
        Assert.That(r.RecordSuccess(C), Is.True, "C 성공 = A·C 전원 → 해제");
    }

    [Test]
    public void T15_부활자는_다음_라운드에_편입()
    {
        BossRelayRoster r = Three();
        r.Remove(C);
        r.RecordSuccess(A);
        Assert.That(r.AddPending(C), Is.False);
        Assert.That(r.CanAccept(C), Is.EqualTo(BossRelayRoster.Accept.NotParticipant), "현재 라운드는 그대로");
        Assert.That(r.RecordSuccess(B), Is.True);
        Assert.That(r.IsParticipant(C), Is.True);
        Assert.That(r.ParticipantCount, Is.EqualTo(3));
    }

    [Test]
    public void T16_현재_명단이_비고_대기자만_있으면_즉시_새_라운드()
    {
        var r = new BossRelayRoster();
        r.Begin(new[] { A });
        r.Remove(A);
        Assert.That(r.AddPending(B), Is.True);
        Assert.That(r.CanAccept(B), Is.EqualTo(BossRelayRoster.Accept.Accepted));
    }

    [Test]
    public void T17_한명이면_성공할_때마다_라운드_갱신()
    {
        var r = new BossRelayRoster();
        r.Begin(new[] { A });
        Assert.That(r.RecordSuccess(A), Is.True);
        Assert.That(r.RecordSuccess(A), Is.True);
        Assert.That(r.Round, Is.EqualTo(3));
    }

    [Test]
    public void T18_전원_이탈하면_명단이_빈다()
    {
        var r = new BossRelayRoster();
        r.Begin(new[] { A, B });
        r.Remove(A);
        Assert.That(r.Remove(B), Is.False);
        Assert.That(r.ParticipantCount, Is.EqualTo(0));
    }
}

public class BossChainCounterRulesTests
{
    [TestCase(1, 2)]
    [TestCase(2, 3)]
    [TestCase(3, 4)]
    [TestCase(0, 2)]
    public void 회차_수는_인원_더하기_1(int party, int rounds) =>
        Assert.That(BossChainCounterRules.RoundsFor(party), Is.EqualTo(rounds));

    [TestCase(4, 4, BossChainCounterRules.Outcome.Full)]
    [TestCase(2, 4, BossChainCounterRules.Outcome.Partial)]
    [TestCase(2, 3, BossChainCounterRules.Outcome.Partial)]
    [TestCase(1, 3, BossChainCounterRules.Outcome.None)]
    [TestCase(1, 2, BossChainCounterRules.Outcome.Partial)]
    [TestCase(0, 2, BossChainCounterRules.Outcome.None)]
    public void 결과_판정(int ok, int total, BossChainCounterRules.Outcome expected) =>
        Assert.That(BossChainCounterRules.Resolve(ok, total), Is.EqualTo(expected));

    [Test]
    public void 순번은_위협_높은_순_같으면_id_순()
    {
        var input = new List<KeyValuePair<ulong, float>>
        {
            new KeyValuePair<ulong, float>(3, 10f),
            new KeyValuePair<ulong, float>(1, 50f),
            new KeyValuePair<ulong, float>(2, 10f),
        };
        Assert.That(BossChainCounterRules.Order(input), Is.EqualTo(new ulong[] { 1, 2, 3 }));
    }

    [Test]
    public void 다음_순번은_쓰러진_사람을_건너뛰고_순환()
    {
        var order = new List<ulong> { 1, 2, 3 };
        Assert.That(BossChainCounterRules.NextIndex(order, 1, id => id != 2), Is.EqualTo(2));
        Assert.That(BossChainCounterRules.NextIndex(order, 3, id => true), Is.EqualTo(0), "끝에서 처음으로");
        Assert.That(BossChainCounterRules.NextIndex(order, 0, id => false), Is.EqualTo(-1));
    }
}
