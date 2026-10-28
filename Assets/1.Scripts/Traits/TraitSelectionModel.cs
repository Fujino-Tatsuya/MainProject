using System;
using System.Collections.Generic;

/// <summary>선택 요청의 판정 결과. Accepted 외에는 서버가 무시하고 로그만 남긴다.</summary>
public enum TraitChoiceResult
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
public enum TraitJoinResult
{
    /// <summary>1장 이상 제시했다 — 고를 때까지 미선택.</summary>
    Offered,
    /// <summary>참가자로 넣었지만 0장(풀 소진) — 고를 것이 없어 바로 완료로 친다.</summary>
    Empty,
    /// <summary>이미 이번 라운드 참가자다(제시 중·고름·자동 획득 모두) — 아무것도 바꾸지 않는다.</summary>
    AlreadyInRound,
    /// <summary>시작한 라운드가 없다(또는 서버 준비 전).</summary>
    NoRound,
    /// <summary>제시 없이 바로 무작위 1장을 획득했다(<see cref="TraitSelectionModel.AddParticipantWithoutOffer"/> — 디버그 구간 스킵).</summary>
    Granted,
}

/// <summary>
/// 고정 후보 공급(할 일 125) — 그 플레이어의 <paramref name="tier"/> 차(1~4) 후보 특성 키를 순서대로 <paramref name="result"/> 에 채운다.
/// 채우기 전에 <paramref name="result"/> 는 비어 있다. 캐릭터를 모르거나 후보가 없으면 비운 채 둔다.
/// </summary>
public delegate void TraitCandidateSource(ulong clientId, int tier, List<int> result);

/// <summary>
/// 특성 선택의 순수 규칙(할 일 15 T1) — Unity·NGO 비의존, EditMode 테스트 대상.
/// 플레이어(clientId)별로 이번 라운드 제시 후보·선택 완료 여부·획득 목록을 가진다.
///
/// 두 방식:
///  · 고정 후보(할 일 125, 디렉터가 쓴다) — <see cref="TraitSelectionModel(TraitCandidateSource, Func{int, bool})"/>.
///    특성 키 = 특성 ID 해시(<see cref="TraitIdHash"/>). 차수 = 그 플레이어의 획득 수 + 1. 후보는 공급자 순서 그대로(이미 얻은 것 제외),
///    자동 선택·제시 없는 획득 = 첫 번째. 난수 인자는 무시한다.
///  · 무작위(옛 방식) — <see cref="TraitSelectionModel(int, IEnumerable{int})"/>. 특성 키 = 풀 인덱스(0..poolSize-1),
///    난수는 <c>next(maxExclusive)</c> 로 주입한다(0 이상 maxExclusive 미만).
/// </summary>
public sealed class TraitSelectionModel
{
    private sealed class PlayerState
    {
        public ulong ClientId;
        public readonly List<int> Offered = new List<int>();
        public readonly List<int> Acquired = new List<int>();
        public bool InRound;
        public bool Chosen;
        /// <summary>이번 참가가 혼자만의 재선택 라운드다(<see cref="StartPersonalRound"/>).</summary>
        public bool Personal;
        public int OfferSeq;
    }

    private readonly int poolSize;
    private readonly HashSet<int> unusable;
    private readonly TraitCandidateSource fixedCandidates;
    private readonly Func<int, bool> isKnownTrait;
    private readonly Dictionary<ulong, PlayerState> players = new Dictionary<ulong, PlayerState>();
    private readonly List<int> candidateScratch = new List<int>();
    private readonly List<int> sourceScratch = new List<int>();
    private int roundOfferCount;
    private int lastOfferSeq;

    /// <summary>무작위 방식(옛 튜토리얼 풀). 특성 키 = 풀 인덱스.</summary>
    /// <param name="poolSize">풀 크기.</param>
    /// <param name="unusableIndices">제시하면 안 되는 인덱스(빈 칸 등). null 가능.</param>
    public TraitSelectionModel(int poolSize, IEnumerable<int> unusableIndices = null)
    {
        this.poolSize = Math.Max(0, poolSize);
        unusable = unusableIndices != null ? new HashSet<int>(unusableIndices) : new HashSet<int>();
    }

