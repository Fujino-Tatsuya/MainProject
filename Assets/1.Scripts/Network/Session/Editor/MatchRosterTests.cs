using NUnit.Framework;

/// <summary>
/// 판 명단(C14) — 시작·끝, 명단 안/밖, 퇴장 뒤 자리 유지, 재합류 묶기(옛 clientId), 판마다 비우기(누적 0).
/// </summary>
public sealed class MatchRosterTests
{
    static MatchRoster Begun(params string[] playerIds)
    {
        var roster = new MatchRoster();
        roster.Begin(playerIds);
        return roster;
    }

    [Test]
    public void 시작_전에는_비활성이고_아무도_없다()
    {
        var roster = new MatchRoster();
        Assert.IsFalse(roster.IsActive);
        Assert.AreEqual(0, roster.Count);
        Assert.IsFalse(roster.Contains("a"));
        Assert.IsFalse(roster.BindClient("a", 1, out _, out _), "비활성이면 묶지 않는다");
    }

    [Test]
    public void 시작하면_그_순간_명단만_고정한다()
    {
        var roster = Begun("a", "b", "", null, "a");
        Assert.IsTrue(roster.IsActive);
        Assert.AreEqual(2, roster.Count, "빈 값·중복은 건너뛴다");
        Assert.IsTrue(roster.Contains("a"));
        Assert.IsTrue(roster.Contains("b"));
        Assert.IsFalse(roster.Contains("c"), "명단 밖 새 사람");
        Assert.IsFalse(roster.Contains(null));
    }

    [Test]
    public void 빈_명단으로도_판은_활성이다()
    {
        var roster = new MatchRoster();
        roster.Begin(null);
        Assert.IsTrue(roster.IsActive);
        Assert.AreEqual(0, roster.Count);
    }

    [Test]
    public void 명단_밖은_묶지_않는다()
    {
        var roster = Begun("a");
        Assert.IsFalse(roster.BindClient("c", 5, out var rejoined, out _));
        Assert.IsFalse(rejoined);
        Assert.AreEqual(0, roster.BoundCount);
    }

    [Test]
    public void 처음_묶기는_재합류가_아니다()
    {
        var roster = Begun("a");
        Assert.IsTrue(roster.BindClient("a", 0, out var rejoined, out _));
        Assert.IsFalse(rejoined);
        Assert.IsTrue(roster.TryGetLastClientId("a", out var last));
        Assert.AreEqual(0UL, last);

        Assert.IsTrue(roster.BindClient("a", 0, out rejoined, out _));
        Assert.IsFalse(rejoined, "같은 clientId 를 다시 묶어도 재합류가 아니다");
        Assert.AreEqual(1, roster.BoundCount);
    }

    [Test]
    public void 퇴장해도_명단_자리와_마지막_clientId_는_남는다()
    {
        var roster = Begun("a", "b");
        roster.BindClient("b", 2, out _, out _);

        Assert.IsTrue(roster.UnbindClient(2));
        Assert.IsFalse(roster.UnbindClient(2), "두 번째 퇴장은 할 일이 없다");
        Assert.AreEqual(0, roster.BoundCount);
        Assert.IsTrue(roster.Contains("b"));
        Assert.IsTrue(roster.TryGetLastClientId("b", out var last));
        Assert.AreEqual(2UL, last);
    }

    [Test]
    public void 새_clientId_로_다시_묶으면_재합류와_옛_clientId()
    {
        var roster = Begun("a", "b");
        roster.BindClient("b", 2, out _, out _);
        roster.UnbindClient(2);

        Assert.IsTrue(roster.BindClient("b", 7, out var rejoined, out var oldClientId));
        Assert.IsTrue(rejoined);
        Assert.AreEqual(2UL, oldClientId);
        Assert.IsTrue(roster.TryGetLastClientId("b", out var last));
        Assert.AreEqual(7UL, last);
        Assert.AreEqual(1, roster.BoundCount);
    }

    [Test]
    public void 옛_연결이_안_풀린_채_새로_묶어도_하나만_남는다()
    {
        var roster = Begun("b");
        roster.BindClient("b", 2, out _, out _);

        Assert.IsTrue(roster.BindClient("b", 7, out var rejoined, out var oldClientId));
        Assert.IsTrue(rejoined);
        Assert.AreEqual(2UL, oldClientId);
        Assert.AreEqual(1, roster.BoundCount, "옛 clientId 묶임은 풀린다");
        Assert.IsFalse(roster.UnbindClient(2), "옛 clientId 의 늦은 퇴장은 새 묶임을 건드리지 않는다");
        Assert.AreEqual(1, roster.BoundCount);
    }

    [Test]
    public void 끝내면_비우고_크기를_돌려준다()
    {
        var roster = Begun("a", "b", "c");
        roster.BindClient("a", 0, out _, out _);

        Assert.AreEqual(3, roster.End());
        Assert.IsFalse(roster.IsActive);
        Assert.AreEqual(0, roster.Count);
        Assert.AreEqual(0, roster.BoundCount);
        Assert.IsFalse(roster.Contains("a"));
        Assert.IsFalse(roster.TryGetLastClientId("a", out _));
        Assert.AreEqual(0, roster.End(), "두 번 끝내도 안전하다");
    }

    [Test]
    public void 판마다_새로_시작하면_이전_판_기록이_남지_않는다()
    {
        var roster = new MatchRoster();
        for (var match = 0; match < 3; match++)
        {
            roster.Begin(new[] { "a", "b" });
            roster.BindClient("a", (ulong)(match * 10), out _, out _);
            roster.BindClient("b", (ulong)(match * 10 + 1), out var rejoined, out _);
            Assert.IsFalse(rejoined, $"{match}판 — 지난 판 clientId 로 재합류 판정하지 않는다");
            Assert.AreEqual(2, roster.End());
        }

        roster.Begin(new[] { "c" });
        Assert.IsFalse(roster.Contains("a"), "Begin 이 이전 명단을 버린다");
        Assert.AreEqual(1, roster.Count);
    }

    [Test]
    public void 묶임이_풀려도_마지막_clientId_로_playerId_를_찾는다()
    {
        var roster = Begun("a", "b");
        roster.BindClient("a", 1, out _, out _);
        roster.BindClient("b", 2, out _, out _);

        roster.UnbindClient(2); // 퇴장 콜백 순서상 묶임이 먼저 풀려도
        Assert.IsTrue(roster.TryGetPlayerIdByLastClient(2, out var playerId));
        Assert.AreEqual("b", playerId);

        roster.BindClient("b", 5, out _, out _); // 재합류 뒤에는 옛 clientId 로 찾지 않는다
        Assert.IsFalse(roster.TryGetPlayerIdByLastClient(2, out _));
        Assert.IsTrue(roster.TryGetPlayerIdByLastClient(5, out playerId) && playerId == "b");

        roster.End();
        Assert.IsFalse(roster.TryGetPlayerIdByLastClient(5, out _), "판이 끝나면 없다");
    }
}
