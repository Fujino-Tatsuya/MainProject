using System;
using System.Collections.Generic;

/// <summary>선택 요청의 판정 결과. Accepted 외에는 서버가 무시하고 로그만 남긴다.</summary>
public enum TutorialTraitChoiceResult
{
    Accepted,
    /// <summary>이번 라운드에 제시받은 적이 없다(라운드 참가자가 아니거나 0장 제시).</summary>
    NoOffer,
    /// <summary>이번 라운드에서 이미 골랐다(수동·자동 무관).</summary>
    AlreadyChosen,
    /// <summary>제시된 후보가 아닌 특성이다.</summary>
    NotOffered,
}

/// <summary>라운드 참가자 추가(늦은 도착) 결과.</summary>
public enum TutorialTraitJoinResult
{
    /// <summary>1장 이상 제시했다 — 고를 때까지 미선택.</summary>
    Offered,
    /// <summary>참가자로 넣었지만 0장(풀 소진) — 고를 것이 없어 바로 완료로 친다.</summary>
    Empty,
    /// <summary>이미 이번 라운드 참가자다(제시 중·고름·자동 획득 모두) — 아무것도 바꾸지 않는다.</summary>
    AlreadyInRound,
    /// <summary>시작한 라운드가 없다(또는 서버 준비 전).</summary>
    NoRound,
}

/// <summary>
/// 튜토리얼 특성 선택의 순수 규칙(할 일 15 T1) — Unity·NGO 비의존, EditMode 테스트 대상.
/// 플레이어(clientId)별로 이번 라운드 제시 후보·선택 완료 여부·획득 목록을 가진다.
/// 특성은 풀 인덱스(0..poolSize-1)로 다룬다. 난수는 <c>next(maxExclusive)</c> 로 주입한다(0 이상 maxExclusive 미만).
/// </summary>
public sealed class TutorialTraitSelectionModel
{
    private sealed class PlayerState
    {
        public readonly List<int> Offered = new List<int>();
        public readonly List<int> Acquired = new List<int>();
        public bool InRound;
        public bool Chosen;
    }

    private readonly int poolSize;
    private readonly HashSet<int> unusable;
    private readonly Dictionary<ulong, PlayerState> players = new Dictionary<ulong, PlayerState>();
    private readonly List<int> candidateScratch = new List<int>();
    private int roundOfferCount;

    /// <param name="poolSize">풀 크기.</param>
    /// <param name="unusableIndices">제시하면 안 되는 인덱스(빈 칸 등). null 가능.</param>
    public TutorialTraitSelectionModel(int poolSize, IEnumerable<int> unusableIndices = null)
    {
        this.poolSize = Math.Max(0, poolSize);
        unusable = unusableIndices != null ? new HashSet<int>(unusableIndices) : new HashSet<int>();
    }

    /// <summary>지금까지 시작한 라운드 수(첫 라운드 = 1).</summary>
    public int Round { get; private set; }

    /// <summary>
    /// 후보를 뽑는다: 풀에서 사용 불가·제외 인덱스를 뺀 나머지 중 최대 <paramref name="count"/> 장을 겹치지 않게 무작위로.
    /// 남은 수가 적으면 남은 만큼만. 결과는 <paramref name="result"/> 에 덮어쓴다.
    /// </summary>
    public static void PickCandidates(int poolSize, ICollection<int> excluded, ICollection<int> unusable,
        int count, Func<int, int> next, List<int> result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        result.Clear();
        for (int i = 0; i < poolSize; i++)
        {
            if ((excluded != null && excluded.Contains(i)) || (unusable != null && unusable.Contains(i)))
                continue;
            result.Add(i);
        }

        // 부분 Fisher–Yates — 앞 count 칸만 섞고 자른다.
        int take = Math.Min(Math.Max(0, count), result.Count);
        for (int i = 0; i < take; i++)
        {
            int j = i + ClampIndex(next(result.Count - i), result.Count - i);
            (result[i], result[j]) = (result[j], result[i]);
        }

        result.RemoveRange(take, result.Count - take);
    }

    /// <summary>
    /// 새 라운드를 시작하고 각 참가자에게 후보를 제시한다. 이전 라운드의 제시·참가 상태는 지운다(획득 목록은 유지).
    /// 0장을 받은 참가자는 선택할 것이 없으므로 바로 완료로 친다. 누구라도 1장 이상 받았으면 true.
    /// </summary>
    public bool StartRound(IEnumerable<ulong> clientIds, int offerCount, Func<int, int> next)
    {
        Round++;
        roundOfferCount = offerCount;
        foreach (PlayerState state in players.Values)
        {
            state.InRound = false;
            state.Chosen = false;
            state.Offered.Clear();
        }

        bool anyOffered = false;
        if (clientIds == null)
            return false;

        foreach (ulong clientId in clientIds)
        {
            PlayerState state = GetOrCreate(clientId);
            if (state.InRound)
                continue; // 중복 id

            state.InRound = true;
            PickCandidates(poolSize, state.Acquired, unusable, offerCount, next, candidateScratch);
            state.Offered.AddRange(candidateScratch);
            state.Chosen = state.Offered.Count == 0;
            anyOffered |= state.Offered.Count > 0;
        }

        return anyOffered;
    }

