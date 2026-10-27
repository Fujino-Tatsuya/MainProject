/// <summary>선입력 버퍼가 기억하는 입력 종류(할 일 44).</summary>
public enum PlayerBufferedInput
{
    None,
    Attack,
    Dash,
    SkillMain,
    SkillSub,
    SkillInterrupt, // 우클릭 — 인터럽트 상태 경로도 이 종류를 쓴다
    SkillUltimate
}

/// <summary>Idle/Move 에서 입력으로 행동을 시작해 본 결과 — 선입력 버퍼가 그 입력을 남길지 정한다(할 일 44).</summary>
public enum PlayerActionAttempt
{
    /// <summary>시작했다 — 버퍼를 비운다.</summary>
    Started,
    /// <summary>행동·요청 중이거나 CC 등으로 지금만 못 쓴다 — 유지 시간 동안 들고 있다가 다시 시도한다.</summary>
    Blocked,
    /// <summary>쿨타임·대시 충전 없음·과열처럼 자원이 모자라 거부됐다 — 그 입력을 바로 버린다(재시도 안 함).</summary>
    OutOfResource,
    /// <summary>차단 효과(침묵·속박·약화처럼 그 행동만 막는 상태이상)로 거부됐다 — 바로 버리고 차단 피드백을 낸다.</summary>
    BlockedByEffect,
    /// <summary>그 밖의 거부(사용 조건 CanUse·빈 슬롯·비접지 등) — 바로 버린다. 피드백 없음.</summary>
    Rejected
}

/// <summary>
/// 상태이상으로 행동이 막혔을 때 입력을 보관할지(CC) 버릴지(차단 효과) 가른다(할 일 44). 호출자는 그 행동의
/// 차단 판정(BlocksSkill·BlocksAttack·BlocksMovement·BlocksInterrupt)이 참일 때만 부른다.
/// </summary>
public static class PlayerActionRejectPolicy
{
    /// <summary>
    /// 행동 불능 CC(<see cref="StatusEffectController.IncapacitatingEffects"/> — 기절·에어본)가 하나라도 있으면 보관,
    /// 아니면 그 행동만 막는 봉인으로 본다. 둘이 겹치면 CC 가 이긴다 — 풀린 뒤 다시 시도해 봉인이 남았으면 그때 버린다.
    /// </summary>
    public static PlayerActionAttempt ClassifyEffectBlock(StatusEffectType activeEffects) =>
        (activeEffects & StatusEffectController.IncapacitatingEffects) != 0
            ? PlayerActionAttempt.Blocked
            : PlayerActionAttempt.BlockedByEffect;
}

/// <summary>
/// 선입력 버퍼(할 일 44) — 지금 바로 실행 못 하는 입력을 잠깐 들고 있다가 실행 가능해진 첫 순간에 넘긴다.
/// 종류별로 마지막 누른 시각을 들고 있다(할 일 77) — 살아 있는 입력은 전부 보관하고, 각자 유지 시간이 지나면 따로 만료된다.
/// 같은 종류를 다시 누르면 시각만 갱신한다. 꺼낼 때(<see cref="Peek"/>)는 살아 있는 것 중 우선순위가 가장 높은 것,
/// 같으면 나중에 누른 것. 시간은 호출자가 넘긴다(MonoBehaviour 아님 — EditMode 테스트 대상). 유지 시간 0 이하 = 버퍼 끔.
/// </summary>
public sealed class PlayerInputBuffer
{
    private const int KindCount = (int)PlayerBufferedInput.SkillUltimate + 1;

    // 인덱스 = PlayerBufferedInput. alive 가 거짓이면 그 종류는 없다.
    private readonly bool[] alive = new bool[KindCount];
    private readonly float[] recordedAt = new float[KindCount];
    // 누른 순서 — 같은 우선순위에서 나중 입력을 고른다(같은 프레임이면 기록 순서).
    private readonly int[] recordedOrder = new int[KindCount];
    private int nextOrder;
    // 종류별 우선순위. 기본 전부 0 = 마지막 입력이 이긴다.
    private readonly int[] priorities = new int[KindCount];

