using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 로비 결과 패널 순위 점수(할 일 48 — 사용자 밸런스).
/// score = 가한 피해 × (사망 0회 1.5 / 아니면 1.0) × min(1.15 ^ 간파 성공, 4.0).
/// </summary>
public sealed class LobbyResultRankingTests
{
    static readonly LobbyResultScoreWeights W = LobbyResultScoreWeights.Default;

    static PlayerSessionStats Row(ulong clientId, int damage, int deaths, int counters) => new PlayerSessionStats
    {
        ClientId = clientId,
        Damage = damage,
        Deaths = deaths,
        Counters = counters
    };

    static List<LobbyResultRankEntry> Rank(params PlayerSessionStats[] rows)
    {
        var output = new List<LobbyResultRankEntry> { new LobbyResultRankEntry(9, 9, 9, true) };
        LobbyResultRanking.Rank(rows, W, output);
        return output;
    }

    // ─── 공식 ────────────────────────────────────────────────────────

    [Test]
    public void 기본_계수는_사용자_밸런스_값이다()
    {
        Assert.AreEqual(1.5f, W.NoDeathMultiplier);
        Assert.AreEqual(1.0f, W.DeathMultiplier);
        Assert.AreEqual(1.15f, W.CounterBase);
        Assert.AreEqual(4.0f, W.CounterMultiplierCap);
    }

    [Test]
    public void 무사망이면_1_5배()
    {
        Assert.AreEqual(1500d, LobbyResultRanking.Score(Row(0, 1000, 0, 0), W), 1e-6);
        Assert.AreEqual(1000d, LobbyResultRanking.Score(Row(0, 1000, 1, 0), W), 1e-6);
        Assert.AreEqual(1000d, LobbyResultRanking.Score(Row(0, 1000, 5, 0), W), 1e-6);
    }

    [Test]
    public void 간파는_1_15의_거듭제곱()
    {
        Assert.AreEqual(1d, LobbyResultRanking.CounterMultiplier(0, W), 1e-9);
        Assert.AreEqual(1.15d, LobbyResultRanking.CounterMultiplier(1, W), 1e-6);
        Assert.AreEqual(Math.Pow(1.15d, 3), LobbyResultRanking.CounterMultiplier(3, W), 1e-5);
        Assert.AreEqual(1000d * Math.Pow(1.15d, 2), LobbyResultRanking.Score(Row(0, 1000, 1, 2), W), 1e-3);
    }

    [Test]
    public void 간파_배수는_4에서_멈춘다()
    {
        Assert.Greater(Math.Pow(1.15d, 10), 4d, "전제 — 1.15^10 > 4");
        Assert.Less(LobbyResultRanking.CounterMultiplier(9, W), 4d, "1.15^9 ≈ 3.52 — 아직 상한 아래");
        Assert.AreEqual(4d, LobbyResultRanking.CounterMultiplier(10, W), 1e-9);
        Assert.AreEqual(4d, LobbyResultRanking.CounterMultiplier(1000, W), 1e-9);
        Assert.AreEqual(1000d * 1.5d * 4d, LobbyResultRanking.Score(Row(0, 1000, 0, 50), W), 1e-6);
    }

    [Test]
    public void 피해_0이면_점수_0()
    {
        Assert.AreEqual(0d, LobbyResultRanking.Score(Row(0, 0, 0, 20), W));
    }

    [Test]
    public void 인스펙터_계수가_그대로_쓰인다()
    {
        var custom = new LobbyResultScoreWeights
        {
            NoDeathMultiplier = 2f, DeathMultiplier = 0.5f, CounterBase = 2f, CounterMultiplierCap = 3f
        };
        Assert.AreEqual(100d * 2d * 2d, LobbyResultRanking.Score(Row(0, 100, 0, 1), custom), 1e-6);
        Assert.AreEqual(100d * 0.5d * 3d, LobbyResultRanking.Score(Row(0, 100, 2, 5), custom), 1e-6);
    }

    // ─── 정렬·1위 ────────────────────────────────────────────────────

    [Test]
    public void 점수_내림차순으로_정렬하고_1위만_왕관()
    {
        // P1 48210·사망1·간파6 ≈ 111,513 / P2 41875·무사망·간파2 ≈ 83,068 / P3 39940·무사망·간파11 = 239,640(상한)
        List<LobbyResultRankEntry> ranked = Rank(
            Row(0, 48210, 1, 6),
            Row(1, 41875, 0, 2),
            Row(2, 39940, 0, 11));

        Assert.AreEqual(3, ranked.Count, "기존 내용은 비우고 채운다");
        Assert.AreEqual(new[] { 2, 0, 1 }, new[] { ranked[0].PlayerIndex, ranked[1].PlayerIndex, ranked[2].PlayerIndex });
        Assert.AreEqual(new[] { 1, 2, 3 }, new[] { ranked[0].Rank, ranked[1].Rank, ranked[2].Rank });
        Assert.IsTrue(ranked[0].Crowned);
        Assert.IsFalse(ranked[1].Crowned);
        Assert.IsFalse(ranked[2].Crowned);
    }

    [Test]
    public void 동점이면_슬롯_순()
    {
        List<LobbyResultRankEntry> ranked = Rank(
            Row(0, 100, 1, 0),
            Row(1, 300, 1, 0),
            Row(2, 100, 1, 0),
            Row(3, 300, 1, 0));

        Assert.AreEqual(new[] { 1, 3, 0, 2 },
            new[] { ranked[0].PlayerIndex, ranked[1].PlayerIndex, ranked[2].PlayerIndex, ranked[3].PlayerIndex });
        Assert.AreEqual(new[] { 1, 2, 3, 4 }, new[] { ranked[0].Rank, ranked[1].Rank, ranked[2].Rank, ranked[3].Rank },
            "동점도 다른 순위 번호");
        Assert.IsTrue(ranked[0].Crowned, "동점 1위는 슬롯이 빠른 쪽만 왕관");
        Assert.IsFalse(ranked[1].Crowned);
    }

    [Test]
    public void 모두_0점이면_왕관_없이_슬롯_순()
    {
        List<LobbyResultRankEntry> ranked = Rank(Row(0, 0, 0, 3), Row(1, 0, 2, 0));
        Assert.AreEqual(0, ranked[0].PlayerIndex);
        Assert.AreEqual(1, ranked[0].Rank);
        Assert.IsFalse(ranked[0].Crowned, "최고 점수가 0 이면 왕관 없음");
        Assert.IsFalse(ranked[1].Crowned);
    }

    [Test]
    public void 혼자여도_점수가_있으면_왕관()
    {
        List<LobbyResultRankEntry> ranked = Rank(Row(5, 10, 3, 0));
        Assert.AreEqual(1, ranked.Count);
        Assert.IsTrue(ranked[0].Crowned);
    }

    [Test]
    public void 행이_없으면_비운다()
    {
        Assert.AreEqual(0, Rank().Count);
        var output = new List<LobbyResultRankEntry> { new LobbyResultRankEntry(0, 1, 1, true) };
        LobbyResultRanking.Rank(null, W, output);
        Assert.AreEqual(0, output.Count);
    }
}
