using System;
using System.Collections.Generic;

/// <summary>
/// 특성 디버그 랩(DebugTraitLab 씬, 할 일 135)의 순수 규칙 — 보유 특성 ID 목록을 고친다. Unity 비의존, EditMode 테스트 대상.
/// 진짜 상태는 서버 디렉터의 획득 행이다 — 패널은 매번 <see cref="Sync"/> 로 받아 고친 뒤 결과를 <see cref="TraitDirector.ReplaceAcquiredServer"/> 로 보낸다.
/// <list type="bullet">
/// <item>후보 = 그 캐릭터의 고정표 12개(<see cref="TraitCatalog.Spec"/>, 차수 1~4 × 3).</item>
/// <item>차수마다 하나(실게임 = 차수당 3장 중 1장): 같은 차수의 다른 것을 넣으면 기존 것을 빼고 새 것을 넣는다. <see cref="IgnoreRules"/> 면 동시 보유.</item>
/// <item>보유 목록은 항상 표 순서(차수 → 표 순서) — 실게임 획득 순서(1차부터)와 같다.</item>
/// <item>캐릭터가 바뀌면 보유를 비운다.</item>
/// </list>
/// 2차 상충(효과를 잃는 보유 특성)은 막지 않는다 — 실게임 확인창도 [확인]이면 그대로 허용한다. 경고 문구만 <see cref="GetConflicts"/> 로 만든다.
/// </summary>
public sealed class TraitLabModel
{
    private readonly List<TraitCatalog.Slot> slots = new List<TraitCatalog.Slot>();
    private readonly List<string> owned = new List<string>();
    private readonly List<string> scratch = new List<string>();

    public TutorialCharacter Character { get; private set; } = TutorialCharacter.None;

    /// <summary>디버그 — 차수당 하나 규칙을 끈다(같은 차수 동시 보유).</summary>
    public bool IgnoreRules { get; set; }

    /// <summary>지금 캐릭터의 고정표 칸(차수 → 표 순서). 캐릭터 모름이면 빈 목록.</summary>
    public IReadOnlyList<TraitCatalog.Slot> Slots => slots;

    /// <summary>보유 특성 ID(표 순서).</summary>
    public IReadOnlyList<string> Owned => owned;

    /// <summary>캐릭터를 정한다. 바뀌면 보유를 비우고 true.</summary>
    public bool SetCharacter(TutorialCharacter character)
    {
        if (character == Character)
            return false;

        Character = character;
        slots.Clear();
        IReadOnlyList<TraitCatalog.Slot> spec = TraitCatalog.Spec;
        for (int i = 0; i < spec.Count; i++)
        {
            if (spec[i].Character == character)
                slots.Add(spec[i]);
        }

        owned.Clear();
        return true;
    }

    /// <summary>서버 보유 목록으로 맞춘다 — 지금 캐릭터 표에 없는 ID 는 버린다. 결과는 표 순서.</summary>
    public void Sync(IEnumerable<string> ownedIds)
    {
        owned.Clear();
        if (ownedIds == null)
            return;

        foreach (string id in ownedIds)
        {
            if (IndexOf(id) >= 0 && !owned.Contains(id))
                owned.Add(id);
        }

        SortOwned();
    }

    public bool IsOwned(string id) => owned.Contains(id);

    /// <summary>넣기/빼기. 보유 중이면 뺀다. 아니면 넣는다(규칙 = 같은 차수 기존 것을 뺀다). 지금 캐릭터 표에 없으면 false.</summary>
    public bool Toggle(string id)
    {
        int index = IndexOf(id);
        if (index < 0)
            return false;

        if (owned.Remove(id))
            return true;

        if (!IgnoreRules)
            RemoveTier(slots[index].Tier);

        owned.Add(id);
        SortOwned();
        return true;
    }

    /// <summary>전부 뺀다. 뺀 것이 있었으면 true.</summary>
    public bool ClearAll()
    {
        bool any = owned.Count > 0;
        owned.Clear();
        return any;
    }

    /// <summary>
    /// 차수별 랜덤 — 보유를 비우고 1~4차에서 하나씩 무작위로 넣는다(<paramref name="next"/>(n) = 0..n-1).
    /// 규칙 무시와 무관하게 차수당 하나다. 넣은 수.
    /// </summary>
    public int RandomPerTier(Func<int, int> next)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        owned.Clear();
        for (int tier = 1; tier <= TraitCatalog.TierCount; tier++)
        {
            scratch.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Tier == tier)
                    scratch.Add(slots[i].Id);
            }

            if (scratch.Count == 0)
                continue;

            int pick = next(scratch.Count);
            owned.Add(scratch[pick < 0 || pick >= scratch.Count ? 0 : pick]);
        }

        return owned.Count;
    }

    /// <summary>
    /// 보유 중 상충 짝(고른 특성 → 효과를 잃는 보유 특성) — 실게임 2차 확인창과 같은 등록부 규칙(<see cref="TraitContentRegistry.GetDisabledBy"/>).
    /// 결과는 덮어쓴다(보유 순서).
    /// </summary>
    public void GetConflicts(TraitContentRegistry registry, List<(string chosen, string disabled)> result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();
        if (registry == null)
            return;

        for (int i = 0; i < owned.Count; i++)
        {
            registry.GetDisabledBy(owned[i], owned, scratch);
            for (int j = 0; j < scratch.Count; j++)
                result.Add((owned[i], scratch[j]));
        }
    }

    private int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id))
            return -1;

        for (int i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i].Id, id, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void RemoveTier(int tier)
    {
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            int index = IndexOf(owned[i]);
            if (index >= 0 && slots[index].Tier == tier)
                owned.RemoveAt(i);
        }
    }

    private void SortOwned() => owned.Sort((a, b) => IndexOf(a).CompareTo(IndexOf(b)));
}