    public PlayerInputBuffer(float holdSeconds)
    {
        HoldSeconds = holdSeconds;
    }

    public float HoldSeconds { get; set; }

    public void SetPriority(PlayerBufferedInput input, int priority)
    {
        int index = (int)input;
        if (index > 0 && index < priorities.Length)
            priorities[index] = priority;
    }

    public int GetPriority(PlayerBufferedInput input)
    {
        int index = (int)input;
        return index > 0 && index < priorities.Length ? priorities[index] : 0;
    }

    /// <summary>
    /// 같은 우선순위끼리는 순서를 지키며 <paramref name="kinds"/> 를 우선순위 높은 순으로 정렬한다(제자리, 할당 없음).
    /// 상태 머신이 후보를 시도하는 순서다.
    /// </summary>
    public void SortByPriority(PlayerBufferedInput[] kinds)
    {
        for (int i = 1; i < kinds.Length; i++)
        {
            PlayerBufferedInput current = kinds[i];
            int priority = GetPriority(current);
            int j = i - 1;
            while (j >= 0 && GetPriority(kinds[j]) < priority)
            {
                kinds[j + 1] = kinds[j];
                j--;
            }

            kinds[j + 1] = current;
        }
    }

    /// <summary>새로 눌린 입력을 기록한다 — 다른 종류는 그대로 두고, 같은 종류면 시각만 갱신한다. 버퍼가 꺼져 있으면 false.</summary>
    public bool Record(PlayerBufferedInput input, float now)
    {
        int index = (int)input;
        if (HoldSeconds <= 0f || index <= 0 || index >= KindCount)
            return false;

        alive[index] = true;
        recordedAt[index] = now;
        recordedOrder[index] = ++nextOrder;
        return true;
    }

    /// <summary>지금 꺼낼 입력 — 살아 있는 것 중 우선순위가 가장 높은 것, 같으면 나중에 누른 것. 없으면 None.</summary>
    public PlayerBufferedInput Peek(float now)
    {
        PlayerBufferedInput best = PlayerBufferedInput.None;
        for (int i = 1; i < KindCount; i++)
        {
            if (!IsAlive(i, now))
                continue;

            if (best == PlayerBufferedInput.None ||
                priorities[i] > priorities[(int)best] ||
                (priorities[i] == priorities[(int)best] && recordedOrder[i] > recordedOrder[(int)best]))
                best = (PlayerBufferedInput)i;
        }

        return best;
    }

    /// <summary>그 종류가 지금 꺼낼 입력(<see cref="Peek"/>)인가. 살아 있어도 더 높은 입력에 밀려 있으면 false.</summary>
    public bool Has(PlayerBufferedInput input, float now) =>
        input != PlayerBufferedInput.None && Peek(now) == input;

    /// <summary>그 종류가 순위와 무관하게 살아 있는가.</summary>
    public bool Contains(PlayerBufferedInput input, float now)
    {
        int index = (int)input;
        return index > 0 && index < KindCount && IsAlive(index, now);
    }

    /// <summary>
    /// 그 종류가 순위와 무관하게 살아 있으면 마지막으로 누른 시각을 준다(대쉬 간파 창 판정, 할 일 115). 없으면 false.
    /// </summary>
    public bool TryGetRecordedTime(PlayerBufferedInput input, float now, out float time)
    {
        int index = (int)input;
        if (index > 0 && index < KindCount && IsAlive(index, now))
        {
            time = recordedAt[index];
            return true;
        }

        time = 0f;
        return false;
    }

    /// <summary>
    /// 그 종류가 살아 있고 <paramref name="than"/> 보다 우선순위가 높은가 — 더 높은 다른 입력(대시 등)에 밀려 있어도 참.
    /// 평타 콤보를 이을지 볼 때 쓴다(할 일 77 후속): 평타보다 높은 스킬 입력이 살아 있으면 다음 타를 잇지 않는다.
    /// </summary>
    public bool ContainsAbove(PlayerBufferedInput input, PlayerBufferedInput than, float now) =>
        Contains(input, now) && GetPriority(input) > GetPriority(than);

