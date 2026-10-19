using System;
using System.Collections.Generic;

/// <summary>순위 점수 계수(사용자 밸런스, 할 일 48). <see cref="LobbyResultPanel"/> 인스펙터 값이 넘어온다.</summary>
[Serializable]
public struct LobbyResultScoreWeights
{
    public const float DefaultNoDeathMultiplier = 1.5f;
    public const float DefaultDeathMultiplier = 1.0f;
    public const float DefaultCounterBase = 1.15f;
    public const float DefaultCounterMultiplierCap = 4.0f;

    public float NoDeathMultiplier;     // 사망 0회일 때 배수
    public float DeathMultiplier;       // 1회 이상 사망했을 때 배수
    public float CounterBase;           // 간파 배수 = CounterBase ^ 간파 성공 수
    public float CounterMultiplierCap;  // 간파 배수 상한

    public static LobbyResultScoreWeights Default => new LobbyResultScoreWeights
    {
        NoDeathMultiplier = DefaultNoDeathMultiplier,
        DeathMultiplier = DefaultDeathMultiplier,
        CounterBase = DefaultCounterBase,
        CounterMultiplierCap = DefaultCounterMultiplierCap
    };
}

/// <summary>한 행의 순위 결과. <see cref="PlayerIndex"/> = <see cref="SessionResult.Players"/> 안 위치(슬롯 순서).</summary>
public readonly struct LobbyResultRankEntry
{
    public readonly int PlayerIndex;
    public readonly int Rank;      // 1부터. 동점도 슬롯 순으로 다른 순위를 받는다
    public readonly double Score;
    public readonly bool Crowned;  // 왕관 배지 — 1위이고 점수가 0 보다 클 때만

    public LobbyResultRankEntry(int playerIndex, int rank, double score, bool crowned)
    {
        PlayerIndex = playerIndex;
        Rank = rank;
        Score = score;
        Crowned = crowned;
    }
}

/// <summary>
/// 로비 결과 패널 순위(MonoBehaviour 밖 — EditMode 테스트 대상).
/// score = 가한 피해 × (사망 0회 ? NoDeathMultiplier : DeathMultiplier) × min(CounterBase ^ 간파 성공 수, CounterMultiplierCap).
/// 점수 내림차순, 동점이면 슬롯 순(입력 순서 = clientId 오름차순). 피해 0 이면 점수 0.
/// 최고 점수가 0 이면(아무도 피해를 못 냈다) 왕관을 주지 않는다.
/// </summary>
public static class LobbyResultRanking
{
    public static double Score(PlayerSessionStats stats, LobbyResultScoreWeights weights)
    {
        if (stats.Damage <= 0)
            return 0d;

        double deathMultiplier = stats.Deaths <= 0 ? weights.NoDeathMultiplier : weights.DeathMultiplier;
        return stats.Damage * deathMultiplier * CounterMultiplier(stats.Counters, weights);
    }

    /// <summary>min(CounterBase ^ counters, CounterMultiplierCap). 간파 0 회면 1.</summary>
    public static double CounterMultiplier(int counters, LobbyResultScoreWeights weights)
    {
        double multiplier = Math.Pow(weights.CounterBase, Math.Max(0, counters));
        return Math.Min(multiplier, weights.CounterMultiplierCap);
    }

    /// <summary>순위를 매겨 <paramref name="output"/> 을 순위 순서로 채운다(기존 내용은 비운다).</summary>
    public static void Rank(IReadOnlyList<PlayerSessionStats> players, LobbyResultScoreWeights weights,
        List<LobbyResultRankEntry> output)
    {
        output.Clear();
        if (players == null || players.Count == 0)
            return;

        var scored = new List<(int Index, double Score)>(players.Count);
        for (int i = 0; i < players.Count; i++)
            scored.Add((i, Score(players[i], weights)));

        // List.Sort 는 안정 정렬이 아니라 동점 순서를 인덱스로 직접 고정한다.
        scored.Sort((a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.Index.CompareTo(b.Index);
        });

        bool crownTop = scored[0].Score > 0d;
        for (int i = 0; i < scored.Count; i++)
            output.Add(new LobbyResultRankEntry(scored[i].Index, i + 1, scored[i].Score, crownTop && i == 0));
    }
}