    /// <summary>고정 후보 방식(할 일 125). 특성 키 = ID 해시.</summary>
    /// <param name="candidates">플레이어·차수별 고정 후보 공급자.</param>
    /// <param name="isKnownTrait">유효한 특성 키인가(재합류 복원·후보 검증). null 이면 공급자가 낸 키를 모두 믿는다(복원은 0 만 거른다).</param>
    public TraitSelectionModel(TraitCandidateSource candidates, Func<int, bool> isKnownTrait)
    {
        fixedCandidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        this.isKnownTrait = isKnownTrait;
        unusable = new HashSet<int>();
    }

    /// <summary>고정 후보 방식인가.</summary>
    public bool IsFixed => fixedCandidates != null;

    /// <summary>그 플레이어가 다음에 받을 차수(획득 수 + 1). 고정 후보 방식에서 후보를 고르는 기준이다.</summary>
    public int NextTier(ulong clientId) => GetAcquired(clientId).Count + 1;

    // 그 플레이어의 후보를 고른다 — 고정 방식 = 공급자 순서에서 이미 얻은 것·중복·무효 제외 후 앞 count 개, 무작위 방식 = PickCandidates.
    private void PickFor(PlayerState state, int count, Func<int, int> next, List<int> result)
    {
        if (!IsFixed)
        {
            PickCandidates(poolSize, state.Acquired, unusable, count, next, result);
            return;
        }

        result.Clear();
        sourceScratch.Clear();
        fixedCandidates(state.ClientId, state.Acquired.Count + 1, sourceScratch);
        int take = Math.Max(0, count);
        for (int i = 0; i < sourceScratch.Count && result.Count < take; i++)
        {
            int key = sourceScratch[i];
            if (IsRestorable(key) && !state.Acquired.Contains(key) && !result.Contains(key))
                result.Add(key);
        }
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
            state.Personal = false;
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
            Offer(state, offerCount, next);
            anyOffered |= state.Offered.Count > 0;
        }