    /// <summary>살아 있는 입력 수.</summary>
    public int Count(float now)
    {
        int count = 0;
        for (int i = 1; i < KindCount; i++)
        {
            if (IsAlive(i, now))
                count++;
        }

        return count;
    }

    /// <summary>그 종류만 지운다. 다른 종류는 그대로 둔다(거부로 버림·평타 뒤 예약으로 옮김).</summary>
    public void Remove(PlayerBufferedInput input)
    {
        int index = (int)input;
        if (index > 0 && index < KindCount)
            alive[index] = false;
    }

    /// <summary>
    /// 행동 중 소비처(콤보 예약·조준 취소 재입력 등)가 그 입력을 받아들였다 — 그 종류만 지운다.
    /// 전체를 비우면 평타를 누르고 있는 동안 콤보 예약이 매 프레임 부르며 공격 중 누른 스킬 입력까지 지운다(할 일 77 버그).
    /// Idle/Move 에서 새 행동을 시작할 때의 전체 비움은 <see cref="Resolve"/>(Started)·<see cref="Clear"/> 가 맡는다.
    /// </summary>
    public void Consume(PlayerBufferedInput input) => Remove(input);

    public void Clear()
    {
        for (int i = 0; i < KindCount; i++)
            alive[i] = false;
    }

    /// <summary>
    /// 그 입력으로 시작을 시도한 결과를 반영한다. 시작 = 전부 비움, 막힘(바쁨·CC) = 전부 그대로 둠,
    /// 그 밖의 거부(자원 부족·차단 효과·기타) = 그 입력만 버림 — 남은 것 중 다음 순위가 <see cref="Peek"/> 에 올라온다.
    /// 거부된 입력을 바로 버리므로 같은 입력이 거부(로그·피드백)를 두 번 내지 않는다.
    /// </summary>
    public void Resolve(PlayerBufferedInput input, PlayerActionAttempt attempt)
    {
        switch (attempt)
        {
            case PlayerActionAttempt.Started:
                Clear();
                break;
            case PlayerActionAttempt.Blocked:
                break;
            default:
                Remove(input);
                break;
        }
    }

    /// <summary>그 거부가 입력을 버리는가(다음 후보로 넘어감). 시작·막힘(보관)은 false.</summary>
    public static bool DropsInput(PlayerActionAttempt attempt) =>
        attempt != PlayerActionAttempt.Started && attempt != PlayerActionAttempt.Blocked;

    // 유지 시간이 지났으면 그 종류만 지운다(한 번 만료되면 되살아나지 않는다).
    private bool IsAlive(int index, float now)
    {
        if (!alive[index])
            return false;

        if (HoldSeconds <= 0f || now - recordedAt[index] > HoldSeconds)
        {
            alive[index] = false;
            return false;
        }

        return true;
    }

    public static PlayerBufferedInput FromSkillSlot(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => PlayerBufferedInput.SkillMain,
        PlayerSkillSlot.Sub => PlayerBufferedInput.SkillSub,
        PlayerSkillSlot.Interrupt => PlayerBufferedInput.SkillInterrupt,
        PlayerSkillSlot.Ultimate => PlayerBufferedInput.SkillUltimate,
        _ => PlayerBufferedInput.None
    };

    /// <summary><see cref="FromSkillSlot"/> 의 역. 스킬 입력이 아니면 false.</summary>
    public static bool TryGetSkillSlot(PlayerBufferedInput input, out PlayerSkillSlot slot)
    {
        switch (input)
        {
            case PlayerBufferedInput.SkillMain: slot = PlayerSkillSlot.Main; return true;
            case PlayerBufferedInput.SkillSub: slot = PlayerSkillSlot.Sub; return true;
            case PlayerBufferedInput.SkillInterrupt: slot = PlayerSkillSlot.Interrupt; return true;
            case PlayerBufferedInput.SkillUltimate: slot = PlayerSkillSlot.Ultimate; return true;
            default: slot = default; return false;
        }
    }
}
