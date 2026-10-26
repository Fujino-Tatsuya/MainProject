#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 103 C7b — QA 자동 부활 대기(<see cref="QABoss103ReviveQueue"/>)와 집계 열(qa_revives·note).</summary>
public sealed class QABoss103AutoReviveTests
{
    private readonly List<ulong> _due = new List<ulong>();

    [Test]
    public void 지연은_튜토리얼_자동_부활과_같다()
    {
        var q = new QABoss103ReviveQueue(TutorialDeathRulesModel.DefaultReviveDelaySeconds);
        Assert.AreEqual(3.0, q.DelaySeconds);
    }

    [Test]
    public void Soul_3초_뒤에_꺼낸다()
    {
        var q = new QABoss103ReviveQueue(3.0);
        q.OnSoul(1, 10.0);
        q.TakeDue(12.9, _due);
        Assert.AreEqual(0, _due.Count, "아직 3초 전");
        Assert.IsTrue(q.IsPending(1));
        q.TakeDue(13.0, _due);
        CollectionAssert.AreEqual(new ulong[] { 1 }, _due);
        Assert.AreEqual(0, q.Count, "꺼내면 대기에서 빠진다 — 실패해도 재시도 없음");
        q.TakeDue(20.0, _due);
        Assert.AreEqual(0, _due.Count);
    }

    [Test]
    public void 대기_중_Soul_신호가_다시_와도_처음_시각()
    {
        var q = new QABoss103ReviveQueue(3.0);
        q.OnSoul(2, 10.0);
        q.OnSoul(2, 12.0);
        q.TakeDue(13.0, _due);
        CollectionAssert.AreEqual(new ulong[] { 2 }, _due);
    }

    [Test]
    public void Soul_을_벗어나면_취소()
    {
        var q = new QABoss103ReviveQueue(3.0);
        q.OnSoul(1, 10.0);
        Assert.IsTrue(q.Cancel(1));
        Assert.IsFalse(q.Cancel(1));
        q.TakeDue(100.0, _due);
        Assert.AreEqual(0, _due.Count);
    }

    [Test]
    public void 여럿이면_시각이_된_사람만_id_순서()
    {
        var q = new QABoss103ReviveQueue(3.0);
        q.OnSoul(5, 10.0);
        q.OnSoul(0, 11.0);
        q.OnSoul(3, 20.0);
        q.TakeDue(14.0, _due);
        CollectionAssert.AreEqual(new ulong[] { 0, 5 }, _due);
        Assert.IsTrue(q.IsPending(3));
    }

    [Test]
    public void 집계_qa_revives_열과_무적_없음_note()
    {
        var s = new QABoss103RunStats();
        s.Begin(0.0);
        s.QaRevive(0, true);
        s.QaRevive(0, true);
        s.QaRevive(2, true);
        s.QaRevive(9, true);   // 자리 밖은 무시
        s.End(60.0);
        var meta = new QABoss103RunMeta { Outcome = QABoss103Outcome.Wipe, CombatSeconds = 60.0 };

        string[] h = QABoss103Metrics.CharsHeader;
        int col = Array.IndexOf(h, "qa_revives");
        Assert.GreaterOrEqual(col, 0);
        List<string[]> rows = QABoss103CsvRows.Chars(meta, s);
        Assert.AreEqual(h.Length, rows[0].Length);
        Assert.AreEqual("2", rows[0][col]);
        Assert.AreEqual("0", rows[1][col]);
        Assert.AreEqual("1", rows[2][col]);
        Assert.AreEqual(3, s.QaRevivesTotal());

        int note = Array.IndexOf(QABoss103Metrics.RunsHeader, "note");
        StringAssert.DoesNotContain(QABoss103AutoRevive.NoInvulnNote, QABoss103CsvRows.Run(meta, s)[note]);
        s.QaRevive(1, false);
        StringAssert.Contains(QABoss103AutoRevive.NoInvulnNote, QABoss103CsvRows.Run(meta, s)[note]);
    }
}
#endif
