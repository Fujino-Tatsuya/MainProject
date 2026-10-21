using System;
using System.Collections.Generic;

/// <summary>특성 초기화 팝업 응답의 판정 결과(할 일 30). Kept·Reset 외에는 서버가 무시하고 로그만 남긴다.</summary>
public enum TutorialTraitResetAnswer
{
    /// <summary>[아니오] — 특성 유지, 팝업만 닫힌다.</summary>
    Kept,
    /// <summary>[예] — 원복 후 재선택 라운드를 연다.</summary>
    Reset,
    /// <summary>팝업 대기 중이 아니다(대상 아님·이미 응답·재선택 중·이탈).</summary>
    NotPrompted,
    /// <summary>지난 팝업의 응답이다(그 사이 다시 전멸해 새 팝업이 떴다).</summary>
    StaleGeneration,
}

/// <summary>
/// 전멸 리스폰 뒤 특성 초기화 팝업·재선택 진행의 순수 규칙(할 일 30 3단계) — Unity·NGO 비의존, EditMode 테스트 대상.
/// 플레이어별 상태 = 없음 → 팝업 대기(세대 번호) → (예) 재선택 중(남은 라운드) → 없음.
/// 세대 = 전멸 리스폰 한 번. 다시 전멸하면 새 세대로 전원 팝업을 다시 띄우고 지난 세대 응답은 버린다.
/// 라운드 제시·선택 자체는 <see cref="TutorialTraitSelectionModel"/> 몫 — 여기는 "몇 라운드 남았나"만 센다.
/// 원본: Docs/tech/party-lives-respawn.md §4.
/// </summary>
public sealed class TutorialTraitResetModel
{
    /// <summary>재선택 라운드 수 — 복도 1~4 와 같은 4번.</summary>
    public const int ReselectRounds = 4;

    private sealed class PlayerState
    {
        public int PromptGeneration; // 0 = 팝업 없음
        public int RoundsLeft;        // 재선택 중 남은 라운드(지금 열린 라운드는 이미 뺐다)
        public bool Reselecting;
    }

    private readonly Dictionary<ulong, PlayerState> players = new Dictionary<ulong, PlayerState>();

    /// <summary>마지막으로 띄운 팝업 세대(0 = 아직 없음).</summary>
    public int Generation { get; private set; }

    /// <summary>누군가 팝업 대기 또는 재선택 중이다 — 보스 진입 패드를 잠근다(D3).</summary>
    public bool IsBusy => players.Count > 0;

    /// <summary>
    /// 새 세대로 팝업을 띄운다(전멸 리스폰마다). 이전 세대의 팝업·재선택은 모두 끝낸다 —
    /// 재선택 도중이던 플레이어 id 를 <paramref name="interrupted"/> 에 채운다(호출자가 열린 제시를 거둔다). 새 세대 번호를 돌려준다.
    /// </summary>
    public int BeginPrompts(IEnumerable<ulong> clientIds, List<ulong> interrupted)
    {
        interrupted?.Clear();
        foreach (KeyValuePair<ulong, PlayerState> pair in players)
        {
            if (pair.Value.Reselecting)
                interrupted?.Add(pair.Key);
        }

        players.Clear();
        Generation++;
        if (clientIds == null)
            return Generation;

        foreach (ulong clientId in clientIds)
        {
            if (!players.ContainsKey(clientId))
                players.Add(clientId, new PlayerState { PromptGeneration = Generation });
        }

        return Generation;
    }

    /// <summary>그 플레이어가 팝업 대기 중이면 true + 세대.</summary>
    public bool TryGetPrompt(ulong clientId, out int generation)
    {
        generation = players.TryGetValue(clientId, out PlayerState state) ? state.PromptGeneration : 0;
        return generation > 0;
    }

    /// <summary>
    /// 팝업 응답 판정. 대기 중이고 세대가 맞을 때만 받는다(중복·지연 응답 거부).
    /// Kept = 상태 비움. Reset = 재선택 중(남은 라운드 <see cref="ReselectRounds"/>) — 호출자가 원복 후 <see cref="TryTakeReselectRound"/> 로 라운드를 연다.
    /// </summary>
    public TutorialTraitResetAnswer Answer(ulong clientId, int generation, bool reset)
    {
        if (!players.TryGetValue(clientId, out PlayerState state) || state.PromptGeneration == 0)
            return TutorialTraitResetAnswer.NotPrompted;
        if (state.PromptGeneration != generation)
            return TutorialTraitResetAnswer.StaleGeneration;

        if (!reset)
        {
            players.Remove(clientId);
            return TutorialTraitResetAnswer.Kept;
        }

        state.PromptGeneration = 0;
        state.Reselecting = true;
        state.RoundsLeft = ReselectRounds;
        return TutorialTraitResetAnswer.Reset;
    }

    /// <summary>재선택 중인가(팝업에 예 — 라운드가 남았거나 마지막 라운드 진행 중).</summary>
    public bool IsReselecting(ulong clientId) => players.TryGetValue(clientId, out PlayerState state) && state.Reselecting;

    /// <summary>재선택 남은 라운드(지금 열린 라운드 제외). 재선택 중이 아니면 0.</summary>
    public int RoundsLeft(ulong clientId) => players.TryGetValue(clientId, out PlayerState state) && state.Reselecting ? state.RoundsLeft : 0;