    /// <summary>
    /// 지금 라운드에 참가자 1명을 더한다(복도 중간 지점에 늦게 도착 · 자동 획득 직전의 미도착자).
    /// 그 사람에게만 라운드 시작 때와 같은 장 수로 후보를 뽑고, 이미 참가한 사람의 제시·선택은 건드리지 않는다.
    /// 이미 참가자면(고른 뒤 왕복 포함) 무시한다 — 한 라운드에 창은 한 번만.
    /// </summary>
    public TutorialTraitJoinResult AddParticipant(ulong clientId, Func<int, int> next)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));
        if (Round == 0)
            return TutorialTraitJoinResult.NoRound;

        PlayerState state = GetOrCreate(clientId);
        if (state.InRound)
            return TutorialTraitJoinResult.AlreadyInRound;

        state.InRound = true;
        state.Offered.Clear();
        PickCandidates(poolSize, state.Acquired, unusable, roundOfferCount, next, candidateScratch);
        state.Offered.AddRange(candidateScratch);
        state.Chosen = state.Offered.Count == 0;
        return state.Chosen ? TutorialTraitJoinResult.Empty : TutorialTraitJoinResult.Offered;
    }

    /// <summary>선택 판정. Accepted 면 획득 목록에 넣고 이번 라운드를 완료 처리한다.</summary>
    public TutorialTraitChoiceResult TryChoose(ulong clientId, int traitIndex)
    {
        if (!players.TryGetValue(clientId, out PlayerState state) || !state.InRound)
            return TutorialTraitChoiceResult.NoOffer;
        if (state.Chosen)
            return state.Offered.Count == 0 ? TutorialTraitChoiceResult.NoOffer : TutorialTraitChoiceResult.AlreadyChosen;
        if (!state.Offered.Contains(traitIndex))
            return TutorialTraitChoiceResult.NotOffered;

        state.Chosen = true;
        state.Acquired.Add(traitIndex);
        return TutorialTraitChoiceResult.Accepted;
    }

    /// <summary>
    /// 자동 획득 대상: 아직 안 고른 참가자의 제시 후보 중 무작위 1장. 대상이 아니면 -1.
    /// 판정만 한다 — 실제 획득은 반환값으로 <see cref="TryChoose"/> 를 부른다.
    /// </summary>
    public int PickAutoChoice(ulong clientId, Func<int, int> next)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));
        if (!HasPendingChoice(clientId))
            return -1;

        List<int> offered = players[clientId].Offered;
        return offered[ClampIndex(next(offered.Count), offered.Count)];
    }

    /// <summary>이번 라운드 참가자이고 1장 이상 제시받았는데 아직 안 골랐다.</summary>
    public bool HasPendingChoice(ulong clientId)
    {
        return players.TryGetValue(clientId, out PlayerState state) && state.InRound && !state.Chosen;
    }

    /// <summary>이번 라운드 참가자인가.</summary>
    public bool IsInRound(ulong clientId)
    {
        return players.TryGetValue(clientId, out PlayerState state) && state.InRound;
    }

    /// <summary>이번 라운드 참가자 전원이 골랐는가(0장 제시·이탈자 제외). 라운드가 없었으면 true.</summary>
    public bool IsRoundComplete()
    {
        foreach (PlayerState state in players.Values)
        {
            if (state.InRound && !state.Chosen)
                return false;
        }

        return true;
    }

    /// <summary>이번 라운드에서 아직 안 고른 참가자 id 를 채운다(자동 획득 일괄 처리용).</summary>
    public void GetPendingClients(List<ulong> result)
    {
        result.Clear();
        foreach (KeyValuePair<ulong, PlayerState> pair in players)
        {
            if (pair.Value.InRound && !pair.Value.Chosen)
                result.Add(pair.Key);
        }
    }

    /// <summary>
    /// 선택 창 무적 동기화 계획. <paramref name="shielded"/> = 지금 토큰을 건 플레이어.
    /// 해제 = 걸려 있는데 더는 미선택이 아니다(골랐음·자동 획득·이탈). 걸기 = 미선택인데 아직 안 걸렸다 —
    /// 제시 순간 플레이어 오브젝트가 없어 못 건 사람도 여기로 다시 나온다(고른 뒤엔 안 나온다).
    /// </summary>
    public void PlanSelectionShields(ICollection<ulong> shielded, List<ulong> release, List<ulong> acquire)
    {
        release.Clear();
        acquire.Clear();
        if (shielded != null)
        {
            foreach (ulong clientId in shielded)
            {
                if (!HasPendingChoice(clientId))
                    release.Add(clientId);
            }
        }

        foreach (KeyValuePair<ulong, PlayerState> pair in players)
        {
            if (pair.Value.InRound && !pair.Value.Chosen && (shielded == null || !shielded.Contains(pair.Key)))
                acquire.Add(pair.Key);
        }
    }

    /// <summary>접속 종료 — 라운드 참가에서 빼서 전원 완료 판단을 막지 않게 한다. 획득 목록도 지운다.</summary>
    public void RemovePlayer(ulong clientId)
    {
        players.Remove(clientId);
    }

    public IReadOnlyList<int> GetOffered(ulong clientId)
    {
        return players.TryGetValue(clientId, out PlayerState state) ? state.Offered : (IReadOnlyList<int>)Array.Empty<int>();
    }

    public IReadOnlyList<int> GetAcquired(ulong clientId)
    {
        return players.TryGetValue(clientId, out PlayerState state) ? state.Acquired : (IReadOnlyList<int>)Array.Empty<int>();
    }

    private PlayerState GetOrCreate(ulong clientId)
    {
        if (!players.TryGetValue(clientId, out PlayerState state))
        {
            state = new PlayerState();
            players.Add(clientId, state);
        }

        return state;
    }

    // 주입된 난수가 범위를 벗어나도 안전하게 접는다.
    private static int ClampIndex(int value, int count)
    {
        return value < 0 ? 0 : value >= count ? count - 1 : value;
    }
}
