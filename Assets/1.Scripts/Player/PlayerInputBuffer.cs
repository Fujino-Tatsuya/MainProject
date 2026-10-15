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
    OutOfResource
}

/// <summary>
/// 선입력 버퍼(할 일 44) — 지금 바로 실행 못 하는 입력을 잠깐 들고 있다가 실행 가능해진 첫 순간에 넘긴다.
/// 마지막 1개만 기억한다(새 입력이 이전 것을 덮는다). 유지 시간이 지나면 없는 것으로 본다.
/// 시간은 호출자가 넘긴다(MonoBehaviour 아님 — EditMode 테스트 대상). 유지 시간 0 이하 = 버퍼 끔.
/// </summary>
public sealed class PlayerInputBuffer
{
    private PlayerBufferedInput kind = PlayerBufferedInput.None;
    private float recordedAt;

    public PlayerInputBuffer(float holdSeconds)
    {
        HoldSeconds = holdSeconds;
    }

    public float HoldSeconds { get; set; }

    /// <summary>새로 눌린 입력을 기록한다. 이전 입력은 버린다.</summary>
    public void Record(PlayerBufferedInput input, float now)
    {
        if (HoldSeconds <= 0f || input == PlayerBufferedInput.None)
            return;

        kind = input;
        recordedAt = now;
    }

    /// <summary>지금 살아 있는 입력. 만료됐으면 None.</summary>
    public PlayerBufferedInput Peek(float now)
    {
        if (kind == PlayerBufferedInput.None)
            return PlayerBufferedInput.None;

        if (HoldSeconds <= 0f || now - recordedAt > HoldSeconds)
        {
            kind = PlayerBufferedInput.None;
            return PlayerBufferedInput.None;
        }

        return kind;
    }

    public bool Has(PlayerBufferedInput input, float now) =>
        input != PlayerBufferedInput.None && Peek(now) == input;

    /// <summary>그 종류가 들어 있으면 지운다. 다른 종류는 그대로 둔다.</summary>
    public void Consume(PlayerBufferedInput input)
    {
        if (input != PlayerBufferedInput.None && kind == input)
            kind = PlayerBufferedInput.None;
    }

    public void Clear()
    {
        kind = PlayerBufferedInput.None;
    }

    /// <summary>
    /// 그 입력으로 시작을 시도한 결과를 반영한다. 시작 = 전부 비움, 자원 부족 = 그 입력만 버림, 막힘 = 그대로 둠.
    /// 자원 부족 입력을 바로 버리므로 같은 입력이 거부(로그·피드백)를 두 번 내지 않는다.
    /// </summary>
    public void Resolve(PlayerBufferedInput input, PlayerActionAttempt attempt)
    {
        switch (attempt)
        {
            case PlayerActionAttempt.Started:
                Clear();
                break;
            case PlayerActionAttempt.OutOfResource:
                Consume(input);
                break;
        }
    }

    public static PlayerBufferedInput FromSkillSlot(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => PlayerBufferedInput.SkillMain,
        PlayerSkillSlot.Sub => PlayerBufferedInput.SkillSub,
        PlayerSkillSlot.Interrupt => PlayerBufferedInput.SkillInterrupt,
        PlayerSkillSlot.Ultimate => PlayerBufferedInput.SkillUltimate,
        _ => PlayerBufferedInput.None
    };
}