        return anyOffered;
    }

    /// <summary>
    /// 지금 라운드에 참가자 1명을 더한다(복도 중간 지점에 늦게 도착 · 자동 획득 직전의 미도착자).
    /// 그 사람에게만 라운드 시작 때와 같은 장 수로 후보를 뽑고, 이미 참가한 사람의 제시·선택은 건드리지 않는다.
    /// 이미 참가자면(고른 뒤 왕복 포함) 무시한다 — 한 라운드에 창은 한 번만.
    /// </summary>
    public TraitJoinResult AddParticipant(ulong clientId, Func<int, int> next)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));
        if (Round == 0)
            return TraitJoinResult.NoRound;

        PlayerState state = GetOrCreate(clientId);
        if (state.InRound)
            return TraitJoinResult.AlreadyInRound;

        state.InRound = true;
        state.Personal = false;
        Offer(state, roundOfferCount, next);
        return state.Chosen ? TraitJoinResult.Empty : TraitJoinResult.Offered;
    }

    /// <summary>
    /// 지금 라운드에 참가자 1명을 <b>제시 없이</b> 더하고 바로 무작위 1장을 획득시킨다(할 일 34 디버그 구간 스킵).
    /// 제시 후보·제시 번호(<see cref="GetOfferSeq"/>)를 건드리지 않으므로 클라 창이 열리지 않는다. 후보는 <see cref="AddParticipant"/> 와 같은
    /// 규칙(획득·사용 불가 제외)으로 뽑고, 라운드 장 수가 0 이거나 풀이 소진됐으면 <see cref="TraitJoinResult.Empty"/>.
    /// 이미 참가자면 무시한다. <paramref name="traitIndex"/> = 획득한 인덱스(<see cref="TraitJoinResult.Granted"/> 아니면 -1).
    /// </summary>
    public TraitJoinResult AddParticipantWithoutOffer(ulong clientId, Func<int, int> next, out int traitIndex)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        traitIndex = -1;
        if (Round == 0)
            return TraitJoinResult.NoRound;

        PlayerState state = GetOrCreate(clientId);
        if (state.InRound)
            return TraitJoinResult.AlreadyInRound;

        state.InRound = true;
        state.Personal = false;
        state.Chosen = true;
        state.Offered.Clear();
        PickFor(state, Math.Min(1, roundOfferCount), next, candidateScratch);
        if (candidateScratch.Count == 0)
            return TraitJoinResult.Empty;

        traitIndex = candidateScratch[0];
        state.Acquired.Add(traitIndex);
        return TraitJoinResult.Granted;
    }

    /// <summary>
    /// 그 플레이어 혼자만의 라운드를 연다(할 일 30 특성 재선택). 라운드 번호(<see cref="Round"/>)는 올리지 않는다 —
    /// 창은 그 플레이어의 <see cref="GetOfferSeq"/> 로 새로 열리고, 다른 플레이어의 참가·제시·선택·창은 건드리지 않는다.
    /// 장 수는 <paramref name="offerCount"/>, 획득한 특성은 빠진다. 1장 이상 제시했으면 true, 0장(풀 소진)이면 바로 완료로 치고 false.
    /// </summary>
    public bool StartPersonalRound(ulong clientId, int offerCount, Func<int, int> next)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        PlayerState state = GetOrCreate(clientId);
        state.InRound = true;
        state.Personal = true;
        Offer(state, offerCount, next);
        return !state.Chosen;
    }

    // 그 플레이어에게 새 후보를 뽑아 제시한다 — 제시 번호는 이때만 바뀐다(모델 전체에서 단조 증가라 같은 값이 다시 오지 않는다).
    private void Offer(PlayerState state, int offerCount, Func<int, int> next)
    {
        state.Offered.Clear();
        PickFor(state, offerCount, next, candidateScratch);
        state.Offered.AddRange(candidateScratch);
        state.Chosen = state.Offered.Count == 0;
        state.OfferSeq = ++lastOfferSeq;
    }

    /// <summary>
    /// 특성 초기화(할 일 30) — 그 플레이어의 획득·제시·참가를 비운다. <see cref="RemovePlayer"/> 와 달리 등록은 남긴다.
    /// 효과 원복은 호출자 몫(이 모델은 인덱스만 안다).
    /// </summary>
    public void ResetPlayer(ulong clientId)
    {
        if (!players.TryGetValue(clientId, out PlayerState state))
            return;

        state.Acquired.Clear();
        state.Offered.Clear();
        state.InRound = false;
        state.Chosen = false;
        state.Personal = false; // 제시 번호는 남긴다 — 다음 제시가 더 큰 번호로 새 창을 연다
    }

    /// <summary>
    /// 아직 안 고른 제시를 거둔다(재선택 도중 다시 전멸 — 할 일 30). 고른 특성은 그대로. 참가에서 빠지므로 라운드 완료를 막지 않는다.
    /// 거둔 제시가 있었으면 true.
    /// </summary>
    public bool CancelOffer(ulong clientId)
    {
        if (!players.TryGetValue(clientId, out PlayerState state) || !state.InRound || state.Chosen)
            return false;

        state.Offered.Clear();
        state.InRound = false;
        state.Personal = false;
        return true;
    }

    /// <summary>선택 판정. Accepted 면 획득 목록에 넣고 이번 라운드를 완료 처리한다.</summary>
    public TraitChoiceResult TryChoose(ulong clientId, int traitIndex)
    {
        if (!players.TryGetValue(clientId, out PlayerState state) || !state.InRound)
            return TraitChoiceResult.NoOffer;
        if (state.Chosen)
            return state.Offered.Count == 0 ? TraitChoiceResult.NoOffer : TraitChoiceResult.AlreadyChosen;
        if (!state.Offered.Contains(traitIndex))
            return TraitChoiceResult.NotOffered;

        state.Chosen = true;
        state.Acquired.Add(traitIndex);
        return TraitChoiceResult.Accepted;
    }

    /// <summary>
    /// 자동 획득 대상: 아직 안 고른 참가자의 제시 후보 중 1장 — 고정 후보 방식은 첫 번째, 무작위 방식은 무작위. 대상이 아니면 -1.
    /// 판정만 한다 — 실제 획득은 반환값으로 <see cref="TryChoose"/> 를 부른다.
    /// </summary>
    public int PickAutoChoice(ulong clientId, Func<int, int> next)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));
        if (!HasPendingChoice(clientId))
            return -1;

        List<int> offered = players[clientId].Offered;
        return IsFixed ? offered[0] : offered[ClampIndex(next(offered.Count), offered.Count)];
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

    /// <summary>지금 참가가 혼자만의 재선택 라운드인가(<see cref="StartPersonalRound"/> — 복도 라운드 아님).</summary>
    public bool IsPersonalRound(ulong clientId)
    {
        return players.TryGetValue(clientId, out PlayerState state) && state.InRound && state.Personal;
    }

    /// <summary>
    /// 그 플레이어의 제시 번호(0 = 제시받은 적 없음). 그 플레이어에게 새 제시가 들어갈 때만 바뀐다 —
    /// 다른 사람의 라운드·참가 추가·재선택에는 변하지 않는다. 창은 이 번호로 열고 닫는다(<see cref="TraitWindowModel"/>).
    /// </summary>
    public int GetOfferSeq(ulong clientId)
    {
        return players.TryGetValue(clientId, out PlayerState state) ? state.OfferSeq : 0;
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

    // ── 재합류 복원(할 일 48 C14 R4c) ─────────────────────────────────────

    /// <summary>
    /// 판 도중 재합류 — 퇴장 때 보관한 획득 목록을 새 clientId 에 되돌린다(획득 순서 유지).
    /// 이미 가진 인덱스·중복·범위 밖·사용 불가는 건너뛴다(같은 목록을 두 번 넣어도 한 번만 — 멱등).
    /// 제시 번호는 올리지 않는다 — 창이 열리거나 획득 토스트가 뜨지 않는다. 넣은 인덱스는 <paramref name="added"/> 에 덧붙인다(null 가능).
    /// 효과 적용은 호출자 몫. 넣은 수를 돌려준다.
    /// </summary>
    public int RestoreAcquired(ulong clientId, IReadOnlyList<int> traitIndices, List<int> added = null)
    {
        if (traitIndices == null || traitIndices.Count == 0)
            return 0;

        PlayerState state = GetOrCreate(clientId);
        int count = 0;
        for (int i = 0; i < traitIndices.Count; i++)
        {
            int index = traitIndices[i];
            if (!IsRestorable(index) || state.Acquired.Contains(index))
                continue;

            state.Acquired.Add(index);
            added?.Add(index);
            count++;
        }

        return count;
    }

    /// <summary>
    /// 재합류 — 퇴장 전에 지금 복도 라운드에서 이미 골랐던(0장 포함) 사람을 "참가·고름"으로 되돌린다.
    /// 그래야 문이 열릴 때 미도착자 자동 획득·중간 지점 늦은 도착이 같은 라운드에서 한 장을 더 주지 않는다(<see cref="TraitJoinResult.AlreadyInRound"/>).
    /// 라운드가 그새 바뀌었거나 이미 참가 중이면 아무것도 안 하고 false.
    /// </summary>
    public bool RestoreRoundDone(ulong clientId, int round)
    {
        if (round <= 0 || round != Round)
            return false;

        PlayerState state = GetOrCreate(clientId);
        if (state.InRound)
            return false;

        state.InRound = true;
        state.Chosen = true;
        state.Personal = false;
        state.Offered.Clear();
        return true;
    }

    /// <summary>
    /// 재합류 — 퇴장 때 열려 있던 재선택(개인 라운드) 후보를 그대로 다시 제시한다(할 일 30 재선택 창 복원).
    /// 이미 획득·범위 밖·사용 불가 후보는 뺀다. 남은 게 없으면 아무것도 안 하고 false(호출자가 다음 재선택 라운드를 연다).
    /// 새 제시 번호를 받는다 — 그 사람 창만 새로 열린다. 다른 사람의 참가·제시는 건드리지 않는다.
    /// </summary>
    public bool RestorePersonalOffer(ulong clientId, IReadOnlyList<int> offered)
    {
        if (offered == null || offered.Count == 0)
            return false;

        PlayerState state = GetOrCreate(clientId);
        candidateScratch.Clear();
        for (int i = 0; i < offered.Count; i++)
        {
            int index = offered[i];
            if (IsRestorable(index) && !state.Acquired.Contains(index) && !candidateScratch.Contains(index))
                candidateScratch.Add(index);
        }

        if (candidateScratch.Count == 0)
            return false;

        state.Offered.Clear();
        state.Offered.AddRange(candidateScratch);
        state.InRound = true;
        state.Personal = true;
        state.Chosen = false;
        state.OfferSeq = ++lastOfferSeq;
        return true;
    }

    // 고정 방식 = 유효한 특성 키(0 = 없음은 거른다), 무작위 방식 = 풀 범위 안·사용 가능 인덱스.
    private bool IsRestorable(int key) => IsFixed
        ? key != TraitIdHash.None && (isKnownTrait == null || isKnownTrait(key))
        : key >= 0 && key < poolSize && !unusable.Contains(key);

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
            state = new PlayerState { ClientId = clientId };
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
