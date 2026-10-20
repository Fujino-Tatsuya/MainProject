using System.Collections.Generic;

/// <summary>
/// 레이드 '간파 릴레이' 명단·잠금 규칙(PLAN-boss-raid-relay §1 · 기획 기믹1 §3-5 · §4-4 · §4-5). 순수 C# — EditMode 테스트 대상.
///
/// - 라운드 명단 전원이 한 번씩 성공하면 성공 기록(=잠금)을 비우고 다음 라운드(자유 순서).
/// - 성공한 사람은 이번 라운드 동안 잠긴다 — 잠긴 사람의 간파는 인정하지 않는다.
/// - 쓰러짐·이탈 = 명단·성공 기록에서 빼고 남은 사람으로 전원 성공 여부를 다시 계산한다.
/// - 부활·복귀 = 현재 라운드 인원을 늘리지 않고 <b>다음 라운드</b>에 편입한다. 현재 명단이 비면 즉시 새 라운드.
/// 키 = 플레이어 clientId.
/// </summary>
public sealed class BossRelayRoster
{
    public enum Accept { Accepted, Locked, NotParticipant }

    readonly List<ulong> _participants = new List<ulong>();
    readonly HashSet<ulong> _succeeded = new HashSet<ulong>();
    readonly List<ulong> _pending = new List<ulong>();

    public int Round { get; private set; }
    public IReadOnlyList<ulong> Participants => _participants;
    public int ParticipantCount => _participants.Count;
    public int SucceededCount => _succeeded.Count;

    public void Begin(IEnumerable<ulong> participants)
    {
        _participants.Clear();
        _succeeded.Clear();
        _pending.Clear();
        foreach (ulong id in participants)
            if (!_participants.Contains(id)) _participants.Add(id);
        Round = 1;
    }

    public void Clear()
    {
        _participants.Clear();
        _succeeded.Clear();
        _pending.Clear();
        Round = 0;
    }

    public bool IsParticipant(ulong id) => _participants.Contains(id);
    public bool IsLocked(ulong id) => _succeeded.Contains(id);
    public bool IsPending(ulong id) => _pending.Contains(id);

    /// <summary>이 사람의 간파를 받을 수 있는가(명단에 있고 이번 라운드에 아직 성공하지 않음).</summary>
    public Accept CanAccept(ulong id) =>
        !_participants.Contains(id) ? Accept.NotParticipant
        : _succeeded.Contains(id) ? Accept.Locked
        : Accept.Accepted;

    /// <summary>
    /// 성공을 기록한다. 반환 = 이번 성공으로 라운드가 넘어갔는가(전원 성공 → 잠금 전부 해제).
    /// <see cref="CanAccept"/> 가 Accepted 가 아니면 아무것도 하지 않고 false.
    /// </summary>
    public bool RecordSuccess(ulong id)
    {
        if (CanAccept(id) != Accept.Accepted) return false;
        _succeeded.Add(id);
        return TryAdvanceRound();
    }

    /// <summary>쓰러짐·이탈. 반환 = 재계산으로 라운드가 넘어갔는가.</summary>
    public bool Remove(ulong id)
    {
        _pending.Remove(id);
        if (!_participants.Remove(id)) return false;
        _succeeded.Remove(id);
        return TryAdvanceRound();
    }

    /// <summary>부활·복귀 — 다음 라운드 대기. 반환 = 현재 명단이 비어 즉시 새 라운드를 열었는가.</summary>
    public bool AddPending(ulong id)
    {
        if (_participants.Contains(id) || _pending.Contains(id)) return false;
        _pending.Add(id);
        if (_participants.Count > 0) return false;
        StartNextRound();
        return true;
    }

    bool TryAdvanceRound()
    {
        if (_participants.Count == 0)
        {
            if (_pending.Count == 0) return false;   // 아무도 없다 — 기믹 쪽이 중단을 판단한다
            StartNextRound();
            return true;
        }
        foreach (ulong id in _participants)
            if (!_succeeded.Contains(id)) return false;
        StartNextRound();
        return true;
    }

    void StartNextRound()
    {
        _succeeded.Clear();
        foreach (ulong id in _pending)
            if (!_participants.Contains(id)) _participants.Add(id);
        _pending.Clear();
        Round++;
    }
}

/// <summary>레이드 '연쇄 간파' 규칙(PLAN-boss-raid-relay §2 · 협동 변형 기획 §8). 순수 C#.</summary>
public static class BossChainCounterRules
{
    public enum Outcome { None, Partial, Full }

    /// <summary>회차 수 — 1인 2 · 2인 3 · 3인 4(인원 + 1). 0 이하는 1인으로 본다.</summary>
    public static int RoundsFor(int partySize) => System.Math.Max(1, partySize) + 1;

    /// <summary>결과 — 전부 성공 = 제압 / 절반 이상 = 그로기→취약 / 그 미만 = 보상 없음.</summary>
    public static Outcome Resolve(int successes, int total)
    {
        if (total <= 0 || successes <= 0) return Outcome.None;
        if (successes >= total) return Outcome.Full;
        return successes * 2 >= total ? Outcome.Partial : Outcome.None;
    }

    /// <summary>순번 — 위협 높은 순, 같으면 id 오름차순(안정).</summary>
    public static List<ulong> Order(IList<KeyValuePair<ulong, float>> threatById)
    {
        var list = new List<KeyValuePair<ulong, float>>(threatById);
        list.Sort((a, b) =>
        {
            int byThreat = b.Value.CompareTo(a.Value);
            return byThreat != 0 ? byThreat : a.Key.CompareTo(b.Key);
        });
        var order = new List<ulong>(list.Count);
        foreach (var kv in list) order.Add(kv.Key);
        return order;
    }

    /// <summary>
    /// 다음 지목 순번 위치 — <paramref name="from"/> 부터 순환하며 처음으로 <paramref name="isAvailable"/> 인 위치.
    /// 아무도 없으면 -1.
    /// </summary>
    public static int NextIndex(IList<ulong> order, int from, System.Func<ulong, bool> isAvailable)
    {
        int n = order != null ? order.Count : 0;
        if (n == 0) return -1;
        int start = ((from % n) + n) % n;
        for (int k = 0; k < n; k++)
        {
            int i = (start + k) % n;
            if (isAvailable(order[i])) return i;
        }
        return -1;
    }
}