    /// <summary>
    /// 다음 재선택 라운드를 연다 — 남았으면 1 빼고 true(호출자가 라운드 제시). 다 썼으면 재선택을 끝내고(상태 비움) false.
    /// 재선택 중이 아니면 false.
    /// </summary>
    public bool TryTakeReselectRound(ulong clientId)
    {
        if (!players.TryGetValue(clientId, out PlayerState state) || !state.Reselecting)
            return false;

        if (state.RoundsLeft <= 0)
        {
            players.Remove(clientId);
            return false;
        }

        state.RoundsLeft--;
        return true;
    }

    /// <summary>접속 종료 — 대기·재선택 상태를 지운다(패드 잠금이 남지 않게). 지운 게 있으면 true.</summary>
    public bool Remove(ulong clientId) => players.Remove(clientId);

    // ── 재합류 보관·복원(할 일 48 C14 R4c · Q-D b) ─────────────────────────

    /// <summary>
    /// 퇴장 직전 그 플레이어의 팝업·재선택 상태를 읽는다(<see cref="Remove"/> 전에 부른다). 상태가 없으면 false(값은 0·false).
    /// <paramref name="promptGeneration"/> = 팝업 대기 세대(0 = 없음), <paramref name="roundsLeft"/> = 재선택 중 남은 라운드(지금 열린 라운드 제외).
    /// </summary>
    public bool TryCapture(ulong clientId, out int promptGeneration, out bool reselecting, out int roundsLeft)
    {
        if (!players.TryGetValue(clientId, out PlayerState state))
        {
            promptGeneration = 0;
            reselecting = false;
            roundsLeft = 0;
            return false;
        }

        promptGeneration = state.PromptGeneration;
        reselecting = state.Reselecting;
        roundsLeft = state.Reselecting ? state.RoundsLeft : 0;
        return true;
    }

    /// <summary>재합류 — 그 세대 팝업 대기를 되살린다. 세대가 지금 세대가 아니거나(지난 팝업) 이미 상태가 있으면 false.</summary>
    public bool RestorePrompt(ulong clientId, int generation)
    {
        if (generation <= 0 || generation != Generation || players.ContainsKey(clientId))
            return false;

        players.Add(clientId, new PlayerState { PromptGeneration = generation });
        return true;
    }

    /// <summary>
    /// 재합류 — 재선택 중 상태를 되살린다(남은 라운드 = 지금 열린 라운드 제외 수). 이미 상태가 있으면 false.
    /// 열려 있던 라운드의 제시는 호출자가 선택 모델에 되살린다.
    /// </summary>
    public bool RestoreReselect(ulong clientId, int roundsLeft)
    {
        if (players.ContainsKey(clientId))
            return false;

        players.Add(clientId, new PlayerState { Reselecting = true, RoundsLeft = Math.Max(0, roundsLeft) });
        return true;
    }

    /// <summary>그 플레이어에게 팝업·재선택 상태가 있는가.</summary>
    public bool Has(ulong clientId) => players.ContainsKey(clientId);

    /// <summary>팝업 대기 중인 플레이어 id(복제 행 재작성용).</summary>
    public void GetPrompted(List<ulong> result)
    {
        result.Clear();
        foreach (KeyValuePair<ulong, PlayerState> pair in players)
        {
            if (pair.Value.PromptGeneration > 0)
                result.Add(pair.Key);
        }
    }
}

/// <summary>
/// 플레이어에게 적용한 특성 효과 장부(할 일 30) — (플레이어, 특성 인덱스) → 원복에 필요한 핸들. Unity·NGO 비의존.
/// 디렉터가 효과를 걸 때 기록하고, 초기화·이탈 때 그 플레이어 몫만 꺼내 되돌린다(다른 플레이어 것은 남는다).
/// </summary>
public sealed class TutorialTraitEffectLedger<THandle>
{
    public readonly struct Record
    {
        public readonly ulong ClientId;
        public readonly int TraitIndex;
        public readonly THandle Handle;

        public Record(ulong clientId, int traitIndex, THandle handle)
        {
            ClientId = clientId;
            TraitIndex = traitIndex;
            Handle = handle;
        }
    }

    private readonly List<Record> records = new List<Record>();

    public int Count => records.Count;

    public IReadOnlyList<Record> All => records;

    public void Add(ulong clientId, int traitIndex, THandle handle) => records.Add(new Record(clientId, traitIndex, handle));

    /// <summary>그 플레이어의 기록 수.</summary>
    public int CountOf(ulong clientId)
    {
        int count = 0;
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].ClientId == clientId)
                count++;
        }

        return count;
    }

    /// <summary>그 플레이어에게 그 특성의 기록이 있는가(재합류 복원이 효과를 두 번 걸지 않게).</summary>
    public bool Has(ulong clientId, int traitIndex)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].ClientId == clientId && records[i].TraitIndex == traitIndex)
                return true;
        }

        return false;
    }

    /// <summary>그 플레이어의 기록을 장부에서 빼 <paramref name="taken"/> 에 채운다(적용 순서). 뺀 수.</summary>
    public int TakeAll(ulong clientId, List<Record> taken)
    {
        taken.Clear();
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].ClientId == clientId)
                taken.Add(records[i]);
        }

        records.RemoveAll(r => r.ClientId == clientId);
        return taken.Count;
    }

    public void Clear() => records.Clear();
}
