using NUnit.Framework;
using UnityEngine;

public sealed class BossIntroRulesTests
{
    private static readonly Vector3 South = Vector3.back;

    [Test]
    public void Formation_Solo_IsCenterOnStopLine()
    {
        Vector3 p = BossIntroFormation.StopPoint(Vector3.zero, South, 0, 1, 6f, 2.2f, 0.8f, true);
        Assert.That(Vector3.Distance(p, new Vector3(0f, 0f, -6f)), Is.LessThan(1e-4f));
    }

    [Test]
    public void Formation_Two_IsHorizontalLine_Symmetric()
    {
        Vector3 a = BossIntroFormation.StopPoint(Vector3.zero, South, 0, 2, 6f, 2f, 0.8f, true);
        Vector3 b = BossIntroFormation.StopPoint(Vector3.zero, South, 1, 2, 6f, 2f, 0.8f, true);
        Assert.AreEqual(-6f, a.z, 1e-4f);
        Assert.AreEqual(-6f, b.z, 1e-4f);
        Assert.AreEqual(0f, a.x + b.x, 1e-4f);
        Assert.AreEqual(2f, Mathf.Abs(a.x - b.x), 1e-4f);
    }

    [Test]
    public void Formation_Three_Triangle_LeaderFrontSidesBehind()
    {
        Vector3 lead = BossIntroFormation.StopPoint(Vector3.zero, South, 0, 3, 6f, 2.2f, 0.8f, true);
        Vector3 l = BossIntroFormation.StopPoint(Vector3.zero, South, 1, 3, 6f, 2.2f, 0.8f, true);
        Vector3 r = BossIntroFormation.StopPoint(Vector3.zero, South, 2, 3, 6f, 2.2f, 0.8f, true);
        Assert.AreEqual(0f, lead.x, 1e-4f);
        Assert.AreEqual(-6f, lead.z, 1e-4f);
        Assert.AreEqual(-6.8f, l.z, 1e-4f);
        Assert.AreEqual(-6.8f, r.z, 1e-4f);
        Assert.AreEqual(0f, l.x + r.x, 1e-4f);
    }

    [Test]
    public void Formation_Three_LineWhenTriangleOff()
    {
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(-6f, BossIntroFormation.StopPoint(Vector3.zero, South, i, 3, 6f, 2f, 0.8f, false).z, 1e-4f);
    }

    [Test]
    public void StartPoint_IsBehindStop_ByWalkDistance()
    {
        Vector3 stop = new Vector3(1f, 0.5f, -6f);
        Vector3 start = BossIntroFormation.StartPoint(stop, South, 3.5f);
        Assert.That(Vector3.Distance(start, new Vector3(1f, 0.5f, -9.5f)), Is.LessThan(1e-4f));
    }

    [Test]
    public void Flat_ZeroFallsBackToWorldBack()
    {
        Assert.AreEqual(Vector3.back, BossIntroFormation.Flat(Vector3.up));
    }

    [Test]
    public void SkidEase_EndpointsAndMonotonic()
    {
        Assert.AreEqual(0f, BossIntroFormation.SkidEase(0f), 1e-5f);
        Assert.AreEqual(1f, BossIntroFormation.SkidEase(1f), 1e-5f);
        Assert.Greater(BossIntroFormation.SkidEase(0.5f), 0.5f);
    }

    [Test]
    public void SkipVotes_UnanimousOnlyWhenAllEligibleVoted()
    {
        var votes = new BossIntroSkipVotes();
        ulong[] eligible = { 0, 1, 2 };
        Assert.IsTrue(votes.TryVote(0, eligible));
        Assert.IsFalse(votes.TryVote(0, eligible));      // 중복
        Assert.IsFalse(votes.TryVote(9, eligible));      // 참가자 아님
        Assert.IsFalse(votes.IsUnanimous(eligible));
        votes.TryVote(1, eligible);
        votes.TryVote(2, eligible);
        Assert.IsTrue(votes.IsUnanimous(eligible));
    }

    [Test]
    public void SkipVotes_LeaverRemoved_RemainingStillDecide()
    {
        var votes = new BossIntroSkipVotes();
        votes.TryVote(0, new ulong[] { 0, 1 });
        ulong[] after = { 0 };
        Assert.IsFalse(votes.Prune(after));
        Assert.IsTrue(votes.IsUnanimous(after));
        votes.TryVote(1, new ulong[] { 0, 1 });
        Assert.IsTrue(votes.Prune(after));
        Assert.AreEqual(1, votes.Count);
        Assert.IsFalse(votes.IsUnanimous(new ulong[0]));
    }

    [Test]
    public void SkipHold_FiresOnce_StaysFullAfterRelease()
    {
        var hold = new BossIntroSkipHold();
        Assert.IsFalse(hold.Tick(true, 0.6f, 1f));
        Assert.AreEqual(0.6f, hold.Progress(1f), 1e-5f);
        Assert.IsFalse(hold.Tick(false, 0.1f, 1f));      // 채우기 전에 뗌 → 0
        Assert.AreEqual(0f, hold.Progress(1f), 1e-5f);
        Assert.IsFalse(hold.Tick(true, 0.5f, 1f));
        Assert.IsTrue(hold.Tick(true, 0.5f, 1f));        // 1초 → 표
        Assert.IsFalse(hold.Tick(true, 0.5f, 1f));       // 한 번만
        Assert.IsFalse(hold.Tick(false, 0.5f, 1f));
        Assert.AreEqual(1f, hold.Progress(1f), 1e-5f);   // 떼도 유지
        hold.Reset();
        Assert.AreEqual(0f, hold.Progress(1f), 1e-5f);
    }
}
